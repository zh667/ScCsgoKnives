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

// "loadcheck": call the shipped C# loaders for every embedded CS2 asset and assert the
// values that actually arrive. A Python check of the JSON on disk cannot catch a
// serializer contract break - 0.16.4 shipped a cs2_effects.json whose scalar `lifetime`
// and snake_case keys made Cs2Effects throw on load, so every tracer and the whole CS2
// flash envelope were silently inactive while the Python selftest passed.
if (args.Length > 0 && args[0] == "loadcheck") {
    var checks = new List<object>();
    void Check(string name, bool ok, string detail) {
        checks.Add(new { name, ok, detail });
    }

    foreach (string gun in new[] { "ak47", "m4a1s", "awp" }) {
        Cs2Effects.Gun fx = Cs2Effects.Get(gun);
        Check($"effects/{gun}/loaded", fx is not null, fx is null ? "Cs2Effects.Get returned null" : "ok");
        if (fx is not null) {
            Check($"effects/{gun}/muzzle0", fx.MuzzlePos0 is { Length: >= 3 },
                  fx.MuzzlePos0 is null ? "null" : $"[{string.Join(',', fx.MuzzlePos0)}]");
            Cs2Effects.Flash flash = Cs2Effects.GetFlash(gun, false);
            Check($"effects/{gun}/flash", flash is not null, flash is null ? "no default flash" : "ok");
            if (flash is not null) {
                Check($"effects/{gun}/flash.seconds", flash.Seconds > 0f && flash.Seconds < 2f, $"{flash.Seconds:0.####} s");
                Check($"effects/{gun}/flash.frames", flash.SequenceFrames >= 1, $"{flash.SequenceFrames} frames");
                Check($"effects/{gun}/flash.alpha", flash.AlphaMid > 0f && flash.AlphaMid <= 1f, $"{flash.AlphaMid:0.####}");
            }
            Check($"effects/{gun}/tracer.freq", Cs2Effects.TracerFrequency(gun) >= 1,
                  $"{Cs2Effects.TracerFrequency(gun)}");
            Cs2Effects.Tracer tr = fx.Tracer;
            Check($"effects/{gun}/tracer.speed", tr is not null && tr.Speed is > 1000f,
                  tr?.Speed?.ToString("0.#") ?? "null");
            Check($"effects/{gun}/tracer.length", tr is not null && tr.MaxLength is > 100f,
                  tr?.MaxLength?.ToString("0.#") ?? "null");
            Check($"effects/{gun}/tracer.fadein", tr is not null && tr.LengthFadeIn is > 0f,
                  tr?.LengthFadeIn?.ToString("0.####") ?? "null");
        }

        KnifeTuning.Override("GunNumbers", 1f);
        Cs2Weapons.Gun wp = Cs2Weapons.Get(gun);
        Check($"weapons/{gun}/loaded", wp is not null, wp is null ? "null" : "ok");
        if (wp is not null) {
            Check($"weapons/{gun}/damage", wp.Damage > 0f, $"{wp.Damage:0.#}");
            Check($"weapons/{gun}/spread", wp.SpreadDegrees > 0f, $"{wp.SpreadDegrees:0.####} deg");
            Check($"weapons/{gun}/kick", wp.KickPitchDegrees > 0f, $"{wp.KickPitchDegrees:0.####} deg");
            Check($"weapons/{gun}/falloff", wp.RangeModifier is > 0f and < 1f, $"{wp.RangeModifier:0.###}");
            Check($"weapons/{gun}/maxspeed", wp.MaxSpeed is { Length: >= 1 }, wp.MaxSpeed is null ? "null" : $"{wp.MaxSpeed[0]:0.#}");
        }
        KnifeTuning.Override("GunNumbers", 0f);

        Check($"rig/{gun}/parts", Cs2Rig.GetMeshParts(gun).Count > 0, $"{Cs2Rig.GetMeshParts(gun).Count} parts");
        Check($"rig/{gun}/deploy", Cs2Rig.Duration(gun, "deploy") > 0f, $"{Cs2Rig.Duration(gun, "deploy"):0.####} s");
        Cs2Rig.Pose pose = Cs2Rig.Sample(gun, "idle", 0f);
        Check($"rig/{gun}/sample", pose is not null && pose.Bones.Count >= 60,
              pose is null ? "null" : $"{pose.Bones.Count} bones, {pose.Parts.Count} parts");
        // A binding whose matrices deserialised as null would come back as the identity
        // and put every part at the origin, which no bone-count check would notice.
        if (pose is not null) {
            int parts = Cs2Rig.GetMeshParts(gun).Count;
            Check($"rig/{gun}/bindings", pose.Parts.Count == parts,
                  $"{pose.Parts.Count} part matrices for {parts} parts");
            bool placed = Cs2Rig.GetMeshParts(gun).All(n => pose.GetPart(n) != Matrix.Identity);
            Check($"rig/{gun}/bindings.nonidentity", placed,
                  placed ? "all parts placed" : "a part matrix is the identity");
            float reloadSeconds = Cs2Rig.Duration(gun, "reload");
            Cs2Rig.Pose later = reloadSeconds > 0.2f ? Cs2Rig.Sample(gun, "reload", reloadSeconds * 0.5f) : null;
            bool moves = later is not null
                && Vector3.Distance(later.GetBoneOrigin("clip"), pose.GetBoneOrigin("clip")) > 0.05f;
            Check($"rig/{gun}/animates", moves,
                  later is null ? "no reload clip" :
                  $"magazine moves {Vector3.Distance(later.GetBoneOrigin("clip"), pose.GetBoneOrigin("clip")):0.##} in mid-reload");
        }
    }

    Check("sounds/clips", Cs2Sounds.ClipCount > 0, $"{Cs2Sounds.ClipCount} clips");
    Check("sounds/ak47:reload", Cs2Sounds.TryGet("ak47:reload", out var reload) && reload.Length >= 5,
          Cs2Sounds.TryGet("ak47:reload", out var r2) ? $"{r2.Length} cues" : "missing");
    Cs2SkinnedMesh arms = Cs2SkinnedMesh.Arms;
    Check("arms/loaded", arms is not null, arms is null ? "null" : $"{arms.Skinned.Length} vertices");
    Check("arms/primitives", arms is not null && arms.Primitives.Length == 2,
          arms is null ? "null" : $"{arms.Primitives.Length}");

    int bad = checks.Count(c => !(bool)c.GetType().GetProperty("ok").GetValue(c));
    Console.WriteLine(JsonSerializer.Serialize(new { failed = bad, checks }));
    return bad == 0 ? 0 : 1;
}

