using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Engine;
using Engine.Media;

/// <summary>M1b: the packaged headshot geometry against the real vanilla models in Content.zip. Every creature
/// model the game's Database.xml assigns to a vanilla head-bearing model class must be registered by the mod,
/// nothing else may be, and in the bind pose the head box must sit above the body and be found by a ray.</summary>
static class HeadshotRegression {
    internal record Result(string Name, bool Ok, string Detail);
    static readonly string[] HeadClasses = ["HumanModel", "FourLeggedModel", "BirdModel", "FlightlessBirdModel"];
    static IEnumerable<Vector3> Corners(BoundingBox b) {
        foreach (float x in new[] { b.Min.X, b.Max.X }) foreach (float y in new[] { b.Min.Y, b.Max.Y }) foreach (float z in new[] { b.Min.Z, b.Max.Z }) yield return new Vector3(x, y, z);
    }
    internal static List<Result> Run(Assembly mod, string vanillaContent) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail) => results.Add(new("headshot/" + name, ok, detail));
        try {
            using var zip = ZipFile.OpenRead(vanillaContent);
            XElement database; using (var s = zip.GetEntry("Assets/Database.xml").Open()) database = XElement.Load(s);
            var byGuid = new Dictionary<string, XElement>();
            foreach (var e in database.Descendants().Where(e => e.Attribute("Guid") is not null)) byGuid.TryAdd((string)e.Attribute("Guid"), e); // the database repeats a few GUIDs
            string Param(XElement component, string name) {
                var p = component.Elements("Parameter").FirstOrDefault(x => (string)x.Attribute("Name") == name);
                if (p is not null) return (string)p.Attribute("Value");
                string parent = (string)component.Attribute("InheritanceParent");
                return parent is not null && byGuid.TryGetValue(parent, out var inherited) ? Param(inherited, name) : null;
            }
            var expected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var fish = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var component in database.Descendants("MemberComponentTemplate")) {
                string cls = (string)component.Attribute("Name"), model = Param(component, "ModelName");
                if (string.IsNullOrEmpty(model) || zip.GetEntry("Assets/" + model + ".dae") is null) continue;
                if (HeadClasses.Contains(cls)) expected.Add(model); else if (cls == "FishModel") fish.Add(model);
            }
            var rules = mod.GetType("Game.ScHeadRules"); var geometry = mod.GetType("Game.ScHeadshot"); var ruleType = mod.GetType("Game.ScHeadRule");
            var registered = new SortedSet<string>((string[])rules.GetField("VanillaHeadModels").GetValue(null), StringComparer.OrdinalIgnoreCase);
            object defaultRule = ruleType.GetField("Default").GetValue(null);
            var withHead = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            ModelData Load(string route) { using var s = zip.GetEntry("Assets/" + route + ".dae").Open(); using var ms = new MemoryStream(); s.CopyTo(ms); ms.Position = 0; return Collada.Load(ms); }
            foreach (string route in expected.Concat(fish))
                if ((bool)geometry.GetMethod("HasHeadMesh").Invoke(null, [Load(route), defaultRule])) withHead.Add(route);
            Check("vanilla-list-matches-database", registered.SetEquals(withHead),
                $"database head-class models with a Head mesh: {withHead.Count}; registered: {registered.Count}; " +
                (registered.SetEquals(withHead) ? "identical" : "missing " + string.Join(",", withHead.Except(registered)) + " extra " + string.Join(",", registered.Except(withHead))));
            Check("fish-have-no-head-mesh", fish.Count > 0 && !fish.Any(withHead.Contains), $"{fish.Count} fish models, none with a Head mesh");
            var boxType = mod.GetType("Game.ScPartBox");
            foreach (string route in registered) {
                var data = Load(route);
                var parts = (IList)geometry.GetMethod("PartsFromModelData").Invoke(null, [data, defaultRule, 1f]);
                (BoundingBox Local, Matrix World, bool Head) Read(object part) => ((BoundingBox)boxType.GetProperty("Local").GetValue(part), (Matrix)boxType.GetProperty("World").GetValue(part), (bool)boxType.GetProperty("Head").GetValue(part));
                var boxes = parts.Cast<object>().Select(Read).ToList();
                var head = boxes.Single(b => b.Head); var body = boxes.First(b => !b.Head && data.Meshes.Any(m => m.Name == "Body"));
                var bodyBox = boxes.First(b => !b.Head && data.Bones[data.Meshes.First(m => m.BoundingBox == b.Local).ParentBoneIndex].Name == "Body");
                Vector3 headCentre = Vector3.Transform(head.Local.Center(), head.World), bodyCentre = Vector3.Transform(bodyBox.Local.Center(), bodyBox.World);
                // World extents per bone axis (the bone may be pitched, so the size vector cannot simply be transformed).
                Vector3 headSize = new(Vector3.TransformNormal(new Vector3(head.Local.Size().X, 0, 0), head.World).Length(),
                    Vector3.TransformNormal(new Vector3(0, head.Local.Size().Y, 0), head.World).Length(), Vector3.TransformNormal(new Vector3(0, 0, head.Local.Size().Z), head.World).Length());
                float bodyBottom = Corners(bodyBox.Local).Select(c => Vector3.Transform(c, bodyBox.World).Y).Min();
                object Resolve(Vector3 origin, Vector3 direction, float max) => geometry.GetMethod("Resolve").Invoke(null, [parts, origin, direction, max]);
                int Part(object r) => (int)r.GetType().GetField("Item1").GetValue(r); float Dist(object r) => (float)r.GetType().GetField("Item2").GetValue(r);
                var fromAbove = Resolve(headCentre + Vector3.UnitY * 3, -Vector3.UnitY, 64);
                var insideBody = Resolve(bodyCentre, Vector3.UnitX, 64);
                var far = Resolve(headCentre + new Vector3(50, 0, 0), -Vector3.UnitY, 64);
                bool above = headCentre.Y > bodyBottom; // a bison's head hangs below its hump, but never below the body's underside
                bool sane = headSize.X < 1.6f && headSize.Y < 1.6f && headSize.Z < 1.6f && headSize.Length() > .05f;
                Check("model/" + route, Part(fromAbove) == 2 && Dist(fromAbove) > 0 && Dist(fromAbove) < 3 && Part(insideBody) == 1 && Dist(insideBody) == 0 && Part(far) == 0 && above && sane,
                    $"head centre {headCentre.X:0.00},{headCentre.Y:0.00},{headCentre.Z:0.00} size {headSize.X:0.00}×{headSize.Y:0.00}×{headSize.Z:0.00} m; body centre y {bodyCentre.Y:0.00} bottom {bodyBottom:0.00}; from above={Part(fromAbove)}@{Dist(fromAbove):0.00} inside body={Part(insideBody)} far={Part(far)}");
            }
        }
        catch (Exception e) { Check("run", false, e.ToString()); }
        return results;
    }
}
