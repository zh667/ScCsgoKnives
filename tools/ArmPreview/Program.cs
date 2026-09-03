// ArmPreview <knife> <clip> <fps> <out.json> [fx fy]
//   Runs the mod's own arm maths headless (CsmcFirstPersonRenderer.SweepHeadless)
//   and writes one JSON document: every frame's solved arms and mesh-part
//   matrices. tools/preview.py draws it. Nothing here re-implements the renderer.
//
// ArmPreview trace <knife> <clip> <fps> <out.jsonl>
//   The M9 offline matrix replay harness. Plays the clip on a virtual clock and
//   writes, per frame, every CSMC bone's world matrix (KnifeRigPose.Bones --
//   InverseNormalization * absolute * Normalization, the attachment frame for
//   external geometry) plus every mesh part's binding matrix. This is CSMC's own
//   animation -> skeleton stage, reconstructed offline from the .animbin we hold,
//   through the validated CsmcKnifeRig sampler (clip durations verified identical
//   to the binary). tools/trace.py decomposes it to t/q/s and checks it frame to
//   frame. Only the weapon rig (hand_r, weapon_hand_r, arm_lower_r, fingers,
//   root) is reconstructible from our data; LeftArm/RightArm are a separate arm
//   animatable not in the weapon animbin -- our SC port synthesises the arm and
//   does not need them.
using System.Text.Json;
using Engine;
using Game;

static float[] M(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
static float[] V(Vector3 v) => [v.X, v.Y, v.Z];

static int Resolve(string knife, string clip) {
    int variant = -1;
    for (int v = 0; v < CsmcKnifeRig.AssetCount; v++) if (CsmcKnifeRig.GetAssetName(v) == knife) variant = v;
    if (variant < 0) { Console.Error.WriteLine($"unknown knife '{knife}'"); return -1; }
    if (!CsmcKnifeRig.HasClip(variant, clip)) { Console.Error.WriteLine($"{knife} has no clip '{clip}'"); return -2; }
    return variant;
}

KnifeLog.ToConsole = true;   // before any renderer or rig access, so no log ever reaches stdout

// key=value arguments override KnifeTuning defaults (e.g. ArmRollMode=2 SquareAtHold=0),
// so alternative settings can be compared offline without touching the tuning file.
{
    var overrides = args.Where(a => a.Contains('=')).ToArray();
    args = args.Where(a => !a.Contains('=')).ToArray();
    foreach (string o in overrides) {
        string[] kv = o.Split('=', 2);
        if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !KnifeTuning.Override(kv[0], v)) {
            Console.Error.WriteLine($"unknown tunable override '{o}'"); return 2;
        }
    }
}

if (args.Length > 0 && args[0] == "trace") {
    string knife = args.Length > 1 ? args[1] : "m9";
    string clip = args.Length > 2 ? args[2] : "inspect";
    int fps = args.Length > 3 ? int.Parse(args[3]) : 30;
    string outPath = args.Length > 4 ? args[4] : null;
    if (outPath is null) { Console.Error.WriteLine("usage: ArmPreview trace <knife> <clip> <fps> <out.jsonl>"); return 2; }
    int variant = Resolve(knife, clip);
    if (variant < 0) return 2;
    float duration = CsmcKnifeRig.GetDuration(variant, clip);
    int count = (int)MathF.Round(duration * fps);
    using StreamWriter w = new(outPath, false);
    for (int i = 0; i <= count; i++) {
        float t = MathF.Min(i / (float)fps, duration);
        KnifeRigPose pose = CsmcKnifeRig.Sample(variant, clip, t);
        var line = new {
            frame = i,
            t,
            clip = pose.SourceClip,
            alias = pose.ClipAlias,
            bones = pose.Bones.ToDictionary(b => b.Key, b => M(b.Value)),
            attachments = pose.Attachments.ToDictionary(a => a.Key, a => V(a.Value)),
            parts = pose.Bindings.ToDictionary(p => p.Key, p => M(p.Value)),
        };
        w.WriteLine(JsonSerializer.Serialize(line));
    }
    Console.Error.WriteLine($"wrote {count + 1} frames ({duration:0.###}s @ {fps} fps) -> {outPath}");
    return 0;
}

