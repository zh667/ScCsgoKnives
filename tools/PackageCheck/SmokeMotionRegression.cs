using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Engine;

// post-mp-bugs-20260930 item 2: the smoke's shape is world data. The delivered DLL's puffs must be the same set at every
// viewer distance (no 18/40 m band re-layout), and the quad each puff is drawn as must keep its size from every viewer
// position along strafe, orbit, rise, approach and fast-pass paths (the cloud-bounds fit never engaged), in both editions
// and at every smoke age. Facing the viewer is what a billboard does; a size or a place that follows the viewer is not.
static class SmokeMotionRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results = [];
        var visuals = mod.GetType("Game.ScGrenadeVisuals"); var stateType = mod.GetType("Game.ScGrenadeState"); var spriteType = mod.GetType("Game.ScGrenadeVisuals+Sprite");
        var policy = mod.GetType("Game.ScResourcePolicy"); string edition = (string)policy.GetProperty("Edition").GetValue(null);
        var configure = policy.GetMethod("ConfigureEdition", BindingFlags.Static | BindingFlags.NonPublic);
        var sp = Expression.Parameter(typeof(object)); var st = Expression.Parameter(typeof(object)); var v1 = Expression.Parameter(typeof(Vector3)); var v2 = Expression.Parameter(typeof(Vector3));
        var quad = Expression.Lambda<Func<object, object, Vector3, Vector3, (Vector3, Vector3)>>(
            Expression.Call(visuals.GetMethod("SmokeQuad"), Expression.Convert(sp, spriteType), Expression.Convert(st, stateType), v1, v2), sp, st, v1, v2).Compile();
        // Round 4: a puff is three quads (facing the character, across it, horizontal); none may be clipped by the fit either.
        var quads = Expression.Lambda<Func<object, object, Vector3, Vector3, (Vector3, Vector3)[]>>(
            Expression.Call(visuals.GetMethod("SmokeQuads"), Expression.Convert(sp, spriteType), Expression.Convert(st, stateType), v1, v2), sp, st, v1, v2).Compile();
        object State(float age, float remaining, int id) {
            var s = Activator.CreateInstance(stateType);
            void Set(string n, object v) => stateType.GetField(n).SetValue(s, v);
            Set("Kind", 2); Set("Effect", true); Set("Age", age); Set("Remaining", remaining); Set("Position", new Vector3(10, 64, -3)); Set("Id", id);
            return s;
        }
        string Describe(object sprite) => string.Join("|", new[] { "Position", "Width", "Height", "Rotation", "Frame", "Color" }.Select(n => spriteType.GetProperty(n).GetValue(sprite).ToString()));
        object[] Sprites(object state, float distance) => ((IEnumerable)visuals.GetMethod("Smoke").Invoke(null, [state, distance])).Cast<object>().ToArray();
        IEnumerable<Vector3> Path(Vector3 centre) {
            for (int i = 0; i <= 40; i++) yield return centre + new Vector3(-8 + 16f * i / 40, 1.62f, -8);                  // strafe past
            for (int i = 0; i <= 60; i++) { float a = MathF.Tau * i / 60; yield return centre + new Vector3(MathF.Cos(a) * 7, 1.62f, MathF.Sin(a) * 7); } // orbit
            for (int i = 0; i <= 30; i++) yield return centre + new Vector3(0, .9f + 9f * i / 30, -8);                        // rise
            for (int i = 0; i <= 40; i++) yield return centre + new Vector3(0, 1.62f, -(45 - 41f * i / 40));                 // approach through the old 40/18 m bands
            for (int i = 0; i <= 20; i++) yield return centre + new Vector3(-30 + 60f * i / 20, 3, -5);                       // fast pass
            for (int i = 0; i <= 10; i++) yield return centre + new Vector3(0, 6 + 4f * i / 10, 0);                          // above
        }
        try {
            foreach (bool lite in new[] { false, true }) {
                configure.Invoke(null, [lite ? "Optimized512" : "Full"]);
                foreach (var (label, age, remaining) in new[] { ("full", 2f, 12f), ("growing", .45f, 17f), ("fading", 3f, .6f) }) {
                    var state = State(age, remaining, 3); Vector3 centre = (Vector3)mod.GetType("Game.ScSmokeVolume").GetMethod("Center").Invoke(null, [state]);
                    var layouts = new[] { 3f, 5f, 17f, 19f, 39f, 41f, 79f }.Select(d => string.Join("\n", Sprites(state, d).Select(Describe))).Distinct().ToArray();
                    int count = Sprites(state, 5).Length;
                    results.Add(new($"smoke-motion/{(lite ? "lite" : "full")}/{label}/same-puffs-at-every-distance", layouts.Length == 1 && count == (int)visuals.GetProperty("SmokePuffCount").GetValue(null), $"{count} puffs; {layouts.Length} distinct layouts over 3..79 m"));
                    var sprites = Sprites(state, 8); int clipped = 0, tested = 0; float worst = 1;
                    foreach (var eye in Path(centre)) {
                        Vector3 forward = Vector3.Normalize(centre - eye); Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                        foreach (var sprite in sprites) {
                            float w = (float)spriteType.GetProperty("Width").GetValue(sprite), h = (float)spriteType.GetProperty("Height").GetValue(sprite);
                            var (r, u) = quad(sprite, state, eye, right);
                            float fit = Math.Min(r.Length() / w, u.Length() / h);
                            foreach (var (qr, qu) in quads(sprite, state, eye, right)) fit = Math.Min(fit, Math.Min(qr.Length() / w, qu.Length() / h));
                            tested++; worst = Math.Min(worst, fit);
                            if (fit < .999f) clipped++;
                        }
                    }
                    results.Add(new($"smoke-motion/{(lite ? "lite" : "full")}/{label}/puff-size-independent-of-the-viewer", clipped == 0, $"{clipped} of {tested} (puff, viewer position) pairs clipped by the bounds fit; smallest fit {worst:0.000}"));
                }
            }
        }
        finally { configure.Invoke(null, [edition]); }
        return results;
    }
}
