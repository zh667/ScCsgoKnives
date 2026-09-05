using System.IO;
using System.Reflection;
using System.Text.Json;
using Engine;

namespace Game;

/// <summary>
/// The CS2 acceptance run, inside the shipped assembly.
///
/// It lives here rather than in a tool so that the same assertions can be run against
/// the DLL inside a packaged .scmod, which is the artifact that actually reaches the
/// device. tools/PackageCheck opens the package, verifies its SHA-256, loads this
/// assembly out of it and calls RunJson by reflection; tools/ArmPreview calls it
/// directly against the working tree. A tool that re-implemented these checks would
/// be testing itself.
///
/// Why it asserts loader status and not only values: 0.16.5 shipped a cs2_effects.json
/// that deserialised correctly and still logged
/// "Could not read AnimationData.cs2_effects.json: Object reference not set to an
/// instance of an object", because the success log called TracerFrequency while the
/// dictionary it reads was still being assigned. Every value check passed. Each loader
/// now publishes LoadError and this fails on any of them.
/// </summary>
public static class Cs2SelfTest {
    static readonly string[] Guns = ["ak47", "m4a1s", "awp"];

    /// <summary>Every alias KnifeAnimationController can pick for a knife.</summary>
    static readonly string[] ControllerAliases =
        ["deploy", "deploy2", "idle", "idle2", "inspect", "inspect2", "inspect3", "slash1", "slash2"];

    /// <summary>
    /// The knives whose CS:MC rig offers a second draw or a second/third inspect.
    /// Named rather than counted so the check cannot pass by the table going empty:
    /// 0.17.0 would have passed a "no knife is missing an alias" test, because the
    /// aliases were not in the CS2 files for anything to be missing from.
    /// </summary>
    static readonly (string Alias, string[] Knives)[] RequiredVariants = [
        ("deploy2", ["butterfly", "canis", "cord", "kukri", "outdoor", "skeleton", "ursus"]),
        ("inspect2", ["butterfly", "canis", "cord", "css", "falchion", "outdoor",
                      "skeleton", "stiletto", "talon", "ursus"]),
        ("inspect3", ["butterfly"]),
    ];

    sealed class Check {
        public string Name { get; set; }
        public bool Ok { get; set; }
        public string Detail { get; set; }
    }

