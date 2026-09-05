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
    /// <summary>The three that predate the rigid-parts pipeline; they use OBJ mesh parts.</summary>
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

        // Exercise the exact regression: even an old tuning file requesting CS:MC
        // must never send any of the original knives / guns to box hands.
        KnifeTuning.KnifeProfile = 0f;
        KnifeTuning.GunProfile = 0f;
        KnifeTuning.Cs2Arms = 0f;
        Check("firstperson/legacy-switches", KnifeTuning.KnifeProfile == 1f && KnifeTuning.GunProfile == 1f
            && KnifeTuning.Cs2Arms == 1f, "old profile / arms settings cannot disable CS2 hands");
        Check("firstperson/no-legacy-rigs", !typeof(Cs2SelfTest).Assembly.GetManifestResourceNames()
            .Any(n => n.EndsWith(".csmc.animation.json", StringComparison.Ordinal)), "no embedded CS:MC animation");
        foreach (int variant in Enumerable.Range(0, CsmcKnifeRig.AssetCount)) {
            string asset = CsmcKnifeRig.GetAssetName(variant);
            var pose = CsmcKnifeRig.Sample(variant, "idle", 0f);
            Check($"firstperson/{asset}/route", CsmcKnifeRig.IsCs2Only(variant)
                && CsmcFirstPersonRenderer.Route(variant, pose) == "cs2", "CS2-only route");
            var weaponArms = Cs2SkinnedMesh.Arms;
            bool skinned = weaponArms is not null && weaponArms.SetPose(Cs2Rig.Sample(asset, "idle", 0f), Cs2Placement.Placement());
            if (skinned) weaponArms.Skin();
            Check($"firstperson/{asset}/hands", skinned && weaponArms.Skinned.Length > 6000
                && weaponArms.Skinned.All(v => float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y) && float.IsFinite(v.Position.Z)),
                "real finger / glove mesh binds to this weapon's CS2 skeleton");
            if (!CsmcKnifeRig.IsGun(variant)) {
                var knifeMesh = Cs2SkinnedMesh.Weapon(asset);
                bool valid = knifeMesh is not null && knifeMesh.SetPose(Cs2Rig.Sample(asset, "idle", 0f), Cs2Placement.Placement());
                var blockMesh = new BlockMesh();
                if (valid) {
                    knifeMesh.Skin();
                    Cs2BlockMesh.Append(blockMesh, knifeMesh.Skinned, knifeMesh.Primitives.SelectMany(p => p.Indices));
                }
                Check($"firstperson/{asset}/world-mesh", valid && blockMesh.Vertices.Count > 0 && blockMesh.Indices.Count > 0,
                    "inventory / dropped knife uses the CS2 mesh");
            }
        }
        Check("gunspec/zeus-ten-seconds", GunSpec.ForAsset("taser").RechargeSeconds == 10f, "requested ten-second cooldown");
        foreach (string asset in new[] { "aug", "sg556" }) {
            Vector3 eyeView = Vector3.Transform(Cs2Ironsight.Eye(asset), Cs2Placement.Placement() * Cs2Ironsight.Correction(asset));
            Check($"firstperson/{asset}/eye", eyeView.Length() < 0.00001f, "viewmodel offsets cancel at the optical eye");
            foreach (float aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f }) {
                Matrix projection = Cs2Ironsight.Projection(aspect);
                Check($"firstperson/{asset}/scope-{aspect:0.##}", MathF.Abs(projection.M11 * aspect - projection.M22) < 0.0001f
                    && Cs2Ironsight.Aperture(asset) is > 0f and < 1f, "circular aperture and viewport-height framing");
            }
        }

        // Loader status first: a loader can produce usable values and still have failed.
        Check("load/effects", Cs2Effects.LoadError is null, Cs2Effects.LoadError ?? "ok");
        Check("load/weapons", Cs2Weapons.LoadError is null, Cs2Weapons.LoadError ?? "ok");
        Check("load/sounds", Cs2Sounds.LoadError is null, Cs2Sounds.LoadError ?? "ok");

        // Every gun in the table, not the first three: 0.18.2 shipped eight with no
        // entry at all, drawing their flash and tracer from defaults.
        foreach (string gun in GunSpec.All.Select(g => g.Name)) {
            Cs2Effects.Gun fx = Cs2Effects.Get(gun);
            GunSpec fxSpec = GunSpec.ForAsset(gun);
            Check($"effects/{gun}/loaded", fx is not null, fx is null ? "Cs2Effects.Get returned null" : "ok");
            if (fx is not null && fxSpec is { MuzzleEffects: false }) {
                // The Zeus: its model spawns no muzzle particle and its tracer is a wire
                // the ribbon does not draw; the table must say so, and GunSpec must agree.
                bool none = (fx.Flash is null || fx.Flash.Count == 0) && (fx.Tracer?.Passes is null || fx.Tracer.Passes.Length == 0);
                Check($"effects/{gun}/none", none, none ? "no flash, no drawable tracer, as the model and the vpcf say" : "the table has effects GunSpec turns off");
                // The Zeus has its own effect file: the arc CS2 draws over the wires,
                // the muzzle glow, flare and sparks, the impact glow and sparks.
                // Not gated on Applies(): a file that failed to load makes Applies false
                // for every gun, and the checks below would simply vanish (the first
                // negative test of 0.20.1 passed 2475/2475 that way). The one gun with
                // no muzzle effects is the one the file must name.
                {
                    Cs2TaserEffect.File z = Cs2TaserEffect.Data;
                    Check($"effects/{gun}/zeus.loaded", Cs2TaserEffect.LoadError is null && z is not null && z.Gun == gun,
                          Cs2TaserEffect.LoadError ?? (z?.Gun == gun ? "cs2_taser_effect.json" : $"cs2_taser_effect.json names {z?.Gun ?? "nothing"}, not {gun}"));
                    if (z is not null && z.Gun == gun) {
                        Check($"effects/{gun}/zeus.arc",
                              z.Arc.Life > 0f && z.Arc.Points >= 2 && z.Arc.Passes is { Length: > 0 } && z.Arc.ColorMin is { Length: >= 3 } && z.Arc.RadiusInchesAt(1) > 0f,
                              $"{z.Arc.Life:0.##} s from {z.Arc.StartSeconds:0.##} s, {z.Arc.Points:0} points, {z.Arc.Passes?.Length ?? 0} rope passes, colour {string.Join(',', z.Arc.ColorMin ?? [])}, radius to {z.Arc.RadiusInchesAt((int)z.Arc.Points):0.##} in");
                        Check($"effects/{gun}/zeus.muzzle", z.MuzzleGlow?.Count > 0 && z.MuzzleFlash?.Count > 0 && z.MuzzleSparks?.Count > 0,
                              $"{z.MuzzleGlow?.Count:0} glow, {z.MuzzleFlash?.Count:0} flare ({z.MuzzleFlash?.Blend}), {z.MuzzleSparks?.Count} sparks");
                        Check($"effects/{gun}/zeus.impact", z.ImpactGlow?.Count > 0 && z.ImpactSparks?.Count > 0,
                              $"{z.ImpactGlow?.Count:0} glow, {z.ImpactSparks?.Count} sparks over {z.ImpactSparks?.EmissionSeconds:0.##} s");
                        string[] needed = [z.Arc.Passes?[0].Textures?[0], z.MuzzleGlow?.Texture, z.MuzzleFlash?.Texture, z.MuzzleSparks?.Texture, z.ImpactGlow?.Texture];
                        Check($"effects/{gun}/zeus.textures", needed.All(t => t is not null && Cs2TaserEffect.BakedTexture(t) is not null),
                              string.Join(", ", needed.Select(t => $"{t}->{Cs2TaserEffect.BakedTexture(t) ?? "MISSING"}")));
                        Check($"effects/{gun}/zeus.wire", z.Wire is { Rendered: false }, "the wires have no renderer in CS2; only the arc over them is drawn");
                    }
                }
                continue;
            }
            if (fx is not null) {
                Check($"effects/{gun}/muzzle0", fx.MuzzlePos0 is { Length: >= 3 },
                      fx.MuzzlePos0 is null ? "null" : $"[{string.Join(',', fx.MuzzlePos0)}]");
                // The vdata's muzzle and the rig's muzzle bone at idle describe one point
                // in two files; the AK's agree to three decimals. A gun whose vdata stem
                // was matched to the wrong weapon would show up here as inches apart.
                Cs2Rig.Pose idle = Cs2Rig.Sample(gun, "idle", 0f);
                if (fx.MuzzlePos0 is { Length: >= 3 } && idle is not null && idle.HasBone("muzzle")) {
                    // The two agree to a thousandth on the AK, P90, FAMAS and Desert Eagle,
                    // by 0.95 in on the AWP and by 7.7 in on the SSG08 - each time along
                    // the barrel only, the vdata sitting behind the bone. So the same
                    // bore line is what is asserted (across the barrel), the along-barrel
                    // offset is reported, and the renderer draws from the bone, which is
                    // where the model attaches CS2's own flash (attachment muzzle_flash).
                    Vector3 declared = new(fx.MuzzlePos0[0], fx.MuzzlePos0[1], fx.MuzzlePos0[2]);
                    Vector3 bone = idle.GetBoneOrigin("muzzle");
                    Vector3 delta = declared - bone;
                    float across = MathF.Sqrt(delta.Y * delta.Y + delta.Z * delta.Z);
                    Check($"effects/{gun}/muzzle.bone", across < 1.5f,
                          $"{across:0.####} in across the bore, {(delta.X >= 0f ? "+" : "")}{delta.X:0.###} in along it, from the rig's muzzle bone");
                }
                if (fxSpec is { HasSilencer: true }) {
                    Check($"effects/{gun}/muzzle1", fx.MuzzlePos1 is { Length: >= 3 },
                          fx.MuzzlePos1 is null ? "no silenced muzzle" : $"[{string.Join(',', fx.MuzzlePos1)}]");
                    Check($"effects/{gun}/flash.silenced", Cs2Effects.GetFlash(gun, true) is not null
                              && !ReferenceEquals(Cs2Effects.GetFlash(gun, true), Cs2Effects.GetFlash(gun, false)),
                          Cs2Effects.GetFlash(gun, true) is null ? "no silenced flash" : "own silenced flash");
                }
                Cs2Effects.Flash flash = Cs2Effects.GetFlash(gun, false);
                Check($"effects/{gun}/flash", flash is not null, flash is null ? "no default flash" : "ok");
                if (flash is not null) {
                    Check($"effects/{gun}/flash.seconds", flash.Seconds > 0f && flash.Seconds < 2f, $"{flash.Seconds:0.####} s");
                    Check($"effects/{gun}/flash.frames", flash.SequenceFrames >= 1, $"{flash.SequenceFrames} frames");
                    Check($"effects/{gun}/flash.alpha", flash.AlphaMid > 0f && flash.AlphaMid <= 1f, $"{flash.AlphaMid:0.####}");
                }
                // A suppressed gun draws no tracer in CS2: the MP5-SD's vdata says
                // m_nTracerFrequency 0, and that is what is expected of it.
                bool noTracer = fxSpec is { SilencedAlways: true };
                Check($"effects/{gun}/tracer.freq", noTracer ? Cs2Effects.TracerFrequency(gun) == 0 : Cs2Effects.TracerFrequency(gun) >= 1,
                      $"{Cs2Effects.TracerFrequency(gun)}{(noTracer ? " (suppressed, none in CS2)" : "")}");
                Cs2Effects.Tracer tr = fx.Tracer;
                Check($"effects/{gun}/tracer.speed", tr is not null && tr.Speed is > 1000f,
                      tr?.Speed?.ToString("0.#") ?? "null");
                string[] baked = ["cs2_tracer_add", "cs2_tracer_blend", "cs2_tracer_smg", "cs2_tracer_tintable"];
                Check($"effects/{gun}/tracer.textures",
                      tr?.Passes is { Length: >= 1 } && tr.Passes.All(p => p.Texture is not null && baked.Contains(p.Texture)),
                      tr?.Passes is null ? "no passes"
                          : string.Join(',', tr.Passes.Select(p => p.Texture ?? $"(none for {p.SourceTexture})")));
                // The SMG tracer is a rope with the texture scrolled down it, not a
                // moving trail: one pass, one texture repeat (100 in) as the trail, no
                // screen clamp and no fade envelope. Those are its numbers, not holes.
                bool rope = tr?.Passes is { Length: > 0 } && tr.Passes.All(p => p.IsRope);
                Check($"effects/{gun}/tracer.length", tr is not null && (rope ? tr.MaxLength is > 50f : tr.MaxLength is > 100f),
                      tr?.MaxLength?.ToString("0.#") ?? "null");
                if (tr is not null) {
                    // The trail is drawn twice, with the CS2 textures. A single pass, or
                    // one with no baked texture, means the ribbon falls back to nothing.
                    Check($"effects/{gun}/tracer.passes", rope ? tr.Passes is { Length: 1 } : tr.Passes is { Length: 2 },
                          $"{tr.Passes?.Length ?? 0} passes{(rope ? " (rope)" : "")}");
                    bool textured = tr.Passes is { Length: > 0 }
                        && tr.Passes.All(p => !string.IsNullOrEmpty(p.Texture) && !string.IsNullOrEmpty(p.SourceTexture));
                    Check($"effects/{gun}/tracer.textures", textured,
                          tr.Passes is null ? "no passes"
                          : string.Join(", ", tr.Passes.Select(p => $"{p.Texture ?? "none"}<-{p.SourceTexture ?? "none"}")));
                    bool clamped = tr.Passes is { Length: > 0 }
                        && tr.Passes.All(p => p.IsRope ? p.MinSize == 0f && p.MaxSize >= 1f : p.MinSize > 0f && p.MaxSize > p.MinSize);
                    Check($"effects/{gun}/tracer.sizeclamp", clamped,
                          tr.Passes is null ? "no passes"
                          : string.Join(", ", tr.Passes.Select(p => $"{p.MinSize:0.#####}..{p.MaxSize:0.#####}")));
                    // Trails are clamped on screen so their world width barely matters;
                    // a rope's is what is drawn, and CS2's AUG rope is 2..3 in (63 mm mid).
                    bool radius = tr.Passes is { Length: > 0 }
                        && tr.Passes.All(p => tr.HalfWidthMetres(p) is > 0.001f && tr.HalfWidthMetres(p) < (p.IsRope ? 0.1f : 0.05f));
                    Check($"effects/{gun}/tracer.radius", radius,
                          tr.Passes is null ? "no passes"
                          : string.Join(", ", tr.Passes.Select(p => $"{tr.HalfWidthMetres(p) * 1000f:0.##} mm")));
                    Check($"effects/{gun}/tracer.trailseconds", rope ? tr.TrailSecondsMid is > 0.001f and < 1f : tr.TrailSecondsMid is > 0.01f and < 1f,
                          $"{tr.TrailSecondsMid:0.####} s");
                    // The fade envelope must actually be an envelope: dark at the muzzle,
                    // full in the middle, going out at the end. The rope has none and
                    // must be flat instead.
                    bool envelope = rope
                        ? tr.PathAlpha(0.1f) > 0.99f && tr.PathAlpha(0.5f) > 0.99f
                        : tr.PathAlpha(0.1f) < 0.01f && tr.PathAlpha(0.5f) > 0.99f && tr.PathAlpha(0.99f) < 0.99f;
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
                GunSpec wpSpec = GunSpec.ForAsset(gun);
                bool notAFirearm = wpSpec is { MuzzleEffects: false };   // the Zeus: 0 spread, 0 kick in the vdata
                Check($"weapons/{gun}/spread", notAFirearm ? wp.SpreadDegrees == 0f : wp.SpreadDegrees > 0f, $"{wp.SpreadDegrees:0.####} deg");
                Check($"weapons/{gun}/kick", notAFirearm ? wp.KickPitchDegrees == 0f : wp.KickPitchDegrees > 0f, $"{wp.KickPitchDegrees:0.####} deg");
                Check($"weapons/{gun}/falloff", wp.RangeModifier is > 0f and < 1f, $"{wp.RangeModifier:0.###}");
                Check($"weapons/{gun}/maxspeed", wp.MaxSpeed is { Length: >= 1 }, wp.MaxSpeed is null ? "null" : $"{wp.MaxSpeed[0]:0.#}");
            }
            KnifeTuning.Override("GunNumbers", 0f);

            // The first three carry OBJ mesh parts in the rig; the rest a .cs2.parts.
            int rigParts = Cs2Rig.GetMeshParts(gun).Count;
            int rigidParts = rigParts == 0 ? Cs2RigidMesh.For(gun)?.Parts.Length ?? 0 : 0;
            Check($"rig/{gun}/parts", rigParts + rigidParts > 0,
                  rigParts > 0 ? $"{rigParts} parts" : $"{rigidParts} rigid parts (.cs2.parts)");
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
                // Something of the gun itself has to move mid-reload - the magazine on
                // most, the MP5-SD names its mag differently and the belt guns swing a
                // cover - so the part bones are all measured and the largest reported.
                string[] partBones = Cs2Rig.GetMeshParts(gun).Count > 0
                    ? Cs2Rig.GetMeshParts(gun).Select(KnifeRigPose.BoneOf).ToArray()
                    : Cs2RigidMesh.For(gun)?.Parts.Select(pp => Cs2RigidMesh.For(gun).Joints[pp.Joint]).ToArray() ?? [];
                string mover = "";
                float magazineMoves = 0f;
                foreach (string bone in partBones.Distinct()) {
                    if (later is null || !pose.HasBone(bone) || bone is "weapon_offset" or "weapon" or "root_motion") continue;
                    float d = Vector3.Distance(later.GetBoneOrigin(bone), pose.GetBoneOrigin(bone));
                    if (d > magazineMoves) { magazineMoves = d; mover = bone; }
                }
                bool moves = later is not null && magazineMoves > 0.05f;
                bool noReloadInCs2 = reloadSeconds <= 0f && !Cs2Rig.HasAlias(gun, "reload");
                Check($"rig/{gun}/animates", moves || noReloadInCs2,
                      noReloadInCs2 ? "no reload in CS2 (the Zeus)" : later is null ? "no reload clip" : $"{mover} moves {magazineMoves:0.##} in mid-reload");
                // An additive clip (the R8's prepare_shoot) is deltas over the idle:
                // composed, its hands stay within an inch or two of the idle's while
                // something - the hammer, the fingers - moves against it. Read as a
                // plain clip the hands sit at the origin, which is what 0.20.0 drew.
                foreach (string alias in Cs2Rig.AdditiveAliases(gun)) {
                    float half = Cs2Rig.Duration(gun, alias) * 0.5f;
                    Cs2Rig.Pose layered = Cs2Rig.Sample(gun, alias, half);
                    Cs2Rig.Pose under = Cs2Rig.Sample(gun, "idle", half);
                    float handDrift = layered is null || under is null ? float.NaN
                        : Vector3.Distance(layered.GetBoneOrigin("hand_R"), under.GetBoneOrigin("hand_R"));
                    float handFromOrigin = layered?.GetBoneOrigin("hand_R").Length() ?? 0f;
                    float largest = 0f;
                    string moved = "";
                    if (layered is not null && under is not null) {
                        foreach ((string name, Matrix m) in layered.Bones) {
                            float d = Vector3.Distance(m.Translation, under.GetBoneOrigin(name));
                            if (d > largest) { largest = d; moved = name; }
                        }
                    }
                    Check($"rig/{gun}/{alias}.additive",
                          layered is not null && under is not null && handFromOrigin > 1f && handDrift < 2f && largest > 0.1f,
                          layered is null ? "no pose" : $"hand_R {handFromOrigin:0.##} in from origin, {handDrift:0.###} in off the idle; {moved} moves {largest:0.##} in");
                }
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
                // The rope has no screen clamp, so its 1.9 in half-width is 87 px at half a
                // metre - and is never drawn there: its head is 5 m out by the first frame
                // after the shot. It is judged at 5 m instead.
                // A rope is judged where it is seen: its head is 5 m out by the first
                // frame after the shot, and CS2 gives the AUG's a 2..3 in radius that
                // no clamp narrows, so the near limit is taken at 10 m and the far floor
                // at 40 m; the clamped trails keep 0.5 m and 80 m.
                bool ropePass = spec.Passes.All(p => p.IsRope);
                float nearAt = ropePass ? 10f : 0.5f;
                float farAt = ropePass ? 40f : 80f;
                if (ropePass) {
                    worstNear = spec.Passes.Max(pass => Cs2Tracer.HalfWidthScreenFraction(spec, pass, nearAt, worldProjection.M22, out _) * Height);
                    worstFar = spec.Passes.Min(pass => Cs2Tracer.HalfWidthScreenFraction(spec, pass, farAt, worldProjection.M22, out _) * Height);
                }
                Check($"tracer/{gun}/width.near", worstNear <= 16f, $"{worstNear:0.##} px half-width at {nearAt:0.#} m");
                Check($"tracer/{gun}/width.far", worstFar >= 0.5f, $"{worstFar:0.##} px half-width at {farAt:0.#} m");
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

        // The variant number is GunSpec.All's index and it goes into saved worlds, so
        // the order is frozen: append only. Inserting a gun renumbers everything after
        // it and a saved AWP comes back as whatever took index 2.
        bool orderKept = GunSpec.All.Length >= GunSpec.FrozenOrder.Length
            && GunSpec.FrozenOrder.Select((n, i) => GunSpec.All[i].Name == n).All(x => x);
        Check("gunspec/order", orderKept,
              orderKept
                  ? $"[{string.Join(',', GunSpec.All.Select(g => g.Name))}] still starts with "
                    + $"[{string.Join(',', GunSpec.FrozenOrder)}]"
                  : $"[{string.Join(',', GunSpec.All.Select(g => g.Name))}] no longer starts with "
                    + $"[{string.Join(',', GunSpec.FrozenOrder)}]; variants may only be appended");

        // The widened variant field has to read old saves and round-trip new ones.
        var layout = new List<string>();
        for (int v = 0; v < Math.Min(GunSpec.All.Length, 4); v++) {
            for (int r = 0; r <= 63; r += 21) {
                foreach (bool sil in new[] { false, true }) {
                    // Old encoding, exactly as versions up to 0.17.2 wrote it.
                    int old = (v & 0x3) | ((r & 0x3F) << 2) | (sil ? 1 << 8 : 0);
                    if (GunSpec.GetVariant(old) != v || GunSpec.GetRounds(old) != r
                        || GunSpec.GetSilencerOff(old) != sil)
                        layout.Add($"old({v},{r},{sil}) -> ({GunSpec.GetVariant(old)},{GunSpec.GetRounds(old)},{GunSpec.GetSilencerOff(old)})");
                }
            }
        }
        for (int v = 0; v < 64; v++) {
            foreach (int r in new[] { 0, 1, 30, 100, 127 }) {
                foreach (bool sil in new[] { false, true }) {
                    int data = GunSpec.MakeData(v, r, sil);
                    if (GunSpec.GetVariant(data) != v || GunSpec.GetRounds(data) != r
                        || GunSpec.GetSilencerOff(data) != sil)
                        layout.Add($"new({v},{r},{sil}) -> ({GunSpec.GetVariant(data)},{GunSpec.GetRounds(data)},{GunSpec.GetSilencerOff(data)})");
                    if (data >> 15 != 0) layout.Add($"new({v},{r},{sil}) uses bit {data:X} beyond 15");
                }
            }
        }
        Check("gunspec/layout", layout.Count == 0,
              layout.Count == 0
                  ? "old saves read back unchanged, and 64 variants x 0..127 rounds x silencer round-trip in 15 bits"
                  : string.Join("; ", layout.Take(4)));

        Check("load/variants", Cs2SoundVariants.LoadError is null, Cs2SoundVariants.LoadError ?? $"{Cs2SoundVariants.All.Count} cues");

        // Every gun GunSpec lists must be drawable and audible. A gun added to the
        // table without its rig, its mesh or its sounds would show up in the creative
        // menu and then draw nothing, which no other check here would catch.
        foreach (GunSpec spec in GunSpec.All) {
            string gun = spec.Name;
            bool legacy = Guns.Contains(gun);
            Cs2Weapons.Gun data = Cs2Weapons.Get(gun);
            Cs2Effects.Gun fx = Cs2Effects.Get(gun);
            Check($"gun/{gun}/rig", Cs2Rig.Has(gun), Cs2Rig.Has(gun) ? "ok" : "no CS2 rig");
            if (!Cs2Rig.Has(gun)) continue;

            // The path from the draw hook to DrawCs2, as the shipped code decides it.
            // 0.18.1 passed every check above and drew the eight CS2-only guns as a bare
            // block: the CS:MC sample was null, the hook dropped a null pose, and the
            // renderer's asset gate ran before its profile dispatch. Each link is asked
            // here by name, from the tables alone, so no camera is needed.
            int variant = Enumerable.Range(0, CsmcKnifeRig.AssetCount)
                .First(v => CsmcKnifeRig.GetAssetName(v) == gun);
            KnifeRigPose sample = null;
            string sampleError = null;
            try { sample = CsmcKnifeRig.Sample(variant, "idle", 0f); }
            catch (Exception e) { sampleError = e.Message; }
            Check($"gun/{gun}/sample", sample is not null && sample.ClipAlias == "idle",
                  sampleError ?? (sample is null ? "null pose"
                      : $"{sample.SourceClip} ({(CsmcKnifeRig.IsCs2Only(variant) ? "CS2 stand-in" : "CS:MC")})"));
            string route = CsmcFirstPersonRenderer.Route(variant, sample);
            string expectedRoute = CsmcKnifeRig.IsCs2Only(variant) || Cs2Placement.Active(variant) ? "cs2" : "csmc";
            Check($"gun/{gun}/route", route == expectedRoute,
                  $"{route}, expected {expectedRoute} with GunProfile={KnifeTuning.GunProfile:0.#}");
            // The controller's clip choices come from the rig that draws. Asking the
            // CS:MC table gave "idle" for every shot of a CS2-only gun.
            string shot = KnifeAnimationController.ShootClip(variant, false, _ => 0);
            bool shotOk = shot != "idle" && (!Cs2Placement.Active(variant) || Cs2Rig.HasAlias(gun, shot));
            Check($"gun/{gun}/shootClip", shotOk, shot);
            bool reloads = spec.Magazine > 0 && !(spec.Magazine == 1 && spec.RechargeSeconds > 0f);
            if (reloads)
                Check($"gun/{gun}/reloadClip", KnifeAnimationController.ReloadClip(variant) is not null,
                      KnifeAnimationController.ReloadClip(variant) ?? "none");
            if (spec.HasSilencer)
                Check($"gun/{gun}/silencerClip",
                      KnifeAnimationController.SilencerClip(variant, true) is not null
                          && KnifeAnimationController.SilencerClip(variant, false) is not null,
                      $"{KnifeAnimationController.SilencerClip(variant, true) ?? "none"}/"
                          + $"{KnifeAnimationController.SilencerClip(variant, false) ?? "none"}");
            // The empty magazine: a rig that carries shoot_empty / idle_slide_back /
            // reload_empty must be asked for them on the last round and after it, and
            // one that does not (the rifles) must get exactly what it got before.
            bool slideLocks = Cs2Rig.HasAlias(gun, "shootEmpty");
            string lastShot = KnifeAnimationController.ShootClip(variant, false, true, _ => 0);
            Check($"gun/{gun}/shootClip.empty", slideLocks ? lastShot == "shootEmpty" : lastShot == shot,
                  $"{lastShot}{(slideLocks ? "" : " (rig has no shootEmpty)")}");
            string emptyIdle = KnifeAnimationController.IdleClip(variant, true);
            Check($"gun/{gun}/idleClip.empty",
                  Cs2Rig.HasAlias(gun, "idleEmpty") ? emptyIdle == "idleEmpty" : emptyIdle == "idle",
                  emptyIdle);
            Check($"gun/{gun}/idleClip.loaded", KnifeAnimationController.IdleClip(variant, false) == "idle", "idle");
            if (reloads) {
                string emptyReload = KnifeAnimationController.ReloadClip(variant, true);
                Check($"gun/{gun}/reloadClip.empty",
                      Cs2Rig.HasAlias(gun, "reloadEmpty") ? emptyReload == "reloadEmpty" : emptyReload == "reload",
                      emptyReload ?? "none");
                if (Cs2Rig.HasAlias(gun, "reloadEmpty"))
                    Check($"gun/{gun}/cues.reloadEmpty", Cs2Sounds.TryGet($"{gun}:reloadEmpty", out var rc) && rc.Length >= 2,
                          Cs2Sounds.TryGet($"{gun}:reloadEmpty", out var rc2) ? $"{rc2.Length} cues" : "no CS2 cues");
            }
            // The scope: how many levels and whether it hides the gun are CS2's
            // (m_nZoomLevels, m_bHideViewModelWhenZoomed); a gun that keeps its
            // viewmodel aims down its own scope with the ironsight clips.
            if (data is not null && spec.ZoomLevels.Length > 0) {
                Check($"gun/{gun}/scope.levels", spec.ZoomLevels.Length == data.ZoomLevels,
                      $"{spec.ZoomLevels.Length} in GunSpec, {data.ZoomLevels} in the vdata");
                Check($"gun/{gun}/scope.hides", spec.ScopeHidesWeapon == data.HideViewModelWhenZoomed,
                      $"GunSpec {spec.ScopeHidesWeapon}, vdata {data.HideViewModelWhenZoomed}");
                // m_bUnzoomsAfterShot: the bolt actions leave the scope for the cycle, the
                // auto-snipers and the AUG / SG 553 do not (0.20.0 unscoped all six).
                Check($"gun/{gun}/scope.unzoom", spec.UnzoomsAfterShot == data.UnzoomsAfterShot,
                      $"GunSpec {spec.UnzoomsAfterShot}, vdata {data.UnzoomsAfterShot}");
                if (!spec.ScopeHidesWeapon) {
                    Check($"gun/{gun}/scope.ironsight",
                          KnifeAnimationController.IdleClip(variant, false, true) == "ironsightIdle"
                              && KnifeAnimationController.ShootClip(variant, false, false, true, _ => 0) == "ironsightShoot",
                          $"{KnifeAnimationController.IdleClip(variant, false, true)} / "
                              + $"{KnifeAnimationController.ShootClip(variant, false, false, true, _ => 0)}");
                }
            }
            if (data is not null && spec.ZoomLevels.Length == 0)
                Check($"gun/{gun}/scope.none", data.ZoomLevels == 0, $"vdata has {data.ZoomLevels} zoom levels");
            // Unscoped, the aim clips must not leak in.
            Check($"gun/{gun}/idleClip.unscoped", KnifeAnimationController.IdleClip(variant, false, false) == "idle", "idle");
            // The shell-by-shell reload: CS2 marks the shotguns' one reload clip with
            // WPN_RELOAD_INTRO / LOOP / OUTRO and runs the loop once per shell. The
            // sections, the summed length and the elapsed-to-clip-time mapping are the
            // shipped code's, checked against the events in the rig file.
            Cs2Rig.ReloadSections sections = Cs2Rig.GetReloadSections(gun);
            bool loopsInCs2 = gun is "nova" or "xm1014" or "sawedoff";
            Check($"gun/{gun}/reload.looped", (sections is not null) == loopsInCs2,
                  sections is null ? "one-pass reload" : $"loop {sections.LoopStart:0.###}+{sections.LoopLength:0.###} s, outro from {sections.OutroStart:0.###} to {sections.End:0.###} s");
            if (sections is not null) {
                bool sane = sections.LoopStart > 0f && sections.LoopLength > 0f
                    && sections.OutroStart >= sections.LoopStart + sections.LoopLength - 0.01f
                    && sections.End > sections.OutroStart
                    && sections.AddAmmoInLoop >= 0f && sections.AddAmmoInLoop <= sections.LoopLength;
                Check($"gun/{gun}/reload.sections", sane, $"add-ammo {sections.AddAmmoInLoop:0.###} s into the loop");
                float full = sections.Duration(spec.Magazine);
                float expected = sections.LoopStart + spec.Magazine * sections.LoopLength + (sections.End - sections.OutroStart);
                Check($"gun/{gun}/reload.duration", MathF.Abs(full - expected) < 1e-4f && full > Cs2Rig.Duration(gun, "reload"),
                      $"{spec.Magazine} shells take {full:0.###} s (the clip alone is {Cs2Rig.Duration(gun, "reload"):0.###} s)");
                // The mapping: intro plays once, the loop repeats, the outro follows the last loop.
                int loops = 3;
                float t1 = sections.ClipTime(loops, sections.LoopStart * 0.5f);
                float t2 = sections.ClipTime(loops, sections.LoopStart + sections.LoopLength + 0.05f);
                float t3 = sections.ClipTime(loops, sections.LoopStart + loops * sections.LoopLength + 0.1f);
                float t4 = sections.ClipTime(loops, 100f);
                bool mapped = MathF.Abs(t1 - sections.LoopStart * 0.5f) < 1e-4f
                    && MathF.Abs(t2 - (sections.LoopStart + 0.05f)) < 1e-4f
                    && MathF.Abs(t3 - (sections.OutroStart + 0.1f)) < 1e-4f
                    && MathF.Abs(t4 - sections.End) < 1e-4f;
                Check($"gun/{gun}/reload.cliptime", mapped,
                      $"intro {t1:0.###}, second loop {t2:0.###}, outro {t3:0.###}, past the end {t4:0.###}");
                Check($"gun/{gun}/reload.seconds", MathF.Abs(KnifeAnimationController.ReloadSeconds(variant, true, spec.Magazine) - full) < 1e-4f,
                      $"{KnifeAnimationController.ReloadSeconds(variant, true, spec.Magazine):0.###} s for a full magazine");
            }
            // The three specials. The R8: an alternate cycle only with a fanning clip,
            // and the hammer clip for the primary. The Dual Berettas: left, right, left
            // from 30, each gun's last-round clip, the idles by count. The Zeus: no
            // muzzle particle in its model, no tracer to draw, one round and a recharge.
            bool fans = Cs2Rig.HasAlias(gun, "shootAlt");
            Check($"gun/{gun}/altfire", (spec.CycleSecondsAlternate > 0f) == fans
                      && (!fans || (data is not null && MathF.Abs(spec.CycleSecondsAlternate - data.CycleSecondsAlternate) < 1e-4f)),
                  fans ? $"shootAlt at {spec.CycleSecondsAlternate:0.###} s (vdata pair {data?.CycleSecondsAlternate:0.###})" : "no alternate fire");
            if (fans) {
                Check($"gun/{gun}/altfire.clip", KnifeAnimationController.ShootClip(variant, false, false, false, true, -1, _ => 0) == "shootAlt", "shootAlt");
                Check($"gun/{gun}/altfire.prepare", Cs2Rig.HasAlias(gun, "prepareShoot") && Cs2Rig.Duration(gun, "prepareShoot") > 0f,
                      $"prepareShoot {Cs2Rig.Duration(gun, "prepareShoot"):0.###} s");
            }
            if (Cs2Rig.HasAlias(gun, "shootLeft")) {
                string s30 = KnifeAnimationController.ShootClip(variant, false, false, false, false, 30, _ => 0);
                string s29 = KnifeAnimationController.ShootClip(variant, false, false, false, false, 29, _ => 0);
                string s2 = KnifeAnimationController.ShootClip(variant, false, false, false, false, 2, _ => 0);
                string s1 = KnifeAnimationController.ShootClip(variant, false, true, false, false, 1, _ => 0);
                Check($"gun/{gun}/dual.alternation", s30 == "shootLeft" && s29 == "shoot1" && s2 == "shootLeftLast" && s1 == "shootRightLast",
                      $"30:{s30} 29:{s29} 2:{s2} 1:{s1}");
                Check($"gun/{gun}/dual.idles",
                      KnifeAnimationController.IdleClip(variant, 1, false) == "idleLeftEmpty"
                          && KnifeAnimationController.IdleClip(variant, 0, false) == "idleBothEmpty"
                          && KnifeAnimationController.IdleClip(variant, 2, false) == "idle",
                      $"1:{KnifeAnimationController.IdleClip(variant, 1, false)} 0:{KnifeAnimationController.IdleClip(variant, 0, false)}");
                Check($"gun/{gun}/dual.muzzles", spec.LeftMuzzleBone is not null
                          && Cs2Rig.Sample(gun, "idle", 0f) is { } dp && dp.HasBone(spec.MuzzleBone) && dp.HasBone(spec.LeftMuzzleBone),
                      $"{spec.MuzzleBone} / {spec.LeftMuzzleBone ?? "none"}");
            }
            else
                Check($"gun/{gun}/single.idle", KnifeAnimationController.IdleClip(variant, 2, false) == "idle", "idle");
            bool noFlash = fx is not null && (fx.Flash is null || fx.Flash.Count == 0);
            Check($"gun/{gun}/muzzleEffects", spec.MuzzleEffects == !noFlash,
                  spec.MuzzleEffects ? "drawn" : "none: the model spawns no muzzle particle");
            if (spec.Magazine == 1 && KnifeAnimationController.ReloadClip(variant, true) is null)
                Check($"gun/{gun}/recharge", spec.RechargeSeconds > 0f,
                      $"{spec.RechargeSeconds:0.#} s (assumed), one round, no reload clip");
            string silencedDraw = KnifeAnimationController.DeployClip(variant, true);
            Check($"gun/{gun}/deployClip.silenced",
                  Cs2Rig.HasAlias(gun, "deploySilenced") ? silencedDraw == "deploySilenced" : silencedDraw == "deploy",
                  silencedDraw);
            Check($"gun/{gun}/deployClip.plain", KnifeAnimationController.DeployClip(variant, false) == "deploy", "deploy");
            if (Cs2Rig.HasAlias(gun, "deploySilenced"))
                Check($"gun/{gun}/cues.deploySilenced", Cs2Sounds.TryGet($"{gun}:deploySilenced", out var dc) && dc.Length >= 1,
                      Cs2Sounds.TryGet($"{gun}:deploySilenced", out var dc2) ? $"{dc2.Length} cues" : "no CS2 cues");
            // CS2's own cue times for the clips the behaviour schedules: a reload with
            // no cues is a silent reload, which the device showed for all eight.
            // An inspect can be silent in CS2 (the P250's and CZ75's lookat01 carry no
            // sound events), so it is reported but not required; a draw or a reload
            // with no cue at all is a hole.
            foreach ((string clip, int atLeast) in new[] { ("deploy", 1), ("reload", 2), ("inspect", 0) }) {
                if (clip == "reload" && !reloads) continue;
                bool has = Cs2Sounds.TryGet($"{gun}:{clip}", out var cues);
                Check($"gun/{gun}/cues.{clip}", atLeast == 0 || (has && cues.Length >= atLeast),
                      has ? $"{cues.Length} cues [{string.Join(',', cues.Select(c => c.Name).Distinct())}]" : "no CS2 cues (the clip has no sound events)");
            }
            if (spec.HasSilencer)
                foreach (string clip in new[] { "attach", "detach" }) {
                    bool has = Cs2Sounds.TryGet($"{gun}:{clip}", out var cues);
                    Check($"gun/{gun}/cues.{clip}", has && cues.Length >= 1,
                          has ? $"{cues.Length} cues" : "no CS2 cues");
                }
            foreach (string alias in new[] { "deploy", "idle", "shoot", "reload", "inspect" }) {
                // The Taser has no reload in CS2 and no gun in the table needs one it
                // has not got; anything else missing is a hole. "shoot" stands for
                // whichever shot alias the gun uses: the M4A1-S has shootSilenced and
                // shootUnsilenced where the rest have shoot1.
                if (alias == "reload" && (spec.Magazine <= 0 || (spec.Magazine == 1 && spec.RechargeSeconds > 0f))) continue;
                string[] tried = alias == "shoot"
                    ? ["shoot1", "shootSilenced", "shootUnsilenced"] : [alias];
                string found = tried.FirstOrDefault(a => Cs2Rig.HasAlias(gun, a));
                Check($"gun/{gun}/{alias}", found is not null,
                      found is not null
                          ? $"{found} -> {Cs2Rig.ResolvedClip(gun, found)} ({Cs2Rig.Duration(gun, found):0.###} s)"
                          : $"no clip for any of [{string.Join(',', tried)}]");
            }
            if (spec.HasSilencer) {
                foreach (string alias in new[] { "attach", "detach" })
                    Check($"gun/{gun}/{alias}", Cs2Rig.HasAlias(gun, alias),
                          Cs2Rig.ResolvedClip(gun, alias) ?? "no clip");
            }
            // Mesh: the first three ship OBJ parts, the rest a .cs2.parts.
            if (legacy) {
                Check($"gun/{gun}/mesh", Cs2Rig.GetMeshParts(gun).Count > 0,
                      $"{Cs2Rig.GetMeshParts(gun).Count} OBJ parts");
            }
            else {
                Cs2RigidMesh mesh = Cs2RigidMesh.For(gun);
                Check($"gun/{gun}/mesh", mesh is not null,
                      mesh is null ? "no .cs2.parts"
                      : $"{mesh.VertexCount} vertices, {mesh.Parts.Length} parts, "
                        + $"{mesh.BlendedTriangleCount} blended triangles");
                if (mesh is not null) {
                    Cs2Rig.Pose pose = Cs2Rig.Sample(gun, "idle", 0f);
                    bool posed = pose is not null && mesh.SetPose(pose, Cs2Placement.Placement());
                    Check($"gun/{gun}/pose", posed, posed ? "joints resolved" : "no joint resolved");
                    // Every part must get a matrix. A bone the clips do not animate -
                    // the M4A4's sight - falls back to the weapon root, which is
                    // correct for a rigid attachment and is reported so the
                    // substitution stays visible; a part with no matrix at all would
                    // simply not be drawn.
                    var orphans = mesh.Parts.Where(p => !mesh.TryPartWorld(p, out _))
                                            .Select(p => mesh.Joints[p.Joint]).ToArray();
                    Check($"gun/{gun}/bones", posed && orphans.Length == 0,
                          orphans.Length > 0 ? $"no matrix for [{string.Join(',', orphans)}]"
                          : mesh.Substituted.Length == 0 ? "every part's bone is animated"
                          : $"on the weapon root: [{string.Join(',', mesh.Substituted)}]");
                }
            }
            // Sound: the fire cue has to exist in the shipped variant table.
            string fire = $"{gun}_fire";
            Check($"gun/{gun}/sound", Cs2SoundVariants.All.ContainsKey(fire),
                  Cs2SoundVariants.All.TryGetValue(fire, out int n) ? $"{n} fire variants"
                  : "no fire sound installed");
            if (spec.HasSilencer)
                Check($"gun/{gun}/sound.silenced",
                      Cs2SoundVariants.All.ContainsKey($"{gun}_fire_silenced"),
                      Cs2SoundVariants.All.TryGetValue($"{gun}_fire_silenced", out int m)
                          ? $"{m} silenced variants" : "no silenced fire sound");
            // Burst is CS2's, not ours: only two guns have one and both need its timing.
            if (data is not null)
                Check($"gun/{gun}/burst", spec.HasBurstMode == data.HasBurstMode
                          && (!spec.HasBurstMode || (spec.BurstCycleSeconds > 0f && spec.BurstShotSeconds > 0f)),
                      spec.HasBurstMode
                          ? $"{spec.BurstShots} rounds, {spec.BurstCycleSeconds:0.###} s cycle, "
                            + $"{spec.BurstShotSeconds:0.###} s apart"
                          : "no burst, matching the vdata");
        }

        // Everything above reads a loader; nothing above touches the renderer, and
        // 0.18.1 added eight manifest entries that made its static constructor throw
        // looking for a CS:MC animation the CS2-only guns never had. The whole of
        // first person is behind that constructor, so it is checked by name here.
        string armInit = "ok";
        try {
            // The constructor itself, with none of InitHeadless's side effects: it
            // is where the per-variant tables are built and where the throw was.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(CsmcFirstPersonRenderer).TypeHandle);
        } catch (Exception e) {
            armInit = (e.InnerException ?? e).Message;
        }
        Check("firstperson/init", armInit == "ok", armInit);

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
