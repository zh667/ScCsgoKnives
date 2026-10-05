using System.IO.Compression;
using System.Reflection;
using Engine;

/// <summary>Round 9 (docs/tasks/round9-tp-aim-cs2-tracer-hud-20261001.md): the CS2 sniper "wisp" — the line an AWP, SSG 08,
/// G3SG1 or SCAR-20 shot leaves in the air — and the tracer textures it and the streak's colour lookup need. The numbers
/// checked are the .vpcf's (Cs2Wisp); the look is the user's acceptance.</summary>
static class Cs2WispRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("cs2-wisp/" + name, ok, detail));
        var wisp = mod.GetType("Game.Cs2Wisp", false);
        if (wisp is null) { Check("the core draws the CS2 sniper wisp (Game.Cs2Wisp)", false, "type missing"); return results; }
        object Call(string name, params object[] args) => wisp.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
        object Spec(string name) => wisp.GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
        string SystemOf(object spec) => (string)spec?.GetType().GetProperty("System").GetValue(spec);

        var expected = new Dictionary<string, string> { ["awp"] = "weapon_tracers_rifle_wisp", ["ssg08"] = "weapon_tracers_rifle_wisp_ssg",
            ["g3sg1"] = "weapon_tracers_rifle_wisp_ssg", ["scar20"] = "weapon_tracers_rifle_wisp_scar" };
        foreach (var gun in new[] { "awp", "ssg08", "g3sg1", "scar20", "ak47", "m4a1s", "m4a4", "deagle", "nova", "negev", "mp9", "taser" }) {
            string got = SystemOf(Call("For", gun));
            Check($"{gun}: {(expected.TryGetValue(gun, out var want) ? want : "no wisp")}", got == (expected.TryGetValue(gun, out want) ? want : null), got ?? "none");
        }
        int Points(string spec, float metres) => (int)Call("PointCount", Spec(spec), metres);
        Check("point count follows the shot length (8 short, 36 at 2000 in for the AWP and SSG, 30 for the SCAR)",
            Points("Awp", 3f) == 8 && Points("Awp", 2000 * .0254f) == 36 && Points("Ssg", 2000 * .0254f) == 36 && Points("Scar", 2000 * .0254f) == 30 && Points("Awp", 25f) is > 8 and < 36,
            $"{Points("Awp", 3f)} {Points("Awp", 25f)} {Points("Awp", 50.8f)} {Points("Scar", 50.8f)}");
        float R(float t) => (float)Call("RadiusScale", t);
        Check("radius ×3 → ×0.5 at 7.5 % of life → ×5 at the end (AWP 9 in → 1.5 in → 15 in)", MathF.Abs(R(0) - 3) < 1e-3f && MathF.Abs(R(.075f) - .5f) < 1e-3f && MathF.Abs(R(1) - 5) < 1e-3f,
            $"{R(0)} {R(.075f)} {R(.5f)} {R(1)}");
        float Life(string spec, float t) => (float)Call("Curve", Spec(spec).GetType().GetProperty("LifeAlpha").GetValue(Spec(spec)), t);
        Check("alpha over life rises fast and is gone at the end (AWP, SSG, SCAR)", Life("Awp", .04f) > .8f && Life("Awp", 1) == 0 && Life("Ssg", .045f) > .95f && Life("Scar", .05f) > .9f && Life("Scar", .27f) < .3f);
        Check("glows at first, unlit smoke from 50 % of life", (float)Call("Glow", 0f) == 2 && (float)Call("Glow", .5f) == 0 && (float)Call("Glow", .31f) is > .8f and < 1f);
        Check("the line fades in from both ends", (float)Call("Taper", 0f, 20) == 0 && (float)Call("Taper", 19f, 20) == 0 && (float)Call("Taper", 10f, 20) > .9f);
        // A long sky shot: 36 points 7 m apart are drawn as pieces of at most 0.75 m, starting and ending on the line's ends.
        var line = Enumerable.Range(0, 36).Select(i => new Vector3(0, 70, -7f * i)).ToArray();
        var joints = new Vector3[512]; var index = new float[512];
        int count = (int)Call("Joints", line, .75f, joints, index);
        float longest = Enumerable.Range(1, count - 1).Max(i => Vector3.Distance(joints[i - 1], joints[i]));
        Check("the drawn rope is cut into pieces of about 0.75 m (under 0.9 m; the curve runs unevenly next to its ends) between its points, end to end",
            count > 300 && longest < .9f && Vector3.Distance(joints[0], line[0]) < 1e-4f && Vector3.Distance(joints[count - 1], line[^1]) < 1e-4f && index[count - 1] == 35,
            $"{count} joints, longest piece {longest:0.000} m");

        // A 30 m AWP line, simulated for its whole life at 60 Hz: dies at 2 s, never blows up, drifts more in the middle than
        // near its ends (DampenToCP).
        var trailType = mod.GetType("Game.Cs2WispTrail", true);
        var random = Activator.CreateInstance(typeof(Game.Random), [1234]);
        Vector3 start = new(0, 70, 0), end = new(0, 70, -30);
        var trail = Activator.CreateInstance(trailType, [Spec("Awp"), start, end, random]);
        var points = (Vector3[])trailType.GetField("Points").GetValue(trail);
        var initial = (Vector3[])points.Clone();
        var update = trailType.GetMethod("Update");
        int steps = 0; while (!(bool)trailType.GetProperty("Dead").GetValue(trail) && steps < 1000) { update.Invoke(trail, [1 / 60f]); steps++; }
        float Moved(int i) => Vector3.Distance(points[i], initial[i]);
        float max = Enumerable.Range(0, points.Length).Max(Moved);
        bool finite = points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
        Check("a 30 m line lives 2 s, stays finite, drifts less than 2 m and more in the middle than at its ends",
            steps is >= 119 and <= 121 && finite && max < 2 && Moved(points.Length / 2) > Moved(0) && Moved(points.Length / 2) > Moved(points.Length - 1),
            $"{steps} steps, {points.Length} points, max {max:0.000} m, ends {Moved(0):0.000}/{Moved(points.Length - 1):0.000}, middle {Moved(points.Length / 2):0.000}");

        // The textures in this package (Full PNG or Lite WebP).
        using var zip = ZipFile.OpenRead(package);
        foreach (var name in new[] { "cs2_tracer_add_lut", "cs2_tracer_blend_lut", "cs2_wisp_energy", "cs2_wisp_smoke" }) {
            var entry = zip.GetEntry($"Assets/Textures/ScCsgoKnives/{name}.png") ?? zip.GetEntry($"Assets/Textures/ScCsgoKnives/{name}.webp");
            Check($"package carries {name}", entry is not null && entry.Length > 100, entry?.FullName ?? "missing");
        }
        return results;
    }
}