    public static string RunJson() {
        var checks = new List<Check>();
        void Check(string name, bool ok, string detail) =>
            checks.Add(new Check { Name = name, Ok = ok, Detail = detail });

        // Loader status first: a loader can produce usable values and still have failed.
        Check("load/effects", Cs2Effects.LoadError is null, Cs2Effects.LoadError ?? "ok");
        Check("load/weapons", Cs2Weapons.LoadError is null, Cs2Weapons.LoadError ?? "ok");
        Check("load/sounds", Cs2Sounds.LoadError is null, Cs2Sounds.LoadError ?? "ok");

        foreach (string gun in Guns) {
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
                if (tr is not null) {
                    // The trail is drawn twice, with the CS2 textures. A single pass, or
                    // one with no baked texture, means the ribbon falls back to nothing.
                    Check($"effects/{gun}/tracer.passes", tr.Passes is { Length: 2 },
                          $"{tr.Passes?.Length ?? 0} passes");
                    bool textured = tr.Passes is { Length: > 0 }
                        && tr.Passes.All(p => !string.IsNullOrEmpty(p.Texture) && !string.IsNullOrEmpty(p.SourceTexture));
                    Check($"effects/{gun}/tracer.textures", textured,
                          tr.Passes is null ? "no passes"
                          : string.Join(", ", tr.Passes.Select(p => $"{p.Texture ?? "none"}<-{p.SourceTexture ?? "none"}")));
                    bool clamped = tr.Passes is { Length: > 0 }
                        && tr.Passes.All(p => p.MinSize > 0f && p.MaxSize > p.MinSize);
                    Check($"effects/{gun}/tracer.sizeclamp", clamped,
                          tr.Passes is null ? "no passes"
                          : string.Join(", ", tr.Passes.Select(p => $"{p.MinSize:0.#####}..{p.MaxSize:0.#####}")));
                    bool radius = tr.Passes is { Length: > 0 }
                        && tr.Passes.All(p => tr.HalfWidthMetres(p) is > 0.001f and < 0.05f);
                    Check($"effects/{gun}/tracer.radius", radius,
                          tr.Passes is null ? "no passes"
                          : string.Join(", ", tr.Passes.Select(p => $"{tr.HalfWidthMetres(p) * 1000f:0.##} mm")));
                    Check($"effects/{gun}/tracer.trailseconds", tr.TrailSecondsMid is > 0.01f and < 1f,
                          $"{tr.TrailSecondsMid:0.####} s");
                    // The fade envelope must actually be an envelope: dark at the muzzle,
                    // full in the middle, going out at the end.
                    bool envelope = tr.PathAlpha(0.1f) < 0.01f && tr.PathAlpha(0.5f) > 0.99f
                                    && tr.PathAlpha(0.99f) < 0.99f;
                    Check($"effects/{gun}/tracer.envelope", envelope,
                          $"{tr.PathAlpha(0.1f):0.###} / {tr.PathAlpha(0.5f):0.###} / {tr.PathAlpha(0.99f):0.###} at u=0.1/0.5/0.99");
                    Check($"effects/{gun}/tracer.alpha", tr.AlphaMid is > 0.1f and <= 1f, $"{tr.AlphaMid:0.####}");
                    Check($"effects/{gun}/tracer.lengthscale", tr.LengthScale(100f) >= tr.LengthScale(1f),
                          $"x{tr.LengthScale(1f):0.###} at 1 m, x{tr.LengthScale(100f):0.###} at 100 m");
                    // 0.16.5 fell back to flat white whenever ColorMin/Max were missing.
                    // They are never missing now: white here is CS2's own white, and the
                    // generator says so, because the spark texture carries the colour.
                    Check($"effects/{gun}/tracer.colour",
                          tr.ColorMin is { Length: >= 3 } && tr.ColorMax is { Length: >= 3 },
                          tr.ColorMin is null ? "no ColorMin"
                          : $"[{string.Join(',', tr.ColorMin)}]..[{string.Join(',', tr.ColorMax)}]"
                            + (tr.ColorFromTexture ? " (from the texture)" : ""));
                }
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

            // The tracer must leave the barrel the player sees, not the eye. Both
            // projections take the same view space, so this is exact by construction -
            // it is checked anyway, because getting the ratio upside down would put the
            // origin further from the muzzle than doing nothing at all.
            Matrix root = Cs2Placement.Placement();
            const int Width = 1400, Height = 1050;
            float aspect = Width / (float)Height;
            Matrix worldProjection = Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(60f), aspect, 0.1f, 1024f);
            Matrix viewProjection = Matrix.CreatePerspectiveFieldOfView(
                MathUtils.DegToRad(Cs2Placement.FovYDegrees(KnifeTuning.Cs2ViewmodelFov)), aspect, 0.02f, 64f);
            Vector3 muzzleView = CsmcFirstPersonRenderer.MuzzleViewPoint(gun, false, root);
            Vector2 drawn = Cs2Tracer.ToPixels(muzzleView, viewProjection.M11, viewProjection.M22, Width, Height);
            Vector2 started = Cs2Tracer.ToPixels(
                Cs2Tracer.ReprojectView(muzzleView, viewProjection.M11 / worldProjection.M11),
                worldProjection.M11, worldProjection.M22, Width, Height);
            float error = Vector2.Distance(drawn, started);
            Vector2 eye = Cs2Tracer.ToPixels(new Vector3(0f, 0f, -0.001f), worldProjection.M11, worldProjection.M22, Width, Height);
            Check($"tracer/{gun}/origin", error <= 3f,
                  $"{error:0.###} px from the drawn muzzle at 1400x1050 (the eye ray's origin is {Vector2.Distance(drawn, eye):0.#} px away)");

            // Screen-space clamping is the thing that stops the near field being a plank.
            Cs2Effects.Tracer spec = fx?.Tracer;
            if (spec?.Passes is { Length: > 0 }) {
                float worstNear = 0f, worstFar = float.MaxValue;
                foreach (Cs2Effects.TracerPass pass in spec.Passes) {
                    worstNear = MathF.Max(worstNear,
                        Cs2Tracer.HalfWidthScreenFraction(spec, pass, 0.5f, worldProjection.M22, out _) * Height);
                    worstFar = MathF.Min(worstFar,
                        Cs2Tracer.HalfWidthScreenFraction(spec, pass, 80f, worldProjection.M22, out _) * Height);
                }
                Check($"tracer/{gun}/width.near", worstNear <= 16f, $"{worstNear:0.##} px half-width at 0.5 m");
                Check($"tracer/{gun}/width.far", worstFar >= 0.5f, $"{worstFar:0.##} px half-width at 80 m");
            }
        }

