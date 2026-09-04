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
    // The assertions live in the mod (Cs2SelfTest), so tools/PackageCheck can run the
    // identical code against the DLL inside a packaged .scmod.
    string json = Cs2SelfTest.RunJson();
    Console.WriteLine(json);
    return JsonDocument.Parse(json).RootElement.GetProperty("failed").GetInt32() == 0 ? 0 : 1;
}

// "tracer <gun> [worldFovY] [width] [height]": everything the CS2 tracer ribbon is
// made of, straight out of the shipped code. The renderer calls the same Cs2Tracer
// helpers, so what this prints is what gets drawn: the muzzle's pixel under the
// viewmodel projection and the reprojected world start's pixel under the game
// camera's (the fix for the tracer leaving the eye instead of the barrel), the
// per-pass half-width in pixels against depth, the trail length against age, and the
// alpha envelope against the fraction of the shot line flown.
if (args.Length > 0 && args[0] == "tracer") {
    string gun = args.Length > 1 ? args[1] : "ak47";
    float worldFovY = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 60f;
    int width = args.Length > 3 ? int.Parse(args[3]) : 1400;
    int height = args.Length > 4 ? int.Parse(args[4]) : 1050;
    float aspect = width / (float)height;

    Cs2Effects.Gun fx = Cs2Effects.Get(gun);
    Cs2Effects.Tracer spec = fx?.Tracer;
    if (spec is null) { Console.Error.WriteLine($"no CS2 tracer for {gun}"); return 2; }

    Matrix world = Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(worldFovY), aspect, 0.1f, 1024f);
    Matrix viewmodel = Matrix.CreatePerspectiveFieldOfView(
        MathUtils.DegToRad(Cs2Placement.FovYDegrees(KnifeTuning.Cs2ViewmodelFov)), aspect, 0.02f, 64f);
    Matrix root = Cs2Placement.Placement();

    var muzzles = new List<object>();
    foreach (bool silenced in new[] { false, true }) {
        if (silenced && fx.MuzzlePos1 is null) continue;
        Vector3 view = CsmcFirstPersonRenderer.MuzzleViewPoint(gun, silenced, root);
        Vector2 drawn = Cs2Tracer.ToPixels(view, viewmodel.M11, viewmodel.M22, width, height);
        Vector3 reprojected = Cs2Tracer.ReprojectView(view, viewmodel.M11 / world.M11);
        Vector2 tracerPixel = Cs2Tracer.ToPixels(reprojected, world.M11, world.M22, width, height);
        Vector2 naive = Cs2Tracer.ToPixels(Vector3.Zero + new Vector3(0f, 0f, -0.001f), world.M11, world.M22, width, height);
        muzzles.Add(new {
            silenced,
            viewSpace = new[] { view.X, view.Y, view.Z },
            drawnPixel = new[] { drawn.X, drawn.Y },
            tracerPixel = new[] { tracerPixel.X, tracerPixel.Y },
            errorPixels = Vector2.Distance(drawn, tracerPixel),
            eyeOriginPixel = new[] { naive.X, naive.Y },
            eyeOriginErrorPixels = Vector2.Distance(drawn, naive),
        });
    }

    var passes = new List<object>();
    foreach (Cs2Effects.TracerPass pass in spec.Passes ?? []) {
        var widths = new List<object>();
        foreach (float depth in new[] { 0.5f, 1f, 2f, 5f, 10f, 20f, 40f, 80f }) {
            float half = Cs2Tracer.HalfWidth(spec, pass, depth, world.M22, out float fade);
            float fraction = Cs2Tracer.HalfWidthScreenFraction(spec, pass, depth, world.M22, out _);
            widths.Add(new { depth, halfMetres = half, halfPixels = fraction * height, sizeFade = fade,
                             unclampedPixels = spec.HalfWidthMetres(pass) / Cs2Tracer.MetresPerScreenHeight(depth, world.M22) * height });
        }
        var lengths = new List<object>();
        foreach (float age in new[] { 0.005f, 0.01f, 0.02f, 0.04f, 0.08f, 0.16f })
            lengths.Add(new { age, trailMetres = Cs2Tracer.TrailMetres(spec, pass, age, 10f) });
        passes.Add(new {
            pass.Texture, pass.SourceTexture, pass.Blend, pass.RadiusScale, pass.LengthFadeIn,
            pass.MinSize, pass.MaxSize, pass.StartFadeSize, pass.EndFadeSize, pass.Additive,
            halfWidthMetres = spec.HalfWidthMetres(pass), widths, lengths,
        });
    }

    var envelope = new List<object>();
    for (int k = 0; k <= 100; k++) {
        float u = k / 100f;
        envelope.Add(new { u, alpha = spec.PathAlpha(u) * spec.AlphaMid });
    }

    // What a 60 fps recording would actually catch: CS2 kills the particle at the
    // impact point, so a near shot is over in a frame or two. Reported, not gated -
    // it is the number to hold a real capture against.
    var flights = new List<object>();
    foreach (float metres in new[] { 5f, 10f, 20f, 40f, 80f }) {
        float life = metres / spec.MetresPerSecond;
        var frames = new List<object>();
        for (int f = 0; f * (1f / 60f) < life; f++) {
            float age = f / 60f;
            float head = MathUtils.Min(age * spec.MetresPerSecond, metres);
            Cs2Effects.TracerPass first = (spec.Passes ?? [])[0];
            float trail = Cs2Tracer.TrailMetres(spec, first, age, MathUtils.Max(head, 0.01f));
            frames.Add(new { frame = f, age, head, tail = MathUtils.Max(0f, head - trail),
                             alpha = spec.PathAlpha(head / metres) * spec.AlphaMid });
        }
        flights.Add(new { metres, lifeSeconds = life, framesAt60 = frames.Count, frames });
    }

    Console.WriteLine(JsonSerializer.Serialize(new {
        gun, worldFovY, viewmodelFov = KnifeTuning.Cs2ViewmodelFov,
        viewmodelFovY = Cs2Placement.FovYDegrees(KnifeTuning.Cs2ViewmodelFov),
        width, height, frequency = Cs2Effects.TracerFrequency(gun),
        spec.Source, spec.Speed, spec.MaxLength, spec.Radius, spec.TrailSeconds, spec.Alpha,
        spec.ColorMin, spec.ColorMax, spec.ColorFromTexture, spec.Unmodelled,
        metresPerSecond = spec.MetresPerSecond, trailMetresCap = spec.TrailMetres,
        lengthScale = new { at1m = spec.LengthScale(1f), at10m = spec.LengthScale(10f), at100m = spec.LengthScale(100f) },
        muzzles, passes, envelope, flights,
    }));
    return 0;
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
