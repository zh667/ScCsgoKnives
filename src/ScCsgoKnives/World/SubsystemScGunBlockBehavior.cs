using Engine;
using Engine.Graphics;
using Engine.Input;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Gameplay of the CS guns: firing (hitscan with spread and camera kick), the
/// magazine kept in block data, reloading, the AWP scope (camera zoom plus overlay)
/// and the M4A1-S silencer. Animation is driven through KnifeAnimationController,
/// drawing through CsmcFirstPersonRenderer.
///
/// Controls: left mouse fires (held for automatic), R reloads, right mouse scopes
/// the AWP (press again for the second zoom, a third time to leave) or toggles the
/// M4A1-S silencer, the Edit Item key inspects.
/// </summary>
public sealed class SubsystemScGunBlockBehavior : SubsystemBlockBehavior, IUpdateable, IDrawable {
    sealed class GunState {
        public readonly ScCombatFeedback Feedback = new();
        public ScAmmoHud AmmoHud;
        public double NextShot;
        public double BusyUntil = -1;          // reload or silencer clip in progress
        public ScReloadTransaction Reload;
        public double DropAt = -1, InsertAt = -1;
        public int PendingRounds = -1;         // magazine to write when the reload clip ends
        /// <summary>A shotgun reload: when each shell counts (WPN_RELOAD_ADD_AMMO of each loop).</summary>
        public readonly List<double> ShellTimes = [];
        /// <summary>Fire was pressed during a shell-by-shell reload: shoot as soon as the pump is done.</summary>
        public bool FireAfterReload;
        /// <summary>The R8's hammer is drawn; the cocked shot fires at this time.</summary>
        public double PrepareUntil = -1;
        public double PrepareStartedAt;
        /// <summary>A gun with no reload (the Zeus) has a fresh charge at this time.</summary>
        public double RechargeAt = -1;
        public bool PendingSilencerOff;
        public bool SilencerPending;
        public int Zoom;                       // 0 = hip, 1.. = scope level
        public int RescopeLevel;               // scope level to return to after a shot (CS2: the AWP unscopes for the bolt, then re-zooms)
        public double RescopeAt = -1;
        public float SavedViewAngle = float.NaN;
        public float SavedLookSensitivity = float.NaN;
        public float KickPitch, KickYaw;
        public bool FireLatch;
        /// <summary>Burst mode selected, on the two guns CS2 gives one (Glock-18, FAMAS).</summary>
        public bool BurstMode;
        /// <summary>Shots still owed by the burst in progress, and when the next is due.</summary>
        public int BurstRemaining;
        public double BurstNextAt = -1;
        public int LastValue = int.MinValue;
        /// <summary>Sounds due at a game time: the magazine, bolt and screw noises inside a clip.</summary>
        public readonly List<(double At, string Name)> Scheduled = [];
    }

    /// <summary>
    /// When each sound inside a clip plays, in seconds from the clip start. Timed from the
    /// clips themselves (tools output, 2026-09-04): the magazine part leaving and returning,
    /// the bolt bone's travel, the hand reaching the bolt, the silencer's motion phases.
    /// Files are CS:MC's own (CSMCSoundResources.jar), installed under Audio/ScCsgoKnives.
    /// </summary>
    static readonly Dictionary<string, (float At, string Name)[]> s_clipSounds = new(StringComparer.Ordinal) {
        ["ak47:reload"] = [(0.43f, "ak47_clipout"), (1.13f, "ak47_clipin"), (1.75f, "ak47_boltpull")],
        ["ak47:inspect"] = [(0.20f, "ak47_inspect_f006"), (0.43f, "ak47_inspect_f013"), (3.33f, "ak47_inspect_f100")],
        ["awp:shoot1"] = [(0.73f, "awp_boltback"), (1.00f, "awp_boltforward")],
        ["awp:reload"] = [(0.47f, "awp_clipout"), (1.47f, "awp_clipin"), (1.60f, "awp_cliphit"), (2.73f, "awp_boltback"), (3.03f, "awp_boltforward")],
        ["m4a1s:reload"] = [(0.47f, "m4a1s_clipout"), (1.30f, "m4a1s_clipin"), (1.42f, "m4a1s_cliphit"), (2.00f, "m4a1s_boltforward")],
        ["m4a1s:attach"] = [(0.17f, "m4a1s_silencer_screw_on_start"), (0.97f, "m4a1s_silencer_on"), (1.2f, "m4a1s_silencer_screw_1"), (1.7f, "m4a1s_silencer_screw_2"), (2.2f, "m4a1s_silencer_screw_3"), (2.7f, "m4a1s_silencer_screw_4"), (3.2f, "m4a1s_silencer_screw_5")],
        ["m4a1s:detach"] = [(1.0f, "m4a1s_silencer_screw_1"), (1.4f, "m4a1s_silencer_screw_2"), (1.8f, "m4a1s_silencer_screw_3"), (2.2f, "m4a1s_silencer_screw_4"), (2.6f, "m4a1s_silencer_screw_5"), (3.07f, "m4a1s_silencer_screw_off_end"), (4.13f, "m4a1s_silencer_off")],
    };
    /// <summary>Random variants shipped per event name (name_1 .. name_n).</summary>
    /// <summary>
    /// How many numbered files a cue ships, read from cs2_sound_variants.json, which
    /// tools/install_gun_sounds_cs2.py writes by counting the OGGs it installed.
    ///
    /// It used to be a table here, so adding a gun's sounds meant editing this file as
    /// well, and a count that disagreed with what shipped asked the engine for a file
    /// that is not there.
    /// </summary>
    static readonly Dictionary<string, int> s_variants = Cs2SoundVariants.All;

    /// <summary>Queues the clip's cues; false when neither table has any.</summary>
    bool Schedule(GunState state, string spec, string clip, double startedAt, bool silenced = false) {
        string key = $"{spec}:{clip}";
        // CS2's own event frames when the profile asks for them, else the bone-timed
        // table. Either way a clip CS2 has no cue for falls back to the old row, and
        // a gun the old table never knew - the CS2-only eight - takes the CS2 cues
        // whatever the profile says, since those are the only cues it has.
        bool cs2Sounds = KnifeTuning.GunProfile >= 0.5f || KnifeTuning.GunSoundProfile >= 0.5f;
        if (!cs2Sounds || !Cs2Sounds.TryGet(key, out var list))
            if (!s_clipSounds.TryGetValue(key, out list) && !Cs2Sounds.TryGet(key, out list)) return false;
        foreach ((float at, string name) in list) {
            // The M4A1-S bolt sounds differently with the silencer on (m4a1_silencer_bolt*).
            string n = silenced && name is "m4a1s_boltback" or "m4a1s_boltforward" ? name + "_silenced" : name;
            state.Scheduled.Add((startedAt + at, n));
        }
        return list.Length > 0;
    }

    void PlayScheduled(ComponentPlayer player, GunState state, double now) {
        if (state.Scheduled.Count == 0) return;
        for (int i = state.Scheduled.Count - 1; i >= 0; i--) {
            if (now < state.Scheduled[i].At) continue;
            PlaySound(player, state.Scheduled[i].Name);
            state.Scheduled.RemoveAt(i);
        }
    }

