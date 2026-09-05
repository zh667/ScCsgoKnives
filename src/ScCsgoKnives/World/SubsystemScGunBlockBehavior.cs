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
        public double NextShot;
        public double BusyUntil = -1;          // reload or silencer clip in progress
        public int PendingRounds = -1;         // magazine to write when the reload clip ends
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
        public bool HintShown;
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

    public void Draw(Camera camera, int drawOrder) {
        DrawTracers(camera);
        if (CsmcFirstPersonRenderer.ScopeOverlayActive) CsmcFirstPersonRenderer.DrawScopeOverlay();
    }

    public override void Dispose() {
        Project.FindSubsystem<SubsystemDrawing>(false)?.RemoveDrawable(this);
        base.Dispose();
    }

    public override void Load(ValuesDictionary valuesDictionary) {
        base.Load(valuesDictionary);
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

    public void Update(float dt) {
        KnifeQa.Step();
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        foreach (ComponentPlayer player in m_players.ComponentPlayers) {
            if (!m_states.TryGetValue(player, out GunState state)) m_states[player] = state = new GunState();
            int value = player.ComponentMiner.ActiveBlockValue;
            bool holdingGun = Terrain.ExtractContents(value) == gunIndex && player.ComponentHealth.Health > 0f;
            if (!holdingGun) {
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
        }
    }

    void UpdateGun(ComponentPlayer player, GunState state, int value, float dt) {
        GunSpec spec = ScGunBlock.SpecOf(value);
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        double now = m_time.GameTime;
        int data = Terrain.ExtractData(value);
        int rounds = GunSpec.GetRounds(data);
        PlayerInput input = player.ComponentInput.PlayerInput;

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
                // The draw clip's own cues where CS2 has them - the FAMAS and M4A4 work
                // the bolt quietly during theirs - else the single draw file.
                if (!Schedule(state, spec.Name, deployClip, now)) PlaySound(player, $"{spec.Name}_draw");
                ShowAmmo(player, spec, rounds);
                if (!state.HintShown) {
                    state.HintShown = true;
                    player.ComponentGui.DisplaySmallMessage(string.Format(LanguageControl.Get("ScCsgoKnives", "Message", "GunHint"), SettingsManager.GetKeyboardMapping("EditItem", false)?.ToString() ?? "G"), Color.White, true, false);
                }
            }
            state.LastValue = value;
        }

        // A reload or silencer clip that has run its course commits its result.
        if (state.BusyUntil >= 0 && now >= state.BusyUntil) {
            state.BusyUntil = -1;
            if (state.PendingRounds >= 0) {
                data = GunSpec.SetRounds(data, state.PendingRounds);
                rounds = state.PendingRounds;
                state.PendingRounds = -1;
                value = WriteData(player, value, data);
                ShowAmmo(player, spec, rounds);
            }
            if (state.SilencerPending) {
                state.SilencerPending = false;
                data = GunSpec.SetSilencerOff(data, state.PendingSilencerOff);
                value = WriteData(player, value, data);
            }
        }
        PlayScheduled(player, state, now);
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
        if (!busy && rounds < spec.Magazine && (reloadKey || (wantsFire && rounds == 0))) {
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
            Fire(player, state, model, spec, value, data, rounds, input);
        }

        RecoverKick(player, state, dt,
            Cs2Weapons.Kick(spec.Name, false, spec.KickPitchDegrees, spec.KickYawDegrees,
                spec.KickRecoverPerSecond).Recover);
    }

    void Fire(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value, int data, int rounds, PlayerInput input, bool inBurst = false) {
        double now = m_time.GameTime;
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
        else if (!inBurst) {
            state.NextShot = now + spec.CycleSeconds;
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
        if (state.Zoom > 0) {
            // CS2: a scoped shot drops the scope for the bolt cycle and re-zooms to the same level afterwards.
            state.RescopeLevel = state.Zoom;
            state.RescopeAt = now + spec.CycleSeconds;
            LeaveScope(player, state);
        }
        KnifeAnimationController.TriggerShoot(player, silenced, lastRound, state.Zoom > 0);
        CsmcFirstPersonRenderer.MuzzleFlash(silenced ? 0.03f : 0.06f, silenced ? spec.SilencedMuzzleBone : spec.MuzzleBone, spec.Name, silenced);
        PlaySound(player, spec.HasSilencer && silenced ? $"{spec.Name}_fire_silenced" : $"{spec.Name}_fire");
        if (!spec.Automatic) Schedule(state, spec.Name, KnifeAnimationController.CurrentClip(model) ?? "shoot1", now);
        ShowAmmo(player, spec, rounds);

        // Camera kick, applied now and eased back in RecoverKick. The cs2 profile takes
        // the ratios between guns from m_flRecoilMagnitude and the yaw scatter from
        // m_flRecoilAngleVariance; the absolute scale is still the fitted AK value.
        bool alternate = silenced || state.Zoom > 0;
        (float kickPitch, float kickYaw, float _) = Cs2Weapons.Kick(spec.Name, alternate,
            spec.KickPitchDegrees, spec.KickYawDegrees, spec.KickRecoverPerSecond);
        float pitch = MathUtils.DegToRad(kickPitch) * (0.8f + 0.4f * m_random.Float(0f, 1f));
        float yaw = MathUtils.DegToRad(kickYaw) * m_random.Float(-1f, 1f);
        Kick(player, state, pitch, yaw);

        // Hitscan along the view ray with a small random cone.
        Ray3 ray = input.Dig ?? input.Hit ?? new Ray3(player.ComponentCreatureModel.EyePosition, Vector3.Transform(Vector3.UnitZ, player.ComponentCreatureModel.EyeRotation));
        // CS keeps a separate inaccuracy per stance; the cs2 profile blends the vdata's
        // standing and moving values by speed instead of scaling one cone by a constant.
        float spread = Cs2Weapons.SpreadDegrees(spec.Name, alternate,
            player.ComponentBody.Velocity.Length(),
            spec.SpreadDegrees * (state.Zoom > 0 ? 0.35f : 1f));
        // A shotgun fires m_nNumBullets pellets on one trigger pull, each with its own
        // scatter: Nova 9, MAG-7 and Sawed-Off 8, XM1014 6. Every other gun is 1, and
        // the body below is then exactly the single shot it always was.
        int pellets = Math.Max(1, spec.Pellets);
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
            Vector3 tracerStart = CsmcFirstPersonRenderer.TryGetMuzzleWorld(spec.Name, silenced, out Vector3 muzzle)
                ? muzzle : start;
            Vector3 tracerDirection = impact - tracerStart;
            float tracerTravel = tracerDirection.Length();
            if (tracerTravel > 1e-3f) QueueTracer(spec.Name, tracerStart, tracerDirection / tracerTravel, tracerTravel);
            if (body.HasValue && (!terrain.HasValue || body.Value.Distance < terrain.Value.Distance)) {
                Vector3 hitPoint = body.Value.HitPoint();
                // CS damage falls off with distance: damage * RangeModifier^(units/500).
                float power = Cs2Weapons.DamageAt(spec.Name, body.Value.Distance, spec.AttackPower);
                ComponentMiner.AttackBody(body.Value.ComponentBody, player, hitPoint, direction, power, false);
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
    }

    void StartReload(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value) {
        int variant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
        // From an empty magazine the pistols run their reload_empty clip, which also
        // releases the slide; the clip, its length and its cues all follow that choice.
        bool empty = GunSpec.GetRounds(Terrain.ExtractData(value)) <= 0;
        string clip = KnifeAnimationController.ReloadClip(variant, empty) ?? "reload";
        float duration = CsmcKnifeRig.GetProfileDuration(variant, clip);
        if (duration <= 0f) return;
        LeaveScope(player, state);
        KnifeAnimationController.TriggerReload(player, empty);
        state.Scheduled.Clear();
        Schedule(state, spec.Name, clip, m_time.GameTime, spec.HasSilencer && !GunSpec.GetSilencerOff(Terrain.ExtractData(value)));
        state.BusyUntil = m_time.GameTime + duration;
        state.PendingRounds = spec.Magazine;
    }

    public override bool OnAim(Ray3 aim, ComponentMiner componentMiner, AimState state) {
        ComponentPlayer player = componentMiner.ComponentPlayer;
        if (player is null) return false;
        if (!m_states.TryGetValue(player, out GunState gun)) m_states[player] = gun = new GunState();
        // Act on the release (one press, one action). InProgress and Cancelled must return
        // false: ComponentPlayer treats a true from InProgress as "aim refused" and cancels
        // the aim on the spot, so Completed never arrives (0.15.0/0.15.1 right-click bug).
        if (state != AimState.Completed) return false;
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
        string name = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(m_terrain, value);
        componentPlayer.ComponentGui.DisplaySmallMessage(string.Format(LanguageControl.Get("ScCsgoKnives", "Message", "Inspect"), name), Color.White, true, false);
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
        return newValue;
    }

    void ShowAmmo(ComponentPlayer player, GunSpec spec, int rounds) =>
        player.ComponentGui.DisplaySmallMessage(string.Format(LanguageControl.Get("ScCsgoKnives", "Message", "Ammo"), rounds), rounds == 0 ? Color.Red : Color.White, false, false);

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