// "durations": what every timing consumer sees, per gun and clip alias, under both
// GunProfile values, plus a knife for the no-change check. This is the thing that
// regressed - the controller ran on CS:MC lengths while the CS2 rig was drawn - so it
// is asserted offline rather than looked at in game.
if (args.Length > 0 && args[0] == "durations") {
    var rows = new List<object>();
    foreach (string gun in new[] { "ak47", "m4a1s", "awp", "m9", "karambit" }) {
        int variant = Resolve(gun, "idle");
        if (variant < 0) continue;
        bool isGun = CsmcKnifeRig.IsGun(variant);
        foreach (string alias in new[] { "idle", "deploy", "reload", "inspect", "shoot1",
                                         "shootSilenced", "attach", "detach" }) {
            if (!CsmcKnifeRig.HasClip(variant, alias)) continue;
            KnifeTuning.Override("GunProfile", 0f);
            float csmc = CsmcKnifeRig.GetDuration(variant, alias);
            float off = CsmcKnifeRig.GetProfileDuration(variant, alias);
            KnifeTuning.Override("GunProfile", 1f);
            float on = CsmcKnifeRig.GetProfileDuration(variant, alias);
            float cs2 = isGun ? Cs2Rig.Duration(gun, alias) : 0f;
            KnifeTuning.Override("GunProfile", 0f);
            rows.Add(new { gun, alias, isGun, csmc, cs2, profileOff = off, profileOn = on });
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(rows));
    return 0;
}

// "cs2arms <gun> <clip> <t> [out.bin]": the CPU-skinned arm vertices for one pose,
// in engine view space. Without an output path it prints a digest; with one it dumps
// the raw float3 positions so tools/cs2_arms_selftest.py can diff every vertex.
if (args.Length > 0 && args[0] == "cs2arms") {
    string gun = args.Length > 1 ? args[1] : "ak47";
    string clip = args.Length > 2 ? args[2] : "idle";
    float t = args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0f;
    string outPath = args.Length > 4 ? args[4] : null;
    Cs2Rig.Pose pose = Cs2Rig.Sample(gun, clip, t);
    Cs2SkinnedMesh mesh = Cs2SkinnedMesh.Arms;
    if (pose is null || mesh is null) { Console.Error.WriteLine("no CS2 pose or arm mesh"); return 2; }
    if (!mesh.SetPose(pose, Cs2Placement.Placement())) { Console.Error.WriteLine("no joints resolved"); return 2; }
    mesh.Skin();
    var v = mesh.Skinned;
    Vector3 lo = v[0].Position, hi = v[0].Position;
    foreach (var x in v) { lo = Vector3.Min(lo, x.Position); hi = Vector3.Max(hi, x.Position); }
    if (outPath is not null) {
        using var bw = new BinaryWriter(File.Create(outPath));
        bw.Write(v.Length);
        foreach (var x in v) { bw.Write(x.Position.X); bw.Write(x.Position.Y); bw.Write(x.Position.Z); }
        Console.Error.WriteLine($"wrote {v.Length} skinned vertices -> {outPath}");
    }
    Console.WriteLine(JsonSerializer.Serialize(new {
        gun, clip = pose.Clip, t = pose.Time, vertices = v.Length,
        min = new[] { lo.X, lo.Y, lo.Z }, max = new[] { hi.X, hi.Y, hi.Z },
        primitives = mesh.Primitives.Select(p => new { p.Material, tris = p.Indices.Length / 3 }),
    }));
    return 0;
}

// "cs2sweep <gun> <clip> <fps> <out.json>": the cs2 profile's part matrices in the
// same shape ArmPreview's knife sweep writes, so tools/pbr_emulate.py can render the
// CS2 profile offline and tools/cs2_videocheck.py can overlay it on a CS2 capture.
if (args.Length > 0 && args[0] == "cs2sweep") {
    string gun = args.Length > 1 ? args[1] : "ak47";
    string clip = args.Length > 2 ? args[2] : "idle";
    int fps = args.Length > 3 ? int.Parse(args[3]) : 30;
    string outPath = args.Length > 4 ? args[4] : null;
    int W = args.Length > 6 ? int.Parse(args[5]) : 1920;
    int H = args.Length > 6 ? int.Parse(args[6]) : 1080;
    if (outPath is null) { Console.Error.WriteLine("usage: ArmPreview cs2sweep <gun> <clip> <fps> <out.json> [W H]"); return 2; }
    float fovY = Cs2Placement.FovYDegrees(KnifeTuning.Cs2ViewmodelFov);
    float fy = 1f / MathF.Tan(MathUtils.DegToRad(fovY) * 0.5f);
    float fx = fy / ((float)W / H);
    Matrix place = Cs2Placement.Placement();
    float duration = Cs2Rig.Duration(gun, clip);
    int count = duration > 0f ? (int)MathF.Round(duration * fps) : 0;
    var frames = new List<object>();
    for (int i = 0; i <= count; i++) {
        float t = duration > 0f ? MathF.Min(i / (float)fps, duration) : 0f;
        Cs2Rig.Pose pose = Cs2Rig.Sample(gun, clip, t);
        if (pose is null) { Console.Error.WriteLine($"no CS2 pose for {gun}/{clip}"); return 2; }
        var parts = new Dictionary<string, float[]>();
        foreach (string name in Cs2Rig.GetMeshParts(gun)) {
            Matrix m = pose.GetPart(name) * place;
            parts[name] = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                           m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
        }
        frames.Add(new { t, clip = pose.Clip, parts });
    }
    File.WriteAllText(outPath, JsonSerializer.Serialize(new {
        gun, clip, fps, fovY, weaponProjX = fx, weaponProjY = fy, frames }));
    Console.Error.WriteLine($"wrote {frames.Count} cs2 frames -> {outPath}");
    return 0;
}

// "cs2 <gun> <clip> <t> [W H]": the cs2 profile's own landmarks, straight out of
// Cs2Rig and Cs2Placement, so tools/cs2_placement.py can be diffed against the
// shipped C# the same way rigprobe.py is diffed against CsmcKnifeRig.
if (args.Length > 0 && args[0] == "cs2") {
    string gun = args.Length > 1 ? args[1] : "ak47";
    string clip = args.Length > 2 ? args[2] : "idle";
    float t = args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0f;
    int W = args.Length > 5 ? int.Parse(args[4]) : 1920;
    int H = args.Length > 5 ? int.Parse(args[5]) : 1080;
    Cs2Rig.Pose pose = Cs2Rig.Sample(gun, clip, t);
    if (pose is null) { Console.Error.WriteLine($"no CS2 pose for {gun}/{clip}"); return 2; }
    Matrix place = Cs2Placement.Placement();
    float fovY = Cs2Placement.FovYDegrees(KnifeTuning.Cs2ViewmodelFov);
    float fy = 1f / MathF.Tan(MathUtils.DegToRad(fovY) * 0.5f);
    float fx = fy / ((float)W / H);
    var lm = new Dictionary<string, object>();
    // Every bone a manifest might name. These are diagnostics: a bone origin sits
    // inside the model and cannot be pointed at on a screenshot, so the acceptance
    // gate is the silhouette (cs2_reference_check PLACE/HAND). A landmark listed in a
    // manifest that is absent here makes the item fail rather than being skipped.
    foreach (string bone in new[] {
                 "muzzle", "muzzle2", "wpnEnd", "wpnTip", "wpn", "weapon", "weapon_offset",
                 "bolt", "bolt_action", "rail", "clip", "cliprelease", "trigger", "silencer",
                 "hand_R", "hand_L", "arm_lower_R", "arm_lower_L",
                 "finger_index_0_R", "finger_index_1_R", "finger_index_2_R",
                 "finger_index_0_L", "finger_index_1_L", "finger_index_2_L",
                 "finger_thumb_1_R", "finger_thumb_2_R", "finger_thumb_1_L", "finger_thumb_2_L",
                 "finger_middle_1_L", "finger_middle_2_L", "finger_pinky_1_L",
             }) {
        if (!pose.HasBone(bone)) continue;
        Vector3 v = Vector3.Transform(pose.GetBoneOrigin(bone), place);
        float z = -v.Z;
        lm[bone] = new {
            view = new[] { v.X, v.Y, v.Z },
            screen = new[] { (0.5f + 0.5f * v.X * fx / z) * W, (0.5f - 0.5f * v.Y * fy / z) * H },
            depth = z,
        };
    }
    Console.WriteLine(JsonSerializer.Serialize(new { gun, clip = pose.Clip, t = pose.Time, fovY, lm }));
    return 0;
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
