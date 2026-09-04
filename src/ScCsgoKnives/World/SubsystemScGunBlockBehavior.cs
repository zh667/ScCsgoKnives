using Engine;
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
    static readonly Dictionary<string, int> s_variants = new(StringComparer.Ordinal) {
        // CS2 soundevents (local export, 2026-09-04): Weapon_AK47.Single = ak47_01/02/04;
        // Weapon_M4A1.Single (the M4A1-S without its silencer) = m4a1_01..04, the same files as the
        // M4A4; Weapon_M4A1.Silenced = m4a1_silencer_01; Weapon_AWP.Single = awp_01/02; the AWP's
        // zoom in and out both play zoom.wav. Files are CS2's own WAVs re-encoded mono.
        ["ak47_fire"] = 3, ["awp_fire"] = 2, ["m4a1s_fire"] = 4, ["m4a1s_fire_silenced"] = 1,
    };

    void Schedule(GunState state, string spec, string clip, double startedAt, bool silenced = false) {
        string key = $"{spec}:{clip}";
        // CS2's own event frames when the profile asks for them, else the bone-timed
        // table. Either way a clip CS2 has no cue for falls back to the old row.
        if (KnifeTuning.GunSoundProfile < 0.5f || !Cs2Sounds.TryGet(key, out var list))
            if (!s_clipSounds.TryGetValue(key, out list)) return;
        foreach ((float at, string name) in list) {
            // The M4A1-S bolt sounds differently with the silencer on (m4a1_silencer_bolt*).
            string n = silenced && name is "m4a1s_boltback" or "m4a1s_boltforward" ? name + "_silenced" : name;
            state.Scheduled.Add((startedAt + at, n));
        }
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

    public void Draw(Camera camera, int drawOrder) {
        if (CsmcFirstPersonRenderer.ScopeOverlayActive) CsmcFirstPersonRenderer.DrawScopeOverlay();
    }

    public override void Dispose() {
        Project.FindSubsystem<SubsystemDrawing>(false)?.RemoveDrawable(this);
        base.Dispose();
    }

    public override void Load(ValuesDictionary valuesDictionary) {
        base.Load(valuesDictionary);
        m_terrain = Project.FindSubsystem<SubsystemTerrain>(true);
        Project.FindSubsystem<SubsystemDrawing>(true).AddDrawable(this);
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
                state.BusyUntil = now + CsmcKnifeRig.GetDuration(ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value)), "deploy");
                state.PendingRounds = -1;
                state.SilencerPending = false;
                state.Scheduled.Clear();
                PlaySound(player, $"{spec.Name}_draw");
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

        if (!busy && wantsFire && rounds > 0 && now >= state.NextShot) {
            Fire(player, state, model, spec, value, data, rounds, input);
        }

        RecoverKick(player, state, dt, spec.KickRecoverPerSecond);
    }

    void Fire(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value, int data, int rounds, PlayerInput input) {
        double now = m_time.GameTime;
        state.NextShot = now + spec.CycleSeconds;
        rounds--;
        value = WriteData(player, value, GunSpec.SetRounds(data, rounds));
        bool silenced = spec.HasSilencer && !GunSpec.GetSilencerOff(data);
        if (state.Zoom > 0) {
            // CS2: a scoped shot drops the scope for the bolt cycle and re-zooms to the same level afterwards.
            state.RescopeLevel = state.Zoom;
            state.RescopeAt = now + spec.CycleSeconds;
            LeaveScope(player, state);
        }
        KnifeAnimationController.TriggerShoot(player, silenced);
        CsmcFirstPersonRenderer.MuzzleFlash(silenced ? 0.03f : 0.06f, silenced ? spec.SilencedMuzzleBone : spec.MuzzleBone);
        PlaySound(player, silenced ? $"{spec.Name}_fire_silenced" : $"{spec.Name}_fire");
        if (!spec.Automatic) Schedule(state, spec.Name, "shoot1", now);
        ShowAmmo(player, spec, rounds);

        // Camera kick, applied now and eased back in RecoverKick.
        float pitch = MathUtils.DegToRad(spec.KickPitchDegrees) * (0.8f + 0.4f * m_random.Float(0f, 1f));
        float yaw = MathUtils.DegToRad(spec.KickYawDegrees) * m_random.Float(-1f, 1f);
        Kick(player, state, pitch, yaw);

        // Hitscan along the view ray with a small random cone.
        Ray3 ray = input.Dig ?? input.Hit ?? new Ray3(player.ComponentCreatureModel.EyePosition, Vector3.Transform(Vector3.UnitZ, player.ComponentCreatureModel.EyeRotation));
        Vector3 direction = Scatter(ray.Direction, spec.SpreadDegrees * (state.Zoom > 0 ? 0.35f : 1f));
        Vector3 start = ray.Position;
        Vector3 end = start + direction * spec.RangeBlocks;
        BodyRaycastResult? body = m_bodies.Raycast(start, end, 0.35f, (b, d) => b != player.ComponentBody && b.Entity != player.Entity);
        TerrainRaycastResult? terrain = m_terrain.Raycast(start, end, false, true, (v, d) => Terrain.ExtractContents(v) != 0 && BlocksManager.Blocks[Terrain.ExtractContents(v)] is not FluidBlock);
        if (body.HasValue && (!terrain.HasValue || body.Value.Distance < terrain.Value.Distance)) {
            Vector3 hitPoint = body.Value.HitPoint();
            ComponentMiner.AttackBody(body.Value.ComponentBody, player, hitPoint, direction, spec.AttackPower, false);
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

    void StartReload(ComponentPlayer player, GunState state, ComponentFirstPersonModel model, GunSpec spec, int value) {
        int variant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
        float duration = CsmcKnifeRig.GetDuration(variant, "reload");
        if (duration <= 0f) return;
        LeaveScope(player, state);
        KnifeAnimationController.TriggerReload(player);
        state.Scheduled.Clear();
        Schedule(state, spec.Name, "reload", m_time.GameTime, spec.HasSilencer && !GunSpec.GetSilencerOff(Terrain.ExtractData(value)));
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
        else if (spec.HasSilencer && !busy) {
            bool off = GunSpec.GetSilencerOff(Terrain.ExtractData(value));
            int variant = ScGunBlock.AssetIndex(ScGunBlock.GetVariant(value));
            string clip = off ? "attach" : "detach";
            float duration = CsmcKnifeRig.GetDuration(variant, clip);
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
        if (state != null) { state.Scheduled.Clear(); Schedule(state, ScGunBlock.SpecOf(value).Name, "inspect", m_time.GameTime); }
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
        CsmcFirstPersonRenderer.SetScope(true, magnification);
    }

    void LeaveScope(ComponentPlayer player, GunState state) {
        if (state.Zoom == 0 && float.IsNaN(state.SavedViewAngle)) return;
        if (!float.IsNaN(state.SavedViewAngle)) SettingsManager.ViewAngle = state.SavedViewAngle;
        if (!float.IsNaN(state.SavedLookSensitivity)) SettingsManager.LookSensitivity = state.SavedLookSensitivity;
        state.SavedViewAngle = float.NaN;
        state.SavedLookSensitivity = float.NaN;
        state.Zoom = 0;
        CsmcFirstPersonRenderer.SetScope(false, 1f);
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
