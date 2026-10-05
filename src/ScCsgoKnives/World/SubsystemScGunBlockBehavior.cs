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
        public readonly ScGunStance Stance = new();
        public float KickRecoveryRate = 9f;
        /// <summary>The last shot's handling came from the world's mode (ScMode.TryGunStats): its camera kick eases back by
        /// the mode's own rate whatever this device's gunplay preset is, so every player of a match recovers alike.</summary>
        public bool ModeKick;
        /// <summary>How much of a mode's recoil (IScRecoil, deathmatch round 5) the look carries now, in degrees: X up, Y
        /// left. The look is moved by the change each frame, so the player's own aiming in between stays.</summary>
        public Vector2 ModeView;
        public readonly ScCombatFeedback Feedback = new();
        public ScAmmoHud AmmoHud;
        public double NextShot;
        public double BusyUntil = -1;          // KnifeClock: deploy, reload or silencer clip in progress
        public ScReloadTransaction Reload;
        /// <summary>Multiplayer: the request id of the reload in progress (a client's own request; on the server the client's
        /// request it performs, 0 for one the server started itself).</summary>
        public int ReloadId;
        /// <summary>Server: a remote client's reload request that could not start yet (the gun still busy here), and until
        /// when it is kept (ScNet.Now).</summary>
        public int PendingReloadId; public double PendingReloadUntil = -1;
        /// <summary>Client: the server has not yet said what became of the reload this client last showed (completed,
        /// cancelled, refused). Until it does - or <see cref="ServerReloadDeadline"/> (ScNet.Now) passes - no further reload
        /// is started here: the end of the animation shown is not a reload.</summary>
        public bool ServerReloading; public double ServerReloadDeadline = -1;
        public long ReloadAnimationSequence = -1;
        public double DropAt = -1, InsertAt = -1;
        public int PendingRounds = -1;         // magazine to write when the reload clip ends
        /// <summary>A shotgun reload: when each shell counts (WPN_RELOAD_ADD_AMMO of each loop).</summary>
        public readonly List<double> ShellTimes = [];
        /// <summary>Fire was pressed during a shell-by-shell reload: shoot as soon as the pump is done.</summary>
        public bool FireAfterReload;
        /// <summary>The R8's hammer is being drawn (Cocking); the cocked shot is committed at this time if the
        /// primary input is still held. -1: idle. See <see cref="ScRevolverTrigger"/> for the rules.</summary>
        public double PrepareUntil = -1;
        public double PrepareStartedAt;
        /// <summary>NextShot before the cocking began; restored when the cocking is cancelled.</summary>
        public double PrepareResume;
        /// <summary>Frame of the last committed shot of either mode: never two commits in one frame.</summary>
        public int CommitFrame = -1;
        /// <summary>Recovery after the R8's cocked shot: the fanned shot may follow one alternate cycle time later,
        /// as it may after another fanned shot. Without it a cocked shot and a fanned shot could leave one frame apart.</summary>
        public double AlternateReadyAt;
        public bool PendingSilencerOff;
        public bool SilencerPending;
        public int Zoom;                       // 0 = hip, 1.. = scope level
        public int RescopeLevel;               // scope level to return to after a shot (CS2: the AWP unscopes for the bolt, then re-zooms)
        public double RescopeAt = -1;
        public float KickPitch, KickYaw;
        public bool FireLatch;
        /// <summary>Right button held last frame (PC): the scope/mode key acts once per press, on the press edge.</summary>
        public bool AimLatch;
        /// <summary>Set by the OnPlayerInputInteract hook on the press frame: the engine found an interactive
        /// target (door, chest, lever, or another mod's object) for the shared right button.</summary>
        public bool WorldInteract;
        /// <summary>Burst mode selected, on the two guns CS2 gives one (Glock-18, FAMAS).</summary>
        public bool BurstMode;
        /// <summary>Shots still owed by the burst in progress, and when the next is due.</summary>
        public int BurstRemaining;
        public double BurstNextAt = -1;
        public int LastValue = int.MinValue;
        public readonly ScHeldWeaponSelection Selection = new();
        /// <summary>Sounds due on KnifeClock: the magazine, bolt and screw noises inside a clip.</summary>
        public readonly List<(double At, string Name)> Scheduled = [];
        public long InspectSoundToken=-1;
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
        if(state.InspectSoundToken>=0){state.Scheduled.Clear();state.InspectSoundToken=-1;}
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
            state.Scheduled.Add((SoundTime(startedAt + at), n));
        }
        return list.Length > 0;
    }

    void PlayScheduled(ComponentPlayer player, GunState state, double now) {
        now=KnifeClock.Now;
        var hands=player?.Entity?.FindComponent<ComponentFirstPersonModel>();
        if(state.InspectSoundToken>=0 && KnifeAnimationController.ActionToken(hands)!=state.InspectSoundToken){state.Scheduled.Clear();state.InspectSoundToken=-1;}
        if (state.Scheduled.Count == 0) return;
        for (int i = state.Scheduled.Count - 1; i >= 0; i--) {
            if (now < state.Scheduled[i].At) continue;
            // A stalled/background frame must not dump a whole clip's old cues at once.
            // A cue belongs to the action the hands are playing (a switch or a cut inspect fades it: ScPresentationSound).
            if(now-state.Scheduled[i].At<=.2) ScPresentationSound.PlayHeld(hands, KnifeAnimationController.ActionToken(hands), state.Scheduled[i].Name);
            state.Scheduled.RemoveAt(i);
        }
    }
    double SoundTime(double gameTime) => KnifeClock.Now + gameTime - m_time.GameTime;
    public void PresentationTick(ComponentPlayer player) {
        if(!m_states.TryGetValue(player,out var state))return;
        if(player.ComponentHealth.Health<=0 || player.ComponentMiner.ActiveBlockValue!=state.LastValue) {state.Scheduled.Clear();return;}
        PlayScheduled(player,state,m_time.GameTime);
    }
    public void InspectSound(ComponentPlayer player,string clip) {
        if(!m_states.TryGetValue(player,out var state))return;
        state.Scheduled.Clear();
        var spec=ScGunBlock.SpecOf(player.ComponentMiner.ActiveBlockValue);
        if(spec is not null){Schedule(state,spec.Name,clip,m_time.GameTime);state.InspectSoundToken=KnifeAnimationController.ActionToken(player.Entity.FindComponent<ComponentFirstPersonModel>());}
    }

    SubsystemTerrain m_terrain;
    SubsystemBodies m_bodies;
    SubsystemAudio m_audio;
    SubsystemParticles m_particles;
    SubsystemPlayers m_players;
    SubsystemTime m_time;
    readonly Dictionary<ComponentPlayer, GunState> m_states = [];
    readonly ScCasingEffects m_casings=new();
    readonly Random m_random = new();
    static readonly HashSet<string> s_missingSounds = [];

    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScGunBlock>()];
    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    /// <summary>
    /// The AWP scope mask is drawn here, at order 350 (after the sky at 105 and particles at
    /// 300), not in the first-person pass, so nothing paints over it when the player looks up.
    /// </summary>
    public int[] DrawOrders => [10, 350, 2001];
    /// <summary>Whether this player's gun is scoped right now; the crosshair and the vanilla-crosshair hook read it.</summary>
    public bool IsScoped(ComponentPlayer player) => player is not null && m_states.TryGetValue(player, out var state) && state.Zoom > 0;
    ScScopeCamera m_scopeInput;
    public float ScopeMagnification(ComponentPlayer player) {
        if (player is null || !ScGunBindings.Available(player) || !m_states.TryGetValue(player, out var state) || state.Zoom <= 0) return 1f;
        int value = player.ComponentMiner.ActiveBlockValue;
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || !ScGunBlock.IsKnown(value)) return 1f;
        var levels = ScGunBlock.SpecOf(value).ZoomLevels;
        return state.Zoom <= levels.Length ? levels[state.Zoom-1] : 1f;
    }
    public void SuspendScope(ComponentPlayer player) {
        if (!m_states.TryGetValue(player, out var state)) return;
        state.RescopeAt = -1;
        LeaveScope(player, state);
    }
    readonly PrimitivesRenderer2D m_crosshairRenderer = new();

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

    /// <param name="silenced">A suppressor is on: the M4A1-S and USP-S vdata give m_nTracerFrequency [3, 0] / [1, 0], the
    /// second value being the suppressed mode (the one with CS2's smaller spread), so a suppressed shot draws no tracer
    /// (round 9, 2026-10-01; the MP5-SD's integral suppressor is already 0).</param>
    /// <param name="ownFirstPerson">The shooter's own first-person view (the tracer leaves the drawn viewmodel). CS2 shows the
    /// shooter a tracer on every shot there: its first-person AK capture has 8 of 12 shots with one, four in a row, where
    /// every third shot allows 4; seen from outside (its third-person capture, 3 of 8) the gun's m_nTracerFrequency holds.</param>
    void QueueTracer(string gun, Vector3 start, Vector3 direction, float distance, bool silenced, bool ownFirstPerson = false) {
        if (KnifeTuning.GunProfile < 0.5f) return;
        int frequency = silenced && gun is "m4a1s" or "usp_silencer" ? 0 : Cs2Effects.TracerFrequency(gun);
        if (ownFirstPerson && frequency > 1) frequency = 1;
        if (frequency <= 0) return;
        m_shotCounts.TryGetValue(gun, out int n);
        m_shotCounts[gun] = n + 1;
        if ((n + 1) % frequency != 0) return;
        Cs2Effects.Tracer spec = Cs2Effects.Get(gun)?.Tracer;
        if (spec is null) return;
        // CS2 draws the SMGs' and the AUG / SG 553 / M249 / Negev's tracer as a rope from the muzzle to the impact; the user
        // (2026-10-05, on the sample: "感觉都不是和ak awp 一个风格的", then "统一成短线") wants the rifles' short dash on those
        // too: they fly the assault rifle's tracer, at their own frequency. (The rope drawing stays for the data, unused.)
        if (spec.Passes is { } passes && passes.Any(p => p.IsRope)) spec = Cs2Effects.Get("ak47")?.Tracer ?? spec;
        // The lingering line is a child of the tracer system (AWP, SSG 08, G3SG1, SCAR-20): one per drawn tracer, from the
        // muzzle (CP0) to the impact (CP1).
        if (Cs2Wisp.For(gun) is { } wisp && distance > .5f) {
            if (m_wisps.Count >= 16) m_wisps.RemoveAt(0);
            m_wisps.Add(new Cs2WispTrail(wisp, start, start + direction * distance, m_random));
        }
        // The sniper tracers (weapon_tracers_rifle, _ssg, _scar) start their streak 20 in further along the shot
        // (C_INIT_PositionOffset).
        if (Cs2Wisp.For(gun) is not null) { float skip = MathF.Min(20 * Cs2Wisp.Inch, distance * .5f); start += direction * skip; distance -= skip; }
        if (m_tracers.Count > 32) m_tracers.RemoveAt(0);
        m_tracers.Add(new TracerShot(start, direction, distance, m_time.GameTime, spec));
    }
    readonly List<Cs2WispTrail> m_wisps = [];
    readonly Vector3[] m_wispJoints = new Vector3[512];
    readonly float[] m_wispIndex = new float[512];

    /// <summary>How many quads the ribbon is cut into; the width is solved per joint.</summary>
    const int TracerSegments = 24;
    /// <summary>The SMG / AUG ropes' light against CS2's first-person frames (DrawTrailPass).</summary>
    const float RopeLight = .55f;
    /// <summary>Half the span of the streak texture's width a trail / rope ribbon samples across (DrawTrailPass).</summary>
    const float TrailAcrossHalf = 1f / 6f, RopeAcrossHalf = 1f / 12f;
    /// <summary>The hue of the tracer lookup textures averaged across their width (DrawTrailPass).</summary>
    static readonly Color StreakHue = new(255, 218, 186);

    Texture2D m_tracerAdd, m_tracerBlend, m_tracerSmg, m_tracerTintable, m_tracerAddLut, m_tracerBlendLut, m_wispEnergy, m_wispSmoke;
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
            // Round 9: the streak passes' 1D colour lookups (white core, orange/red fringe) baked into copies of the two
            // streak textures (tools/import_cs2_tracer_round9.py), and the sniper wisp's two rope textures.
            m_tracerAddLut = OptionalTexture("cs2_tracer_add_lut"); m_tracerBlendLut = OptionalTexture("cs2_tracer_blend_lut");
            m_wispEnergy = OptionalTexture("cs2_wisp_energy"); m_wispSmoke = OptionalTexture("cs2_wisp_smoke");
        }
        // A pass with no baked texture must not quietly borrow the other one's: the
        // two are different images with different blend modes.
        return name switch {
            "cs2_tracer_add" => m_tracerAddLut ?? m_tracerAdd,
            "cs2_tracer_blend" => m_tracerBlendLut ?? m_tracerBlend,
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
                // The SMG's rope runs from the muzzle to the scrolled head: everything behind the head takes the texture's
                // head-side edge (its V clamps), a thin line from the muzzle as CS2 draws the P90's.
                float trail = pass.IsRope ? head : Cs2Tracer.TrailMetres(spec, pass, age, fromViewer);
                float tail = MathUtils.Max(0f, head - trail);
                if (head - tail < 1e-4f) continue;
                DrawTrailPass(t, spec, pass, tail, head, trail, pathAlpha, eye, forward, projY);
            }
        }
        m_tracerRenderer.Flush(camera.ViewProjectionMatrix);
    }

    void DrawTrailPass(in TracerShot t, Cs2Effects.Tracer spec, Cs2Effects.TracerPass pass,
                       float tail, float head, float trail, float pathAlpha,
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
        // A trail coloured by its texture alone (the AK's white C_INIT_RandomColor): CS2 squeezes the streak's width into a
        // fifth of the ribbon, so a sub-pixel line reaches the screen as the lookup texture's average across its width - warm
        // (R/B 1.4-1.6 along the bright part of cs2_tracer_add_lut / _blend_lut) - where our 1-3 px ribbon samples its white
        // core. The pass takes that average's hue; its light is left as it was (about what CS2's AK frames add).
        if (!pass.IsRope && spec.ColorFromTexture) tint = StreakHue;
        // The SMG rope's colour fades from ColorFade at the start to its own colour (C_OP_ColorInterpolate, start 1, end 0: the
        // fade colour at the beginning of its life), over its flight here.
        if (pass.IsRope && spec.ColorFade is { Length: >= 3 } fadeTo) {
            float w = MathUtils.Saturate(head / MathUtils.Max(t.Distance, 1e-3f));
            tint = new Color((byte)MathUtils.Lerp(fadeTo[0], tint.R, w), (byte)MathUtils.Lerp(fadeTo[1], tint.G, w), (byte)MathUtils.Lerp(fadeTo[2], tint.B, w), tint.A);
        }
        // Along a trail CS2 maps the texture's V (C_OP_RenderTrails m_bClampV; the source streaks run down their rows), 0 at
        // the head and 1 at the full trail's tail, through the pass's final V scale and offset and the clamp: the AK's -1.5 and
        // 1.2 put the streak's bright tip a seventh of the trail behind the round and its faded end at 0.8 of the trail. The
        // baked texture's U is the source's V (tools/cs2_tracer_texture.py transposes), so it is sampled there directly. Past
        // the texture's tail edge nothing shows: the drawn range stops there. (The first tracers sample read U as the along
        // axis: ScaleU 5 squeezed the streak into a fifth of the trail, a few pixels on screen.)
        float scaleV = pass.TextureScaleV, offsetV = pass.TextureOffsetV;
        if (!pass.IsRope && pass.ClampUVs && scaleV < -1e-3f) tail = MathUtils.Max(tail, head - trail * offsetV / -scaleV);
        if (head - tail < 1e-4f) return;
        float Along(float metresBehindHead) {
            if (pass.IsRope) return .9f;   // the rope behind its head: the streak's bright head-side edge
            float cv = offsetV + scaleV * metresBehindHead / MathUtils.Max(trail, 1e-4f);
            return pass.ClampUVs ? MathUtils.Saturate(cv) : cv;
        }
        // CS2 draws the rope about 11 px wide with the streak's white core a tenth of it inside an amber fringe; ours is the
        // 1-3 px line above, which samples the core alone and, added at full strength, burns to white on a light wall. Its
        // light is scaled to what CS2's own P90 frames add over the wall (about 105, 95, 80 of 255 at 540 lines, the core a
        // little brighter before that capture's downscale). Calibrated, not read.
        float light = pass.IsRope ? RopeLight : 1f;
        Vector3 previous = default, previousSide = default;
        float previousFade = 0f, previousAlong = 0f;
        bool hasPrevious = false;
        for (int k = 0; k <= TracerSegments; k++) {
            float f = k / (float)TracerSegments;
            float along = MathUtils.Lerp(tail, head, f);
            Vector3 p = t.Start + t.Direction * along;
            Vector3 toEye = p - eye;
            float depth = Vector3.Dot(toEye, forward);
            float half = Cs2Tracer.HalfWidth(spec, pass, depth, projY, out float fade);
            // The rope sets no screen clamp and a 2.5 in radius from control points the game fills; CS2 shows the P90's line
            // 2-3 px wide: the rifle trails' clamp (0.00075..0.002 of the viewport height). Calibrated, not read.
            if (pass.IsRope) { float per = Cs2Tracer.MetresPerScreenHeight(depth, projY); half = MathUtils.Clamp(half, .00075f * per, .002f * per); }
            // Degenerate only where the trail runs exactly through the eye axis. The
            // joint is dropped, and so is the quad that would have used it: carrying
            // `previous` across the gap would stretch a segment over the whole hole.
            Vector3 side = Vector3.Cross(t.Direction, toEye);
            float length = side.Length();
            if (!float.IsFinite(length) || length < 1e-6f) { hasPrevious = false; continue; }
            side = side * (half / length);

            if (hasPrevious && previousFade + fade > 0f) {
                float a0 = spec.AlphaMid * pathAlpha * previousFade * light;
                float a1 = spec.AlphaMid * pathAlpha * fade * light;
                var c0 = new Color(tint.R, tint.G, tint.B, (byte)MathUtils.Clamp(255f * a0, 0f, 255f));
                var c1 = new Color(tint.R, tint.G, tint.B, (byte)MathUtils.Clamp(255f * a1, 0f, 255f));
                float u0 = previousAlong, u1 = Along(head - along);
                // U runs tail (0) to head (1) in the baked streak; V crosses the width. CS2's U scale and offset across it
                // squeeze the streak into a fifth of the ribbon, which its antialiasing and bloom spread back into a line about
                // 2 px wide at 540 lines; drawn plainly, the streak's core (a fifth of the texture's width) would cover a
                // third of a pixel of our 1-3 px ribbon. A trail spans the middle third of the texture's width instead, so
                // the core covers about 60% of the ribbon (calibrated on CS2's AK frames, not read). A rope's streak has its
                // core in a tenth of the width (CS2 draws the rope about 11 px wide, the P90's line about 2 px at 540 lines):
                // it spans the middle sixth, the same 60%.
                float across = pass.IsRope ? RopeAcrossHalf : TrailAcrossHalf, vIn = .5f + across, vOut = .5f - across;
                batch.QueueTriangle(previous - previousSide, previous + previousSide, p + side,
                                    new Vector2(u0, vIn), new Vector2(u0, vOut), new Vector2(u1, vOut), c0);
                batch.QueueTriangle(previous - previousSide, p + side, p - side,
                                    new Vector2(u0, vIn), new Vector2(u1, vOut), new Vector2(u1, vIn), c1);
            }
            previous = p;
            previousSide = side;
            previousFade = fade;
            previousAlong = Along(head - along);
            hasPrevious = true;
        }
    }

    Texture2D OptionalTexture(string name) {
        try { return ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + name); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("cs2-tracer-texture-" + name, $"CS2 tracer texture {name} unavailable: {e.Message}"); return null; }
    }

    /// <summary>
    /// The sniper's lingering line (Cs2Wisp): two ropes through the drifting points, as the wisp .vpcf's two RenderRopes.
    /// Core (beam_energy_01): radius ×0.5, screen size ≤ 0.03 with a fade at 0.015..0.025, V every 200 in, scrolling
    /// −500 → −100 in/s, additive. Smoke (beam_smoke_01): radius ×3, ≤ 0.1 with a fade at 0.05..0.1, colour ×(150,159,165),
    /// V every 350..750 in, scrolling −200 → −20 in/s, self-illumination 0.2. CS2's HALF_BLEND_ADD with overbright 2 is drawn
    /// as additive (core) and straight alpha blending (smoke); the ring sprites and the beam_generic_2 / base_rope / crack / breakup
    /// layers are not drawn (round 9 brief).
    /// </summary>
    void DrawWisps(Camera camera) {
        if (m_wisps.Count == 0) return;
        TracerTexture("");
        if (m_wispEnergy is null && m_wispSmoke is null) return;
        m_tracerRenderer ??= new PrimitivesRenderer3D();
        Vector3 eye = camera.ViewPosition, forward = camera.ViewDirection;
        float projY = camera.ProjectionMatrix.M22;
        if (!float.IsFinite(projY) || projY <= 1e-4f) return;
        foreach (var w in m_wisps) {
            float life = w.Age / Cs2Wisp.Lifetime;
            float alpha = w.Alpha * Cs2Wisp.Curve(w.Spec.LifeAlpha, life);
            if (alpha <= .002f) continue;
            float radius = w.Spec.RadiusInches * Cs2Wisp.Inch * Cs2Wisp.RadiusScale(life);
            float glow = MathUtils.Saturate(Cs2Wisp.Glow(life));
            Vector3 mid = w.Points[w.Points.Length / 2];
            float light = LightingManager.LightIntensityByLightValue[Math.Clamp(m_terrain.Terrain.GetCellLight(Terrain.ToCell(mid.X), Terrain.ToCell(mid.Y), Terrain.ToCell(mid.Z)), 0, 15)];
            Vector3 colour = Cs2Wisp.Colour(life, w.ColourPick);
            float age = w.Age;
            if (m_wispSmoke is not null)
                // Straight RGBA (ContentReader) with straight vertex colours: NonPremultiplied. The engine's AlphaBlend is
                // premultiplied (One, InverseSourceAlpha) and added the smoke at full colour whatever its alpha (r9a-r9e frames).
                DrawWispRope(w, m_wispSmoke, BlendState.NonPremultiplied, 0, radius * 3, .1f, .05f, .1f, w.Spec.SmokeFadeDot,
                    // ×2: the rope's m_flOverbrightFactor (left out before r9g: the smoke drew mid-grey, hard to see on the sky).
                    colour * new Vector3(150, 159, 165) / 255f * 2f * (.2f + .8f * MathUtils.Max(light, glow)),
                    alpha * Cs2Wisp.SmokeAlphaScale(w.Spec, w.Length), w.SmokeRepeat, (-200 * age + 45 * age * age) * Cs2Wisp.Inch, eye, forward, projY);
            if (m_wispEnergy is not null)
                DrawWispRope(w, m_wispEnergy, BlendState.Additive, 1, radius * .5f, .03f, .015f, .025f, w.Spec.CoreFadeDot,
                    colour * MathUtils.Lerp(light, 1f, glow), MathUtils.Min(1f, alpha * w.Spec.CoreAlpha * 2), 200 * Cs2Wisp.Inch,
                    (-500 * age + 100 * age * age) * Cs2Wisp.Inch, eye, forward, projY);
        }
        m_tracerRenderer.Flush(camera.ViewProjectionMatrix);
    }

    /// <param name="fadeDot">m_flStartFadeDot / m_flEndFadeDot: |rope direction · camera forward| above x fades to nothing at y. A
    /// rope seen exactly end-on has no width direction at all and is dropped whatever the file says, and the width direction
    /// is kept continuous along the rope so a segment never twists into a bow-tie.</param>
    void DrawWispRope(Cs2WispTrail w, Texture2D texture, BlendState blend, int layer, float halfWorld, float maxSize, float startFade, float endFade,
                      Vector2 fadeDot, Vector3 colour, float alpha, float repeat, float scroll, Vector3 eye, Vector3 forward, float projY) {
        if (halfWorld <= 0 || alpha <= .002f) return;
        TexturedBatch3D batch = m_tracerRenderer.TexturedBatch(texture, useAlphaTest: false, layer: layer,
            DepthStencilState.DepthRead, RasterizerState.CullNoneScissor, blend, SamplerState.LinearWrap);
        int n = w.Points.Length;
        int count = Cs2Wisp.Joints(w.Points, .75f, m_wispJoints, m_wispIndex);
        var points = m_wispJoints;
        Vector3 previousLeft = default, previousRight = default, previousSideDirection = default; Color previousColour = default; float previousV = 0; bool hasPrevious = false;
        for (int i = 0; i < count; i++) {
            Vector3 p = points[i];
            Vector3 tangent = points[Math.Min(i + 1, count - 1)] - points[Math.Max(i - 1, 0)];
            Vector3 toEye = p - eye;
            float depth = Vector3.Dot(toEye, forward);
            float perFraction = Cs2Tracer.MetresPerScreenHeight(depth, projY);
            Vector3 side = Vector3.Cross(tangent, toEye); float length = side.Length();
            float tangentLength = tangent.Length(), eyeDistance = toEye.Length();
            if (perFraction <= 0f || !float.IsFinite(length) || length < 1e-6f || tangentLength < 1e-6f) { hasPrevious = false; continue; }
            float endOn = length / (tangentLength * eyeDistance);   // sine of the angle between the rope and the view ray
            float dot = MathF.Sqrt(MathF.Max(0f, 1f - endOn * endOn));
            if (dot > .9995f) { hasPrevious = false; continue; }
            // Fade dot against the camera's forward axis (round 10: measured against the ray to each point, a line fired from
            // beside a nearby free camera ran along that ray and the SSG 08 / G3SG1 / SCAR-20 lines vanished — the user saw only
            // the AWP's, whose ropes fade at 0.995 or not at all).
            float forwardDot = MathF.Abs(Vector3.Dot(tangent / tangentLength, forward));
            float dotFade = fadeDot.Y > fadeDot.X ? 1f - MathUtils.Saturate((forwardDot - fadeDot.X) / (fadeDot.Y - fadeDot.X)) : 1f;
            if (hasPrevious && Vector3.Dot(side, previousSideDirection) < 0) side = -side;
            previousSideDirection = side;
            float onScreen = halfWorld / perFraction;
            float fade = 1f - MathUtils.Saturate((onScreen - startFade) / (endFade - startFade));
            float half = MathF.Min(halfWorld, maxSize * perFraction);
            float far = MathUtils.Saturate((Cs2Wisp.MaxDrawDistance - toEye.Length()) / 2.5f);
            float a = alpha * Cs2Wisp.Taper(m_wispIndex[i], n) * fade * far * dotFade;
            side *= half / length;
            var c = new Color((byte)MathUtils.Clamp(colour.X * 255, 0, 255), (byte)MathUtils.Clamp(colour.Y * 255, 0, 255), (byte)MathUtils.Clamp(colour.Z * 255, 0, 255),
                              (byte)MathUtils.Clamp(a * 255, 0, 255));
            float v = (w.Length * m_wispIndex[i] / (n - 1) + scroll) / repeat;
            Vector3 left = p - side, right = p + side;
            if (hasPrevious && (previousColour.A > 0 || c.A > 0)) {
                batch.QueueTriangle(previousLeft, previousRight, right, new Vector2(0, previousV), new Vector2(1, previousV), new Vector2(1, v), previousColour);
                batch.QueueTriangle(previousLeft, right, left, new Vector2(0, previousV), new Vector2(1, v), new Vector2(0, v), c);
            }
            previousLeft = left; previousRight = right; previousColour = c; previousV = v; hasPrevious = true;
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
        public ComponentPlayer Owner;
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

    void QueueZeus(ComponentPlayer player,Vector3 muzzle, bool muzzleSolved, Vector3 end, Vector3 direction, bool hit) {
        Cs2TaserEffect.File fx = Cs2TaserEffect.Data;
        if (fx is null || KnifeTuning.GunProfile < 0.5f) return;
        var shot = new ZeusShot {
            Owner=player,Muzzle = muzzle, End = end, Direction = direction, At = m_time.GameTime, Hit = hit,
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
        CsmcFirstPersonRenderer.ZeusMuzzle(player,KnifeClock.Now);
        // Once per shot, so a device log says where the arc started and ended.
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
            Vector3 muzzle = ReferenceEquals(shot.Owner,camera.GameWidget?.PlayerData?.ComponentPlayer)
                && CsmcFirstPersonRenderer.TryGetPlayerMuzzleWorld(shot.Owner,fx.Gun, false, out Vector3 m) ? m : shot.Muzzle;
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
        if(drawOrder==10){try{m_casings.Draw(camera);}finally{Display.BlendState=blend;Display.DepthStencilState=depth;Display.RasterizerState=rasterizer;}return;}
        if (drawOrder == 2001) {
            // After the vanilla sights pass (2000), so exactly one crosshair is ever on screen.
            var owner = camera.GameWidget?.PlayerData?.ComponentPlayer;
            try {
                if (ScGunCrosshair.Active(owner, camera, IsScoped(owner)))
                    ScGunCrosshair.Draw(m_crosshairRenderer, camera, ScUiSettings.CrosshairColor, ScUiSettings.CrosshairStyle);
            }
            catch (Exception e) { KnifeDiagnostics.WarnOnce("gun-crosshair", "gun crosshair: " + e); }
            // On its own: a crosshair failure must not take the damage direction with it (video-feedback-20260929 R3).
            // Each camera draws only its own player's marks, so split screens never share them.
            try { ScDamageIndicator.Draw(m_crosshairRenderer, camera, owner, m_time.GameTime); }
            catch (Exception e) { KnifeDiagnostics.WarnOnce("damage-direction", "damage direction: " + e); }
            finally { Display.BlendState = blend; Display.DepthStencilState = depth; Display.RasterizerState = rasterizer; }
            return;
        }
        try {
            DrawTracers(camera);
            DrawWisps(camera);
            DrawZeus(camera);
            CsmcFirstPersonRenderer.DrawFirstPersonEffects(camera);
            if (CsmcFirstPersonRenderer.ScopeOverlayFor(camera)) CsmcFirstPersonRenderer.DrawScopeOverlay(camera);
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
        if (!string.IsNullOrEmpty(m_travelSource)) ScTravelArrival.LastLeft = m_travelSource;
        if (m_scopeInput is not null) Project.FindSubsystem<SubsystemUpdate>(false)?.RemoveUpdateable(m_scopeInput);
        foreach (var pair in m_states) LeaveScope(pair.Key, pair.Value);
        m_diagnostics?.Flush("world_dispose");
        CsmcFirstPersonRenderer.ClearFirstPersonEffects();
        foreach (var state in m_states.Values) state.AmmoHud?.Dispose();
        m_states.Clear();
        m_casings.Clear();
        m_blooms.Clear();
        m_wisps.Clear();
        Project.FindSubsystem<SubsystemDrawing>(false)?.RemoveDrawable(this);
        if (ScGunRegistry.Current == m_registry) { ScGunRegistry.Current = null; ScGunMutation.HolderLocator = null; }
        if (m_registry is not null) m_registry.RecoveryOwner = null;
        base.Dispose();
    }

    /// <summary>A death is credited once. The transition from alive to dead can only happen once per creature, and
    /// the pellets of one trigger pull are merged before the attack, so a shotgun cannot count a target twice;
    /// this table is the belt-and-braces guard for a corpse hit by a second source in the same frame.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentHealth, object> s_countedDeaths = new();
    static readonly object s_countedMarker = new();

    public void ReportHit(ComponentPlayer player, ComponentBody body, int weapon, Vector3 point, int outcome, double now, ScGunKillCredit credit = null) {
        string name = BlocksManager.Blocks[Terrain.ExtractContents(weapon)].GetDisplayName(m_terrain, weapon);
        string target = body.Entity.FindComponent<ComponentCreature>()?.DisplayName ?? "生物";
        float distance = Vector3.Distance(player.ComponentCreatureModel.EyePosition, point);
        // The marker, kill panel and kill sound are the shooter's own screen: a remote client's shooter gets them sent.
        if (ScNet.IsLocal(player)) ShowHit(player, outcome, target, name, distance);
        else ScNetFeedback.Hit(player, outcome, target, name, distance);
        if (outcome != 2 || credit is null) return;
        CountKill(player, body, credit);
    }

    /// <summary>This player's hit/kill feedback (the crosshair marker and kill panel read it).</summary>
    public ScCombatFeedback FeedbackOf(ComponentPlayer player) => player is not null && m_states.TryGetValue(player, out var state) ? state.Feedback : null;
    /// <summary>A confirmed hit on this process's player's screen (its own shot, or the server's word for it).</summary>
    public void ShowHit(ComponentPlayer player, int outcome, string target, string weapon, float distance) {
        if (!m_states.TryGetValue(player, out var state)) m_states[player] = state = new GunState();
        double now = m_time.GameTime;
        // The kill sound and the kill panel are display switches; neither gates the counting.
        bool sound = outcome == 2 && now - state.Feedback.KillAt > .07;
        state.Feedback.Record(outcome, target, weapon, distance, now);
        if (sound && ScUiSettings.KillSound) ScCombatAudio.PlayKill();
    }

    /// <summary>Queues one confirmed kill against the gun that fired the shot. Writing it into the record happens
    /// on a later update through the one gun transaction; queuing here keeps the write out of a damage callback.</summary>
    void CountKill(ComponentPlayer player, ComponentBody body, ScGunKillCredit credit) {
        if (m_registry is null || m_registry.Disabled) return;
        if (!ScGunKillRules.Counts(body, player, FreeUse(player), out _)) return;
        var health = body.Entity.FindComponent<ComponentHealth>();
        if (health is null) return;
        if (s_countedDeaths.TryGetValue(health, out _)) return;
        s_countedDeaths.Add(health, s_countedMarker);
        long id = m_registry.Kills.Enqueue(credit.RecordId, credit.Variant);
        if (id < 0) KnifeLog.Warning($"gun kill credential for record {credit.RecordId} was refused as malformed");
    }

    /// <summary>Pre-0.35 key: only read to recognise a world saved by 0.34 or earlier.</summary>
    const string RechargeKey = "ZeusRechargeAt";
    /// <summary>M4 (0.35.0): the world's gun state table. Every gun's rounds, silencer and exact durability live in its
    /// record; the item value carries only the model and the record id, so state follows the item everywhere.</summary>
    ScGunRegistry m_registry;
    ValuesDictionary m_releaseBackup;
    ValuesDictionary m_integrityProtection;
    readonly HashSet<ComponentPlayer> m_integrityTold = [];
    bool m_saveReady;
    const string RegistryKey = "GunRegistry";
    const string LayoutKey = "GunDataLayout";
    int m_worldLayout;
    ScGunRegistry.WorldStatus m_worldStatus;
    ValuesDictionary m_officialMigration;
    ValuesDictionary m_schemaUpgrade;
    bool m_migrationNotice;
    readonly HashSet<ComponentPlayer> m_migrationTold = [];
    readonly HashSet<ComponentPlayer> m_oldFormatTold = [], m_legacyTold = [];
    readonly Dictionary<ComponentPlayer, double> m_brokenNoticeAt = [];
    double m_duplicateScanAt = -1;
    SubsystemGameInfo m_gameInfo;
    bool Creative => (m_gameInfo ??= Project.FindSubsystem<SubsystemGameInfo>(true)).WorldSettings.GameMode == GameMode.Creative;
    /// <summary>No ammunition items, no wear and no growth credit for this player's guns: a creative world, or a world
    /// whose mode says so for this player (deathmatch-addon). Every ordinary world has no mode: the creative answer alone.</summary>
    bool FreeUse(ComponentPlayer player) => Creative || ScModes.FreeUse(player);
    static string HolderKey(ComponentPlayer player) => ScGunHolders.PlayerKey(player, player.ComponentMiner.Inventory?.ActiveSlotIndex ?? -1);
    double m_recoveryAt = -1;
    double m_scanReportAt;
    int m_scanCalls;
    double m_scanTotalMs, m_scanMaxMs;
    /// <summary>Always a new engine snapshot, including changes made by other mods in this frame.
    /// Pending gun refunds also hold an instance until they have been delivered.</summary>
    List<ScGunHolders.Holder> Holders() {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        var holders = ScGunHolders.Scan(Project, gunIndex).ToList();
        foreach (var batch in m_registry.Recovery.Batches) foreach (var step in batch.Steps) {
            if (step.Count <= 0 || Terrain.ExtractContents(step.Value) != gunIndex || !ScGunHolders.MatchesRecord(step.Value)) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(step.Value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId)
                holders.Add(new ScGunHolders.Holder(id, $"recovery:{batch.Id}", null, -1));
        }
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        m_scanCalls++; m_scanTotalMs += elapsed; m_scanMaxMs = Math.Max(m_scanMaxMs, elapsed);
        if (m_time is not null && m_time.GameTime >= m_scanReportAt) {
            m_scanReportAt = m_time.GameTime + 30;
            if (m_scanMaxMs >= 2 || m_registry.Next >= 950)
                KnifeLog.Diagnostic($"[GUN_STORAGE] 30s audit: scans={m_scanCalls}, totalMs={m_scanTotalMs:0.##}, maxMs={m_scanMaxMs:0.##}, holders={holders.Count}, records={m_registry.Count}, next={m_registry.Next}. IDs are never reclaimed.");
            m_scanCalls = 0; m_scanTotalMs = m_scanMaxMs = 0;
        }
        return holders;
    }
    /// <summary>A refused shot or reload: tell the player once per two seconds, never fire, never charge.</summary>
    void Refused(ComponentPlayer player, ScGunResult result, double now) {
        if (result is ScGunResult.StateChanged or ScGunResult.Busy) return; // transient: the next frame sees the settled state
        if (m_brokenNoticeAt.TryGetValue(player, out double last) && now - last < 2) return;
        m_brokenNoticeAt[player] = now;
        ScNetFeedback.Tell(player, ScGunMutation.Explain(result), Color.Red);
        int value=player.ComponentMiner.ActiveBlockValue;
        int id=GunSpec.GetId(Terrain.ExtractData(value));
        KnifeDiagnostics.WarnOnce($"gun-refused-{player.PlayerData.PlayerIndex}-{id}-{result}",
            $"[GUN_STATE] operation refused: player={player.PlayerData.PlayerIndex}, id={id}, variant={GunSpec.GetVariant(Terrain.ExtractData(value))}, reason={result}, next={m_registry.Next}, quarantined={m_registry.QuarantinedCount}. No state synthesized or cleared.");
    }
    /// <summary>Holder audit (plan §6): a record held in two places at once (creative copy, glitch) is split through the
    /// same transaction every other change uses - Commit sees the other holder and publishes a clone for the acting copy.
    /// The first holder in scan order keeps the id when no one has used it since the split; a dropped item waits for pickup.
    /// Never reclaims ids. First use of a moved gun is guarded by ScGunMutation itself, this is the sweep for the rest.</summary>
    void SplitDuplicates() => SplitDuplicates(Holders());
    readonly Dictionary<string, (string State, double RetryAt)> m_duplicateRetry = new();
    void SplitDuplicates(List<ScGunHolders.Holder> holders) {
        if (m_registry is null || m_registry.Disabled) return;
        var liveKeys = holders.Select(h => h.Key).ToHashSet();
        foreach (var key in m_duplicateRetry.Keys.Where(k => !liveKeys.Contains(k)).ToArray()) m_duplicateRetry.Remove(key);
        int budget = 4;
        foreach (var group in holders.GroupBy(h => h.Id).Where(g => g.Count() > 1).ToArray()) {
            var keeper = group.First();
            if (m_registry.PeekNextId() < 0) {
                KnifeDiagnostics.WarnOnce($"registry-full-duplicate-{group.Key}", $"[GUN_STORAGE] id={group.Key} has {group.Count()} distinct holders; next={m_registry.Next}, no free ID. Audit allocation retries suspended until capacity changes. First={keeper.Key}, type={keeper.Inventory?.GetType().FullName}");
                continue;
            }
            foreach (var other in group.Skip(1).Where(h => h.Inventory is not null)) {
                double now = m_time?.GameTime ?? 0;
                string state = $"{group.Key}/{m_registry.Next}/{m_registry.Get(group.Key)?.Revision}/{ScInventoryTransaction.Revision(other.Inventory)}/{other.Inventory.GetSlotValue(other.Slot)}/{other.Inventory.GetSlotCount(other.Slot)}/" + string.Join(";", group.Select(h => h.Key).Order());
                if (m_duplicateRetry.TryGetValue(other.Key, out var retry) && retry.State == state && now < retry.RetryAt) continue;
                if (--budget < 0) return;
                var m = ScGunMutation.Prepare(other.Inventory, other.Slot, other.Key, out ScGunResult why);
                // A live second inventory is sufficient positive evidence; no negative cache can approve use.
                // Dropped/projectile-only witnesses retain the full fresh scan fallback.
                var witness = group.FirstOrDefault(h => h.Key != other.Key && h.Inventory is not null);
                if (m is not null && witness.Inventory is not null)
                    m.DuplicateWitness = () => ScGunHolders.StillDuplicates(other, witness, BlocksManager.GetBlockIndex<ScGunBlock>(true));
                var result = m is null ? why : m.Commit(_ => { });
                if (result == ScGunResult.Success) {
                    m_duplicateRetry.Remove(other.Key);
                    KnifeLog.Information($"[GUN_STORAGE] real duplicate {group.Key}: {other.Key} ({other.Inventory.GetType().FullName}) -> {m.Id}; next={m_registry.Next}");
                } else {
                    m_duplicateRetry[other.Key] = (state, now + 5);
                    KnifeDiagnostics.WarnOnce($"duplicate-{group.Key}-{other.Key}-{result}", $"[GUN_STORAGE] record={group.Key}, holder={other.Key}, type={other.Inventory.GetType().FullName}, next={m_registry.Next}: split deferred: {result}");
                }
            }
        }
    }
    /// <summary>Whether any player holding this inventory is in the middle of an action a record write must not
    /// disturb: a reload, a silencer change, a burst, the R8's drawn hammer or any first-person action clip.
    ///
    /// The test is per inventory, not per slot, because a commit bumps the whole inventory's revision, and a
    /// reload in progress reads a bumped revision as "the gun moved" and cancels itself. A kill credit or a level
    /// therefore waits for the action to end; neither is lost, both are retried on the next sweep.</summary>
    bool ActionBusy(IInventory inventory) {
        if (inventory is null) return true;
        foreach (var pair in m_states) {
            if (!ReferenceEquals(pair.Key.ComponentMiner?.Inventory, inventory)) continue;
            var state = pair.Value;
            if (state.BusyUntil >= 0 || state.Reload is not null || state.BurstRemaining > 0 || state.PrepareUntil >= 0
                || state.ShellTimes.Count > 0 || state.SilencerPending) return true;
            var model = pair.Key.Entity?.FindComponent<ComponentFirstPersonModel>();
            if (model is not null && KnifeAnimationController.IsBusy(model)) return true;
        }
        return false;
    }
    bool HolderBusy(ScGunHolders.Holder holder) => ActionBusy(holder.Inventory);
    /// <summary>Writes queued kill credits, then applies any level that has been earned and is safe to apply.
    /// Neither step refills a magazine, repairs a gun or completes a charge.</summary>
    void UpdateGrowth(List<ScGunHolders.Holder> holders) {
        if (m_registry is null || m_registry.Disabled) return;
        ScGunGrowthService.Advance(m_registry, holders, m_time.GameTime, HolderBusy, (id, from, to) => {
            // The first sweep can run before m_states is populated (e.g. an old 104-kill gun).
            foreach (var player in m_players.ComponentPlayers) {
                var inventory = player.ComponentMiner?.Inventory;
                if (inventory is null || inventory.ActiveSlotIndex < 0) continue;
                int held = inventory.GetSlotValue(inventory.ActiveSlotIndex);
                if (Terrain.ExtractContents(held) != BlocksManager.GetBlockIndex<ScGunBlock>(true)
                    || GunSpec.GetId(Terrain.ExtractData(held)) != id) continue;
                string name = BlocksManager.Blocks[Terrain.ExtractContents(held)].GetDisplayName(m_terrain, held);
                player.ComponentGui.DisplaySmallMessage($"{name}\n升级成功：Lv{from} → Lv{to}！" + (to == ScGunGrowth.MaxLevel ? "已满级。" : ""), new Color(90, 210, 225), true, true);
            }
        });
    }
    void BrokenNotice(ComponentPlayer player, double now) {
        if (m_brokenNoticeAt.TryGetValue(player, out double last) && now - last < 2) return;
        m_brokenNoticeAt[player] = now;
        player.ComponentGui.DisplaySmallMessage("枪械已损坏，请到装配台维修", Color.Red, true, false);
    }

    public override void Load(ValuesDictionary valuesDictionary) {
        m_saveReady = false;
        string identity=valuesDictionary.GetValue<string>(ScGunTravel.WorldIdentity,null);
        if(identity is not null && !Guid.TryParse(identity,out _))throw new InvalidOperationException("枪械跨世界身份数据损坏，拒绝加载");
        m_travelWorldIdentity=identity??Guid.NewGuid().ToString("N");
        m_travelIdentities=valuesDictionary.GetValue<ValuesDictionary>(ScGunTravel.Identities,null);
        m_travelBackup=valuesDictionary.GetValue<ValuesDictionary>(ScGunTravel.Backup,null);
        ScGunSaveGuard.Validate(valuesDictionary); // fail even if the XML hook was bypassed
        base.Load(valuesDictionary);
        string migrationError = valuesDictionary.GetValue<string>(ScGun0282Migration.ErrorKey, null);
        if (migrationError is not null) throw new InvalidOperationException("0.28.2 枪械迁移未执行，原世界未改动：" + migrationError);
        m_officialMigration = valuesDictionary.GetValue<ValuesDictionary>(ScGun0282Migration.Marker, null);
        // Where this world's pre-upgrade backup went, kept with the world so the player can always find it.
        m_schemaUpgrade = valuesDictionary.GetValue<ValuesDictionary>(ScGunSchemaUpgrade.Marker, null);
        m_releaseBackup = valuesDictionary.GetValue<ValuesDictionary>(ScGunSchemaUpgrade.ReleaseMarker, null);
        m_integrityProtection = valuesDictionary.GetValue<ValuesDictionary>(ScGunLoadIntegrity.ProtectionKey, null);
        if (m_schemaUpgrade is not null)
            KnifeLog.Information($"gun record schema upgraded from {m_schemaUpgrade.GetValue<int>("From", 0)}; world backup at {m_schemaUpgrade.GetValue<string>("Backup", "?")}");
        m_migrationNotice = valuesDictionary.GetValue<bool>(ScGun0282Migration.NoticeKey, false);
        m_time = Project.FindSubsystem<SubsystemTime>(true);
        m_registry = ScGunRegistry.Load(valuesDictionary.GetValue<ValuesDictionary>(RegistryKey, null), m_time.GameTime);
        if (m_registry.UnknownSchema) throw new InvalidOperationException("枪械记录或补偿格式无法安全读取，已拒绝进入世界；请使用兼容版本或有效备份。");
        ScGunRegistry.Current = m_registry;
        m_registry.RecoveryOwner = inventory => ScGunHolders.RecoveryOwner(Project, inventory);
        // Guns carried between worlds (ScItemTravel): this world's identity, the identity of each record, the
        // transfers already committed here.
        m_travel = new ScTravelLedger { WorldIdentity = m_travelWorldIdentity };
        m_travel.LoadIdentities(m_travelIdentities);
        m_travel.LoadReceipts(valuesDictionary.GetValue<ValuesDictionary>(ScTravelLedger.ReceiptsKey, null));
        m_registry.Travel = m_travel;
        ScGunMutation.HolderLocator = (id, except) => Holders().Where(h => h.Id == id && h.Key != except).Select(h => h.Key).ToArray();
        m_worldLayout = valuesDictionary.GetValue<int>(LayoutKey, 0);
        m_worldStatus = ScGunRegistry.Classify(m_worldLayout, valuesDictionary.ContainsKey(RegistryKey), valuesDictionary.ContainsKey(RechargeKey) || valuesDictionary.ContainsKey("GunWear"));
        m_registry.LegacyWorld = m_worldStatus == ScGunRegistry.WorldStatus.Legacy;
        KnifeLog.Information($"gun registry: {m_registry.Count} records ({m_registry.QuarantinedCount} quarantined), next id {m_registry.Next}; world gun data layout stamp {m_worldLayout}, status {m_worldStatus}, this version {GunSpec.DataLayout}, schema read {(m_registry.UnknownSchema ? "unknown" : m_registry.LoadedSchema.ToString())} -> written {ScGunRegistry.Schema}, counter rule {m_registry.GrowthMode}, {m_registry.Kills.Count} kill credit(s) pending"
            + (m_registry.Disabled ? " - guns disabled in this world" : ""));
        ScSushiCompatibility.LogOnce(Project);
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
        m_travelSource = Project.FindSubsystem<SubsystemGameInfo>(true).DirectoryName;
        m_scopeInput = new ScScopeCamera(this, m_players);
        Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(m_scopeInput);
        m_time = Project.FindSubsystem<SubsystemTime>(true);
        ScGunplaySettings.Load();
        m_diagnostics = new ScGunDiagnostics(ScGunplaySettings.Diagnostics);
        m_saveReady = true;
        // subworld-travel-generic-20261003: a player who arrives from another world of this world's tree carrying guns
        // whose records stayed there (a sub-world mod that restores only the item values after loading). Single player
        // only: the sub-world mods found are single-player ones, and a host's peers are not this check's to read.
        try { m_arrival = ScNet.IsAuthority && !ScNet.IsHost ? ScTravelArrival.Prepare(m_travelSource, ScTravelArrival.LastLeft, ScGunTravel.ImportedOnLoad > 0, m_travel, ScWorldTree.IsWorld, ScWorldTree.Children, ScWorldTree.ReadProject) : null; }
        catch (Exception e) { m_arrival = null; KnifeLog.Warning("[GUN_TRAVEL] arrival check not prepared: " + e.Message); }
    }

    public override void Save(ValuesDictionary valuesDictionary) {
        // Before any output mutation, including base.Save: partial/unknown loads cannot be saved.
        ScGunSaveGuard.ValidateSave(m_saveReady, m_worldLayout, m_registry);
        base.Save(valuesDictionary);
        if(!string.IsNullOrWhiteSpace(m_travelSource))valuesDictionary.SetValue(ScGunTravel.SourcePath,m_travelSource);
        if(!string.IsNullOrWhiteSpace(m_travelWorldIdentity))valuesDictionary.SetValue(ScGunTravel.WorldIdentity,m_travelWorldIdentity);
        if(m_travel is not null)m_travelIdentities=m_travel.SaveIdentities();
        if(m_travelIdentities is not null)valuesDictionary.SetValue(ScGunTravel.Identities,m_travelIdentities);
        // Written only by a world that has taken a live transfer; a build that does not know the key drops it and
        // loses nothing but the repeat protection of a trip in flight.
        if(m_travel?.SaveReceipts() is {} receipts)valuesDictionary.SetValue(ScTravelLedger.ReceiptsKey,receipts);
        if(m_travelBackup is not null)valuesDictionary.SetValue(ScGunTravel.Backup,m_travelBackup);
        // Zeus charge lives in the gun records now (per instance); the per-player ZeusRechargeAt table is no longer written.
        if (m_registry is not null) valuesDictionary.SetValue(RegistryKey, m_registry.Save(m_time.GameTime));
        if (m_officialMigration is not null) valuesDictionary.SetValue(ScGun0282Migration.Marker, m_officialMigration);
        if (m_schemaUpgrade is not null) valuesDictionary.SetValue(ScGunSchemaUpgrade.Marker, m_schemaUpgrade);
        if (m_releaseBackup is not null) valuesDictionary.SetValue(ScGunSchemaUpgrade.ReleaseMarker, m_releaseBackup);
        if (m_integrityProtection is not null) valuesDictionary.SetValue(ScGunLoadIntegrity.ProtectionKey, m_integrityProtection);
        valuesDictionary.SetValue(LayoutKey, ScGunRegistry.StampFor(m_registry?.LegacyWorld == true)); // a legacy world stays marked legacy
    }

    public void Update(float dt) {
        KnifeQa.Step();
        m_casings.Update(dt,m_terrain,m_audio);
        for (int i = m_wisps.Count - 1; i >= 0; i--) { m_wisps[i].Update(dt); if (m_wisps[i].Dead) m_wisps.RemoveAt(i); }
        m_diagnostics?.Tick(m_time.GameTime);
        // A remote multiplayer client only shows the server's world: record maintenance is the server's.
        bool authority = ScNet.IsAuthority;
        if (authority && m_registry is not null && !m_registry.Disabled && m_time.GameTime >= m_recoveryAt) {
            m_recoveryAt = m_time.GameTime + 1;
            m_registry.Recovery.Retry(owner => ScGunHolders.ResolveRecoveryOwner(Project, owner));
        }
        ScNetMirror.RecordsTick(m_registry, m_time.GameTime);
        if (m_arrival is not null) {
            if (authority && m_players.ComponentPlayers.Count == 1 && m_players.ComponentPlayers[0] is { } traveller) {
                string message = m_arrival.Step(Project, traveller.ComponentMiner.Inventory, ScItemTravel.GunBlock, Time.RealTime);
                if (message is not null) traveller.ComponentGui.DisplaySmallMessage(message, Color.White, false, false);
            }
            if (m_arrival.Finished) m_arrival = null;
        }
        ScNetGuns.ClientTick();
        ScNet.HostTick(Project);
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        foreach (var pair in m_states) if (!m_players.ComponentPlayers.Contains(pair.Key)) {
            pair.Value.AmmoHud?.Dispose(); pair.Value.AmmoHud = null;
        }
        if (authority && m_time.GameTime >= m_duplicateScanAt) {
            m_duplicateScanAt = m_time.GameTime + .5;
            var holders = Holders();
            SplitDuplicates(holders);
            UpdateGrowth(holders);
        }
        foreach (ComponentPlayer player in m_players.ComponentPlayers) UpdatePlayer(player, dt, authority, gunIndex);
    }

    /// <summary>One player's guns for this frame: this process's own player from its devices, a remote client's player
    /// (server) from its replicated input; another client's player is left to the server. (Its own method so the offline
    /// network checks run exactly this for a client and a server end.)</summary>
    void UpdatePlayer(ComponentPlayer player, float dt, bool authority, int gunIndex) {
        if (!m_states.TryGetValue(player, out GunState state)) m_states[player] = state = new GunState();
        var physical = player.ComponentBody;
        bool grounded = physical.StandingOnValue.HasValue || physical.StandingOnBody is not null;
        bool jumping = (player.ComponentLocomotion.JumpOrder > 0 || player.ComponentLocomotion.LastJumpOrder > 0) && physical.Velocity.Y > .1f;
        // Multiplayer: a remote client's player is interpolated here, never standing on anything; its client reports its footing.
        if (ScNetGuns.RemoteInput(player) is { } footing) { grounded = footing.Grounded; jumping = footing.Jumping; }
        else if (ScNet.IsRemoteClient && ScNet.IsLocal(player)) { ScNetGuns.LocalStance(grounded, jumping); ScNetGuns.Observe(player); }
        state.Stance.Update(m_time.GameTime, grounded, jumping, state.Zoom > 0);
        if (ScGunplaySettings.Enabled || state.ModeKick) RecoverKick(player,state,dt,state.KickRecoveryRate);
        ModeViewKick(player, state);
        int value = player.ComponentMiner.ActiveBlockValue;
        // Actions must advance even when no first-person camera renders this player.
        if(player.Entity.FindComponent<ComponentFirstPersonModel>() is {} actionModel && !KnifeQa.Active) {
            int visual=Project.FindSubsystem<SubsystemScC4>(false)?.ViewmodelValue(player,value)??value;
            visual=Project.FindSubsystem<SubsystemScGrenades>(false)?.ViewmodelValue(player,visual)??visual;
            KnifeAnimationController.Update(actionModel, player.ComponentHealth.Health>0 ? visual : 0);
            // Multiplayer: what the others see this player's weapon doing comes from the process that reads its devices.
            if (ScNet.Role is ScNetRole.Host or ScNetRole.Client && ScNet.IsLocal(player))
                ScNetPresentation.Tick(player, KnifeAnimationController.ReadAction(actionModel),
                    Project.FindSubsystem<SubsystemScGrenades>(false)?.ThrowPhase(player) ?? default, Project.FindSubsystem<SubsystemScC4>(false)?.PlantPhase(player) ?? default);
        }
        // Multiplayer: this process reads the player's devices (local), the server runs a remote client's player from
        // its replicated input, and a client leaves every other player to the server.
        bool local = ScNet.IsLocal(player);
        if (!local && ScNetGuns.RemoteInput(player) is null) return;
        if (authority && (ScGunSkinTemplateBlock.IsTemplate(value) || ScGunCounterTemplateBlock.IsTemplate(value)) && player.ComponentHealth.Health > 0) {
            var inventory = player.ComponentMiner.Inventory;
            bool counter = ScGunCounterTemplateBlock.IsTemplate(value);
            var result = counter
                ? ScGunCounterTemplateBlock.Materialize(inventory, inventory.ActiveSlotIndex, HolderKey(player))
                : ScGunSkinTemplateBlock.Materialize(inventory, inventory.ActiveSlotIndex, HolderKey(player));
            if (result != ScGunResult.Success) Refused(player, result, m_time.GameTime);
            value = player.ComponentMiner.ActiveBlockValue;
        }
        // subworld-travel-generic-20261003: a gun that arrived from another world of this tree and has no local number yet is
        // neither used nor reported as damaged (ScTravelArrival.Pending; at most the moment until the inventory settles)
        bool arriving = m_arrival?.Pending(player.ComponentMiner.Inventory.ActiveSlotIndex, value) == true;
        if (local) { // notices are for the players this process shows
            if (m_integrityProtection?.GetValue<int>("Count", 0) > 0 && m_integrityTold.Add(player))
                player.ComponentGui.DisplaySmallMessage("正常枪械可继续使用；部分枪械记录异常，已原样保留并暂停使用，需原始备份恢复。备份由玩家自行管理。", Color.Yellow, true, false);
            if (m_migrationNotice && m_migrationTold.Add(player))
                player.ComponentGui.DisplaySmallMessage($"已兼容 0.28.2：{m_officialMigration?.GetValue<int>("Guns", 0) ?? 0} 把旧枪保留型号与弹量，耐久已补满。备份由玩家自行管理。", Color.White, true, false);
            if (m_registry?.LegacyWorld == true && m_legacyTold.Add(player))
                player.ComponentGui.DisplaySmallMessage("此世界由 0.34 及更早版本保存，本版的枪械在这里全部停用（物品保留原样）。请新建世界。", Color.Red, true, false);
            // A multiplayer client's gun waiting for the server's record is not old or damaged data (ScGunBlock.AwaitsRecord).
            if (!arriving && Terrain.ExtractContents(value) == gunIndex && ScGunBlock.IsOldFormat(value) && !ScGunBlock.AwaitsRecord(value) && m_oldFormatTold.Add(player)) {
                player.ComponentGui.DisplaySmallMessage(GunSpec.IsForeign(Terrain.ExtractData(value)) ? "这把枪是旧版本的数据，本版无法使用（已保留原样）。请新建世界。" : "这把枪的状态记录不存在（旧版本数据或损坏存档），已保留原样。", Color.Red, true, false);
                KnifeLog.Warning($"unusable gun data {Terrain.ExtractData(value)} held by player {player.PlayerData.PlayerIndex}; world status {m_worldStatus}; not decoded, not written");
            }
        }
        bool holdingGun = !arriving && Terrain.ExtractContents(value) == gunIndex && ScGunBlock.IsKnown(value) && player.ComponentHealth.Health > 0f;
        if (!holdingGun) {
            // A client's gun still waiting for its record is shown but cannot be used: say why when the trigger is pulled
            // (a blocked client will never get the record; an accepted one gets it with the server's next rows).
            if (local && ScNet.ClientBlocked && Terrain.ExtractContents(value) == gunIndex && ScGunBlock.AwaitsRecord(value) && ScGunBindings.Available(player)
                && (player.ComponentInput.PlayerInput.Hit.HasValue || player.ComponentInput.PlayerInput.Dig.HasValue || ScGunBindings.Down(player, ScGunFunctions.Fire, true))) ScNet.TellBlocked(player);
            // A trigger held when the gun was put away must not stay pressed on the server; whether this player may act
            // (menus, dialogs) is still reported, since the server's knife, grenade and C4 checks read it too.
            if (local && ScNet.IsRemoteClient) {
                ScNetGuns.SendInput(player, ScGunBindings.Available(player), false, false, false, false, false, 0, LookRay(player), ScGunBindings.ContextAvailable(player), false);
                // Nothing stays predicted for a gun that is not in hand; a gun whose slot arrived before its record
                // asks the server for the record (bounded) instead of waiting for the next unrelated change.
                ScNetGuns.DropPrediction();
                if (Terrain.ExtractContents(value) == gunIndex && ScGunBlock.AwaitsRecord(value)) ScNetMirror.ClientWants(GunSpec.GetId(Terrain.ExtractData(value)));
            }
            if (authority && !local) {
                // A remote client's player without a usable gun in hand (server): nothing it has shown will be fired, and
                // a reload it asked for is not performed.
                ScNetGuns.ServerRefuseDropped(player);
                if (state.PendingReloadId != 0) { ScNetGuns.ReloadResult(player, state.PendingReloadId, ScNetGuns.ReloadPhase.Refused); state.PendingReloadId = 0; }
                ScNetGuns.ServerSettle(player, false);
            }
            state.AmmoHud?.Hide();
            CancelReload(player, state, cancelAnimation: false);
            state.ServerReloading = false;
            LeaveScope(player, state);
            if (!ScGunplaySettings.Enabled && !state.ModeKick) RecoverKick(player, state, dt, 12f);
            state.BusyUntil = -1;
            state.PendingRounds = -1;
            state.SilencerPending = false;
            state.Scheduled.Clear();
            state.LastValue = int.MinValue;
            state.Selection.Reset();
            return;
        }
        if (local && ScNet.ClientBlocked) {
            // A client whose CS network layer is not accepted fires nothing, not even a prediction.
            state.AmmoHud?.Hide();
            if (ScGunBindings.Available(player) && (player.ComponentInput.PlayerInput.Hit.HasValue || player.ComponentInput.PlayerInput.Dig.HasValue || ScGunBindings.Down(player, ScGunFunctions.Fire, true))) ScNet.TellBlocked(player);
            return;
        }
        UpdateGun(player, state, value, dt);
        // A remote client's player (server): shots that client has shown and the server will not fire are settled now.
        if (authority && !local) ScNetGuns.ServerSettle(player, WillFire(player, state));
        if (local) UpdateAmmoHud(player, state);
    }

    /// <summary>Server: whether this remote client's gun may still execute shots that client has shown: with rounds in the
    /// gun and no reload in progress, while the trigger is held on an automatic gun, a press is not taken yet, a burst
    /// runs, the R8's hammer is drawn or a shot is queued behind a shell reload - or, with the trigger up, for
    /// ScNetGuns.ShotLead after the newest shot was reported (a shot shown just before the release is executed with it;
    /// nothing is executed later than that). When false, what the client has shown beyond the server's shots will not
    /// happen (ScNetGuns.ServerSettle).</summary>
    bool WillFire(ComponentPlayer player, GunState state) {
        if (ScNetGuns.RemoteInput(player) is not { } remote || state.Reload is not null || !ScNetGuns.Holds(player, remote)) return false;
        int value = player.ComponentMiner.ActiveBlockValue, data = Terrain.ExtractData(value);
        if (!ScGunBlock.IsKnown(value) || GunSpec.GetRounds(data) <= 0 || !FreeUse(player) && ScGunDurability.IsBroken(data)) return false;
        bool recent = ScNet.Now - remote.ShotsAt <= ScNetGuns.ShotLead;
        // (A client that can no longer act - a menu, a dialog - is like a trigger that is up; a dead player's shots are not.)
        if (!remote.Available) return recent && player.ComponentHealth.Health > 0;
        bool held = ScGunBlock.SpecOf(value).Automatic && (remote.Dig || remote.Custom);
        return held || remote.HasPresses || state.BurstRemaining > 0 || state.PrepareUntil >= 0 || state.FireAfterReload || recent;
    }

    /// <summary>Test diagnostics (off): the last shot's camera, eye, shot ray, tracer start and hit, as one line.</summary>
    public static bool DebugShots;
    public static string LastShotDebug = "";
    int m_shotLogLines; double m_shotLogAt = double.NegativeInfinity;
    /// <summary>At most 4 free-camera shot lines a second and 400 a session: an automatic gun held down cannot flood Game.log.</summary>
    bool ShotLogAllowed(ComponentPlayer player) {
        if (m_shotLogLines >= 400 || Time.RealTime - m_shotLogAt < .25) return false;
        m_shotLogLines++; m_shotLogAt = Time.RealTime; return true;
    }
    /// <summary>One frame of gun input: this process's own devices, or (server) what a remote client sent.</summary>
    readonly record struct GunInput(bool Available, bool CustomFire, bool NativeDig, bool NativeHit, bool ReloadKey, bool Touch, Ray3? Dig, Ray3? Hit, Ray3 Look, int RemoteZoom);

    GunInput ReadInput(ComponentPlayer player, bool consumeEvents) {
        if (ScNetGuns.RemoteInput(player) is { } remote) {
            // A trigger the client stopped renewing is let go before anything is read (ScNetGuns.InputLease).
            if (consumeEvents && remote.Expire(ScNet.Now)) ScNet.Trace($"input P{player.PlayerData?.PlayerIndex} expired: no message for {ScNet.Now - remote.ReceivedAt:0.00} s, trigger let go");
            bool hit = consumeEvents && remote.TakeHit(), reload = consumeEvents && remote.TakeReload();
            // A press whose release already arrived still counts once (the trigger is down for this frame).
            bool digPress = consumeEvents && remote.TakeDigPress(), customPress = consumeEvents && remote.TakeCustomPress();
            bool dig = remote.Dig | digPress, custom = remote.Custom | customPress;
            // A press fires along the ray it was sent with, not a later message's.
            Ray3 aim = (hit || digPress || customPress) && remote.TakePressAim() is { } pressed ? pressed : remote.HasAim ? remote.Aim : LookRay(player);
            return new(remote.Available && player.ComponentHealth.Health > 0, custom, dig, hit, reload, remote.Touch,
                dig ? aim : null, hit ? aim : null, aim, remote.Zoom);
        }
        PlayerInput input = player.ComponentInput.PlayerInput;
        bool customFire = (m_fireButtons.GetValueOrDefault(player) || ScGunBindings.Down(player, ScGunFunctions.Fire)) && !ScWeaponTouchPanel.MenuActive;
        bool nativeAllowed = ScMobileControls.NativeGunFireAllowed(player);
        bool reloadKey = consumeEvents && ScGunBindings.Down(player, ScGunFunctions.Reload, true);
        return new(ScGunBindings.Available(player), customFire, nativeAllowed && input.Dig.HasValue, nativeAllowed && input.Hit.HasValue, reloadKey,
            ScMobileControls.UsesTouchInput(player), input.Dig, input.Hit, LookRay(player), -1);
    }

    void UpdateGun(ComponentPlayer player, GunState state, int value, float dt) {
        GunSpec spec = ScGunBlock.SpecOf(value);
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        double now = m_time.GameTime;
        double actionNow = KnifeClock.Now;
        int data = Terrain.ExtractData(value);
        // Multiplayer (ScNet): the server runs this same state machine for a remote client's player from its replicated
        // input and commits everything; that client runs it as a prediction (hands, sounds, recoil, tracers) and commits
        // nothing. Single player is the authority with local input, exactly as before.
        bool local = ScNet.IsLocal(player), authority = ScNet.IsAuthority;
        // A remote client plays with the server's rounds less its own shots the server has not counted yet; the record
        // itself is never touched by a prediction (ScNetGuns.ShownRounds).
        bool predicted = local && !authority;
        int rounds = predicted ? ScNetGuns.ShownRounds(player, GunSpec.GetRounds(data)) : GunSpec.GetRounds(data);
        var gi = ReadInput(player, consumeEvents: true);
        if (ScNetGuns.RemoteInput(player) is { } remoteInput && remoteInput.TakeSecondary()) RequestSecondary(player);
        if (gi.RemoteZoom >= 0 && gi.RemoteZoom != state.Zoom && gi.RemoteZoom <= spec.ZoomLevels.Length) state.Zoom = gi.RemoteZoom;
        if (authority && !local) {
            // A remote client's reload request (server). It is answered in every case: performed (accepted, then completed or
            // cancelled) or refused; one that cannot start yet because the gun is still busy here is kept for a moment.
            ScNetGuns.ServerRefuseDropped(player);
            if (gi.ReloadKey && ScNetGuns.RemoteInput(player) is { ReloadId: not 0 } asked) { state.PendingReloadId = asked.ReloadId; state.PendingReloadUntil = ScNet.Now + ScNetGuns.ReloadRequestLease; }
            if (state.PendingReloadId != 0) {
                if (state.Reload is not null) { state.ReloadId = state.PendingReloadId; state.PendingReloadId = 0; ScNetGuns.ReloadResult(player, state.ReloadId, ScNetGuns.ReloadPhase.Accepted); } // the reload under way here is the one asked for
                else if (rounds >= ScGunGrowth.Capacity(ScGunBlock.GetVariant(value), EffectiveGunStats.LevelOf(value)) || spec.RechargeSeconds > 0f || ScNet.Now > state.PendingReloadUntil) {
                    ScNetGuns.ReloadResult(player, state.PendingReloadId, ScNetGuns.ReloadPhase.Refused); state.PendingReloadId = 0;
                }
            }
        }
        // Server, a remote client's player: the shots that client has shown and this end has neither executed nor refused.
        // They decide when this end fires for that player (ScNetGuns.ShotLead), within its own checks.
        // (Only for the gun those shots were shown with: after a switch the count of the gun put away fires nothing here.)
        var shown = authority && !local ? ScNetGuns.RemoteInput(player) : null;
        int owed = shown is not null && ScNetGuns.Holds(player, shown) ? shown.Owed : 0;
        // A client's input for this frame goes out when the frame's gun update is done (the finally below), so that it
        // carries what this frame did: the count of shots shown includes a shot shown now, with the press and the aim it
        // was shown with, and a reload request goes with the frame that started the reload. (It used to be sent first, so
        // a shot was reported one frame after its press, by which time the press and its aim were already taken.)
        // The scope level and the ray's source are those the frame began with: what the shot itself was taken with.
        int sendZoom = state.Zoom; bool sendScoped = gi.Touch || state.Zoom > 0;
        try {
        var heldInventory = player.ComponentMiner.Inventory;
        bool switched = state.Selection.Observe(heldInventory, heldInventory?.ActiveSlotIndex ?? -1, value, true) || state.LastValue == int.MinValue;
        if (switched) {
            CancelReload(player, state, cancelAnimation: false);
            state.ServerReloading = false;
            LeaveScope(player, state);
            int drawnVariant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
            string deployClip = KnifeAnimationController.DeployClip(drawnVariant, spec.HasSilencer && !GunSpec.GetSilencerOff(data));
            state.BusyUntil = actionNow + CsmcKnifeRig.GetProfileDuration(drawnVariant, deployClip);
            state.PendingRounds = -1;
            state.SilencerPending = false;
            state.Scheduled.Clear();
            state.ShellTimes.Clear();
            state.FireAfterReload = false;
            CancelPrepare(player, state, "switch", now);
            state.RescopeAt = -1;
            state.BurstRemaining = 0; state.BurstNextAt = -1;
            state.LastValue = value;
            string visibleClip=KnifeAnimationController.CurrentClip(model);
            // Clip cues are this process's own player's (they play without position).
            if (!local) { }
            else if(KnifeAnimationController.CurrentVariant(model)==drawnVariant && visibleClip?.StartsWith("inspect",StringComparison.Ordinal)==true){Schedule(state,spec.Name,visibleClip,now);state.InspectSoundToken=KnifeAnimationController.ActionToken(model);}
            else if (!Schedule(state, spec.Name, deployClip, now)) ScPresentationSound.PlayHeld(model, KnifeAnimationController.ActionToken(model), $"{spec.Name}_draw");
        }
        // A remote client's reload is display only: when the server gives the fresh gun in hand its record meanwhile, the
        // slot's value changes under the same selection and the reload shown goes on (it used to be cut off there).
        if (!authority && !switched && state.Reload is not null) state.Reload.FollowAllocation(value);
        if (state.Reload is not null && (!state.Reload.Valid
            || !ReferenceEquals(state.Reload.Inventory, heldInventory)
            || !state.Reload.ModeMatches(FreeUse(player))))
            CancelReload(player, state);
        if (local) PlayScheduled(player,state,now); else state.Scheduled.Clear();
        // Menus block new input, not an already accepted reload/attachment. Its ammo
        // milestones use the same clock as hands and cues, even if world time pauses.
        // A remote client only shows the reload: the server takes the magazines and fills the record.
        if (state.Reload is not null) {
            if (state.DropAt >= 0 && actionNow >= state.DropAt) {
                state.DropAt = -1;
                if (authority && !state.Reload.Discard()) CancelReload(player, state);
            }
            if (state.Reload is not null && state.InsertAt >= 0 && actionNow >= state.InsertAt) {
                double insertAt = state.InsertAt;
                state.InsertAt = -1;
                if (authority && !state.Reload.InsertMagazineAt(actionNow, insertAt)) { if (state.Reload.LastResult != ScGunResult.Success) Refused(player, state.Reload.LastResult, now); CancelReload(player, state); }
            }
            value = player.ComponentMiner.ActiveBlockValue;
            data = Terrain.ExtractData(value); rounds = predicted ? ScNetGuns.ShownRounds(player, GunSpec.GetRounds(data)) : GunSpec.GetRounds(data);
            state.LastValue = value;
        }


        state.LastValue = value;

        // A shotgun's shells count one at a time, at each loop's add-ammo moment.
        while (state.ShellTimes.Count > 0 && actionNow >= state.ShellTimes[0]) {
            state.ShellTimes.RemoveAt(0);
            if (!authority) continue;
            if (state.Reload is not null && state.Reload.InsertShell()) {
                value = state.Reload.Expected; state.LastValue = value;
                data = Terrain.ExtractData(value); rounds = GunSpec.GetRounds(data);
            }
            else { if (state.Reload is not null && state.Reload.LastResult != ScGunResult.Success) Refused(player, state.Reload.LastResult, now); CancelReload(player, state); break; }
        }

        // Inserted rounds are already committed. Only release the firing lock
        // here; the remaining bolt/hand animation still has to finish.
        if (state.BusyUntil >= 0 && actionNow >= state.BusyUntil) {
            state.BusyUntil = -1;
            if (state.Reload is not null) {
                ScControllerFeedback.Reloaded(player);
                if (authority && !local) ScNetGuns.ReloadResult(player, state.ReloadId, ScNetGuns.ReloadPhase.Completed);
            }
            state.Reload = null;
            // The server commits the silencer change; a remote client's mirror shows it.
            if (state.SilencerPending && !authority) state.SilencerPending = false;
            if (state.SilencerPending) {
                state.SilencerPending = false;
                var silencer = ScGunMutation.Prepare(player.ComponentMiner.Inventory, player.ComponentMiner.Inventory.ActiveSlotIndex, HolderKey(player), out ScGunResult silencerWhy);
                bool off = state.PendingSilencerOff;
                var outcome = silencer is null ? silencerWhy : silencer.Commit(r => r.SilencerOff = off);
                if (!local) ScNet.Trace($"silencer P{player.PlayerData.PlayerIndex} off={off}: {outcome}");
                if (outcome == ScGunResult.Success) { value = silencer.Expected; data = Terrain.ExtractData(value); state.LastValue = value; }
                else Refused(player, outcome, now);
            }
        }
        bool busy = state.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model);
        if (!gi.Available) {
            // (Server, a remote client's player.) A shot that client showed in its last frame before it could no longer
            // act - a menu opened in the next one, and both reports arrived together - is still that shot.
            if (shown is not null && owed > 0 && spec.CycleSecondsAlternate <= 0f && ScNet.Now - shown.ShotsAt <= ScNetGuns.ShotLead && player.ComponentHealth.Health > 0
                && !busy && rounds > 0 && state.BurstRemaining == 0 && now >= state.NextShot - ScNetGuns.ShotLead)
                Fire(player, state, model, spec, value, data, rounds, gi with { Dig = null, Hit = null, Look = shown.ShotAim });
            SuspendScope(player);
            // Do not bank an attack while operating a menu and fire it on closing.
            state.FireAfterReload = false;
            CancelPrepare(player, state, "menu", now);
            state.BurstRemaining = 0; state.BurstNextAt = -1;
            return;
        }
        if (state.RescopeAt >= 0 && now >= state.RescopeAt) {
            state.RescopeAt = -1;
            if (!busy && state.Zoom == 0 && rounds > 0 && spec.ZoomLevels.Length > 0) {
                SetZoom(player, state, spec, state.RescopeLevel);
                PlaySound(player, $"{spec.Name}_zoom");
            }
        }

        // Reload: R, or the trigger on an empty magazine.
        bool customFire = gi.CustomFire, nativeDig = gi.NativeDig, nativeHit = gi.NativeHit;
        bool primaryHeld = nativeDig || nativeHit || customFire;
        bool wantsFire = spec.Automatic ? primaryHeld : (nativeHit || customFire) && !state.FireLatch;
        if (spec.CycleSecondsAlternate > 0f && primaryHeld != state.FireLatch)
            ScRevolverTrigger.Note(player, primaryHeld ? "press" : "release", now, rounds, state.PrepareUntil, nativeDig, nativeHit, customFire);
        state.FireLatch = primaryHeld;
        bool reloadKey = gi.ReloadKey;
        // The Zeus: a fresh charge after its recharge time, announced by CS2's own cue.
        // Only a gun that recharges reads or clears the timer: 0.20.1 let whichever
        // gun was held at the 30 s mark consume it (and top itself up), so a Zeus put
        // away and picked up again started over. The timer is game time, saved with
        // the world (Save below), so it also survives leaving and reloading.
        int capacity = ScGunGrowth.Capacity(ScGunBlock.GetVariant(value), EffectiveGunStats.LevelOf(value));
        if (authority && spec.RechargeSeconds > 0f && rounds < capacity && GunSpec.TryGetSnapshot(data, out var charge)) {
            float cycle = ScGunGrowth.RechargeSeconds(spec, charge.Level);
            // Per instance (plan §7): the record says when this Zeus is ready, in game time; a fresh empty one starts charging
            // on first sight, a ready one gets its charge back through the same transaction path as every other change.
            bool ready = charge.RechargeReadyAt >= 0 && now >= charge.RechargeReadyAt;
            if (charge.RechargeReadyAt < 0 || ready) {
                var zeus = ScGunMutation.Prepare(player.ComponentMiner.Inventory, player.ComponentMiner.Inventory.ActiveSlotIndex, HolderKey(player), out ScGunResult zeusWhy);
                var outcome = zeus is null ? zeusWhy : zeus.Commit(r => {
                    if (ready) { r.Rounds = ScGunGrowth.Capacity(r.Variant, r.AppliedGrowthLevel); r.RechargeReadyAt = -1; r.RechargeCycleSeconds = 0; }
                    else { r.RechargeReadyAt = now + cycle; r.RechargeCycleSeconds = cycle; }
                });
                if (outcome == ScGunResult.Success) {
                    value = zeus.Expected; data = Terrain.ExtractData(value); rounds = GunSpec.GetRounds(data); state.LastValue = value;
                    if (ready) PlaySound(player, $"{spec.Name}_chargeready");
                }
                else Refused(player, outcome, now);
            }
        }
        // The R8's cocked shot (video-feedback-20260929 R1): the hammer is drawn while the primary input stays
        // held; letting go before the time is up lets the hammer down and nothing fires. The shot is committed
        // once, on the gameplay clock, whether or not any animation could be shown.
        if (state.PrepareUntil >= 0) {
            // (Server, a remote client's player: the hammer falls when that client has shown the shot. Its report may come
            // with the release, or a little before or after this end's own deadline; this end's clock alone neither fires a
            // shot the client did not show nor drops one it did.)
            bool falls = shown is null ? primaryHeld && now >= state.PrepareUntil : owed > 0 && now >= state.PrepareUntil - ScNetGuns.ShotLead;
            if (!falls && !primaryHeld) CancelPrepare(player, state, "released", now);
            else if (falls) {
                double started = state.PrepareStartedAt, deadline = state.PrepareUntil;
                state.PrepareUntil = -1;
                KnifeAnimationController.EndPrepare(player);
                if (rounds > 0 && !busy && state.CommitFrame != Time.FrameIndex) {
                    ScRevolverTrigger.Note(player, "commit-primary", now, rounds, deadline, nativeDig, nativeHit, customFire);
                    Fire(player, state, model, spec, value, data, rounds, gi, cycleFrom: started);
                }
                else {
                    ScRevolverTrigger.Note(player, busy ? "refused-busy" : rounds <= 0 ? "refused-empty" : "refused-same-frame", now, rounds, deadline, nativeDig, nativeHit, customFire);
                    if (state.NextShot == deadline) state.NextShot = state.PrepareResume;
                }
                return;
            }
            else {
                KnifeAnimationController.DrivePrepare(player, (float)Math.Min(now - state.PrepareStartedAt, state.PrepareUntil - state.PrepareStartedAt), (float)(state.PrepareUntil - state.PrepareStartedAt));
                return;
            }
        }
        // Fire during a shell-by-shell reload cuts it short after the shell in hand.
        if (busy && wantsFire && rounds > 0 && state.ShellTimes.Count > 0 && !state.FireAfterReload) {
            CutReloadShort(player, state, spec, actionNow);
            return;
        }
        if (!busy && state.FireAfterReload) {
            state.FireAfterReload = false;
            if (rounds > 0 && now >= state.NextShot) wantsFire = true;
        }
        bool requested = authority && !local && state.PendingReloadId != 0;
        // A remote client's reload request (server) while that client has shown shots the server has not executed yet,
        // with rounds left: those shots are executed first (the client has already shown them, tracer and sound), then
        // the reload starts. It waits for nothing else: once such a shot can no longer be executed (the trigger up and
        // its report older than ScNetGuns.ShotLead) the reload starts and the shot is settled as skipped.
        if (requested && rounds > 0 && owed > 0 && (wantsFire || ScNet.Now - shown.ShotsAt <= ScNetGuns.ShotLead)) {
            requested = false; reloadKey = false; state.PendingReloadUntil = ScNet.Now + ScNetGuns.ReloadRequestLease;
        }
        // A client that still waits for the server's word on the reload it last showed starts no other one.
        if (predicted && state.ServerReloading) {
            if (ScNet.Now > state.ServerReloadDeadline) {
                state.ServerReloading = false;
                KnifeDiagnostics.WarnOnce("scnet-reload-unanswered", "[ScCsgoNet] client: the server did not say what became of a reload within its time; no longer waited for");
            }
            else if (state.Reload is null) { reloadKey = false; if (rounds == 0) wantsFire = false; }
        }
        if (!busy && rounds < capacity && (reloadKey || requested || (wantsFire && rounds == 0))) {
            if (spec.RechargeSeconds > 0f) {
                // The persistent ammo HUD already shows the live charge countdown.
                return;
            }
            StartReload(player, state, model, spec, value);
            if (authority && !local) {
                // What became of it, for that client: its own request, or (id 0) a reload the server began by itself.
                int id = state.PendingReloadId; state.PendingReloadId = 0;
                if (state.Reload is not null) { state.ReloadId = id; ScNetGuns.ReloadResult(player, id, ScNetGuns.ReloadPhase.Accepted); }
                else if (id != 0) ScNetGuns.ReloadResult(player, id, ScNetGuns.ReloadPhase.Refused);
            }
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
                Fire(player, state, model, spec, value, data, rounds, gi, inBurst: true);
                state.BurstRemaining--;
                state.BurstNextAt = state.BurstRemaining > 0 ? now + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.BurstShotSeconds,EffectiveGunStats.LevelOf(value)) : -1;
                return;
            }
        }

        // Whose decision a shot is. This process's own player: the trigger, at the gun's time. A remote client's player
        // (server): that client's - a shot it has shown is executed, when this end's schedule for the gun has it due
        // within ScNetGuns.ShotLead; the trigger's state as this end happens to see it at that moment decides nothing (it
        // used to: a release arriving a frame early dropped a shot the client had shown, one arriving late added a shot
        // it had not). The R8 still draws its hammer from the trigger; its shot follows the report above.
        bool cocks = spec.CycleSecondsAlternate > 0f;
        bool fires = shown is not null && !cocks ? owed > 0 && now >= state.NextShot - ScNetGuns.ShotLead : wantsFire && now >= state.NextShot;
        if (!busy && fires && rounds > 0 && state.BurstRemaining == 0) {
            // The R8 draws its hammer first (prepare_shoot_revolver) and fires when the
            // cycle time is up. The vdata gives no separate hammer time, so the primary
            // m_flCycleTime (0.5 s) is taken as it - assumed, the one number here that is.
            if (cocks) {
                // A broken gun does not cock either; the notice comes on the press, as for every other gun.
                if (!FreeUse(player) && ScGunDurability.IsBroken(data)) { BrokenNotice(player, now); return; }
                state.PrepareResume = state.NextShot;
                state.PrepareStartedAt = now;
                state.PrepareUntil = now + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.CycleSeconds,EffectiveGunStats.LevelOf(value));
                state.NextShot = state.PrepareUntil;
                // The rule above does not depend on this: without the clip the shot still waits the full time.
                if (!KnifeAnimationController.BeginPrepare(player))
                    KnifeDiagnostics.WarnOnce("r8-prepare-clip", $"{spec.Name} has no prepareShoot clip; the cocked shot keeps its timing without the hammer animation.");
                ScRevolverTrigger.Note(player, "cock", now, rounds, state.PrepareUntil, nativeDig, nativeHit, customFire);
                return;
            }
            // (A remote client's shot goes along the ray it was reported with, whatever the trigger does by now.)
            Fire(player, state, model, spec, value, data, rounds, shown is null ? gi : gi with { Dig = gi.Dig.HasValue ? shown.ShotAim : null, Hit = gi.Hit.HasValue ? shown.ShotAim : null, Look = shown.ShotAim });
        }

        if (!ScGunplaySettings.Enabled && !state.ModeKick) RecoverKick(player, state, dt,
            Cs2Weapons.Kick(spec.Name, false, spec.KickPitchDegrees, spec.KickYawDegrees,
                spec.KickRecoverPerSecond).Recover);
        }
        finally {
            if (local && ScNet.IsRemoteClient)
                ScNetGuns.SendInput(player, gi.Available, gi.NativeDig, gi.NativeHit, gi.CustomFire, false, gi.Touch, sendZoom,
                    ScAimRay.Resolve(player, ScShotAim.Select(sendScoped, gi.Dig, gi.Hit, gi.Look)), ScGunBindings.ContextAvailable(player), state.BurstRemaining > 0);
        }
    }

    /// <summary>Lets the R8's hammer down without a shot: released early, a menu, a switch. A later press starts a
    /// new cocking with its full time; the old deadline is never reused.</summary>
    void CancelPrepare(ComponentPlayer player, GunState state, string reason, double now) {
        if (state.PrepareUntil < 0) return;
        ScRevolverTrigger.Note(player, "cancel-" + reason, now, -1, state.PrepareUntil, false, false, false);
        if (state.NextShot == state.PrepareUntil) state.NextShot = state.PrepareResume;
        state.PrepareUntil = -1;
        KnifeAnimationController.EndPrepare(player);
    }

    readonly Dictionary<int,ScGunBloom> m_blooms = [];
    ScGunDiagnostics m_diagnostics;
    /// <summary>Session-local shot number, part of a kill credential so one shot's kills can be told apart in logs.</summary>
    long m_shotSequence;

    /// <summary>How late after its time a shot may be taken and the next one still be counted from that time (see Fire).</summary>
    public const double ShotCarry = .05;
    void Fire(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value, int data, int rounds, GunInput input,
              bool inBurst = false, bool alternateFire = false, double? cycleFrom = null) {
        double now = m_time.GameTime;
        // Multiplayer: the server fires for real (a remote client's shot included); a remote client only shows its own
        // shot, predicting one round less until the server's record row confirms it. Only the local player feels the kick.
        bool authority = ScNet.IsAuthority, local = ScNet.IsLocal(player);
        // M4: a broken gun never fires; it can still be reloaded, inspected, moved and repaired.
        bool creative = FreeUse(player);
        if (!creative && ScGunDurability.IsBroken(data)) { if (local) BrokenNotice(player, now); return; }
        // The world's mode may end a spawn protection first, or refuse the shot outright (nothing fired, nothing consumed).
        if (authority && !ScModes.AcceptAttack(player, spec.RechargeSeconds > 0 ? ScAttackKind.Zeus : ScAttackKind.Shot)) return;
        int roundsBefore = rounds;
        ScGunKillCredit credit = null;
        if (authority) {
            // The shot is a transaction first (plan §5): one round out, one durability point off (survival), the Zeus's recharge
            // set - all in the record, with a fresh gun getting its record here. Only a committed shot settles anything below;
            // a refused one changes no cycle time, fires no round, plays no effect.
            var transaction = ScGunMutation.Prepare(player.ComponentMiner.Inventory, player.ComponentMiner.Inventory.ActiveSlotIndex, HolderKey(player), out ScGunResult shotWhy);
            var result = transaction is null ? shotWhy : transaction.Commit(r => {
                r.Rounds = Math.Max(0, r.Rounds - 1);
                if (!creative) r.Durability = Math.Max(0, r.Durability - 1);
                if (spec.RechargeSeconds > 0f && r.Rounds <= 0) {
                    float cycle = ScGunGrowth.RechargeSeconds(spec, r.AppliedGrowthLevel);
                    r.RechargeReadyAt = now + cycle; r.RechargeCycleSeconds = cycle;
                }
            });
            if (result != ScGunResult.Success) { Refused(player, result, now); return; }
            value = transaction.Expected; data = Terrain.ExtractData(value); rounds = GunSpec.GetRounds(data);
            // Frozen here, while the record that fired is still known: a kill confirmed later belongs to this gun.
            credit = ScGunKillCredit.For(data, creative, ++m_shotSequence);
            // A remote client's shot: its client is told the server counted it (with the record row that shows it).
            ScNetGuns.ServerShot(player);
        }
        else {
            if (rounds <= 0) return;
            ScNetGuns.PredictShot(player);
            rounds = Math.Max(0, rounds - 1);
        }
        state.CommitFrame = Time.FrameIndex;
        if (spec.CycleSecondsAlternate > 0f) ScRevolverTrigger.Note(player, alternateFire ? "shot-alternate" : "shot-primary", now, roundsBefore - 1, -1, false, false, false);
        if (local) ScControllerFeedback.Shot(player,spec.Name);
        state.LastValue = value;
        if (authority && local && !creative) {
            int durability = GunSpec.GetDurability(data), full = GunSpec.GetMaxDurability(data);
            if (durability <= 0) { player.ComponentGui.DisplaySmallMessage("枪械已损坏，请到装配台维修", Color.Red, true, false); }
        }
        // A burst costs its own cycle time once, not one per round: CS2's Glock-18
        // takes 0.5 s for the burst against 0.15 s for a single shot, the FAMAS 0.55
        // against 0.09. The remaining rounds are scheduled at m_flTimeBetweenBurstShots.
        // inBurst says this shot is one of those, so it cannot start another.
        bool startingBurst = !inBurst && state.BurstMode && spec.HasBurstMode && state.BurstRemaining == 0;
        // mpd2 ammo jitter (2026-10-02): where the next shot's time is counted from. A shot is taken in the first frame at
        // or after its time, so it is always a little late; counting the next one from that frame added the lateness
        // again with every shot (a held AK took 110 ms a shot at 100 frames a second, 120 ms at 50), and two ends with
        // different frames drifted apart shot by shot. The time is now counted from when this shot was due, as long as it
        // was taken within ShotCarry of that (a frame's rounding, down to 20 frames a second): the cadence over a held
        // trigger is the gun's own, whatever the frame rate, and nothing is caught up after a longer wait - a pause, a
        // slower frame rate - where it is counted from now as before. A remote client's shot executed a little ahead of
        // this end's schedule (ScNetGuns.ShotLead) keeps the schedule, and one that arrives late within the same
        // allowance does too, so shots that follow on time are not then ahead of it.
        double due = state.NextShot, late = now - due;
        double from = late < 0 || late <= (authority && !local ? ScNetGuns.ShotLead : ShotCarry) ? due : now;
        if (startingBurst) {
            state.NextShot = from + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.BurstCycleSeconds,EffectiveGunStats.LevelOf(value));
            state.BurstRemaining = Math.Max(0, spec.BurstShots - 1);
            state.BurstNextAt = state.BurstRemaining > 0 ? now + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.BurstShotSeconds,EffectiveGunStats.LevelOf(value)) : -1;
        }
        else if (alternateFire) {
            // The R8's fanned shot: the vdata pair's second cycle time.
            state.NextShot = now + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.CycleSecondsAlternate,EffectiveGunStats.LevelOf(value));
        }
        else if (!inBurst) {
            // The cycle counts from the press: for the R8's cocked shot that is when the
            // hammer started back, not when it fell.
            state.NextShot = (cycleFrom ?? from) + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.CycleSeconds,EffectiveGunStats.LevelOf(value));
            if (spec.CycleSecondsAlternate > 0f)
                state.AlternateReadyAt = now + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.CycleSecondsAlternate,EffectiveGunStats.LevelOf(value));
        }
        // A detachable silencer that is on, or an integral one (the MP5-SD): the
        // flash, the muzzle and the kick follow it. Only the detachable kind has a
        // separate sound file; the integral one's WEAPON_SOUND_SINGLE is already
        // the suppressed shot.
        bool silenced = spec.SilencedAlways || (spec.HasSilencer && !GunSpec.GetSilencerOff(data));
        if (authority) ScGunWorldEffects.NotifyNoise(Project.FindSubsystem<SubsystemNoise>(false), player.ComponentBody.Position, silenced, spec.RechargeSeconds > 0);
        // The round that empties the magazine locks a pistol's slide back (shoot_empty).
        bool lastRound = rounds <= 0;
        bool scopedShot = state.Zoom > 0;
        // Capture before automatic unzoom, animation callbacks or recoil can change aim state.
        var shot = ScShotAim.Capture(spec.Name, input.Touch, scopedShot, silenced,
            alternateFire, input.Dig, input.Hit, input.Look, player.ComponentBody.Velocity.Length(), ScGunHandling.LegacyCone(spec));
        // Camera and character apart (2026-10-01): the shot leaves the eye, in first person along the crosshair, in every other
        // view along the character's own look (where the first-person crosshair would be), and is traced from there. A
        // camera's position or turn never moves or bends the shot (ScAimRay).
        Ray3 cameraRay = shot.Ray;
        shot = shot with { Ray = ScAimRay.Resolve(player, cameraRay) };
        bool handlingAlternate = ScGunHandling.Alternate(spec,scopedShot,silenced,state.BurstMode,alternateFire);
        // The world's mode may put its own weapon numbers in force for this shooter (deathmatch-addon): both ends then use
        // that table, whatever this device's own gunplay preset is. Without a mode: the survival numbers, as before.
        var mode = ScModes.For(player);
        bool modeStats = false;
        EffectiveGunStats effective = mode is not null && (modeStats = mode.TryGunStats(player, spec, value, handlingAlternate, out var ruled)) ? ruled : EffectiveGunStats.Resolve(spec,value,handlingAlternate);
        bool gunplay = modeStats ? effective.Handling is not null : ScGunplaySettings.Enabled;
        ScGunBloom bloom = null;
        ScGunStance.ConeParts? coneParts = null;
        float bloomBefore = 0;
        var shotBody = player.ComponentBody;
        float speedXZ = new Vector2(shotBody.Velocity.X,shotBody.Velocity.Z).Length();
        bool fluidOrLadder = shotBody.ImmersionFactor > .1f || player.ComponentLocomotion.LadderValue.HasValue;
        if (gunplay) {
            int instance = GunSpec.GetId(data);
            if (!m_blooms.TryGetValue(instance,out bloom)) m_blooms[instance] = bloom = new();
            var targetMode = modeStats ? effective.Handling : ScGunHandling.ForMode(spec.Name,spec.ZoomLevels.Length>0 || handlingAlternate);
            bloomBefore = bloom.At(now);
            coneParts = state.Stance.ExplainCone(targetMode,modeStats ? effective.HipHandling ?? effective.Handling : ScGunHandling.ForMode(spec.Name,false),spec.ZoomLevels.Length>0,
                speedXZ,shotBody.CrouchFactor,fluidOrLadder,bloomBefore);
            float cone = coneParts.Value.Total;
            shot = shot with { Spread = cone, Alternate = handlingAlternate };
            bloom.Fired(effective.Handling,now); // after capturing this shot, once per trigger, not per pellet
        }
        // Growth scales the final angle once, whatever produced it, and Lv10 sets it to exactly zero in every
        // stance and mode - there is no leftover bloom or airborne penalty to add back afterwards.
        if (effective.AngleScale != 1f) shot = shot with { Spread = shot.Spread * effective.AngleScale };
        // Lv10 removes the weapon's limit, not the world's: the shot stops where the loaded terrain does.
        float shotRange = effective.UnlimitedRange
            ? ScGunRange.LoadedLimit(m_terrain.Terrain, shot.Ray.Position, shot.Ray.Direction, effective.Range)
            : effective.Range;
        var diagnostic = m_diagnostics?.Active == true ? m_diagnostics.Begin(new ScGunDiagnostics.Context {
            Gun=spec.Name, Preset=modeStats?"mode":ScGunplaySettings.Enabled?"survival":"classic", Player=player.PlayerData.PlayerIndex,
            Instance=GunSpec.GetId(data), Frame=Time.FrameIndex, Time=now, Pellets=Math.Max(1,spec.Pellets),
            AmmoBefore=roundsBefore, AmmoAfter=rounds, DurabilityAfter=GunSpec.GetDurability(data), GunNumbers=(int)KnifeTuning.GunNumbers,
            Touch=ScMobileControls.UsesTouchInput(player), Creative=creative, Scoped=scopedShot, Silenced=silenced,
            Burst=state.BurstMode&&spec.HasBurstMode, Alternate=shot.Alternate, Airborne=state.Stance.Airborne, FluidOrLadder=fluidOrLadder,
            SpeedXZ=speedXZ, Crouch=shotBody.CrouchFactor, AimBlend=state.Stance.AimBlend, LandingFactor=state.Stance.LandingFactor,
            Cone=shot.Spread, Range=shotRange, NearPower=effective.Power, BloomBefore=bloomBefore, BloomAfter=bloom?.Value ?? 0,
            Components=coneParts, FrameMs=Time.FrameDuration*1000,
            Counter=GunSpec.TryGetSnapshot(data,out var counted) && counted.CounterInstalled,
            Level=effective.Level, Kills=GunSpec.TryGetSnapshot(data,out var counts) ? counts.KillCount : 0,
            UnlimitedRange=effective.UnlimitedRange, GrowthRule=m_registry.GrowthMode.ToString(),
            PendingLevel=GunSpec.TryGetSnapshot(data,out var pending) ? pending.PendingGrowthLevel : -1
        }) : null;
        if (scopedShot && spec.UnzoomsAfterShot) {
            // CS2's m_bUnzoomsAfterShot (AWP, SSG 08): a scoped shot drops the scope for
            // the bolt cycle and re-zooms to the same level afterwards. The auto-snipers
            // and the AUG / SG 553 have it false and fire with the scope up.
            state.RescopeLevel = state.Zoom;
            state.RescopeAt = now + ScGunGrowth.ShotInterval(ScGunBlock.GetVariant(value),spec.CycleSeconds,effective.Level);
            LeaveScope(player, state);
        }
        KnifeAnimationController.TriggerShoot(player, silenced, lastRound, scopedShot && !spec.UnzoomsAfterShot, alternateFire,
            spec.LeftMuzzleBone is not null ? roundsBefore : -1);
        // The Dual Berettas flash and trace from the gun that fired.
        string shotClip = KnifeAnimationController.CurrentClip(model);
        // Casings and the muzzle flash come from the first-person weapon: only the player this process shows has one.
        if (local) m_casings.Queue(player,model,spec.Name,shotClip);
        string muzzleBone = silenced ? spec.SilencedMuzzleBone
            : spec.LeftMuzzleBone is not null && shotClip is "shootLeft" or "shootLeftLast" ? spec.LeftMuzzleBone
            : spec.MuzzleBone;
        if (spec.MuzzleEffects && local)
            CsmcFirstPersonRenderer.MuzzleFlash(player,silenced ? 0.03f : 0.06f, muzzleBone, spec.Name, silenced);
        PlaySound(player, spec.HasSilencer && silenced ? $"{spec.Name}_fire_silenced" : $"{spec.Name}_fire");
        // No reload: the Zeus's ten-second recharge was written into its record by the shot transaction above.
        if (!spec.Automatic) Schedule(state, spec.Name, KnifeAnimationController.CurrentClip(model) ?? "shoot1", now);


        // Camera kick, applied now and eased back in RecoverKick. The cs2 profile takes
        // the ratios between guns from m_flRecoilMagnitude and the yaw scatter from
        // m_flRecoilAngleVariance; the absolute scale is still the fitted AK value.
        bool alternate = shot.Alternate;
        (float kickPitch, float kickYaw, float _) = Cs2Weapons.Kick(spec.Name, alternate,
            spec.KickPitchDegrees, spec.KickYawDegrees, spec.KickRecoverPerSecond);
        state.ModeKick = modeStats && gunplay;
        if (gunplay) {
            kickPitch=effective.Handling.KickPitch;kickYaw=effective.Handling.KickYaw;
            state.KickRecoveryRate=effective.Handling.CameraRecoveryT90>0?MathF.Log(10)/effective.Handling.CameraRecoveryT90:12;
        }
        kickPitch *= effective.AngleScale; kickYaw *= effective.AngleScale;
        float pitch = MathUtils.DegToRad(kickPitch) * (gunplay ? .9f + .2f*m_random.Float(0,1) : .8f+.4f*m_random.Float(0,1));
        float yaw = MathUtils.DegToRad(kickYaw) * m_random.Float(-1f, 1f);
        if (diagnostic is not null) { diagnostic.State.KickPitchDegrees=MathUtils.RadToDeg(pitch);diagnostic.State.KickYawDegrees=MathUtils.RadToDeg(yaw); }
        Ray3 ray = shot.Ray;
        // A world's mode may bring its own recoil (deathmatch round 5: CS2's fixed spray patterns): the round leaves along
        // the shooter's aim plus the pattern's offset now, less what the look already carries of it (the view part, moved
        // into the look each frame on the device that aims; a remote client's look arrives with it). The survival kick is
        // then not applied. Both ends run the same pattern from their own shot times.
        var recoil = ScModes.Recoil(player);
        if (recoil is not null) {
            var (bullet, view) = recoil.At(player, now);
            Vector2 carried = local ? state.ModeView : view;
            ray = new Ray3(ray.Position, ScGunHandling.Turned(ray.Direction, bullet.X - carried.X, bullet.Y - carried.Y));
            recoil.Fired(player, spec, handlingAlternate, now);
        }
        // Where a shot's visuals start when no muzzle is drawn: the vanilla gun origin beside the eye the damage ray leaves
        // (ScAimRay), as in every earlier version.
        Vector3 visualOrigin = ScAimRay.GunOrigin(player, ray.Position);
        if (local && recoil is null) Kick(player, state, pitch, yaw); // a remote client's view kicks on its own screen

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
        var hits = new Dictionary<ComponentBody, ScShotHits>();
        float firstDeviation = 0;
        var leafAttempts = new HashSet<Point3>();
        var waterAttempts = new HashSet<Point3>();
        // What other multiplayer clients are told about this shot (they draw and hear it; nothing is recomputed there).
        var seen = authority && ScNet.IsHost ? new List<ScNetGuns.Pellet>(pellets) : null;
        var rewind = authority ? ScNetGuns.RewindFor(player) : null; // a remote client's shot hits what that client saw
        var splashes = seen is null ? null : new List<Vector3>();
        // A body the world's mode says this shooter may not hurt (a spectator, someone not in the match) does not stop the
        // round either: it is not there for this shot. Without a mode every body is, as before.
        IEnumerable<ComponentBody> shootable = mode is null ? m_bodies.Bodies : m_bodies.Bodies.Where(b => mode.MayHurt(player.Entity, b.Entity) != false);
        // A world's mode may let rounds cross blocks and bodies (deathmatch round 6: CS2's penetration): each pellet is then
        // walked on past its first obstacle with what the crossings leave of it. Without a mode a round stops there.
        var penetration = gunplay ? ScModes.Penetration(player) : null;
        for (int pellet = 0; pellet < pellets; pellet++) {
            Vector3 direction = Scatter(ray.Direction, spread);
            if (pellet == 0) firstDeviation = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(ray.Direction), direction), -1f, 1f));
            Vector3 start = ray.Position;
            Vector3 end = start + direction * shotRange;
            long traceStarted = diagnostic is not null ? ScGunDiagnostics.Timestamp() : 0;
            var foliage = diagnostic?.VegetationTrace ?? new ScGunRange.BulletTrace();
            foliage.Leaves.Clear(); foliage.Fluids.Clear();
            TerrainRaycastResult? terrain = ScGunRange.TraceBullet(m_terrain, start, direction, shotRange, foliage);
            ScGunHitTest.Hit? gunHit;
            if (gunplay) gunHit=ScGunHitTest.RaycastCompensated(shootable,player.ComponentBody,start,direction,terrain.HasValue?MathF.BitDecrement(terrain.Value.Distance):shotRange,diagnostic?.Trace,rewind);
            else {
                var body=m_bodies.Raycast(start,end,.35f,(b,d)=>b!=player.ComponentBody && b.Entity!=player.Entity && (mode is null || mode.MayHurt(player.Entity,b.Entity)!=false));
                gunHit=null;
                if (body.HasValue && (!terrain.HasValue || body.Value.Distance<terrain.Value.Distance)) {
                    var part=ScHeadshotProbe.Resolve(body.Value.ComponentBody,start,direction,shotRange,out float precise,out string why);
                    float distance=part==ScHitPart.Unknown?body.Value.Distance:precise;
                    if (!terrain.HasValue || distance<terrain.Value.Distance) gunHit=new(body.Value.ComponentBody,distance,part,why);
                }
            }
            // The tracer runs the shot line, stopping at whatever the bullet hit.
            float travel = shotRange;
            if (gunHit.HasValue) travel = MathUtils.Min(travel, gunHit.Value.Distance);
            if (terrain.HasValue) travel = MathUtils.Min(travel, terrain.Value.Distance);
            ScBulletPenetration.Walk walk = null;
            if (penetration is not null && (gunHit.HasValue || terrain.HasValue)) {
                walk = ScBulletPenetration.Continue(m_terrain, shootable, player.ComponentBody, start, direction, shotRange, gunHit, terrain, penetration, spec,
                    d => effective.PelletPower(spec, d), rewind);
                travel = walk.Travel;
            }
            if (authority && spec.RechargeSeconds <= 0)
                ScGunWorldEffects.BreakLeaves(m_terrain, foliage.Leaves, travel, leafAttempts, ScGunWorldEffects.LeafSample);
            // A bullet entering water from air splashes: the same native effect and Splashes audio the game uses
            // when a projectile or body hits the surface. Water does not stop the bullet, so this is visual only.
            if (spec.MuzzleEffects && foliage.Fluids.Count > 0) {
                var water = foliage.Fluids[0];
                if (waterAttempts.Add(water.Cell)) {
                    m_particles.AddParticleSystem(new WaterSplashParticleSystem(m_terrain, water.Point, false));
                    m_audio.PlayRandomSound("Audio/Splashes", .8f, m_random.Float(-.2f, .2f), water.Point, 8f, true);
                    splashes?.Add(water.Point);
                }
            }
            if (diagnostic is not null) {
                int outcome = gunHit.HasValue ? (gunHit.Value.Part==ScHitPart.Head?0:1) : terrain.HasValue?2:3;
                bool fallback = gunHit.HasValue && (gunHit.Value.Reason?.Contains("fallback")==true || gunHit.Value.Part==ScHitPart.Unknown);
                double power = gunHit.HasValue ? effective.PelletPower(spec,gunHit.Value.Distance)*(gunHit.Value.Part==ScHitPart.Head?effective.HeadMultiplier:1) : 0;
                diagnostic.Pellet(ray.Direction,direction,outcome,travel,fallback,gunHit?.Reason=="logical mesh pose",ScGunDiagnostics.ElapsedMs(traceStarted),power);
            }
            // The tracer leaves the muzzle the player can see, not the hit-detection ray's
            // origin at the eye. The weapon is drawn in CS2's viewmodel projection, so the
            // renderer solves for a world point that lands on the drawn muzzle under the
            // game camera; without one - no cs2 profile, or the gun not drawn this frame -
            // the vanilla gun origin is used, which is what every earlier version did.
            Vector3 impact = start + direction * travel;
            // The first-person weapon this camera drew, else the third-person weapon on the player's body (another camera,
            // another player on this screen), else the gun origin: never a stale first-person frame after a camera switch.
            Vector3 tracerStart = visualOrigin; string tracerFrom = "origin";
            if (spec.MuzzleEffects) {
                if (CsmcFirstPersonRenderer.TryGetPlayerMuzzleWorld(player,spec.Name, silenced, out Vector3 muzzle, muzzleBone)) { tracerStart = muzzle; tracerFrom = "viewmodel"; }
                else if (ScThirdPerson.TryGetMuzzleWorld(player, spec.Name, muzzleBone, out muzzle)) { tracerStart = muzzle; tracerFrom = "body"; }
            }
            Vector3 tracerDirection = impact - tracerStart;
            float tracerTravel = tracerDirection.Length();
            // Free cameras (debug, orbit, perspective-view mods) log every shot's inputs and result to Game.log, bounded, so a
            // player's own session shows what the camera, the character and the shot did (2026-10-01).
            bool freeView = ScAimRay.FreeCamera(player);
            if (DebugShots || freeView && KnifeLog.Diagnostics && ShotLogAllowed(player)) {
                Camera view = player.GameWidget?.ActiveCamera; Vector3? look = ScAimRay.LookDirection(player);
                string line = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"P{player.PlayerData?.PlayerIndex} {spec.Name} camera {cameraRay.Position.X:0.00},{cameraRay.Position.Y:0.00},{cameraRay.Position.Z:0.00} eye {ScAimRay.Eye(player, start).X:0.00},{ScAimRay.Eye(player, start).Y:0.00},{ScAimRay.Eye(player, start).Z:0.00} "
                + $"origin {ScAimRay.GunOrigin(player, start).X:0.00},{ScAimRay.GunOrigin(player, start).Y:0.00},{ScAimRay.GunOrigin(player, start).Z:0.00} camera-dir {cameraRay.Direction.X:0.000},{cameraRay.Direction.Y:0.000},{cameraRay.Direction.Z:0.000} "
                + $"ray {start.X:0.00},{start.Y:0.00},{start.Z:0.00} dir {direction.X:0.000},{direction.Y:0.000},{direction.Z:0.000} tracer {tracerStart.X:0.00},{tracerStart.Y:0.00},{tracerStart.Z:0.00} "
                + $"bone {muzzleBone} impact {impact.X:0.00},{impact.Y:0.00},{impact.Z:0.00} hit {(gunHit.HasValue ? gunHit.Value.Body.Entity?.Id.ToString() : terrain.HasValue ? "terrain " + terrain.Value.Value : "none")} "
                + $"camera-class {view?.GetType().Name} control {view?.IsEntityControlEnabled} movement {view?.UsesMovementControls} "
                + $"aim {(ScNetGuns.RemoteInput(player) is not null || ScAimRay.FirstPerson(player) ? "camera" : "character-look")} look {look?.X:0.000},{look?.Y:0.000},{look?.Z:0.000} "
                + $"view {view?.ViewPosition.X:0.00},{view?.ViewPosition.Y:0.00},{view?.ViewPosition.Z:0.00} view-dir {view?.ViewDirection.X:0.000},{view?.ViewDirection.Y:0.000},{view?.ViewDirection.Z:0.000} "
                + $"input {(input.Touch ? "touch" : input.Dig.HasValue ? "dig" : input.Hit.HasValue ? "hit" : "look")} tracer-from {tracerFrom}");
                if (DebugShots) LastShotDebug = line;
                if (freeView) KnifeLog.Diagnostic("[CS_SHOT] " + line);
            }
            if (spec.MuzzleEffects && tracerTravel > 1e-3f) QueueTracer(spec.Name, tracerStart, tracerDirection / tracerTravel, tracerTravel, silenced, tracerFrom == "viewmodel");
            // The Zeus draws no flash sprite and no ribbon; its own effect runs from the
            // drawn muzzle to wherever the trace ended (CS2's CP1), sparks only on a hit.
            if (Cs2TaserEffect.Applies(spec.Name)) {
                bool solved = CsmcFirstPersonRenderer.TryGetPlayerMuzzleWorld(player,spec.Name, false, out Vector3 zm) || ScThirdPerson.TryGetMuzzleWorld(player, spec.Name, null, out zm);
                QueueZeus(player,solved ? zm : visualOrigin, solved, impact, direction, gunHit.HasValue || terrain.HasValue);
            }
            seen?.Add(new(impact, gunHit.HasValue ? ScNetGuns.Impact.Body : terrain.HasValue ? ScNetGuns.Impact.Block : ScNetGuns.Impact.None,
                !gunHit.HasValue && terrain.HasValue ? terrain.Value.Value : 0));
            if (gunHit is { } accepted) {
                float distance=accepted.Distance;var part=accepted.Part;
                Vector3 hitPoint = start + direction * distance;
                // Survival damage is a per-shot budget, shared across pellets; a head pellet is scaled once, here.
                float power = effective.PelletPower(spec, distance) * (part == ScHitPart.Head ? effective.HeadMultiplier : 1);
                var target = accepted.Body;
                if (!hits.TryGetValue(target, out var landed)) hits[target] = landed = new ScShotHits();
                landed.Add(part, power, hitPoint, direction);
            }
            else if (terrain.HasValue) BlockImpact(start + direction * terrain.Value.Distance, terrain.Value.Value, terrain.Value.CellFace.Face);
            if (walk is not null) {
                // the round went on: holes where it left each block and where it entered the later ones, then the bodies behind
                bool firstWallShown = !gunHit.HasValue && terrain.HasValue;   // the hole where it entered the first block is above
                foreach (var c in walk.Crossings) {
                    if (c.What != ScBulletPenetration.Kind.Block) continue;
                    if (!(firstWallShown && MathF.Abs(c.Entry - terrain.Value.Distance) < 1e-3f)) BlockImpact(start + direction * c.Entry, c.EntryValue, FaceAgainst(direction));
                    BlockImpact(start + direction * c.Exit, c.ExitValue, FaceAgainst(-direction));
                }
                if (walk.FinalBlock is { } stop) BlockImpact(start + direction * stop.Distance, stop.Value, FaceAgainst(direction));
                foreach (var beyond in walk.Hits) {
                    float power = Math.Max(0, effective.PelletPower(spec, beyond.Distance) - beyond.Lost) * (beyond.Part == ScHitPart.Head ? effective.HeadMultiplier : 1);
                    if (!hits.TryGetValue(beyond.Body, out var behind)) hits[beyond.Body] = behind = new ScShotHits();
                    behind.Add(beyond.Part, power, start + direction * beyond.Distance, direction);
                    behind.Crossed = Math.Max(behind.Crossed, beyond.Crossed);
                }
            }
        }
        // Damage is the authority's alone; a remote client's own prediction never hurts anything.
        if (authority) foreach (var hit in hits) {
            var observedHealth = diagnostic is not null ? hit.Key.Entity.FindComponent<ComponentHealth>() : null;
            float healthBefore = observedHealth?.Health ?? float.NaN;
            // What is known about this shot now, for whoever settles its result later (a world's mode): the gun that fired,
            // the scope as it was when the round left, smoke on the way to this target, the shooter's eyes.
            var grenades = Project.FindSubsystem<SubsystemScGrenades>(false);
            var facts = new ScAttackFacts { Kind = spec.RechargeSeconds > 0 ? ScAttackKind.Zeus : ScAttackKind.Shot, WeaponValue = value, Weapon = spec.Name,
                AttackerPlayer = player.PlayerData?.PlayerIndex ?? -1, AttackId = m_shotSequence, HasScope = spec.ZoomLevels.Length > 0, Scoped = scopedShot, Penetrations = hit.Value.Crossed,
                ThroughSmoke = grenades?.SmokeBlocksSight(ray.Position, hit.Value.Point) == true, AttackerBlind = grenades?.IsBodyBlinded(player.ComponentBody) == true,
                Distance = Vector3.Distance(ray.Position, hit.Value.Point) };
            ScSurvivalBalance.AttackWith(hit.Key, player, hit.Value.Point, hit.Value.Direction, hit.Value.Total, now, false, spec.RechargeSeconds > 0, hit.Value.AnyHead, credit, hit.Value, facts);
            diagnostic?.Health(healthBefore,observedHealth?.Health ?? float.NaN);
        }
        if (seen is not null) ScNetGuns.BroadcastShot(player, value, silenced, visualOrigin, seen, splashes);
        if (ScNet.IsRemoteDriven(player))
            ScNet.Trace($"shot P{player.PlayerData.PlayerIndex} {spec.Name} pellets {pellets} hit {string.Join(",", hits.Select(h => h.Key.Entity?.Id + "x" + h.Value.Pellets))} "
                + $"cone {spread:0.0000} dev {firstDeviation:0.0000} speed {speedXZ:0.00} air {state.Stance.Airborne} landing {state.Stance.LandingFactor:0.00} crouch {shotBody.CrouchFactor:0.00} "
                + $"fluid {fluidOrLadder} bloom {bloomBefore:0.0000} compensated {rewind is not null} "
                + $"ray {ray.Position.X:0.00},{ray.Position.Y:0.00},{ray.Position.Z:0.00} dir {ray.Direction.X:0.0000},{ray.Direction.Y:0.0000},{ray.Direction.Z:0.0000} "
                + $"camera {cameraRay.Position.X:0.00},{cameraRay.Position.Y:0.00},{cameraRay.Position.Z:0.00} "
                + $"latest-aim off {(ScNetGuns.RemoteInput(player) is { } latest ? MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(ray.Direction), Vector3.Normalize(latest.Aim.Direction)), -1f, 1f)) : 0):0.0000}");
        m_diagnostics?.Complete(diagnostic);
    }

    /// <summary>Multiplayer client: another player's shot as the server reported it — its sound, tracers and impacts.
    /// Nothing is hit or counted here.</summary>
    public void ShowRemoteShot(int shooterEntityId, int gunValue, bool silenced, Vector3 origin, IReadOnlyList<ScNetGuns.Pellet> pellets, IReadOnlyList<Vector3> splashes) {
        if (Terrain.ExtractContents(gunValue) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || ScGunBlock.SpecOf(gunValue) is not { } spec) return;
        var shooter = Project.Entities.FirstOrDefault(e => e.Id == shooterEntityId);
        var shooterPlayer = shooter?.FindComponent<ComponentPlayer>();
        if (shooterPlayer is not null && ScNet.IsLocal(shooterPlayer)) return; // own shots are already shown
        Vector3 from = shooter?.FindComponent<ComponentCreatureModel>()?.EyePosition ?? origin;
        string name = spec.HasSilencer && silenced ? $"{spec.Name}_fire_silenced" : $"{spec.Name}_fire";
        if (s_variants.TryGetValue(name, out int n)) name = $"{name}_{m_random.Int(1, n)}";
        try { m_audio.PlaySound($"Audio/ScCsgoKnives/{name}", 1f, m_random.Float(-0.05f, 0.05f), from, 24f, true); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("remote-shot-sound-" + name, $"[ScCsgoKnives] sound {name} failed: {e.Message}"); }
        foreach (var p in pellets) {
            Vector3 d = p.End - from; float travel = d.Length();
            if (spec.MuzzleEffects && travel > 1e-3f) QueueTracer(spec.Name, from, d / travel, travel, silenced);
            if (p.Kind != ScNetGuns.Impact.Block) continue;
            int contents = Terrain.ExtractContents(p.BlockValue);
            if (contents <= 0 || contents >= BlocksManager.Blocks.Length || BlocksManager.Blocks[contents] is not { } block) continue;
            int slot = block.GetFaceTextureSlot(4, p.BlockValue);
            m_particles.AddParticleSystem(new BlockDebrisParticleSystem(m_terrain, p.End, 0.45f, 1f, Color.White, slot));
            string material = ImpactFolder(block.GetSoundMaterialName(m_terrain, p.BlockValue));
            if (material is not null) m_audio.PlayRandomSound("Audio/Impacts/" + material, 0.7f, m_random.Float(-0.2f, 0.2f), p.End, 6f, true);
        }
        foreach (var w in splashes) {
            m_particles.AddParticleSystem(new WaterSplashParticleSystem(m_terrain, w, false));
            m_audio.PlayRandomSound("Audio/Splashes", .8f, m_random.Float(-.2f, .2f), w, 8f, true);
        }
    }

    /// <summary>Vanilla ships impact folders Body/Dirt/Glass/Metal/Plant/Soft/Stone/Wood only; its block materials also name
    /// Leaves, Sand and Snow, which 0.33.0 asked for verbatim and the log reported missing.</summary>
    /// <summary>A round meeting a block: its debris and its impact sound (the face picks the debris texture).</summary>
    void BlockImpact(Vector3 point, int value, int face) {
        Block block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        int slot = block.GetFaceTextureSlot(face, value);
        m_particles.AddParticleSystem(new BlockDebrisParticleSystem(m_terrain, point, 0.45f, 1f, Color.White, slot));
        string material = ImpactFolder(block.GetSoundMaterialName(m_terrain, value));
        if (material is not null) m_audio.PlayRandomSound("Audio/Impacts/" + material, 0.7f, m_random.Float(-0.2f, 0.2f), point, 6f, true);
    }
    /// <summary>The cell face a round travelling along <paramref name="direction"/> meets (its dominant axis).</summary>
    static int FaceAgainst(Vector3 direction) {
        Vector3 a = new(MathF.Abs(direction.X), MathF.Abs(direction.Y), MathF.Abs(direction.Z));
        return a.Y >= a.X && a.Y >= a.Z ? (direction.Y > 0 ? 5 : 4) : a.X >= a.Z ? (direction.X > 0 ? 3 : 1) : (direction.Z > 0 ? 2 : 0);
    }

    public static string ImpactFolder(string material) => material switch {
        null or "" => null,
        "Stone" or "Wood" or "Plant" or "Metal" or "Soft" or "Dirt" or "Glass" or "Body" => material,
        "Leaves" => "Plant", "Sand" => "Dirt", "Snow" => "Soft",
        _ => "Stone"
    };

    void CancelReload(ComponentPlayer player, GunState state, bool cancelAnimation = true) {
        if (state.Reload is null) return;
        // A remote client's reload that ends here without completing (server): its client takes its own back.
        if (ScNet.IsHost && !ScNet.IsLocal(player)) ScNetGuns.ReloadResult(player, state.ReloadId, ScNetGuns.ReloadPhase.Cancelled);
        state.Reload.Cancel(); state.Reload = null;
        state.DropAt = state.InsertAt = state.BusyUntil = -1;
        state.ShellTimes.Clear(); state.Scheduled.Clear(); state.FireAfterReload = false;
        if (cancelAnimation) KnifeAnimationController.CancelReloadAction(player, state.ReloadAnimationSequence);
        state.ReloadAnimationSequence = -1;
    }

    public void RequestReload(ComponentPlayer player) {
        if (!ScGunBindings.Available(player)) return;
        int value = player.ComponentMiner.ActiveBlockValue;
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || player.ComponentHealth.Health <= 0) return;
        if (!m_states.TryGetValue(player, out GunState state)) return;
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (state.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model)) return;
        StartReload(player, state, model, ScGunBlock.SpecOf(value), value);
    }
    /// <summary>Client: the server's word on a reload of the gun in hand (ScNetGuns.OpReload). A reload this client shows
    /// and the server does not perform (refused, cancelled) is taken back; one the server began by itself (id 0: the
    /// magazine ran dry there while this client still showed rounds) is shown here. Finishing the animation here never
    /// stands for a reload: the rounds shown are always the server's.</summary>
    public void ServerReload(ComponentPlayer player, int id, ScNetGuns.ReloadPhase phase) {
        if (!ScNet.IsRemoteClient || player is null || !m_states.TryGetValue(player, out GunState state)) return;
        switch (phase) {
            case ScNetGuns.ReloadPhase.Accepted:
                if (id != 0 || state.Reload is not null) break;
                int value = player.ComponentMiner.ActiveBlockValue;
                var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
                if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || !ScGunBlock.IsKnown(value) || player.ComponentHealth.Health <= 0
                    || state.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model)) break;
                BeginReload(player, state, model, ScGunBlock.SpecOf(value), value, request: false);
                break;
            case ScNetGuns.ReloadPhase.Completed:
                // The server finished the reload this client shows (its rounds arrive with the record rows).
                if (id == state.ReloadId) state.ServerReloading = false;
                break;
            case ScNetGuns.ReloadPhase.Refused:
            case ScNetGuns.ReloadPhase.Cancelled:
                if (id != state.ReloadId) break;                       // about an earlier request
                state.ServerReloading = false;
                if (state.Reload is not null) CancelReload(player, state);
                break;
        }
    }

    /// <summary>Client: how long after the end of the reload it shows the server's word on it is still waited for.</summary>
    public const double ServerReloadGrace = 3;
    void StartReload(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value) => BeginReload(player, state, model, spec, value, true);
    /// <param name="request">A multiplayer client asks the server to perform this reload (false: it only shows one the server began).</param>
    void BeginReload(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value, bool request) {
        int data0 = Terrain.ExtractData(value);
        // (A remote client shows its own reload from the rounds it shows; the server reloads from its record.)
        int rounds = ScNetGuns.ShownRounds(player, GunSpec.GetRounds(data0));
        int capacity = ScGunGrowth.Capacity(GunSpec.GetVariant(data0), EffectiveGunStats.LevelOf(value));
        int reserve = GunSpec.TryGetSnapshot(data0, out var loaded) ? loaded.ReserveOverflowRounds : 0;
        if (rounds >= capacity || spec.RechargeSeconds > 0) return;
        // (A client asks for one reload at a time: the server's word on the last one comes first.)
        if (request && state.ServerReloading && ScNet.IsRemoteClient && ScNet.IsLocal(player) && ScNet.Now <= state.ServerReloadDeadline) return;
        bool creative = FreeUse(player);
        IInventory inventory = player.ComponentMiner.Inventory;
        int ammo = ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec));
        int cost = creative ? 0 : ScReloadTransaction.RequiredFor(spec, capacity);
        // A reserve that covers the whole gap pays for the reload on its own.
        if (!creative && reserve >= capacity - rounds) cost = 0;
        int available = creative ? 0 : ScInventoryTransaction.Count(inventory, ammo);
        if (available < cost) {
            player.ComponentGui.DisplaySmallMessage($"装填需要{(spec.Pellets > 1 ? "霰弹" : "通用弹匣")} ×{cost}（现有 {available}）", Color.Red, false, false);
            return;
        }
        int variant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
        bool empty = rounds == 0;
        string clip = KnifeAnimationController.ReloadForPlayer(player, variant, empty);
        bool tube = ScReloadTransaction.IsTube(spec.Name);
        int shells = tube && !creative ? Math.Min(capacity - rounds, available + reserve) : capacity - rounds;
        float duration = clip is "reloadFollowup" or "reloadFollowupEmpty" ? Cs2Rig.Duration(spec.Name,clip) : KnifeAnimationController.ReloadSeconds(variant, empty, shells);
        var milestones = Cs2Rig.ReloadMilestones(spec.Name, clip);
        var sections = tube ? Cs2Rig.GetReloadSections(spec.Name) : null;
        if (duration <= 0 || (tube ? sections is null : milestones is null)) {
            player.ComponentGui.DisplaySmallMessage("该武器缺少可靠装填事件，未消耗弹药。", Color.Red, false, false);
            return;
        }
        LeaveScope(player, state);
        KnifeAnimationController.TriggerReload(player, empty, shells);
        state.ReloadAnimationSequence = KnifeAnimationController.ReloadActionSequence(player);
        state.Reload = new ScReloadTransaction(inventory, inventory.ActiveSlotIndex, value, ammo, cost, capacity, ScGunHolders.PlayerKey(player, inventory.ActiveSlotIndex));
        // A remote client only shows the reload: the server is asked to perform it (every reload this client starts, by the
        // key or by a trigger on an empty magazine), unless this one is the showing of a reload the server already began.
        state.ReloadId = request && ScNet.IsRemoteClient && ScNet.IsLocal(player) ? ScNetGuns.RequestReload() : 0;
        if (ScNet.IsRemoteClient && ScNet.IsLocal(player)) { state.ServerReloading = true; state.ServerReloadDeadline = ScNet.Now + duration + ServerReloadGrace; }
        state.Scheduled.Clear(); state.ShellTimes.Clear(); state.FireAfterReload = false;
        double now = KnifeClock.Now;
        state.BusyUntil = now + duration; state.PendingRounds = -1;
        state.DropAt = state.InsertAt = -1;
        if (tube) {
            for (int k = 0; k < shells; k++) state.ShellTimes.Add(now + sections.LoopStart + k * sections.LoopLength + sections.AddAmmoInLoop);
            ScheduleLooped(state, spec.Name, clip, now, sections, shells);
        }
        else {
            state.DropAt = now + milestones.Value.Drop;
            state.InsertAt = now + milestones.Value.Insert;
            Schedule(state, spec.Name, clip, m_time.GameTime, spec.HasSilencer && !GunSpec.GetSilencerOff(Terrain.ExtractData(value)));
        }
    }

    /// <summary>
    /// The cues of a looped reload: those before the loop section once, those inside
    /// it once per shell, those after it once at the end.
    /// </summary>
    void ScheduleLooped(GunState state, string spec, string clip, double startedAt, Cs2Rig.ReloadSections sections, int loops) {
        if(state.InspectSoundToken>=0){state.Scheduled.Clear();state.InspectSoundToken=-1;}
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

    /// <summary>One action per press: true on the frame the button goes down, never while it stays down.</summary>
    public static bool PressEdge(ref bool latch, bool aiming) { bool edge = aiming && !latch; latch = aiming; return edge; }
    /// <summary>PC right button while a gun is held (called from the UpdatePlayerInputAim hook every frame, whatever the item):
    /// tracks the button and, on a press edge with a gun in hand, runs the secondary action at once. Vanilla's aim path
    /// with its 1.4 s survival / 0.1 s creative cooldown and its release-time trigger is bypassed for guns (0.35.1).</summary>
    public bool AimPressed(ComponentPlayer player, bool aiming, bool holdingGun) {
        if (!m_states.TryGetValue(player, out GunState state)) m_states[player] = state = new GunState();
        bool edge = PressEdge(ref state.AimLatch, aiming);
        if (!edge || !holdingGun) return false;
        // The shared right button drives both the Aim input (the gun's second action) and the separate Interact
        // input (opening a door, pulling a lever, using a chest, another mod's interactive object...). The world
        // interaction always wins: the aim the player is pointing at is what they mean to use, not the gun's mode.
        // The engine's own interact decision is authoritative - NoteWorldInteract records it on the press frame -
        // and the local raycast is a fallback for the frame the interact hook did not run.
        bool blocked = state.WorldInteract;
        state.WorldInteract = false;
        if (!blocked) {
            try { blocked = WorldInteractionAhead(player); }
            catch (Exception e) { KnifeDiagnostics.WarnOnce("gun-interact-probe", "gun right-click interaction probe failed: " + e); }
        }
        if (blocked) return false;
        return RequestSecondary(player);
    }

    /// <summary>Called from the OnPlayerInputInteract hook (which runs before the Aim hook in the same frame) with
    /// the engine's own priority decision. When the player holds a mod weapon and the engine found something to
    /// interact with, the gun's secondary action must not also fire on that press.</summary>
    public void NoteWorldInteract(ComponentPlayer player, bool interactive) {
        if (!m_states.TryGetValue(player, out GunState state)) m_states[player] = state = new GunState();
        state.WorldInteract = interactive;
    }

    /// <summary>True when the crosshair points at a block that answers the Interact button (a door, trapdoor,
    /// lever, button, chest, workbench...). The test is the same one the engine uses to decide priorityInteract.
    /// Shared with the knife so a heavy attack cannot fire together with a door either.</summary>
    public static bool WorldInteractionAhead(ComponentPlayer player) {
        var componentInput = player?.ComponentInput;
        if (componentInput is null) return false;
        PlayerInput input = componentInput.PlayerInput;
        Ray3? ray = input.Interact ?? input.Aim;
        if (ray is null) return false;
        var miner = player.ComponentMiner;
        if (miner is null) return false;
        var terrain = miner.Raycast<TerrainRaycastResult>(ray.Value, RaycastMode.Interaction, true, false, false);
        if (terrain is { } hit) {
            int value = hit.Value;
            var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            if (block is not null && block.GetPriorityInteract(value, miner) > 0) return true;
        }
        var moving = miner.Raycast<MovingBlocksRaycastResult>(ray.Value, RaycastMode.Interaction, false, false, true);
        if (moving is { } blockHit) {
            int value = blockHit.MovingBlock?.Value ?? 0;
            var block = value == 0 ? null : BlocksManager.Blocks[Terrain.ExtractContents(value)];
            if (block is not null && block.GetPriorityInteract(value, miner) > 0) return true;
        }
        return false;
    }

    public override bool OnAim(Ray3 aim, ComponentMiner componentMiner, AimState state) {
        ComponentPlayer player = componentMiner.ComponentPlayer;
        if (player is null) return false;
        // Multiplayer replays other players' aim events; their secondary actions arrive with their gun input instead.
        if (!ScNet.IsLocal(player)) return true;
        if (ScMobileControls.UsesTouchInput(player)) return false;
        // Since 0.35.1 the PC press edge is handled in the UpdatePlayerInputAim hook and vanilla's aim never starts for a
        // gun; this stays for any path that still reaches ComponentMiner.Aim.
        // Act on the release (one press, one action). InProgress and Cancelled must return
        // false: ComponentPlayer treats a true from InProgress as "aim refused" and cancels
        // the aim on the spot, so Completed never arrives (0.15.0/0.15.1 right-click bug).
        if (state != AimState.Completed) return false;
        // The world interaction wins on a shared press: a door being opened must not also scope the gun.
        if (WorldInteractionAhead(player)) return false;
        return RequestSecondary(player);
    }

    public bool RequestSecondary(ComponentPlayer player) {
        if (!ScGunBindings.Available(player)) return false;
        var componentMiner = player.ComponentMiner;
        // A remote client's menus are its own (its input says whether it may act); the server's screen is not its screen.
        bool remote = ScNetGuns.RemoteInput(player) is not null;
        if (player.ComponentHealth.Health <= 0 || !remote && (player.ComponentGui.ModalPanelWidget is not null || DialogsManager.HasDialogs(player.GuiWidget))
            || Terrain.ExtractContents(componentMiner.ActiveBlockValue) != BlocksManager.GetBlockIndex<ScGunBlock>(true)) return false;
        if (!m_states.TryGetValue(player, out GunState gun)) m_states[player] = gun = new GunState();
        int value = componentMiner.ActiveBlockValue;
        GunSpec spec = ScGunBlock.SpecOf(value);
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        bool busy = gun.BusyUntil >= 0 || KnifeAnimationController.IsBusy(model);
        if (remote) ScNet.Trace($"secondary P{player.PlayerData.PlayerIndex} {spec.Name} busy {busy} (gun {gun.BusyUntil >= 0}, viewmodel {KnifeAnimationController.IsBusy(model)})");
        // A remote client does the same here as a prediction and the server does it for real from this press.
        if (ScNet.IsLocal(player)) ScNetGuns.QueueSecondary();
        if (spec.ZoomLevels.Length > 0) {
            if (busy) return true;
            gun.RescopeAt = -1;
            // Scoping interrupts an inspect: the clip goes back to idle (the scoped pose follows from Zoom) and the
            // inspect cues that have not played yet are dropped, so a zoomed AUG is not still turning in the hands.
            if (KnifeAnimationController.CurrentClip(model) is string clip && clip.StartsWith("inspect", StringComparison.Ordinal)) {
                KnifeAnimationController.CancelAction(player);
                gun.Scheduled.RemoveAll(cue => cue.Name.Contains("inspect", StringComparison.Ordinal));
            }
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
            // Its own explicit input: never while the hammer is being drawn for the primary shot, never in the
            // frame another shot was committed, and never as a fallback of the primary.
            double readyAt = Math.Max(gun.NextShot, gun.AlternateReadyAt);
            bool allowed = rounds > 0 && gun.PrepareUntil < 0 && m_time.GameTime >= readyAt && gun.CommitFrame != Time.FrameIndex;
            ScRevolverTrigger.Note(player, allowed ? "commit-alternate" : gun.PrepareUntil >= 0 ? "alternate-refused-cocking" : gun.CommitFrame == Time.FrameIndex ? "alternate-refused-same-frame" : rounds <= 0 ? "alternate-refused-empty" : "alternate-refused-interval",
                m_time.GameTime, rounds, readyAt, false, false, false);
            if (allowed)
                Fire(player, gun, model, spec, value, data, rounds, ReadInput(player, consumeEvents: false), alternateFire: true);
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
                gun.BusyUntil = KnifeClock.Now + duration;
                gun.SilencerPending = true;
                gun.PendingSilencerOff = !off;
            }
        }
        return true;
    }

    readonly Dictionary<ComponentPlayer,bool> m_fireButtons=[];
    ValuesDictionary m_travelIdentities,m_travelBackup;
    ScTravelLedger m_travel;
    string m_travelSource;
    string m_travelWorldIdentity;
    ScTravelArrival m_arrival;
    public bool ReadyForTravel => m_saveReady && m_registry is not null && !m_registry.Disabled
        && m_registry.QuarantinedCount==0 && m_registry.Kills.Count==0 && m_registry.Recovery.Count==0
        && !ScGunMutation.IsCommitting && !m_states.Any(p=>p.Value.Reload is not null || p.Value.BusyUntil>=0
            || p.Value.BurstRemaining>0 || p.Value.PrepareUntil>=0
            || KnifeAnimationController.IsBusy(p.Key.Entity?.FindComponent<ComponentFirstPersonModel>()));
    public void SetFireButton(ComponentPlayer player,bool pressed) => m_fireButtons[player]=pressed;
    public bool FireButtonDown(ComponentPlayer player) => m_fireButtons.GetValueOrDefault(player);
    public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) => false;

    // ---- scope -------------------------------------------------------------------

    void SetZoom(ComponentPlayer player, GunState state, GunSpec spec, int level) {
        if (level <= 0) { LeaveScope(player, state); return; }
        state.Zoom = level;
        float magnification = spec.ZoomLevels[Math.Clamp(level - 1, 0, spec.ZoomLevels.Length - 1)];
        // Projection hook and the post-input adapter apply zoom locally, never to SettingsManager.
        CsmcFirstPersonRenderer.SetPlayerScope(player,true, magnification, spec.ScopeHidesWeapon);
        KnifeAnimationController.SetScoped(player, true);
    }

    void LeaveScope(ComponentPlayer player, GunState state) {
        if (state.Zoom == 0) return;
        state.Zoom = 0;
        CsmcFirstPersonRenderer.SetPlayerScope(player,false, 1f);
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

    /// <summary>The view part of a world mode's recoil (deathmatch round 5) on the player this process aims for: the look
    /// moves by the change since the last frame (only what the engine's look limits let through is counted). When the
    /// mode no longer governs the player, what the look still carries is handed to the survival kick's recovery.</summary>
    void ModeViewKick(ComponentPlayer player, GunState state) {
        if (!ScNet.IsLocal(player)) return;
        var recoil = ScModes.Recoil(player);
        if (recoil is null) {
            if (state.ModeView == Vector2.Zero) return;
            state.KickPitch += MathUtils.DegToRad(state.ModeView.X); state.KickYaw -= MathUtils.DegToRad(state.ModeView.Y);
            state.ModeKick = true; state.ModeView = Vector2.Zero;
            return;
        }
        Vector2 delta = recoil.At(player, m_time.GameTime).View - state.ModeView;
        if (!(delta.LengthSquared() > 1e-10f)) return;
        ComponentLocomotion locomotion = player.ComponentLocomotion;
        Vector2 before = locomotion.LookAngles;
        // LookAngles: X turns right, Y looks up (ComponentCreatureModel.CalculateEyeRotation yaws by -X)
        locomotion.LookAngles = new Vector2(before.X - MathUtils.DegToRad(delta.Y), before.Y + MathUtils.DegToRad(delta.X));
        Vector2 after = locomotion.LookAngles;
        state.ModeView += new Vector2(MathUtils.RadToDeg(after.Y - before.Y), -MathUtils.RadToDeg(after.X - before.X));
    }

    void RecoverKick(ComponentPlayer player, GunState state, float dt, float rate) {
        if (MathF.Abs(state.KickPitch) < 0.0001f && MathF.Abs(state.KickYaw) < 0.0001f) return;
        float k = ScGunplaySettings.Enabled || state.ModeKick ? 1-MathF.Exp(-Math.Max(0,rate)*Math.Max(0,dt)) : MathUtils.Saturate(rate * dt);
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
        return ScGunHandling.Scatter(direction,coneDegrees,m_random.Float(0,1),m_random.Float(0,1));
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

    /// <summary>Read every frame (multiplayer sends it with the gun input), so a player without a camera or model yet -
    /// a frame of a screen change, a headless check - gets a harmless ray instead of stopping the gun update.</summary>
    static Ray3 LookRay(ComponentPlayer player) {
        Camera camera = player.GameWidget?.ActiveCamera;
        if (camera is not null) return new Ray3(camera.ViewPosition, camera.ViewDirection);
        if (player.ComponentCreatureModel is { } model)
            return new Ray3(model.EyePosition, Matrix.CreateFromQuaternion(model.EyeRotation).Forward);
        return new Ray3(player.ComponentBody?.Position ?? Vector3.Zero, -Vector3.UnitZ);
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
        bool creative = FreeUse(player);
        double rechargeRemaining = GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var snapshot) && snapshot.RechargeReadyAt >= 0 ? Math.Max(0, snapshot.RechargeReadyAt - m_time.GameTime) : -1;
        state.AmmoHud.Show(ScAmmoReadout.ReadPredicted(spec, value, player.ComponentMiner.Inventory, creative, rechargeRemaining, state.Reload is not null, ScNetGuns.PendingShots(player)));
    }

    /// <summary>Plays Audio/ScCsgoKnives/&lt;name&gt; when the mod ships it; nothing (and no placeholder) when it does not.</summary>
    public static string ExtensionShotSound(GunSpec spec,bool silenced) {
        string name=spec.HasSilencer&&silenced?$"{spec.Name}_fire_silenced":$"{spec.Name}_fire";
        return "Audio/ScCsgoKnives/"+(s_variants.ContainsKey(name)?name+"_1":name);
    }
    void PlaySound(ComponentPlayer player, string name) {
        if (s_variants.TryGetValue(name, out int n)) name = $"{name}_{m_random.Int(1, n)}";
        string path = $"Audio/ScCsgoKnives/{name}";
        if (s_missingSounds.Contains(path)) return;
        try {
            m_audio.PlaySound(path, 1f, m_random.Float(-0.05f, 0.05f), player.ComponentCreatureModel.EyePosition, 24f, true);
        }
        catch (Exception e) {
            s_missingSounds.Add(path);
            KnifeDiagnostics.WarnOnce("gun-sound-"+path, $"[ScCsgoKnives] sound {path} failed ({e.GetType().Name}: {e.Message}); playing nothing.");
        }
    }
}
