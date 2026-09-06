using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;

static class CombatRegression {
    internal record Result(string Name, bool Ok, string Detail);
    sealed class Target : ComponentBody {
        public override BoundingBox BoundingBox => new(Position - new Vector3(.4f), Position + new Vector3(.4f));
    }
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> result = [];
        void Test(string name, Func<bool> action) {
            try { result.Add(new("combat/" + name, action(), name)); }
            catch (Exception e) { result.Add(new("combat/" + name, false, e.ToString())); }
        }
        object Call(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        var center = new Ray3(new Vector3(0, 1.5f, 0), Vector3.UnitZ);
        var finger = new Ray3(center.Position, Vector3.Normalize(new Vector3(.4f, -.2f, 1)));
        Ray3 Aim(bool touch, Ray3? dig, Ray3? hit) => (Ray3)Call("ScShotAim", "Select", touch, dig, hit, center);
        Test("desktop-retains-mouse-ray", () => Vector3.Distance(Aim(false, finger, null).Direction, finger.Direction) < .000001f);
        Test("touch-camera-ray-even-with-hit-only", () => Aim(true, null, finger).Direction == center.Direction);
        Test("invalid-ray-falls-back", () => Aim(false, new Ray3(center.Position, Vector3.Zero), null).Direction == center.Direction);
        Test("non-unit-ray-normalized", () => Math.Abs(Aim(false, new Ray3(center.Position, Vector3.UnitZ * 4), null).Direction.Length() - 1) < .001);
        foreach (float distance in new[] { 1f, 10, 30, 60, 70 }) Test("touch-api-body-ray/" + distance, () => {
            var bodies = new SubsystemBodies(); var body = new Target { Position = center.Position + Vector3.UnitZ * distance };
            bodies.AddBody(body); var ray = Aim(true, finger, finger);
            var hit = bodies.Raycast(ray.Position, ray.Position + ray.Direction * 64, .35f, (b, d) => true);
            return distance < 64 ? hit?.ComponentBody == body && Math.Abs(hit.Value.Distance - (distance - .75f)) < .002f : hit is null;
        });
        Test("taser-range-preserved", () => {
            var bodies = new SubsystemBodies(); var body = new Target { Position = center.Position + Vector3.UnitZ * 10 }; bodies.AddBody(body);
            return bodies.Raycast(center.Position, center.Position + center.Direction * 3.05f, .35f, (b, d) => true) is null;
        });
        foreach (var c in new[] { (1f,.8f,1), (1f,0f,2), (0f,0f,0), (0f,-1f,0), (.5f,.5f,0), (.5f,.8f,0), (float.NaN,0f,0) })
            Test($"health-result/{c}", () => (int)Call("ScCombatFeedback", "Outcome", c.Item1, c.Item2) == c.Item3);
        Test("kill-feed-bounded-and-captured", () => {
            var type = mod.GetType("Game.ScCombatFeedback"); var state = Activator.CreateInstance(type);
            for (int i = 0; i < 20; i++) type.GetMethod("Record").Invoke(state, [2, "狼\n", "AWP", 25f, (double)i]);
            var kills = (IList)type.GetField("Kills").GetValue(state); var newest = kills[0];
            return kills.Count == 3 && (string)newest.GetType().GetProperty("Target").GetValue(newest) == "狼"
                && (string)newest.GetType().GetProperty("Weapon").GetValue(newest) == "AWP"
                && (float)newest.GetType().GetProperty("Distance").GetValue(newest) == 25;
        });
        Test("magazine-animation-finish-only", () => {
            int data = (int)Call("GunSpec", "MakeData", 0, 8, false), value = Terrain.MakeBlockValue(512, 0, data);
            var inv = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 10; i++) inv.m_slots.Add(0); inv.AddSlotItems(0, value, 1);
            var type = mod.GetType("Game.ScReloadTransaction"); var tx = Activator.CreateInstance(type, [inv, 0, value, 901, 0, 30]);
            if (!(bool)type.GetMethod("Discard").Invoke(tx, null)) return false;
            foreach (double now in new[] { 0, .8, 1.5, 2.999 })
                if ((bool)type.GetMethod("FinishMagazine").Invoke(tx, [now, 3d]) || inv.GetSlotValue(0) != value) return false;
            return (bool)type.GetMethod("FinishMagazine").Invoke(tx, [3d, 3d])
                && (int)Call("GunSpec", "GetRounds", Terrain.ExtractData(inv.GetSlotValue(0))) == 30
                && !(bool)type.GetMethod("FinishMagazine").Invoke(tx, [4d, 3d]);
        });
        for (int kind = 0; kind < 7; kind++) {
            int k = kind;
            Test("supply-ui-colored-with-zero-scene-light/" + k, () => {
                var source = (BlockMesh)Call("ScSurvivalMesh", "Build", k);
                var mesh = (BlockMesh)Call("ScSurvivalMesh", "InventoryMesh", source);
                float saved = LightingManager.LightIntensityByLightValue[15];
                try {
                    LightingManager.LightIntensityByLightValue[15] = 0;
                    var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
                    var renderer = new PrimitivesRenderer3D(); var matrix = Matrix.Identity;
                    BlocksManager.DrawMeshBlock(renderer, mesh, texture, Color.White, 1, ref matrix, new DrawBlockEnvironmentData { Light = 15, DrawBlockMode = DrawBlockMode.UI });
                    var batch = renderer.TexturedBatch(texture, true, 0, null, RasterizerState.CullCounterClockwiseScissor, null, SamplerState.PointClamp);
                    return batch.TriangleVertices.Count == source.Vertices.Count && batch.TriangleVertices.All(v => v.Color.R >= 150 && v.Color.A == 255)
                        && source.Vertices.All(v => !v.IsEmissive) && mesh.Indices.SequenceEqual(source.Indices);
                } finally { LightingManager.LightIntensityByLightValue[15] = saved; }
            });
        }
        using var zip = ZipFile.OpenRead(package);
        Test("supply-texture-explicit-opaque-rgba", () => {
            using var stream = zip.GetEntry("Assets/Textures/ScCsgoKnives/survival_surface.png").Open();
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
            var pixels = Engine.Media.Image.Load(new MemoryStream(bytes));
            try { return bytes[25] == 6 && pixels.Pixels.All(c => c.A == 255 && c.R > 0 && c.G > 0 && c.B > 0); }
            finally { pixels.Dispose(); }
        });
        Test("bf1-original-kill-sound-present", () => zip.GetEntry("Assets/Audio/ScCsgoKnives/bf1_kill_confirm.ogg")?.Length > 10000);
        return result;
    }
}
