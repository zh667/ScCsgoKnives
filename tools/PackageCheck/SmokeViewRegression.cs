using System.Collections;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Engine;

// post-mp-bugs-20260930 §2: turning the camera in place must not open the smoke. Perspective rays from many eye positions
// through the delivered DLL's smoke sprites (delivered atlas alpha, the renderer's quad corners and uv, 0.1 near plane),
// to targets behind the cloud, under several camera yaw/pitch/FOV. "Camera-aligned" is the previous plane (camera right/up),
// "facing" the drawn one (ScGrenadeVisuals.SmokeQuad). A ray counts as blocked where the AI's rule does
// (ScSmokeVolume.EffectiveInsideLength > .5 m); deep = > 1.5 m. No GPU/game-frame claim.
static class SmokeViewRegression {
    internal record Result(string Name, bool Ok, string Detail);
    const float Near = .1f;
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> results = [];
        using var zip = ZipFile.OpenRead(package);
        var entry = zip.GetEntry("Assets/Textures/ScCsgoKnives/grenade_smoke_atlas.png") ?? zip.GetEntry("Assets/Textures/ScCsgoKnives/grenade_smoke_atlas.webp");
        using var data = new MemoryStream(); using (var stream = entry.Open()) stream.CopyTo(data); data.Position = 0;
        var atlas = Engine.Media.Image.Load(data);
        // 2026-10-01: the cloud's surface puffs use the generated smooth texture; its alpha is a product function.
        var smoothVisuals = mod.GetType("Game.ScGrenadeVisuals");
        int smoothKey = (int)smoothVisuals.GetField("SmoothPuffKey").GetValue(null);
        var smoothAlpha = (Func<int, float, float, float>)Delegate.CreateDelegate(typeof(Func<int, float, float, float>), smoothVisuals.GetMethod("SmoothPuffAlpha"));
        try {
            var visuals = mod.GetType("Game.ScGrenadeVisuals"); var volume = mod.GetType("Game.ScSmokeVolume");
            var stateType = mod.GetType("Game.ScGrenadeState");
            object State(float age, float remaining) {
                var s = Activator.CreateInstance(stateType);
                void Set(string n, object v) => stateType.GetField(n).SetValue(s, v);
                Set("Kind", 2); Set("Effect", true); Set("Age", age); Set("Remaining", remaining); Set("Position", Vector3.Zero);
                return s;
            }
            var smokeMethod = visuals.GetMethod("Smoke"); var centerMethod = volume.GetMethod("Center"); var radiusMethod = volume.GetMethod("CurrentRadius");
            var spriteType = mod.GetType("Game.ScGrenadeVisuals+Sprite");
            // Compiled calls into the delivered DLL (millions of evaluations; reflection per call would take minutes).
            var sp = Expression.Parameter(typeof(object)); var st = Expression.Parameter(typeof(object)); var v1 = Expression.Parameter(typeof(Vector3)); var v2 = Expression.Parameter(typeof(Vector3));
            var legacyAxes = Expression.Lambda<Func<object, object, Vector3, Vector3, (Vector3, Vector3)>>(
                Expression.Call(visuals.GetMethod("SmokeAxes"), Expression.Convert(sp, spriteType), Expression.Convert(st, stateType), v1, v2), sp, st, v1, v2).Compile();
            var facingAxes = Expression.Lambda<Func<object, object, Vector3, Vector3, (Vector3, Vector3)>>(
                Expression.Call(visuals.GetMethod("SmokeQuad"), Expression.Convert(sp, spriteType), Expression.Convert(st, stateType), v1, v2), sp, st, v1, v2).Compile();
            // Round 4: the puffs face the character; a camera elsewhere sees the three quads of each puff (ScGrenadeVisuals.SmokeQuads).
            var puffQuads = Expression.Lambda<Func<object, object, Vector3, Vector3, (Vector3, Vector3)[]>>(
                Expression.Call(visuals.GetMethod("SmokeQuads"), Expression.Convert(sp, spriteType), Expression.Convert(st, stateType), v1, v2), sp, st, v1, v2).Compile();
            // 2026-10-01: each quad is drawn with its weight for the viewer (ScGrenadeVisuals.SmokeQuadWeights).
            var v3 = Expression.Parameter(typeof(Vector3));
            var puffWeights = Expression.Lambda<Func<object, object, Vector3, Vector3, Vector3, float[]>>(
                Expression.Call(visuals.GetMethod("SmokeQuadWeights"), Expression.Convert(sp, spriteType), Expression.Convert(st, stateType), v1, v2, v3), sp, st, v1, v2, v3).Compile();
            var disturbances = volume.GetMethod("EffectiveInsideLength").GetParameters()[3].ParameterType;
            var insideLength = Expression.Lambda<Func<Vector3, Vector3, object, float>>(
                Expression.Call(volume.GetMethod("EffectiveInsideLength"), v1, v2, Expression.Convert(st, stateType), Expression.Constant(null, disturbances)), v1, v2, st).Compile();
            string report = Environment.GetEnvironmentVariable("SC_CSGO_VISUAL_REPORT");
            var cases = new List<object>();
            foreach (var (label, age, remaining) in new[] { ("full", 2f, 12f), ("growing", .45f, 17f), ("fading", 3f, .6f) }) {
                var state = State(age, remaining);
                Vector3 center = (Vector3)centerMethod.Invoke(null, [state]); float radius = (float)radiusMethod.Invoke(null, [state]);
                float Inside(Vector3 a, Vector3 b) => insideLength(a, b, state);
                int legacyLeaks = 0, facingLeaks = 0, deepRays = 0, blockedRays = 0, facingEdgeLow = 0, clearRays = 0, facingClearCovered = 0, legacyClearCovered = 0;
                float legacyMin = 1, facingMin = 1, legacySpread = 0, facingSpread = 0, facingClearMax = 0, legacyClearMax = 0;
                string worstLegacy = "", worstFacing = "";
                foreach (float distance in new[] { 4.6f, 6f, 8f, 12f, 20f })
                foreach (float height in new[] { 1.62f, .9f, 4f, 9f })
                foreach (float azimuth in new[] { 0f, .7f, 1.9f, 3.3f }) {
                    Vector3 eye = new(MathF.Cos(azimuth) * distance, height, MathF.Sin(azimuth) * distance);
                    var sprites = ((IEnumerable)smokeMethod.Invoke(null, [state, Vector3.Distance(eye, center)])).Cast<object>()
                        .Select(o => new Puff(o, (Vector3)spriteType.GetProperty("Position").GetValue(o), (int)spriteType.GetProperty("Frame").GetValue(o), ((Color)spriteType.GetProperty("Color").GetValue(o)).A / 255f, (int)spriteType.GetProperty("Texture").GetValue(o))).ToArray();
                    if (sprites.Length == 0) continue;
                    Vector3 away = new(center.X - eye.X, 0, center.Z - eye.Z); away = Vector3.Normalize(away);
                    Vector3 side = new(-away.Z, 0, away.X);
                    foreach (float lateral in new[] { -4f, -2.5f, -1.2f, 0f, 1.2f, 2.5f, 4f })
                    foreach (float targetY in new[] { .2f, .9f, 1.6f, 2.6f, 3.6f }) {
                        Vector3 target = new Vector3(center.X, 0, center.Z) + away * 6f + side * lateral + Vector3.UnitY * targetY;
                        float inside = Inside(eye, target);
                        bool blocked = inside > .5f, deep = inside > 1.5f;
                        // Clear: the eye-target segment passes at least a metre outside the density's soft edge.
                        bool clear = inside <= 0 && Miss(eye, target, center, radius) > 1f;
                        if (!blocked && !clear) continue;
                        float lmin = 1, lmax = 0, fmin = 1, fmax = 0;
                        foreach (var (yaw, pitch, fov) in Orientations()) {
                            // The camera looks past the target by (yaw, pitch): the target stays on screen for every FOV used.
                            Vector3 toTarget = Vector3.Normalize(target - eye);
                            Vector3 forward = Turn(toTarget, yaw, pitch);
                            if (Vector3.Dot(forward, toTarget) < MathF.Cos(MathUtils.DegToRad(fov * .5f * .95f))) continue;
                            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY)), up = Vector3.Cross(right, forward);
                            float legacy = Opacity(sprites, s => legacyAxes(s.Sprite, state, right, up), eye, target, forward);
                            float facing = Opacity(sprites, s => facingAxes(s.Sprite, state, eye, right), eye, target, forward);
                            lmin = Math.Min(lmin, legacy); lmax = Math.Max(lmax, legacy); fmin = Math.Min(fmin, facing); fmax = Math.Max(fmax, facing);
                        }
                        string at = $"eye ({eye.X:0.0},{eye.Y:0.0},{eye.Z:0.0}) target ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}) inside {inside:0.00}m";
                        if (clear) {
                            clearRays++;
                            if (fmax > .35f) facingClearCovered++; if (lmax > .35f) legacyClearCovered++;
                            facingClearMax = Math.Max(facingClearMax, fmax); legacyClearMax = Math.Max(legacyClearMax, lmax);
                            continue;
                        }
                        blockedRays++;
                        legacySpread = Math.Max(legacySpread, lmax - lmin); facingSpread = Math.Max(facingSpread, fmax - fmin);
                        if (!deep) { if (fmin < .5f) facingEdgeLow++; continue; }
                        deepRays++;
                        if (lmin < .85f) legacyLeaks++; if (fmin < .85f) facingLeaks++;
                        if (lmin < legacyMin) { legacyMin = lmin; worstLegacy = at; }
                        if (fmin < facingMin) { facingMin = fmin; worstFacing = at; }
                    }
                }
                string detail = $"deep rays {deepRays} (blocked {blockedRays}); camera-aligned: rays that some yaw/pitch/FOV leaves below 85% {legacyLeaks}, min {legacyMin:0.000}, "
                    + $"largest change with the camera {legacySpread:0.000}{(legacyLeaks > 0 ? " at " + worstLegacy : "")}; facing: below 85% {facingLeaks}, min {facingMin:0.000}, "
                    + $"largest change {facingSpread:0.000}{(facingLeaks > 0 ? " at " + worstFacing : "")}; soft-edge rays under 50% {facingEdgeLow}; "
                    + $"clear rays {clearRays}: covered >35% facing {facingClearCovered} (max {facingClearMax:0.000}) camera-aligned {legacyClearCovered} (max {legacyClearMax:0.000})";
                results.Add(new($"smoke-view/{label}/deep-lines-of-sight-covered-at-every-camera-turn", deepRays > 50 && facingLeaks == 0, detail));
                results.Add(new($"smoke-view/{label}/turning-in-place-does-not-change-coverage", blockedRays > 50 && facingSpread <= .02f, detail));
                results.Add(new($"smoke-view/{label}/clear-lines-of-sight-stay-clear", clearRays > 50 && facingClearCovered == 0, detail));
                // Round 4: the character stands still while the camera is elsewhere (third person behind it, an orbit around it at
                // three heights, a far side view, straight above the cloud): the puffs face the character, and the camera's deep
                // lines of sight must still be covered by the puffs' three quads. Coverage only: what a camera sees is an
                // appearance rule, the AI's rule stays the character's.
                int awayDeep = 0, awayLeaks = 0; float awayMin = 1; string awayWorst = "";
                foreach (var (ad, aaz) in new[] { (6f, 0f), (10f, 1.9f) }) {
                    Vector3 anchor = new(MathF.Cos(aaz) * ad, 1.62f, MathF.Sin(aaz) * ad);
                    Vector3 toCentre = Vector3.Normalize(new Vector3(center.X - anchor.X, 0, center.Z - anchor.Z));
                    var cameras = new List<(string Name, Vector3 Eye)> { ("tpp", anchor - toCentre * 2.25f + Vector3.UnitY * 1.75f), ("far-side", anchor + new Vector3(-toCentre.Z, 0, toCentre.X) * 8f), ("above-cloud", new Vector3(center.X, 12f, center.Z)) };
                    foreach (float h in new[] { 1.6f, 4f, 8f }) foreach (float az in new[] { 0f, .8f, 1.6f, 2.4f, 3.2f, 4f, 4.8f, 5.6f })
                        cameras.Add(($"orbit-{h}-{az}", anchor + new Vector3(MathF.Cos(az) * 5f, h - 1.62f, MathF.Sin(az) * 5f)));
                    var sprites = ((IEnumerable)smokeMethod.Invoke(null, [state, Vector3.Distance(anchor, center)])).Cast<object>()
                        .Select(o => new Puff(o, (Vector3)spriteType.GetProperty("Position").GetValue(o), (int)spriteType.GetProperty("Frame").GetValue(o), ((Color)spriteType.GetProperty("Color").GetValue(o)).A / 255f, (int)spriteType.GetProperty("Texture").GetValue(o))).ToArray();
                    if (sprites.Length == 0) continue;
                    foreach (var (name, eye) in cameras) {
                        Vector3 away = new(center.X - eye.X, 0, center.Z - eye.Z); if (away.LengthSquared() < 1e-4f) away = toCentre; away = Vector3.Normalize(away);
                        Vector3 side = new(-away.Z, 0, away.X);
                        Vector3 right = Vector3.Normalize(Vector3.Cross(toCentre, Vector3.UnitY));
                        foreach (float lateral in new[] { -2.5f, 0f, 2.5f })
                        foreach (float targetY in new[] { .2f, .9f, 1.6f, 2.6f }) {
                            Vector3 target = new Vector3(center.X, 0, center.Z) + away * 6f + side * lateral + Vector3.UnitY * targetY;
                            if (Inside(eye, target) <= 1.5f) continue;
                            Vector3 forward = Vector3.Normalize(target - eye);
                            float clear = 1;
                            for (int k = 0; k < 3; k++) { int q = k; clear *= 1 - Opacity(sprites, s => puffQuads(s.Sprite, state, anchor, right)[q], eye, target, forward, s => puffWeights(s.Sprite, state, anchor, right, eye)[q]); }
                            float coverage = 1 - clear; awayDeep++;
                            if (coverage < .85f) awayLeaks++;
                            if (coverage < awayMin) { awayMin = coverage; awayWorst = $"character ({anchor.X:0.0},{anchor.Y:0.0},{anchor.Z:0.0}) camera {name} ({eye.X:0.0},{eye.Y:0.0},{eye.Z:0.0}) target ({target.X:0.0},{target.Y:0.0},{target.Z:0.0})"; }
                        }
                    }
                }
                {
                    Vector3 eyeAt = new(0, 1.62f, 7f); int puffs = 0, single = 0; float worstExtra = 0;
                    var fpSprites = ((IEnumerable)smokeMethod.Invoke(null, [state, Vector3.Distance(eyeAt, center)])).Cast<object>().ToArray();
                    foreach (var o in fpSprites) {
                        var w = puffWeights(o, state, eyeAt, Vector3.UnitX, eyeAt); puffs++;
                        float extra = w[1] + w[2]; worstExtra = Math.Max(worstExtra, extra);
                        if (Math.Abs(w[0] - 1) < 1e-4f && extra < 1e-4f) single++;
                    }
                    results.Add(new($"smoke-view/{label}/first-person-draws-only-the-facing-quad", puffs > 0 && single == puffs,
                        $"{single} of {puffs} puffs drawn as one full-strength facing quad from the eye; largest extra weight {worstExtra:0.0000}"));
                }
                results.Add(new($"smoke-view/{label}/camera-away-from-the-character-still-covered", awayDeep > 50 && awayLeaks == 0,
                    $"deep camera rays {awayDeep}; below 85% {awayLeaks}; min {awayMin:0.000}{(awayLeaks > 0 ? " at " + awayWorst : "")}"));
                cases.Add(new { label, deepRays, blockedRays, legacyLeaks, legacyMin, legacySpread, facingLeaks, facingMin, facingSpread, facingEdgeLow, clearRays, facingClearCovered, facingClearMax, legacyClearCovered, legacyClearMax, worstLegacy, worstFacing });
            }
            if (!string.IsNullOrEmpty(report)) { Directory.CreateDirectory(report); File.WriteAllText(Path.Combine(report, "smoke-view.json"), JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true })); }
            return results;
        }
        finally { atlas.Dispose(); }

        IEnumerable<(float Yaw, float Pitch, float Fov)> Orientations() {
            foreach (float fov in new[] { 55f, 75f, 100f })
            foreach (var (y, p) in new[] { (0f, 0f), (.8f, 0f), (-.8f, 0f), (0f, .8f), (0f, -.8f), (.6f, .6f), (-.6f, -.6f), (.6f, -.6f), (-.6f, .6f) }) {
                // Fractions of the half field of view (vertical FOV; 16:9 horizontal).
                float half = MathUtils.DegToRad(fov * .5f), halfH = MathF.Atan(MathF.Tan(half) * 16f / 9f);
                yield return (y * halfH, p * half, fov);
            }
        }

        float Opacity(Puff[] sprites, Func<Puff, (Vector3 Right, Vector3 Up)> axesOf, Vector3 eye, Vector3 target, Vector3 forward, Func<Puff, float> weightOf = null) {
            Vector3 d = target - eye; float length = d.Length(); d /= length;
            float clear = 1;
            foreach (var s in sprites) {
                float weight = weightOf?.Invoke(s) ?? 1; if (weight <= 0) continue;
                Vector3 p = s.Position; var (r, u) = axesOf(s);
                // eye + t d = p + a r + b u  ->  [r u -d] (a b t) = eye - p
                if (!Solve(r, u, -d, eye - p, out float a, out float b, out float t)) continue;
                if (t <= 0 || t >= length || Math.Abs(a) > 1 || Math.Abs(b) > 1) continue;
                Vector3 q = eye + d * t;
                if (Vector3.Dot(q - eye, forward) < Near) continue; // in front of the near plane: clipped
                // 2026-10-01: the cloud's surface puffs use the generated smooth texture (its alpha is a product function).
                if (s.Texture == smoothKey) { clear *= 1 - smoothAlpha(s.Frame, (a + 1) * .5f, (1 - b) * .5f) * s.Alpha * weight; continue; }
                int frame = s.Frame; float x = frame % 4 * .25f + .004f, y = frame / 4 * .25f + .004f, span = .242f;
                float uu = x + (a + 1) * .5f * span, vv = y + (1 - b) * .5f * span;
                int tx = Math.Clamp((int)(uu * atlas.Width), 0, atlas.Width - 1), ty = Math.Clamp((int)(vv * atlas.Height), 0, atlas.Height - 1);
                clear *= 1 - atlas.Pixels[ty * atlas.Width + tx].A / 255f * s.Alpha * weight;
            }
            return 1 - clear;
        }
    }

    sealed record Puff(object Sprite, Vector3 Position, int Frame, float Alpha, int Texture);

    static Vector3 Turn(Vector3 direction, float yaw, float pitch) {
        float heading = MathF.Atan2(direction.X, -direction.Z) + yaw, elevation = Math.Clamp(MathF.Asin(Math.Clamp(direction.Y, -1, 1)) + pitch, -1.5f, 1.5f);
        return new Vector3(MathF.Sin(heading) * MathF.Cos(elevation), MathF.Sin(elevation), -MathF.Cos(heading) * MathF.Cos(elevation));
    }

    static bool Solve(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 rhs, out float x0, out float x1, out float x2) {
        float det = Vector3.Dot(c0, Vector3.Cross(c1, c2));
        x0 = x1 = x2 = 0;
        if (Math.Abs(det) < 1e-9f) return false;
        x0 = Vector3.Dot(rhs, Vector3.Cross(c1, c2)) / det;
        x1 = Vector3.Dot(c0, Vector3.Cross(rhs, c2)) / det;
        x2 = Vector3.Dot(c0, Vector3.Cross(c1, rhs)) / det;
        return true;
    }

    /// <summary>How far outside the dome's soft edge (radius + .35 m, heights scaled as ScSmokeVolume.Density does) the
    /// segment passes; negative when it enters.</summary>
    static float Miss(Vector3 a, Vector3 b, Vector3 center, float radius) {
        float best = float.MaxValue;
        for (int i = 0; i <= 200; i++) {
            Vector3 p = Vector3.Lerp(a, b, i / 200f), o = p - center;
            if (p.Y < -.12f) continue;
            o.Y = Math.Max(0, o.Y) * 3.75f / 2.8f;
            best = Math.Min(best, o.Length() - (radius + .35f));
        }
        return best;
    }
}