        // The 22 knives, added 2026-09-05 from CS2's own viewmodel clips. They run
        // the same rig and the same arms as the guns; what is checked here is that
        // each one loads, animates, carries a skinned mesh, and that the mesh's
        // joints actually resolve against that knife's own skeleton - a mesh whose
        // joints did not resolve would silently draw at the origin.
        foreach (string knife in Enumerable.Range(0, CsmcKnifeRig.AssetCount)
                                            .Where(v => !CsmcKnifeRig.IsGun(v))
                                            .Select(CsmcKnifeRig.GetAssetName)
                                            .Distinct(StringComparer.Ordinal)) {
            if (!Cs2Rig.Has(knife)) {
                Check($"knife/{knife}/rig", false, "no CS2 rig");
                continue;
            }
            Check($"knife/{knife}/deploy", Cs2Rig.Duration(knife, "deploy") > 0f,
                  $"{Cs2Rig.Duration(knife, "deploy"):0.####} s");
            Cs2Rig.Pose kp = Cs2Rig.Sample(knife, "idle", 0f);
            Check($"knife/{knife}/sample", kp is not null && kp.Bones.Count >= 55,
                  kp is null ? "null" : $"{kp.Bones.Count} bones");
            // idle2 is absent for bowie, falchion and push in CS2 and in CS:MC alike,
            // so it must resolve to idle rather than to nothing.
            Check($"knife/{knife}/idle2", Cs2Rig.Duration(knife, "idle2") > 0f,
                  $"{Cs2Rig.Duration(knife, "idle2"):0.####} s");
            // Every alias the CS:MC rig offers is one the controller may pick, so
            // with KnifeProfile=1 every one of them has to resolve to a CS2 clip
            // without the idle fallback. 0.17.0 had 18 holes across deploy2,
            // inspect2 and inspect3, and each was drawn as the finished idle pose.
            int variant = Enumerable.Range(0, CsmcKnifeRig.AssetCount)
                .First(v => CsmcKnifeRig.GetAssetName(v) == knife);
            var unanswered = new List<string>();
            foreach (string alias in ControllerAliases) {
                if (!CsmcKnifeRig.HasClip(variant, alias)) continue;
                if (!Cs2Rig.HasAlias(knife, alias)) unanswered.Add(alias);
            }
            Check($"knife/{knife}/aliases", unanswered.Count == 0,
                  unanswered.Count == 0
                      ? $"all of [{string.Join(',', ControllerAliases.Where(a => CsmcKnifeRig.HasClip(variant, a)))}] resolve"
                      : $"no CS2 clip for [{string.Join(',', unanswered)}]");

            Cs2SkinnedMesh km = Cs2SkinnedMesh.Weapon(knife);
            Check($"knife/{knife}/mesh", km is not null,
                  km is null ? "no skinned mesh" : $"{km.Skinned.Length} vertices, {km.Joints.Length} joints");
            if (km is not null && kp is not null) {
                bool posed = km.SetPose(kp, Cs2Placement.Placement());
                Check($"knife/{knife}/pose", posed, posed ? "joints resolved" : "no joint resolved");
                if (posed) {
                    km.Skin();
                    Vector3 lo = km.Skinned[0].Position, hi = lo;
                    foreach (Cs2SkinnedMesh.Vertex v in km.Skinned) {
                        lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position);
                    }
                    float span = Vector3.Distance(lo, hi);
                    // A knife is 15-35 cm across and sits in front of the eye. A mesh
                    // whose joints failed to bind collapses to a point at the origin.
                    Check($"knife/{knife}/skinned", span is > 0.05f and < 1.5f && hi.Z < 0f,
                          $"{span * 100f:0.#} cm across, front face z={hi.Z:0.###}");
                }
            }
        }

        // Forced coverage: these must be present by name, not by whatever the rigs
        // happen to carry. Each also has to be a different clip from its base - a
        // deploy2 that resolved to deploy would look exactly like the bug it fixes.
        foreach ((string alias, string[] knives) in RequiredVariants) {
            string baseAlias = alias.StartsWith("deploy", StringComparison.Ordinal) ? "deploy" : "inspect";
            foreach (string knife in knives) {
                string clip = Cs2Rig.ResolvedClip(knife, alias);
                string basis = Cs2Rig.ResolvedClip(knife, baseAlias);
                Check($"variant/{knife}/{alias}",
                      clip is not null && clip != basis && Cs2Rig.Duration(knife, alias) > 0f,
                      clip is null ? "missing"
                      : clip == basis ? $"resolves to {clip}, the same clip as {baseAlias}"
                      : $"{clip} ({Cs2Rig.Duration(knife, alias):0.####} s), {baseAlias} is {basis}");
            }
        }

        // The AWP's scope numbers come from the vdata, and GunSpec's magnifications
        // must stay equal to CS2's base FOV over each zoomed FOV. They were right but
        // unrecorded; a hand edit to either side would have gone unnoticed.
        KnifeTuning.Override("GunNumbers", 1f);
        Cs2Weapons.Gun awp = Cs2Weapons.Get("awp");
        if (awp is null) {
            Check("weapons/awp/zoom", false, "no CS2 weapon data");
        }
        else {
            Check("weapons/awp/zoom.levels", awp.ZoomLevels == 2 && awp.ZoomFov is { Length: >= 2 },
                  $"{awp.ZoomLevels} levels, fov [{string.Join(',', awp.ZoomFov ?? [])}]");
            float[] want = [.. (awp.ZoomFov ?? []).Where(f => f is > 0f).Select(f => 90f / f.Value)];
            float[] have = GunSpec.ForAsset("awp")?.ZoomLevels ?? [];
            Check("weapons/awp/zoom.magnification",
                  want.Length == have.Length && want.Zip(have).All(p => MathF.Abs(p.First - p.Second) < 1e-3f),
                  $"vdata gives [{string.Join(',', want.Select(x => x.ToString("0.##")))}], "
                  + $"GunSpec has [{string.Join(',', have.Select(x => x.ToString("0.##")))}]");
            Check("weapons/awp/zoom.seconds",
                  awp.ZoomSeconds is { Length: >= 1 } && awp.ZoomSeconds[0] is > 0f and < 0.2f,
                  $"m_flZoomTime [{string.Join(',', awp.ZoomSeconds ?? [])}]; the lens is not gated on it");
            Check("weapons/awp/zoom.hidevm", awp.HideViewModelWhenZoomed,
                  $"{awp.HideViewModelWhenZoomed}");
        }
        KnifeTuning.Override("GunNumbers", 0f);

        Check("sounds/clips", Cs2Sounds.ClipCount > 0, $"{Cs2Sounds.ClipCount} clips");
        Check("sounds/ak47:reload", Cs2Sounds.TryGet("ak47:reload", out var reload) && reload.Length >= 5,
              Cs2Sounds.TryGet("ak47:reload", out var r2) ? $"{r2.Length} cues" : "missing");
        Cs2SkinnedMesh arms = Cs2SkinnedMesh.Arms;
        Check("arms/loaded", arms is not null, arms is null ? "null" : $"{arms.Skinned.Length} vertices");
        Check("arms/primitives", arms is not null && arms.Primitives.Length == 2,
              arms is null ? "null" : $"{arms.Primitives.Length}");

        return JsonSerializer.Serialize(new {
            assembly = typeof(Cs2SelfTest).Assembly.Location,
            version = typeof(Cs2SelfTest).Assembly.GetName().Version?.ToString(),
            failed = checks.Count(c => !c.Ok),
            checks = checks.Select(c => new { name = c.Name, ok = c.Ok, detail = c.Detail }),
        });
    }

    /// <summary>
    /// The sound table exactly as this assembly carries it, for checking the OGGs in
    /// the same package. Reading it from the repository instead would accept a package
    /// whose embedded table and audio files disagree.
    /// </summary>
    public static string SoundTableJson() {
        Assembly assembly = typeof(Cs2SelfTest).Assembly;
        string name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("AnimationData.cs2_sounds.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using Stream stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