    SubsystemTerrain m_terrain;
    SubsystemBodies m_bodies;
    SubsystemAudio m_audio;
    SubsystemParticles m_particles;
    SubsystemPlayers m_players;
    SubsystemTime m_time;
    readonly Dictionary<ComponentPlayer, GunState> m_states = [];
    readonly Random m_random = new();
    static readonly HashSet<string> s_missingSounds = [];

    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScGunBlock>()];
    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    /// <summary>
    /// The AWP scope mask is drawn here, at order 350 (after the sky at 105 and particles at
    /// 300), not in the first-person pass, so nothing paints over it when the player looks up.
    /// </summary>
    public int[] DrawOrders => [350];

    /// <summary>
    /// A tracer in flight: CS2 fires hitscan and draws a trail running the shot line.
    /// Speed and trail length come from the gun's tracer .vpcf (assault rifle
    /// 20500 units/s over 1200 units, AWP 30000 over 900 - inches, so 521 m/s and
    /// 762 m/s), and every m_nTracerFrequency-th shot gets one, as CS2 does.
    /// </summary>
    readonly struct TracerShot {
        public readonly Vector3 Start, Direction;
        public readonly float Distance;
        public readonly double At;
        public readonly Cs2Effects.Tracer Spec;

        public TracerShot(Vector3 start, Vector3 direction, float distance, double at, Cs2Effects.Tracer spec) {
            Start = start; Direction = direction; Distance = distance; At = at; Spec = spec;
        }
    }

    readonly List<TracerShot> m_tracers = [];
    readonly Dictionary<string, int> m_shotCounts = new(StringComparer.Ordinal);
    PrimitivesRenderer3D m_tracerRenderer;

    void QueueTracer(string gun, Vector3 start, Vector3 direction, float distance) {
        if (KnifeTuning.GunProfile < 0.5f) return;
        int frequency = Cs2Effects.TracerFrequency(gun);
        if (frequency <= 0) return;
        m_shotCounts.TryGetValue(gun, out int n);
        m_shotCounts[gun] = n + 1;
        if ((n + 1) % frequency != 0) return;
        Cs2Effects.Tracer spec = Cs2Effects.Get(gun)?.Tracer;
        if (spec is null) return;
        if (m_tracers.Count > 32) m_tracers.RemoveAt(0);
        m_tracers.Add(new TracerShot(start, direction, distance, m_time.GameTime, spec));
    }

    /// <summary>How many quads the ribbon is cut into; the width is solved per joint.</summary>
    const int TracerSegments = 24;

    Texture2D m_tracerAdd, m_tracerBlend, m_tracerSmg, m_tracerTintable;
    bool m_tracerTexturesTried;

    Texture2D TracerTexture(string name) {
        if (!m_tracerTexturesTried) {
            m_tracerTexturesTried = true;
            try {
                m_tracerAdd = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/cs2_tracer_add");
                m_tracerBlend = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/cs2_tracer_blend");
                // The SMG rope's streak (bullet_tracer_seq), one repeat, head at U = 1.
                m_tracerSmg = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/cs2_tracer_smg");
                // The AUG / SG 553 rope's streak (bullet_tracer_tintable), white in the file.
                m_tracerTintable = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/cs2_tracer_tintable");
            }
            catch (Exception e) {
                KnifeDiagnostics.WarnOnce("cs2-tracer-textures", $"Could not load the CS2 tracer textures: {e.Message}");
            }
        }
        // A pass with no baked texture must not quietly borrow the other one's: the
        // two are different images with different blend modes.
        return name switch {
            "cs2_tracer_add" => m_tracerAdd,
            "cs2_tracer_blend" => m_tracerBlend,
            "cs2_tracer_smg" => m_tracerSmg,
            "cs2_tracer_tintable" => m_tracerTintable,
            _ => null,
        };
    }

    /// <summary>
    /// The CS2 tracer trail, as the two C_OP_RenderTrails passes its .vpcf declares.
    ///
    /// The shape is not a fixed-width quad. Per pass, the half-width is the particle
    /// radius times that pass's m_flRadiusScale - 0.5 x 1 inch and 0.75 x 1 inch for
    /// the assault rifle, 0.5 x 2 and 0.65 x 2 for the AWP - and is then clamped in
    /// screen space to m_flMinSize .. m_flMaxSize of the viewport height, which is what
    /// keeps a trail passing the camera from becoming a plank and a distant one from
    /// dropping below a pixel. The AWP's pass additionally fades out between
    /// m_flStartFadeSize and m_flEndFadeSize, so its trail disappears rather than
    /// filling the screen when it goes by close.
    ///
    /// The head-to-tail gradient and the soft edges are the CS2 textures themselves
    /// (tools/cs2_tracer_texture.py bakes materials/effects/spark and frame 4 of
    /// materials/particle/sparks, transposed so U runs along the trail). The AK and
    /// M4A1-S have a white C_INIT_RandomColor, so the spark texture is the only thing
    /// that colours them; the AWP tints it 247,188,94 .. 255,245,219.
    ///
    /// The one convention not stated in the file: m_flMinSize / m_flMaxSize are read as
    /// fractions of the viewport height applied to the half-width. Marked as an
    /// assumption - it is the reading under which the AK's 0.00075 .. 0.002 keeps a
    /// 0.5-inch trail between about 0.8 and 2 pixels of half-width over its whole
    /// useful range, which no other reading does.
    /// </summary>
    void DrawTracers(Camera camera) {
        if (m_tracers.Count == 0) return;
        m_tracerRenderer ??= new PrimitivesRenderer3D();
        double now = m_time.GameTime;
        Vector3 eye = camera.ViewPosition;
        Vector3 forward = camera.ViewDirection;
        float projY = camera.ProjectionMatrix.M22;
        if (!float.IsFinite(projY) || projY <= 1e-4f) return;

        for (int i = m_tracers.Count - 1; i >= 0; i--) {
            TracerShot t = m_tracers[i];
            Cs2Effects.Tracer spec = t.Spec;
            float speed = spec.MetresPerSecond;
            float age = (float)(now - t.At);
            float travelled = age * speed;
            // C_INIT_MoveBetweenPoints runs the particle to the impact point and
            // C_OP_FadeAndKillForTracers kills it there.
            if (travelled >= t.Distance || t.Distance <= 1e-3f) { m_tracers.RemoveAt(i); continue; }
            float u = travelled / t.Distance;
            float pathAlpha = spec.PathAlpha(u);
            if (pathAlpha <= 0.004f) continue;

            float head = travelled;
            Vector3 headPoint = t.Start + t.Direction * head;
            float fromViewer = Vector3.Distance(headPoint, eye);
            foreach (Cs2Effects.TracerPass pass in spec.Passes ?? []) {
                // m_flLengthFadeInTime: the drawn length grows from nothing over this
                // many seconds, so a fresh tracer is a short streak, not a full bar.
                float tail = MathUtils.Max(0f, head - Cs2Tracer.TrailMetres(spec, pass, age, fromViewer));
                if (head - tail < 1e-4f) continue;
                DrawTrailPass(t, spec, pass, tail, head, pathAlpha, eye, forward, projY);
            }
        }
        m_tracerRenderer.Flush(camera.ViewProjectionMatrix);
    }

    void DrawTrailPass(in TracerShot t, Cs2Effects.Tracer spec, Cs2Effects.TracerPass pass,
                       float tail, float head, float pathAlpha,
                       Vector3 eye, Vector3 forward, float projY) {
        Texture2D texture = TracerTexture(pass.Texture);
        if (texture is null) {
            KnifeDiagnostics.WarnOnce($"cs2-tracer-texture-{pass.Texture ?? "none"}",
                $"No tracer texture for {pass.SourceTexture ?? "an unnamed pass"}; that pass is not drawn.");
            return;
        }
        float halfWorld = spec.HalfWidthMetres(pass);
        if (halfWorld <= 0f) return;

        TexturedBatch3D batch = m_tracerRenderer.TexturedBatch(texture, useAlphaTest: false, layer: 0,
            DepthStencilState.DepthRead, RasterizerState.CullNoneScissor,
            BlendState.Additive, SamplerState.LinearClamp);
        if (!pass.BlendUnderstood)
            KnifeDiagnostics.WarnOnce($"cs2-tracer-blend-{pass.Blend}",
                $"CS2 asks for {pass.Blend} on the tracer trail; drawn additively.");

        Color tint = spec.Tint;
        Vector3 previous = default, previousSide = default;
        float previousFade = 0f;
        bool hasPrevious = false;
        for (int k = 0; k <= TracerSegments; k++) {
            float f = k / (float)TracerSegments;
            Vector3 p = t.Start + t.Direction * MathUtils.Lerp(tail, head, f);
            Vector3 toEye = p - eye;
            float depth = Vector3.Dot(toEye, forward);
            float half = Cs2Tracer.HalfWidth(spec, pass, depth, projY, out float fade);
            // Degenerate only where the trail runs exactly through the eye axis. The
            // joint is dropped, and so is the quad that would have used it: carrying
            // `previous` across the gap would stretch a segment over the whole hole.
            Vector3 side = Vector3.Cross(t.Direction, toEye);
            float length = side.Length();
            if (!float.IsFinite(length) || length < 1e-6f) { hasPrevious = false; continue; }
            side = side * (half / length);

            if (hasPrevious && previousFade + fade > 0f) {
                float a0 = spec.AlphaMid * pathAlpha * previousFade;
                float a1 = spec.AlphaMid * pathAlpha * fade;
                var c0 = new Color(tint.R, tint.G, tint.B, (byte)MathUtils.Clamp(255f * a0, 0f, 255f));
                var c1 = new Color(tint.R, tint.G, tint.B, (byte)MathUtils.Clamp(255f * a1, 0f, 255f));
                float u0 = (k - 1) / (float)TracerSegments, u1 = f;
                // U runs tail (0) to head (1); V crosses the width. Both clamp, so the
                // ramp the texture carries is drawn once over the trail rather than
                // tiled - m_flFinalTextureScaleU = 5 is recorded as unmodelled.
                batch.QueueTriangle(previous - previousSide, previous + previousSide, p + side,
                                    new Vector2(u0, 1f), new Vector2(u0, 0f), new Vector2(u1, 0f), c0);
                batch.QueueTriangle(previous - previousSide, p + side, p - side,
                                    new Vector2(u0, 1f), new Vector2(u1, 0f), new Vector2(u1, 1f), c1);
            }
            previous = p;
            previousSide = side;
            previousFade = fade;
            hasPrevious = true;
        }
    }

    // ---- the Zeus ------------------------------------------------------------------

    /// <summary>
    /// One Zeus shot in the world: CS2's weapon_tracers_taser - the arc drawn over
    /// the wires from the muzzle to the trace end, the glow and sparks at the end.
    /// The muzzle systems (weapon_muzzle_flash_taser) ride the first-person weapon
    /// instead: CsmcFirstPersonRenderer.ZeusMuzzle.
    /// </summary>
    sealed class ZeusShot {
        public Vector3 Muzzle, End, Direction;
        public double At;
        public bool Hit;
        public Color ArcTint;
        public float ArcScroll;
        public List<Cs2ZeusParticles.Sprite> ImpactGlow;
        public List<Cs2ZeusParticles.Spark> ImpactSparks;
    }

    readonly List<ZeusShot> m_zeus = [];

    /// <summary>The ropes' texture scroll, repeats per second: CS2 drives m_flTextureVScrollRate by noise, so a constant per shot is assumed.</summary>
    const float ArcScrollMin = 2f, ArcScrollMax = 4f;          // assumed
    /// <summary>World length of one repeat of the arc texture along the rope (m_flTextureVWorldSize is noise-driven too).</summary>
    const float ArcTextureMetres = 0.5f;                       // assumed
    /// <summary>The effect is gone by then (the arc ends at 0.45 s, the longest sparks at 0.4 s).</summary>
    const float ZeusSeconds = 1f;

    void QueueZeus(Vector3 muzzle, bool muzzleSolved, Vector3 end, Vector3 direction, bool hit) {
        Cs2TaserEffect.File fx = Cs2TaserEffect.Data;
        if (fx is null || KnifeTuning.GunProfile < 0.5f) return;
        var shot = new ZeusShot {
            Muzzle = muzzle, End = end, Direction = direction, At = m_time.GameTime, Hit = hit,
            ArcTint = Cs2ZeusParticles.LerpColor(fx.Arc.ColorMin, fx.Arc.ColorMax, m_random.Float(0f, 1f)),
            ArcScroll = m_random.Float(ArcScrollMin, ArcScrollMax),
            ImpactGlow = Cs2ZeusParticles.Sprites(fx.ImpactGlow),
        };
        // The impact sparks fly off the surface: CS2's CP1 frame faces back along the trace.
        if (hit) {
            Vector3 back = -direction;
            Vector3 side = Vector3.Cross(back, Vector3.UnitY);
            if (side.LengthSquared() < 1e-6f) side = Vector3.UnitX;
            side = Vector3.Normalize(side);
            Vector3 up = Vector3.Normalize(Vector3.Cross(side, back));
            shot.ImpactSparks = Cs2ZeusParticles.Sparks(fx.ImpactSparks, end, back, side, up);
        }
        if (m_zeus.Count > 8) m_zeus.RemoveAt(0);
        m_zeus.Add(shot);
        CsmcFirstPersonRenderer.ZeusMuzzle(KnifeClock.Now);
        // Once per shot, so a device log says where the arc started and ended.
        KnifeLog.Information($"[ScCsgoKnives] Zeus shot: arc from {(muzzleSolved ? "the drawn muzzle" : "the eye (muzzle not solved this frame)")} "
            + $"({muzzle.X:0.##},{muzzle.Y:0.##},{muzzle.Z:0.##}) to ({end.X:0.##},{end.Y:0.##},{end.Z:0.##}), "
            + $"{Vector3.Distance(muzzle, end):0.##} m, hit={hit}.");
    }

    void DrawZeus(Camera camera) {
        if (m_zeus.Count == 0) return;
        Cs2TaserEffect.File fx = Cs2TaserEffect.Data;
        if (fx is null) return;
        m_tracerRenderer ??= new PrimitivesRenderer3D();
        double now = m_time.GameTime;
        Vector3 eye = camera.ViewPosition, right = camera.ViewRight, up = camera.ViewUp;
        for (int i = m_zeus.Count - 1; i >= 0; i--) {
            ZeusShot shot = m_zeus[i];
            float age = (float)(now - shot.At);
            if (age > ZeusSeconds) { m_zeus.RemoveAt(i); continue; }
            // The arc's start follows the drawn muzzle while the gun is still the drawn one (C_OP_PositionLock to CP0).
            Vector3 muzzle = CsmcFirstPersonRenderer.TryGetMuzzleWorld(fx.Gun, false, out Vector3 m) ? m : shot.Muzzle;
            DrawZeusArc(shot, fx.Arc, age, muzzle, eye);
            Cs2ZeusParticles.DrawSprites(m_tracerRenderer, shot.ImpactGlow, fx.ImpactGlow, age, shot.End, right, up, DepthStencilState.DepthRead);
            Cs2ZeusParticles.DrawSparks(m_tracerRenderer, shot.ImpactSparks, fx.ImpactSparks, age, eye, Vector3.UnitY, DepthStencilState.DepthRead);
        }
        m_tracerRenderer.Flush(camera.ViewProjectionMatrix);
    }

    /// <summary>
    /// weapon_tracers_taser_wire2: 15 particles from the wire's path, from 0.05 s for
    /// 0.4 s, radius 0..2 in by index shrinking to 0.35 with bias 0.85, gravity on the
    /// free middle (the ends dampened to the control points within 25 in), fading out
    /// over the last 0.1 s; two C_OP_RenderRopes passes at radius scale 0.5.
    /// </summary>
    void DrawZeusArc(ZeusShot shot, Cs2TaserEffect.Arc arc, float age, Vector3 muzzle, Vector3 eye) {
        if (arc?.Passes is null) return;
        float t = age - arc.StartSeconds;
        if (t < 0f || t >= arc.Life) return;
        int n = Math.Max(2, (int)MathF.Round(arc.Points));
        float f = t / arc.Life;
        float fadeSeconds = arc.FadeOut?.Seconds ?? 0f;
        float alpha = fadeSeconds > 0f && arc.Life - t < fadeSeconds ? (arc.Life - t) / fadeSeconds : 1f;
        float scale = arc.Radius?.At(f) ?? 1f;
        float drop = 0.5f * MathF.Abs(arc.Movement?.GravityMetres ?? 0f) * t * t;
        float hold = arc.DampenRangeInches * Cs2Placement.InchesToEngine;
        Vector3 start = muzzle, end = shot.End;
        float length = Vector3.Distance(start, end);
        if (length < 1e-3f) return;
        Vector3[] points = new Vector3[n];
        float[] half = new float[n];
        for (int k = 0; k < n; k++) {
            float u = k / (float)(n - 1);
            Vector3 p = Vector3.Lerp(start, end, u);
            float free = hold > 0f ? MathUtils.Saturate(MathUtils.Min(u * length, (1f - u) * length) / hold) : 1f;
            p.Y -= drop * free;
            points[k] = p;
            half[k] = arc.RadiusInchesAt(k) * Cs2Placement.InchesToEngine * scale;
        }
        float repeats = MathF.Max(1f, length / ArcTextureMetres);
        for (int passIndex = 0; passIndex < arc.Passes.Length; passIndex++) {
            Cs2TaserEffect.RopePass pass = arc.Passes[passIndex];
            string source = pass.Textures is { Length: > 0 } ? pass.Textures[Math.Min(passIndex, pass.Textures.Length - 1)] : null;
            Texture2D texture = Cs2ZeusParticles.Texture(source);
            if (texture is null) continue;
            float scroll = shot.ArcScroll * age + passIndex * 0.37f;
            QueueRope(texture, points, half, pass.RadiusScale ?? 1f, shot.ArcTint, alpha, repeats, scroll, eye);
        }
    }

    void QueueRope(Texture2D texture, Vector3[] points, float[] half, float radiusScale, Color tint, float alpha,
                   float repeats, float scroll, Vector3 eye) {
        TexturedBatch3D batch = m_tracerRenderer.TexturedBatch(texture, useAlphaTest: false, layer: 0,
            DepthStencilState.DepthRead, RasterizerState.CullNoneScissor, BlendState.Additive, SamplerState.LinearWrap);
        Color col = new(tint.R, tint.G, tint.B, (byte)MathUtils.Clamp(255f * alpha, 0f, 255f));
        Vector3 previous = default, previousSide = default;
        float previousU = 0f;
        bool hasPrevious = false;
        for (int k = 0; k < points.Length; k++) {
            Vector3 p = points[k];
            Vector3 along = k + 1 < points.Length ? points[k + 1] - p : p - points[k - 1];
            Vector3 side = Vector3.Cross(along, p - eye);
            float l = side.Length();
            if (!float.IsFinite(l) || l < 1e-6f) { hasPrevious = false; continue; }
            side *= half[k] * radiusScale / l;
            float u = k / (float)(points.Length - 1) * repeats + scroll;
            if (hasPrevious) {
                batch.QueueTriangle(previous - previousSide, previous + previousSide, p + side,
                                    new Vector2(previousU, 1f), new Vector2(previousU, 0f), new Vector2(u, 0f), col);
                batch.QueueTriangle(previous - previousSide, p + side, p - side,
                                    new Vector2(previousU, 1f), new Vector2(u, 0f), new Vector2(u, 1f), col);
            }
            previous = p;
            previousSide = side;
            previousU = u;
            hasPrevious = true;
        }
    }

    public void Draw(Camera camera, int drawOrder) {
        // Our batches leave their own blend and depth states behind; the engine's
        // later draws get back what they had.
        BlendState blend = Display.BlendState;
        DepthStencilState depth = Display.DepthStencilState;
        RasterizerState rasterizer = Display.RasterizerState;
        try {
            DrawTracers(camera);
            DrawZeus(camera);
            if (CsmcFirstPersonRenderer.ScopeOverlayActive) CsmcFirstPersonRenderer.DrawScopeOverlay();
            var player = camera.GameWidget.PlayerData.ComponentPlayer;
            if (player is not null && m_states.TryGetValue(player, out var state)) state.Feedback.Draw(camera, m_time.GameTime);
        }
        finally {
            Display.BlendState = blend;
            Display.DepthStencilState = depth;
            Display.RasterizerState = rasterizer;
        }
    }

    public override void Dispose() {
        foreach (var state in m_states.Values) state.AmmoHud?.Dispose();
        m_states.Clear();
        Project.FindSubsystem<SubsystemDrawing>(false)?.RemoveDrawable(this);
        base.Dispose();
    }

    public void ReportHit(ComponentPlayer player, ComponentBody body, int weapon, Vector3 point, int outcome, double now) {
        if (!m_states.TryGetValue(player, out var state)) m_states[player] = state = new GunState();
        string name = BlocksManager.Blocks[Terrain.ExtractContents(weapon)].GetDisplayName(m_terrain, weapon);
        bool sound = outcome == 2 && now - state.Feedback.KillAt > .07;
        state.Feedback.Record(outcome, body.Entity.FindComponent<ComponentCreature>()?.DisplayName ?? "生物", name,
            Vector3.Distance(player.ComponentCreatureModel.EyePosition, point), now);
        if (sound) ScCombatAudio.PlayKill();
    }

    /// <summary>The Zeus recharge times read from the world, by player index, until each player's state exists.</summary>
    readonly Dictionary<int, double> m_savedRecharge = [];
    const string RechargeKey = "ZeusRechargeAt";

    public override void Load(ValuesDictionary valuesDictionary) {
        base.Load(valuesDictionary);
        ValuesDictionary saved = valuesDictionary.GetValue<ValuesDictionary>(RechargeKey, null);
        if (saved is not null) {
            foreach (KeyValuePair<string, object> kv in saved) {
                if (int.TryParse(kv.Key, out int index) && kv.Value is double at) m_savedRecharge[index] = at;
            }
        }
        m_terrain = Project.FindSubsystem<SubsystemTerrain>(true);
        // The engine logs an ERROR when a drawable is added twice, and this Load can
        // run again on a project reload. AddDrawable itself is a TryAdd and does not
        // throw, so the only damage was the error line - removed rather than left to
        // be read as a real fault next time someone reads the log.
        SubsystemDrawing drawing = Project.FindSubsystem<SubsystemDrawing>(true);
        drawing.RemoveDrawable(this);
        drawing.AddDrawable(this);
        m_bodies = Project.FindSubsystem<SubsystemBodies>(true);
        m_audio = Project.FindSubsystem<SubsystemAudio>(true);
        m_particles = Project.FindSubsystem<SubsystemParticles>(true);
        m_players = Project.FindSubsystem<SubsystemPlayers>(true);
        m_time = Project.FindSubsystem<SubsystemTime>(true);
    }

    public override void Save(ValuesDictionary valuesDictionary) {
        base.Save(valuesDictionary);
        var saved = new ValuesDictionary();
        foreach ((int index, double at) in m_savedRecharge) saved.SetValue(index.ToString(), at);
        foreach ((ComponentPlayer player, GunState state) in m_states) {
            if (player.PlayerData is null) continue;
            string key = player.PlayerData.PlayerIndex.ToString();
            if (state.RechargeAt >= 0) saved.SetValue(key, state.RechargeAt);
            else if (saved.ContainsKey(key)) saved.Remove(key);
        }
        valuesDictionary.SetValue(RechargeKey, saved);
    }

    public void Update(float dt) {
        KnifeQa.Step();
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        foreach (var pair in m_states) if (!m_players.ComponentPlayers.Contains(pair.Key)) {
            pair.Value.AmmoHud?.Dispose(); pair.Value.AmmoHud = null;
        }
        foreach (ComponentPlayer player in m_players.ComponentPlayers) {
            if (!m_states.TryGetValue(player, out GunState state)) {
                m_states[player] = state = new GunState();
                if (m_savedRecharge.Remove(player.PlayerData.PlayerIndex, out double at))
                    state.RechargeAt = double.IsFinite(at) ? Math.Min(at, m_time.GameTime + GunSpec.ForAsset("taser").RechargeSeconds) : -1;
            }
            int value = player.ComponentMiner.ActiveBlockValue;
            bool holdingGun = Terrain.ExtractContents(value) == gunIndex && ScGunBlock.IsKnown(value) && player.ComponentHealth.Health > 0f;
            if (!holdingGun) {
                state.AmmoHud?.Hide();
                CancelReload(player, state);
                LeaveScope(player, state);
                RecoverKick(player, state, dt, 12f);
                state.BusyUntil = -1;
                state.PendingRounds = -1;
                state.SilencerPending = false;
                state.Scheduled.Clear();
                state.LastValue = int.MinValue;
                continue;
            }
            UpdateGun(player, state, value, dt);
            UpdateAmmoHud(player, state);
        }
    }

    void UpdateGun(ComponentPlayer player, GunState state, int value, float dt) {
        GunSpec spec = ScGunBlock.SpecOf(value);
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        double now = m_time.GameTime;
        int data = Terrain.ExtractData(value);
        int rounds = GunSpec.GetRounds(data);
        PlayerInput input = player.ComponentInput.PlayerInput;
        if (state.Reload is not null && (!state.Reload.Valid
            || !state.Reload.ModeMatches(Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode==GameMode.Creative)
            || player.ComponentGui.ModalPanelWidget is not null || DialogsManager.HasDialogs(player.GuiWidget)))
            CancelReload(player, state);
        if (player.ComponentGui.ModalPanelWidget is not null || DialogsManager.HasDialogs(player.GuiWidget)) return;
        if (state.Reload is not null) {
            if (state.DropAt >= 0 && now >= state.DropAt) {
                state.DropAt = -1;
                if (!state.Reload.Discard()) CancelReload(player, state);
            }
            if (state.Reload is not null && state.InsertAt >= 0 && now >= state.InsertAt) {
                state.InsertAt = -1;
                if (!state.Reload.FinishMagazine(now, state.BusyUntil)) CancelReload(player, state);
            }
            value = player.ComponentMiner.ActiveBlockValue;
            data = Terrain.ExtractData(value); rounds = GunSpec.GetRounds(data);
            state.LastValue = value;
        }


        if (value != state.LastValue) {
            if (Terrain.ExtractContents(state.LastValue) != Terrain.ExtractContents(value) || ScGunBlock.GetVariant(state.LastValue) != ScGunBlock.GetVariant(value)) {
                LeaveScope(player, state);
                // The USP-S draws with draw_silenced_pistol when its silencer is on; the
                // controller picks the same clip, so the busy time and the cues match it.
                int drawnVariant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
                string deployClip = KnifeAnimationController.DeployClip(drawnVariant, spec.HasSilencer && !GunSpec.GetSilencerOff(data));
                state.BusyUntil = now + CsmcKnifeRig.GetProfileDuration(drawnVariant, deployClip);
                state.PendingRounds = -1;
                state.SilencerPending = false;
                state.Scheduled.Clear();
                state.ShellTimes.Clear();
                state.FireAfterReload = false;
                state.PrepareUntil = -1;
                // The draw clip's own cues where CS2 has them - the FAMAS and M4A4 work
                // the bolt quietly during theirs - else the single draw file.
                if (!Schedule(state, spec.Name, deployClip, now)) PlaySound(player, $"{spec.Name}_draw");
            }
            state.LastValue = value;
        }

        PlayScheduled(player, state, now);
        // A shotgun's shells count one at a time, at each loop's add-ammo moment.
        while (state.ShellTimes.Count > 0 && now >= state.ShellTimes[0]) {
            state.ShellTimes.RemoveAt(0);
            if (state.Reload is not null && state.Reload.InsertShell()) {
                value = state.Reload.Expected; state.LastValue = value;
                data = Terrain.ExtractData(value); rounds = GunSpec.GetRounds(data);
            }
            else { CancelReload(player, state); break; }
        }

        // A reload or silencer clip that has run its course commits its result.
        if (state.BusyUntil >= 0 && now >= state.BusyUntil) {
            state.BusyUntil = -1;
            state.Reload = null;
            if (state.SilencerPending) {
                state.SilencerPending = false;
                data = GunSpec.SetSilencerOff(data, state.PendingSilencerOff);
                value = WriteData(player, value, data);
            }
        }
        bool busy = state.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model);
        if (state.RescopeAt >= 0 && now >= state.RescopeAt) {
            state.RescopeAt = -1;
            if (!busy && state.Zoom == 0 && rounds > 0 && spec.ZoomLevels.Length > 0) {
                SetZoom(player, state, spec, state.RescopeLevel);
                PlaySound(player, $"{spec.Name}_zoom");
            }
        }

        // Reload: R, or the trigger on an empty magazine.
        bool wantsFire = spec.Automatic ? input.Dig.HasValue || input.Hit.HasValue : input.Hit.HasValue && !state.FireLatch;
        state.FireLatch = input.Dig.HasValue || input.Hit.HasValue;
        bool reloadKey = Keyboard.IsKeyDownOnce(Key.R);
        // The Zeus: a fresh charge after its recharge time, announced by CS2's own cue.
        // Only a gun that recharges reads or clears the timer: 0.20.1 let whichever
        // gun was held at the 30 s mark consume it (and top itself up), so a Zeus put
        // away and picked up again started over. The timer is game time, saved with
        // the world (Save below), so it also survives leaving and reloading.
        if (spec.RechargeSeconds > 0f) {
            if (rounds < spec.Magazine && state.RechargeAt < 0) state.RechargeAt = now + spec.RechargeSeconds;
            if (state.RechargeAt >= 0 && now >= state.RechargeAt) {
                state.RechargeAt = -1;
                if (rounds < spec.Magazine) {
                    rounds = spec.Magazine;
                    data = GunSpec.SetRounds(data, rounds);
                    value = WriteData(player, value, data);
                    PlaySound(player, $"{spec.Name}_chargeready");
                }
            }
        }
        // The R8's cocked shot: the hammer has been drawn for the cycle time, the shot goes.
        if (state.PrepareUntil >= 0 && now >= state.PrepareUntil) {
            state.PrepareUntil = -1;
            if (rounds > 0 && !busy) Fire(player, state, model, spec, value, data, rounds, input, cycleFrom: state.PrepareStartedAt);
            return;
        }
        if (state.PrepareUntil >= 0) return;
        // Fire during a shell-by-shell reload cuts it short after the shell in hand.
        if (busy && wantsFire && rounds > 0 && state.ShellTimes.Count > 0 && !state.FireAfterReload) {
            CutReloadShort(player, state, spec, now);
            return;
        }
        if (!busy && state.FireAfterReload) {
            state.FireAfterReload = false;
            if (rounds > 0 && now >= state.NextShot) wantsFire = true;
        }
        if (!busy && rounds < spec.Magazine && (reloadKey || (wantsFire && rounds == 0))) {
            if (spec.RechargeSeconds > 0f) {
                // The persistent ammo HUD already shows the live charge countdown.
                return;
            }
            StartReload(player, state, model, spec, value);
            return;
        }

        // The rest of a burst fires on its own clock and does not ask the trigger
        // again: CS2's burst is committed once started, and stops only when the
        // magazine runs out.
        if (state.BurstRemaining > 0 && state.BurstNextAt >= 0 && now >= state.BurstNextAt) {
            if (busy || rounds <= 0) {
                state.BurstRemaining = 0;
                state.BurstNextAt = -1;
            }
            else {
                // Decrementing before Fire left BurstRemaining at 0 on the last round,
                // which Fire read as "no burst in progress" and used to start another -
                // one click emptied the magazine. The count is lowered after the shot.
                Fire(player, state, model, spec, value, data, rounds, input, inBurst: true);
                state.BurstRemaining--;
                state.BurstNextAt = state.BurstRemaining > 0 ? now + spec.BurstShotSeconds : -1;
                return;
            }
        }

        if (!busy && wantsFire && rounds > 0 && now >= state.NextShot && state.BurstRemaining == 0) {
            // The R8 draws its hammer first (prepare_shoot_revolver) and fires when the
            // cycle time is up. The vdata gives no separate hammer time, so the primary
            // m_flCycleTime (0.5 s) is taken as it - assumed, the one number here that is.
            if (spec.CycleSecondsAlternate > 0f && KnifeAnimationController.TriggerPrepare(player)) {
                state.PrepareStartedAt = now;
                state.PrepareUntil = now + spec.CycleSeconds;
                state.NextShot = now + spec.CycleSeconds;
                return;
            }
            Fire(player, state, model, spec, value, data, rounds, input);
        }

        RecoverKick(player, state, dt,
            Cs2Weapons.Kick(spec.Name, false, spec.KickPitchDegrees, spec.KickYawDegrees,
                spec.KickRecoverPerSecond).Recover);
    }

    void Fire(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value, int data, int rounds, PlayerInput input,
              bool inBurst = false, bool alternateFire = false, double? cycleFrom = null) {
        double now = m_time.GameTime;
        int roundsBefore = rounds;
        // A burst costs its own cycle time once, not one per round: CS2's Glock-18
        // takes 0.5 s for the burst against 0.15 s for a single shot, the FAMAS 0.55
        // against 0.09. The remaining rounds are scheduled at m_flTimeBetweenBurstShots.
        // inBurst says this shot is one of those, so it cannot start another.
        bool startingBurst = !inBurst && state.BurstMode && spec.HasBurstMode && state.BurstRemaining == 0;
        if (startingBurst) {
            state.NextShot = now + spec.BurstCycleSeconds;
            state.BurstRemaining = Math.Max(0, spec.BurstShots - 1);
            state.BurstNextAt = state.BurstRemaining > 0 ? now + spec.BurstShotSeconds : -1;
        }
        else if (alternateFire) {
            // The R8's fanned shot: the vdata pair's second cycle time.
            state.NextShot = now + spec.CycleSecondsAlternate;
        }
        else if (!inBurst) {
            // The cycle counts from the press: for the R8's cocked shot that is when the
            // hammer started back, not when it fell.
            state.NextShot = (cycleFrom ?? now) + spec.CycleSeconds;
        }
        rounds--;
        value = WriteData(player, value, GunSpec.SetRounds(data, rounds));
        // A detachable silencer that is on, or an integral one (the MP5-SD): the
        // flash, the muzzle and the kick follow it. Only the detachable kind has a
        // separate sound file; the integral one's WEAPON_SOUND_SINGLE is already
        // the suppressed shot.
        bool silenced = spec.SilencedAlways || (spec.HasSilencer && !GunSpec.GetSilencerOff(data));
        // The round that empties the magazine locks a pistol's slide back (shoot_empty).
        bool lastRound = rounds <= 0;
        bool scopedShot = state.Zoom > 0;
        // Capture before automatic unzoom, animation callbacks or recoil can change aim state.
        var shot = ScShotAim.Capture(spec.Name, ScMobileControls.UsesTouchInput(player), scopedShot, silenced,
            alternateFire, input.Dig, input.Hit, LookRay(player), player.ComponentBody.Velocity.Length(), spec.SpreadDegrees);
        if (scopedShot && spec.UnzoomsAfterShot) {
            // CS2's m_bUnzoomsAfterShot (AWP, SSG 08): a scoped shot drops the scope for
            // the bolt cycle and re-zooms to the same level afterwards. The auto-snipers
            // and the AUG / SG 553 have it false and fire with the scope up.
            state.RescopeLevel = state.Zoom;
            state.RescopeAt = now + spec.CycleSeconds;
            LeaveScope(player, state);
        }
        KnifeAnimationController.TriggerShoot(player, silenced, lastRound, scopedShot && !spec.UnzoomsAfterShot, alternateFire,
            spec.LeftMuzzleBone is not null ? roundsBefore : -1);
        // The Dual Berettas flash and trace from the gun that fired.
        string shotClip = KnifeAnimationController.CurrentClip(model);
        string muzzleBone = silenced ? spec.SilencedMuzzleBone
            : spec.LeftMuzzleBone is not null && shotClip is "shootLeft" or "shootLeftLast" ? spec.LeftMuzzleBone
            : spec.MuzzleBone;
        if (spec.MuzzleEffects)
            CsmcFirstPersonRenderer.MuzzleFlash(silenced ? 0.03f : 0.06f, muzzleBone, spec.Name, silenced);
        PlaySound(player, spec.HasSilencer && silenced ? $"{spec.Name}_fire_silenced" : $"{spec.Name}_fire");
        // No reload: the Zeus starts its ten-second recharge at the shot.
        if (rounds <= 0 && spec.RechargeSeconds > 0f) state.RechargeAt = now + spec.RechargeSeconds;
        if (!spec.Automatic) Schedule(state, spec.Name, KnifeAnimationController.CurrentClip(model) ?? "shoot1", now);


        // Camera kick, applied now and eased back in RecoverKick. The cs2 profile takes
        // the ratios between guns from m_flRecoilMagnitude and the yaw scatter from
        // m_flRecoilAngleVariance; the absolute scale is still the fitted AK value.
        bool alternate = shot.Alternate;
        (float kickPitch, float kickYaw, float _) = Cs2Weapons.Kick(spec.Name, alternate,
            spec.KickPitchDegrees, spec.KickYawDegrees, spec.KickRecoverPerSecond);
        float pitch = MathUtils.DegToRad(kickPitch) * (0.8f + 0.4f * m_random.Float(0f, 1f));
        float yaw = MathUtils.DegToRad(kickYaw) * m_random.Float(-1f, 1f);
        Ray3 ray = shot.Ray;
        Kick(player, state, pitch, yaw);

        // Hitscan along the view ray with a small random cone.
        // The press's own ray where there is one. A shot that fires later than the
        // press - the R8's cocked shot half a second after the click, its fanned shot
        // on the aim key - has none, and the fallback here used +Z of the eye rotation,
        // which in this engine is *behind* the player (Matrix.Forward is -Z): the R8
        // shot backwards and hit nothing (0.20.2). The camera's own ray is what
        // ComponentInput builds Dig and Hit from.
        // CS keeps a separate inaccuracy per stance; the cs2 profile blends the vdata's
        // standing and moving values by speed instead of scaling one cone by a constant.
        float spread = shot.Spread;
        // A shotgun fires m_nNumBullets pellets on one trigger pull, each with its own
        // scatter: Nova 9, MAG-7 and Sawed-Off 8, XM1014 6. Every other gun is 1, and
        // the body below is then exactly the single shot it always was.
        int pellets = Math.Max(1, spec.Pellets);
        var hits = new Dictionary<ComponentBody, (float Power, Vector3 Point, Vector3 Direction)>();
        for (int pellet = 0; pellet < pellets; pellet++) {
            Vector3 direction = Scatter(ray.Direction, spread);
            Vector3 start = ray.Position;
            Vector3 end = start + direction * spec.RangeBlocks;
            BodyRaycastResult? body = m_bodies.Raycast(start, end, 0.35f, (b, d) => b != player.ComponentBody && b.Entity != player.Entity);
            TerrainRaycastResult? terrain = m_terrain.Raycast(start, end, false, true, (v, d) => Terrain.ExtractContents(v) != 0 && BlocksManager.Blocks[Terrain.ExtractContents(v)] is not FluidBlock);
            // The tracer runs the shot line, stopping at whatever the bullet hit.
            float travel = spec.RangeBlocks;
            if (body.HasValue) travel = MathUtils.Min(travel, body.Value.Distance);
            if (terrain.HasValue) travel = MathUtils.Min(travel, terrain.Value.Distance);
            // The tracer leaves the muzzle the player can see, not the hit-detection ray's
            // origin at the eye. The weapon is drawn in CS2's viewmodel projection, so the
            // renderer solves for a world point that lands on the drawn muzzle under the
            // game camera; without one - no cs2 profile, or the gun not drawn this frame -
            // the shot ray's origin is used, which is what every earlier version did.
            Vector3 impact = start + direction * travel;
            Vector3 tracerStart = spec.MuzzleEffects && CsmcFirstPersonRenderer.TryGetMuzzleWorld(spec.Name, silenced, out Vector3 muzzle, muzzleBone)
                ? muzzle : start;
            Vector3 tracerDirection = impact - tracerStart;
            float tracerTravel = tracerDirection.Length();
            if (spec.MuzzleEffects && tracerTravel > 1e-3f) QueueTracer(spec.Name, tracerStart, tracerDirection / tracerTravel, tracerTravel);
            // The Zeus draws no flash sprite and no ribbon; its own effect runs from the
            // drawn muzzle to wherever the trace ended (CS2's CP1), sparks only on a hit.
            if (Cs2TaserEffect.Applies(spec.Name)) {
                bool solved = CsmcFirstPersonRenderer.TryGetMuzzleWorld(spec.Name, false, out Vector3 zm);
                QueueZeus(solved ? zm : start, solved, impact, direction, body.HasValue || terrain.HasValue);
            }
            if (body.HasValue && (!terrain.HasValue || body.Value.Distance < terrain.Value.Distance)) {
                Vector3 hitPoint = body.Value.HitPoint();
                // Survival damage is a per-shot budget, shared across pellets.
                float power = ScSurvivalBalance.PelletPower(spec, body.Value.Distance);
                var target = body.Value.ComponentBody;
                hits.TryGetValue(target, out var prior);
                hits[target] = (prior.Power + power, hitPoint, direction);
            }
            else if (terrain.HasValue) {
                Vector3 hitPoint = start + direction * terrain.Value.Distance;
                int hitValue = terrain.Value.Value;
                int contents = Terrain.ExtractContents(hitValue);
                Block block = BlocksManager.Blocks[contents];
                int slot = block.GetFaceTextureSlot(terrain.Value.CellFace.Face, hitValue);
                m_particles.AddParticleSystem(new BlockDebrisParticleSystem(m_terrain, hitPoint, 0.45f, 1f, Color.White, slot));
                string material = block.GetSoundMaterialName(m_terrain, hitValue);
                if (!string.IsNullOrEmpty(material)) m_audio.PlayRandomSound("Audio/Impacts/" + material, 0.7f, m_random.Float(-0.2f, 0.2f), hitPoint, 6f, true);
            }
        }
        foreach (var hit in hits)
            ScSurvivalBalance.Attack(hit.Key, player, hit.Value.Point, hit.Value.Direction, hit.Value.Power, now, zeus: spec.RechargeSeconds > 0);
    }

    void CancelReload(ComponentPlayer player, GunState state) {
        if (state.Reload is null) return;
        state.Reload.Cancel(); state.Reload = null;
        state.DropAt = state.InsertAt = state.BusyUntil = -1;
        state.ShellTimes.Clear(); state.Scheduled.Clear(); state.FireAfterReload = false;
        KnifeAnimationController.CancelAction(player);
    }

    public void RequestReload(ComponentPlayer player) {
        int value = player.ComponentMiner.ActiveBlockValue;
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || player.ComponentHealth.Health <= 0) return;
        if (!m_states.TryGetValue(player, out GunState state)) return;
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (state.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model)) return;
        StartReload(player, state, model, ScGunBlock.SpecOf(value), value);
    }

    void StartReload(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value) {
        int rounds = GunSpec.GetRounds(Terrain.ExtractData(value));
        if (rounds >= spec.Magazine || spec.RechargeSeconds > 0) return;
        bool creative = Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode == GameMode.Creative;
        IInventory inventory = player.ComponentMiner.Inventory;
        int ammo = ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec));
        int cost = creative ? 0 : ScReloadTransaction.Required(spec);
        int available = creative ? 0 : ScInventoryTransaction.Count(inventory, ammo);
        if (available < cost) {
            player.ComponentGui.DisplaySmallMessage($"装填需要{(spec.Pellets > 1 ? "霰弹" : "通用弹匣")} ×{cost}（现有 {available}）", Color.Red, false, false);
            return;
        }
        int variant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
        bool empty = rounds == 0;
        string clip = KnifeAnimationController.ReloadClip(variant, empty);
        bool tube = ScReloadTransaction.IsTube(spec.Name);
        int shells = tube && !creative ? Math.Min(spec.Magazine - rounds, available) : spec.Magazine - rounds;
        float duration = KnifeAnimationController.ReloadSeconds(variant, empty, shells);
        var milestones = Cs2Rig.ReloadMilestones(spec.Name, clip);
        var sections = tube ? Cs2Rig.GetReloadSections(spec.Name) : null;
        if (duration <= 0 || (tube ? sections is null : milestones is null)) {
            player.ComponentGui.DisplaySmallMessage("该武器缺少可靠装填事件，未消耗弹药。", Color.Red, false, false);
            return;
        }
        LeaveScope(player, state);
        KnifeAnimationController.TriggerReload(player, empty, shells);
        state.Reload = new ScReloadTransaction(inventory, inventory.ActiveSlotIndex, value, ammo, cost, spec.Magazine);
        state.Scheduled.Clear(); state.ShellTimes.Clear(); state.FireAfterReload = false;
        double now = m_time.GameTime;
        state.BusyUntil = now + duration; state.PendingRounds = -1;
        state.DropAt = state.InsertAt = -1;
        if (tube) {
            for (int k = 0; k < shells; k++) state.ShellTimes.Add(now + sections.LoopStart + k * sections.LoopLength + sections.AddAmmoInLoop);
            ScheduleLooped(state, spec.Name, clip, now, sections, shells);
        }
        else {
            state.DropAt = now + milestones.Value.Drop;
            state.InsertAt = state.BusyUntil;
            Schedule(state, spec.Name, clip, now, spec.HasSilencer && !GunSpec.GetSilencerOff(Terrain.ExtractData(value)));
        }
    }

    /// <summary>
    /// The cues of a looped reload: those before the loop section once, those inside
    /// it once per shell, those after it once at the end.
    /// </summary>
    void ScheduleLooped(GunState state, string spec, string clip, double startedAt, Cs2Rig.ReloadSections sections, int loops) {
        string key = $"{spec}:{clip}";
        if (!Cs2Sounds.TryGet(key, out var list)) return;
        foreach ((float at, string name) in list) {
            if (at < sections.LoopStart) state.Scheduled.Add((startedAt + at, name));
            else if (at < sections.OutroStart)
                for (int k = 0; k < loops; k++)
                    state.Scheduled.Add((startedAt + sections.LoopStart + k * sections.LoopLength + (at - sections.LoopStart), name));
            else state.Scheduled.Add((startedAt + sections.LoopStart + loops * sections.LoopLength + (at - sections.OutroStart), name));
        }
    }

    /// <summary>
    /// Fire during a shell-by-shell reload: the shell in hand goes in, the loop stops,
    /// the pump plays, and the shot follows it. Cues past the cut are dropped and the
    /// outro's re-timed to the new end.
    /// </summary>
    void CutReloadShort(ComponentPlayer player, GunState state, GunSpec spec, double now) {
        (int loaded, float remaining) = KnifeAnimationController.FinishReloadEarly(player);
        if (loaded < 0) return;
        double end = now + remaining;
        state.ShellTimes.RemoveAll(t => t > end);
        state.Scheduled.RemoveAll(c => c.At > end);
        Cs2Rig.ReloadSections sections = Cs2Rig.GetReloadSections(spec.Name);
        if (sections is not null && Cs2Sounds.TryGet($"{spec.Name}:reload", out var list)) {
            double outroStart = end - (sections.End - sections.OutroStart);
            foreach ((float at, string name) in list)
                if (at >= sections.OutroStart && outroStart + (at - sections.OutroStart) > now)
                    state.Scheduled.Add((outroStart + (at - sections.OutroStart), name));
        }
        state.BusyUntil = end;
        state.FireAfterReload = true;
    }

    public override bool OnAim(Ray3 aim, ComponentMiner componentMiner, AimState state) {
        ComponentPlayer player = componentMiner.ComponentPlayer;
        if (player is null) return false;
        if (ScMobileControls.UsesTouchInput(player)) return false;
        // Act on the release (one press, one action). InProgress and Cancelled must return
        // false: ComponentPlayer treats a true from InProgress as "aim refused" and cancels
        // the aim on the spot, so Completed never arrives (0.15.0/0.15.1 right-click bug).
        if (state != AimState.Completed) return false;
        return RequestSecondary(player);
    }

    public bool RequestSecondary(ComponentPlayer player) {
        var componentMiner = player.ComponentMiner;
        if (player.ComponentHealth.Health <= 0 || player.ComponentGui.ModalPanelWidget is not null
            || DialogsManager.HasDialogs(player.GuiWidget)
            || Terrain.ExtractContents(componentMiner.ActiveBlockValue) != BlocksManager.GetBlockIndex<ScGunBlock>(true)) return false;
        if (!m_states.TryGetValue(player, out GunState gun)) m_states[player] = gun = new GunState();
        int value = componentMiner.ActiveBlockValue;
        GunSpec spec = ScGunBlock.SpecOf(value);
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        bool busy = gun.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model);
        if (spec.ZoomLevels.Length > 0) {
            if (busy) return true;
            gun.RescopeAt = -1;
            SetZoom(player, gun, spec, gun.Zoom >= spec.ZoomLevels.Length ? 0 : gun.Zoom + 1);
            PlaySound(player, gun.Zoom > 0 ? $"{spec.Name}_zoom" : $"{spec.Name}_zoom_out");
        }
        else if (spec.HasBurstMode && !busy) {
            // CS2 switches the Glock-18 and the FAMAS between semi-automatic and a
            // three-round burst on the same key that scopes a rifle and unscrews a
            // silencer. No weapon has two of the three, so they cannot collide.
            gun.BurstMode = !gun.BurstMode;
            gun.BurstRemaining = 0;
            gun.BurstNextAt = -1;
            PlaySound(player, "auto_semiauto_switch");
            player.ComponentGui.DisplaySmallMessage(
                LanguageControl.Get("ScCsgoKnives", "Message",
                                    gun.BurstMode ? "BurstOn" : "BurstOff"),
                Color.White, true, false);
        }
        else if (spec.CycleSecondsAlternate > 0f && !busy) {
            // The R8 fans the hammer on the aim key: an immediate shot with the
            // alternate spread and kick, on the pair's second cycle time.
            int data = Terrain.ExtractData(value);
            int rounds = GunSpec.GetRounds(data);
            if (rounds > 0 && gun.PrepareUntil < 0 && m_time.GameTime >= gun.NextShot)
                Fire(player, gun, model, spec, value, data, rounds, player.ComponentInput.PlayerInput, alternateFire: true);
        }
        else if (spec.HasSilencer && !busy) {
            bool off = GunSpec.GetSilencerOff(Terrain.ExtractData(value));
            int variant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
            string clip = off ? "attach" : "detach";
            float duration = CsmcKnifeRig.GetProfileDuration(variant, clip);
            if (duration > 0f) {
                KnifeAnimationController.TriggerSilencer(player, off);
                gun.Scheduled.Clear();
                Schedule(gun, spec.Name, clip, m_time.GameTime);
                gun.BusyUntil = m_time.GameTime + duration;
                gun.SilencerPending = true;
                gun.PendingSilencerOff = !off;
            }
        }
        return true;
    }

    public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
        int value = inventory.GetSlotValue(slotIndex);
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true)) return false;
        if (m_states.TryGetValue(componentPlayer, out GunState state)) LeaveScope(componentPlayer, state);
        if (!KnifeAnimationController.TriggerInspect(componentPlayer)) return true;
        if (state != null) {
            state.Scheduled.Clear();
            // The cues of the inspect the controller actually picked: the Desert Eagle's
            // second one carries its own nine LookAt sounds, which "inspect" has not got.
            string clip = KnifeAnimationController.CurrentClip(componentPlayer.Entity.FindComponent<ComponentFirstPersonModel>());
            Schedule(state, ScGunBlock.SpecOf(value).Name, clip is not null && clip.StartsWith("inspect", StringComparison.Ordinal) ? clip : "inspect", m_time.GameTime);
        }
        return true;
    }

    // ---- scope -------------------------------------------------------------------

    void SetZoom(ComponentPlayer player, GunState state, GunSpec spec, int level) {
        if (level <= 0) { LeaveScope(player, state); return; }
        if (float.IsNaN(state.SavedViewAngle)) state.SavedViewAngle = SettingsManager.ViewAngle;
        state.Zoom = level;
        float magnification = spec.ZoomLevels[Math.Clamp(level - 1, 0, spec.ZoomLevels.Length - 1)];
        // Survivalcraft's camera is 80 degrees x ViewAngle; the scope narrows it by the magnification.
        SettingsManager.ViewAngle = state.SavedViewAngle / magnification;
        // CS2 scales look sensitivity with the zoomed FOV (zoom_sensitivity_ratio_mouse 1.0): 1/magnification.
        if (float.IsNaN(state.SavedLookSensitivity)) state.SavedLookSensitivity = SettingsManager.LookSensitivity;
        SettingsManager.LookSensitivity = state.SavedLookSensitivity / magnification;
        CsmcFirstPersonRenderer.SetScope(true, magnification, spec.ScopeHidesWeapon);
        KnifeAnimationController.SetScoped(player, true);
    }

    void LeaveScope(ComponentPlayer player, GunState state) {
        if (state.Zoom == 0 && float.IsNaN(state.SavedViewAngle)) return;
        if (!float.IsNaN(state.SavedViewAngle)) SettingsManager.ViewAngle = state.SavedViewAngle;
        if (!float.IsNaN(state.SavedLookSensitivity)) SettingsManager.LookSensitivity = state.SavedLookSensitivity;
        state.SavedViewAngle = float.NaN;
        state.SavedLookSensitivity = float.NaN;
        state.Zoom = 0;
        CsmcFirstPersonRenderer.SetScope(false, 1f);
        KnifeAnimationController.SetScoped(player, false);
    }

    // ---- recoil ------------------------------------------------------------------

    void Kick(ComponentPlayer player, GunState state, float pitch, float yaw) {
        ComponentLocomotion locomotion = player.ComponentLocomotion;
        Vector2 look = locomotion.LookAngles;
        look.X += yaw;
        look.Y = MathUtils.Clamp(look.Y + pitch, -MathUtils.DegToRad(89f), MathUtils.DegToRad(89f));
        locomotion.LookAngles = look;
        state.KickPitch += pitch;
        state.KickYaw += yaw;
    }

    void RecoverKick(ComponentPlayer player, GunState state, float dt, float rate) {
        if (MathF.Abs(state.KickPitch) < 0.0001f && MathF.Abs(state.KickYaw) < 0.0001f) return;
        float k = MathUtils.Saturate(rate * dt);
        float dp = state.KickPitch * k, dy = state.KickYaw * k;
        ComponentLocomotion locomotion = player.ComponentLocomotion;
        Vector2 look = locomotion.LookAngles;
        look.X -= dy;
        look.Y -= dp;
        locomotion.LookAngles = look;
        state.KickPitch -= dp;
        state.KickYaw -= dy;
    }

    // ---- helpers -----------------------------------------------------------------

    Vector3 Scatter(Vector3 direction, float coneDegrees) {
        if (coneDegrees <= 0f) return Vector3.Normalize(direction);
        Vector3 forward = Vector3.Normalize(direction);
        Vector3 side = Vector3.Normalize(Vector3.Cross(forward, MathF.Abs(forward.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        Vector3 up = Vector3.Cross(side, forward);
        float angle = MathUtils.DegToRad(coneDegrees) * MathF.Sqrt(m_random.Float(0f, 1f));
        float phi = m_random.Float(0f, MathF.PI * 2f);
        return Vector3.Normalize(forward * MathF.Cos(angle) + (side * MathF.Cos(phi) + up * MathF.Sin(phi)) * MathF.Sin(angle));
    }

    int WriteData(ComponentPlayer player, int value, int data) {
        int newValue = Terrain.ReplaceData(value, data);
        IInventory inventory = player.ComponentMiner.Inventory;
        int slot = inventory?.ActiveSlotIndex ?? -1;
        if (inventory is null || slot < 0 || inventory.GetSlotValue(slot) != value) return newValue;
        int count = inventory.GetSlotCount(slot);
        inventory.RemoveSlotItems(slot, count);
        inventory.AddSlotItems(slot, newValue, count);
        ScInventoryTransaction.Changed(inventory);
        return newValue;
    }

    static Ray3 LookRay(ComponentPlayer player) {
        Camera camera = player.GameWidget?.ActiveCamera;
        if (camera is not null) return new Ray3(camera.ViewPosition, camera.ViewDirection);
        return new Ray3(player.ComponentCreatureModel.EyePosition,
            Matrix.CreateFromQuaternion(player.ComponentCreatureModel.EyeRotation).Forward);
    }

    void UpdateAmmoHud(ComponentPlayer player, GunState state) {
        var gui = player.ComponentGui;
        int value = player.ComponentMiner.ActiveBlockValue;
        if (gui.ModalPanelWidget is not null || DialogsManager.HasDialogs(player.GuiWidget)
            || !gui.ControlsContainerWidget.IsVisible
            || Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || !ScGunBlock.IsKnown(value)) {
            state.AmmoHud?.Hide(); return;
        }
        if (state.AmmoHud is null) {
            var hud = new ScAmmoHud();
            if (!hud.Attach(gui)) { hud.Dispose(); return; }
            state.AmmoHud = hud;
        }
        var spec = ScGunBlock.SpecOf(value);
        bool creative = Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode == GameMode.Creative;
        state.AmmoHud.Show(ScAmmoReadout.Read(spec, value, player.ComponentMiner.Inventory, creative,
            state.RechargeAt < 0 ? -1 : Math.Max(0, state.RechargeAt - m_time.GameTime), state.Reload is not null));
    }

    /// <summary>Plays Audio/ScCsgoKnives/&lt;name&gt; when the mod ships it; nothing (and no placeholder) when it does not.</summary>
    void PlaySound(ComponentPlayer player, string name) {
        if (s_variants.TryGetValue(name, out int n)) name = $"{name}_{m_random.Int(1, n)}";
        string path = $"Audio/ScCsgoKnives/{name}";
        if (s_missingSounds.Contains(path)) return;
        try {
            m_audio.PlaySound(path, 1f, m_random.Float(-0.05f, 0.05f), player.ComponentCreatureModel.EyePosition, 24f, true);
        }
        catch (Exception e) {
            s_missingSounds.Add(path);
            KnifeLog.Information($"[ScCsgoKnives] sound {path} failed ({e.GetType().Name}: {e.Message}); playing nothing.");
        }
    }
}