// golden: emit, per sample per binding, the RAW attachment sourcePose (stage A, matches the
// round-5 controlled-sample JSONL) and the normalized binding (stage B). Uses production
// CsmcKnifeRig math (SampleRawBindings / Sample().Bindings). tools/golden_m9.py compares.
if (args.Length > 0 && args[0] == "golden") {
    string knife = args.Length > 1 ? args[1] : "m9";
    string clip = args.Length > 2 ? args[2] : "inspect";
    int fps = args.Length > 3 ? int.Parse(args[3]) : 30;
    string outPath = args.Length > 4 ? args[4] : null;
    if (outPath is null) { Console.Error.WriteLine("usage: ArmPreview golden <knife> <clip> <fps> <out.jsonl>"); return 2; }
    int variant = Resolve(knife, clip);
    if (variant < 0) return 2;
    float duration = CsmcKnifeRig.GetDuration(variant, clip);
    int count = (int)MathF.Round(duration * fps);
    using StreamWriter w = new(outPath, false);
    for (int i = 0; i <= count; i++) {
        float t = MathF.Min(i / (float)fps, duration);
        var raw = CsmcKnifeRig.SampleRawBindings(variant, clip, t);
        var norm = CsmcKnifeRig.Sample(variant, clip, t).Bindings;
        var line = new {
            sampleIndex = i,
            timeSeconds = t,
            clip,
            rawBindings = raw.ToDictionary(b => b.Key, b => M(b.Value)),
            normBindings = norm.ToDictionary(b => b.Key, b => M(b.Value)),
        };
        w.WriteLine(JsonSerializer.Serialize(line));
    }
    Console.Error.WriteLine($"wrote {count + 1} golden samples ({duration:0.###}s @ {fps} fps) -> {outPath}");
    return 0;
}

{
    string knife = args.Length > 0 ? args[0] : "m9";
    string clip = args.Length > 1 ? args[1] : "inspect";
    int fps = args.Length > 2 ? int.Parse(args[2]) : 30;
    string outPath = args.Length > 3 ? args[3] : null;
    float fx = args.Length > 5 ? float.Parse(args[4]) : 0f;
    float fy = args.Length > 5 ? float.Parse(args[5]) : 0f;
    if (outPath is null) { Console.Error.WriteLine("usage: ArmPreview <knife> <clip> <fps> <out.json> [fx fy]  |  ArmPreview trace <knife> <clip> <fps> <out.jsonl>"); return 2; }
    int variant = Resolve(knife, clip);
    if (variant < 0) return 2;

    CsmcFirstPersonRenderer.InitHeadless(fx, fy);
    var frames = CsmcFirstPersonRenderer.SweepHeadless(variant, clip, fps);

    static object S(CsmcFirstPersonRenderer.QaSample s) => s.Valid ? new {
        grip = V(s.Grip), elbow = V(s.Elbow), seat = V(s.Seat), axis = V(s.Axis), side = V(s.Side), up = V(s.Up),
        width = s.Width, reach = s.Reach, overshoot = s.Overshoot, clearance = s.Clearance,
        rigidDeg = s.RigidDeg, resolvedDeg = s.ResolvedDeg, stillness = s.Stillness, holdDeg = s.HoldDeg,
        weaponHand = M(s.WeaponHand), handR = M(s.HandR),
    } : null;

    var doc = new {
        knife, clip, fps,
        projX = CsmcFirstPersonRenderer.ProjX, projY = CsmcFirstPersonRenderer.ProjY,
        weaponProjX = CsmcFirstPersonRenderer.WeaponProjX, weaponProjY = CsmcFirstPersonRenderer.WeaponProjY,
        frames = frames.Select(f => new {
            t = f.T, clip = f.Clip, right = S(f.Right), left = S(f.Left),
            parts = f.Parts.ToDictionary(p => p.Key, p => M(p.Value)),
        }).ToList(),
    };
    File.WriteAllText(outPath, JsonSerializer.Serialize(doc));
    Console.Error.WriteLine($"wrote {frames.Count} frames -> {outPath}");
    return 0;
}
