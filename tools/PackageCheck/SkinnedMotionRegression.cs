using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;

static class SkinnedMotionRegression {
    internal record Result(string Name, bool Ok, string Detail);
    sealed class Body : ComponentBody {
        public BoundingBox Box;
        public override BoundingBox BoundingBox => Box;
    }
    internal static List<Result> Run(Assembly mod, string modelPath) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) {
            try { results.Add(new("hit-motion/" + name, test(), name)); }
            catch (Exception e) { results.Add(new("hit-motion/" + name, false, e.ToString())); }
        }
        object Call(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        var skin = new ModelSkin { JointIndices = [0], InverseBindMatrices = [Matrix.Identity] };
        if (modelPath is not null) Test("real-glb-load", () => {
            using var stream = File.OpenRead(modelPath);
            var data = ModelData.Load(stream);
            if (data.Skin is null || data.Skin.JointCount == 0 || data.Meshes.Count < 1) return false;
            skin = data.Skin;
            return data.Meshes.All(m => m.MeshParts.Count > 0);
        });
        var model = new Model { Skin = skin };
        var root = new ModelBone { Model = model, Index = 0, Name = "Head", Transform = Matrix.Identity };
        model.m_rootBone = root; model.m_bones.Add(root);
        model.m_meshes.Add(new ModelMesh { ParentBone = root, IsVisible = true,
            BoundingBox = new BoundingBox(new Vector3(-.001f), new Vector3(.001f)) });
        var creature = Blank<ComponentHumanModel>(); creature.m_model = model;
        var body = new Body(); var entity = Blank<Entity>();
        body.m_entity = entity; creature.m_entity = entity; entity.m_components = [body, creature];
        Test("skin-never-uses-rigid-head-box", () => {
            var rule = mod.GetType("Game.ScHeadRule").GetProperty("Default")?.GetValue(null)
                ?? mod.GetType("Game.ScHeadRule").GetField("Default").GetValue(null);
            var parts = (IEnumerable)Call("ScHeadshotProbe", "Parts", model, rule, new[] { Matrix.Identity });
            return !parts.Cast<object>().Any();
        });
        foreach (Vector3 pos in new[] { Vector3.Zero, new Vector3(50, 20, -30), new Vector3(-100, 3, 80) }) {
            Test("skin-body-nearest-wall-miss/" + pos, () => {
                body.Box = new BoundingBox(pos + new Vector3(-2.5f, 0, 5), pos + new Vector3(2.5f, 2, 10));
                Vector3 origin = pos + new Vector3(0, 1, 0);
                object Ray(Vector3 start, float limit, ComponentBody shooter = null) => Call("ScGunHitTest", "Raycast", new[] { body }, shooter, start, Vector3.UnitZ, limit);
                var hit = Ray(origin, 20);
                if (hit is null) return false;
                string reason = (string)hit.GetType().GetProperty("Reason").GetValue(hit);
                float distance = (float)hit.GetType().GetProperty("Distance").GetValue(hit);
                return reason.Contains("skinned model") && hit.GetType().GetProperty("Part").GetValue(hit).ToString() == "Body"
                    && Math.Abs(distance - 4.92f) < .001 && Ray(origin, 4) is null
                    && Ray(origin + new Vector3(2.7f, 0, 0), 20) is null && Ray(origin, 20, body) is null;
            });
        }
        Test("classic-skin-does-not-invent-headshot", () => {
            object[] args = [body, Vector3.Zero, Vector3.UnitZ, 20f, 0f, null];
            var part = mod.GetType("Game.ScHeadshotProbe").GetMethod("Resolve").Invoke(null, args);
            return part.ToString() == "Unknown" && ((string)args[5]).Contains("body-only");
        });
        var motionType = mod.GetType("Game.ScWeaponWalkMotion");
        Matrix Sample(object state, double now, float speed, bool grounded = true, bool aim = false) =>
            (Matrix)motionType.GetMethod("Sample").Invoke(state, [now, speed, grounded, aim]);
        Matrix[] Run(int fps, float speed, bool aim = false) {
            var state = Activator.CreateInstance(motionType); List<Matrix> frames = [];
            for (int i = 0; i <= fps * 5; i++) frames.Add(Sample(state, (double)i / fps, speed, true, aim));
            return frames.ToArray();
        }
        Test("cadence-independent-of-frame-rate", () => {
            var slow = Run(30, 4.5f); var fast = Run(120, 4.5f);
            return slow.Select((m, i) => Vector3.Distance(m.Translation, fast[i * 4].Translation)).Max() < .00001f;
        });
        foreach (float speed in new[] { 0, .1f, 1, 4.5f, 20, 1000, float.NaN, float.PositiveInfinity }) Test("bounded-and-smooth/" + speed, () => {
            var frames = Run(60, speed);
            if (frames.Any(m => !float.IsFinite(m.M11 + m.M22 + m.M33 + m.Translation.Length())
                || Math.Abs(m.Translation.Z) > .02401 || Math.Abs(m.Translation.X) > .00801 || Math.Abs(m.Translation.Y) > .00601)) return false;
            // The designed steady velocity bound is 2*pi*1.55/60 *
            // sqrt(.008^2 + (.006*2)^2 + .024^2) = .004545 per frame.
            return frames.Skip(1).Select((m, i) => Vector3.Distance(m.Translation, frames[i].Translation)).Max() < .0046;
        });
        Test("extreme-speed-same-cadence", () => Run(60, 4.5f).SequenceEqual(Run(60, 1000)));
        Test("aim-reduces-motion", () => Run(60, 4.5f, true).Max(m => m.Translation.Length()) < .004);
        Test("stop-airborne-repeat-and-pause", () => {
            var state = Activator.CreateInstance(motionType);
            for (int i = 0; i <= 60; i++) Sample(state, i / 60d, 4.5f);
            var a = Sample(state, 1, 1000, false);
            if (a != Sample(state, 1, 0)) return false;
            Matrix end = a;
            for (int i = 61; i <= 120; i++) end = Sample(state, i / 60d, 4.5f, false);
            return end.Translation.Length() < .00001 && Sample(state, 20, 1000) == Matrix.Identity
                && Sample(state, double.NaN, 1000) == Matrix.Identity;
        });
        Test("first-person-uses-bounded-motion", () => {
            var render = mod.GetType("Game.CsmcFirstPersonRenderer").GetMethod("CreateBodyMotion", BindingFlags.NonPublic | BindingFlags.Static);
            return CombatRegression.Calls(render).Any(m => m.DeclaringType == motionType && m.Name == "Sample")
                && !CombatRegression.Calls(render).Any(m => m.Name == "get_MovementAnimationPhase");
        });
        Test("actual-first-person-motion-and-platform-relative-speed", () => {
            var clock = mod.GetType("Game.KnifeClock");
            var virtualField = clock.GetField("Virtual"); var nowField = clock.GetField("VirtualNow");
            object oldVirtual = virtualField.GetValue(null), oldNow = nowField.GetValue(null);
            var renderer = mod.GetType("Game.CsmcFirstPersonRenderer");
            var method = renderer.GetMethod("CreateBodyMotion", BindingFlags.NonPublic | BindingFlags.Static);
            var scope = renderer.GetField("s_drawingScope", BindingFlags.NonPublic | BindingFlags.Static);
            object oldScope = scope.GetValue(null);
            try {
                virtualField.SetValue(null, true); scope.SetValue(null, Activator.CreateInstance(scope.FieldType,true));
                var player = Blank<ComponentPlayer>(); player.ComponentBody = new Body { StandingOnValue = 1, Velocity = new Vector3(4.5f, 0, 0) };
                var fp = Blank<ComponentFirstPersonModel>(); fp.m_componentPlayer = player;
                var reference = Activator.CreateInstance(motionType);
                for (int i = 0; i <= 120; i++) {
                    nowField.SetValue(null, i / 60d);
                    Matrix actual = (Matrix)method.Invoke(null, [fp]);
                    if (actual != Sample(reference, i / 60d, 4.5f) || actual != (Matrix)method.Invoke(null, [fp])) return false;
                }
                var passenger = Blank<ComponentFirstPersonModel>(); passenger.m_componentPlayer = player;
                player.ComponentBody.StandingOnBody = new Body { Velocity = player.ComponentBody.Velocity };
                for (int i = 0; i < 60; i++) {
                    nowField.SetValue(null, 3 + i / 60d);
                    if ((Matrix)method.Invoke(null, [passenger]) != Matrix.Identity) return false;
                }
                player.ComponentBody.StandingOnBody = null;
                player.ComponentBody.StandingOnVelocity = player.ComponentBody.Velocity;
                for (int i = 0; i < 60; i++) {
                    nowField.SetValue(null, 4 + i / 60d);
                    if ((Matrix)method.Invoke(null, [passenger]) != Matrix.Identity) return false;
                }
                return true;
            } finally {
                virtualField.SetValue(null, oldVirtual); nowField.SetValue(null, oldNow);
                scope.SetValue(null, oldScope);
            }
        });
        return results;
    }
}
