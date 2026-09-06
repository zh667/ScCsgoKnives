using Engine;

namespace Game;

public static class KnifeAnimationController {
    enum ActionKind { Idle, Draw, Inspect, Slash, Shoot, Reload, Attach, Detach, Prepare, Grenade }

    sealed class State {
        public int Variant = -1;
        public ActionKind Action;
        public string ClipAlias = "idle";
        public double StartedAt;
        public float LastPokePhase;
        /// <summary>An inspect asked for while a draw was playing, started when it ends.</summary>
        public bool PendingInspect;
        /// <summary>Aiming down the gun's own scope (AUG, SG 553): idle and shots use the ironsight clips.</summary>
        public bool Scoped;
        /// <summary>A shotgun reload: how many shells the loop section runs for (-1: a one-pass reload).</summary>
        public int ReloadLoops = -1;
        public Cs2Rig.ReloadSections Sections;
        public KnifeRigPose Pose;
    }

    /// <summary>The length of the action that is running: the looped reload's own sum, else the clip's.</summary>
    static float ActionDuration(State state, int variant) =>
        state.Action == ActionKind.Reload && state.Sections is not null && state.ReloadLoops >= 0
            ? state.Sections.Duration(state.ReloadLoops)
            : CsmcKnifeRig.GetProfileDuration(variant, state.ClipAlias);

    /// <summary>The clip time to draw at `elapsed` seconds into the action.</summary>
    static float ClipTime(State state, float elapsed) =>
        state.Action == ActionKind.Reload && state.Sections is not null && state.ReloadLoops >= 0
            ? state.Sections.ClipTime(state.ReloadLoops, elapsed)
            : Math.Max(0f, elapsed);

    static readonly Dictionary<ComponentFirstPersonModel, State> s_states = [];
    static readonly System.Random s_random = new();

    /// <summary>
    /// Whether the profile that will actually draw this variant has the clip.
    ///
    /// Asking CsmcKnifeRig alone is what broke 0.17.0: with KnifeProfile=1 the
    /// controller kept picking deploy2 and inspect2/3 from the CS:MC table while the
    /// CS2 rig had no such clip, so the renderer drew idle and the knife appeared to
    /// jump straight to the finished pose. 67 of one session's 182 actions went that
    /// way. The clips exist in CS2 and now ship, but the query has to follow the
    /// profile regardless, or the next asymmetry does the same thing silently.
    /// </summary>
    static bool HasAlias(int variant, string alias) =>
        Cs2Placement.Active(variant)
            ? Cs2Rig.HasAlias(CsmcKnifeRig.GetAssetName(variant), alias)
            : CsmcKnifeRig.HasClip(variant, alias);

    /// <summary>One of the inspect clips the active profile can play.</summary>
    static string PickInspect(int variant) {
        string[] available = [.. new[] { "inspect", "inspect2", "inspect3" }
            .Where(alias => HasAlias(variant, alias))];
        return available.Length > 0 ? available[s_random.Next(available.Length)] : "inspect";
    }

    /// <summary>Rig manifest index of a held item: a knife variant, a gun variant after the knives, or -1.</summary>
    public static int ResolveVariant(int itemValue) {
        int contents = Terrain.ExtractContents(itemValue);
        if (contents == BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) return ScKnifeBlock.IsKnown(itemValue) ? ScKnifeBlock.GetVariant(itemValue) : -1;
        if (contents == BlocksManager.GetBlockIndex<ScGunBlock>(true)) return ScGunBlock.AssetIndex(ScGunBlock.GetVariant(itemValue));
        if (contents == BlocksManager.GetBlockIndex<ScGrenadeBlock>(true)) return ScGrenadeBlock.AssetIndex(itemValue);
        return -1;
    }

    public static KnifeRigPose Update(ComponentFirstPersonModel model, int itemValue) {
        int variant = ResolveVariant(itemValue);
        if (variant < 0) {
            if (s_states.TryGetValue(model, out State oldState)) {
                oldState.Variant = -1;
                oldState.Pose = null;
            }
            return null;
        }

        if (!s_states.TryGetValue(model, out State state)) {
            state = new State();
            s_states.Add(model, state);
        }

        // Inventory/dialogs affect gameplay input, not the visual animation clock.
        // Keep sampling real hands every frame, including a switch made in a menu.
        if (state.Variant != variant) {
            state.Scoped = false;
            // Whether a knife has a second draw is a property of its rig, not
            // of it being the butterfly.
            Log.Information($"[ScCsgoKnives] controller: state.Variant {state.Variant} -> {variant} (itemValue={itemValue}, rawVariant={ScKnifeBlock.GetVariant(itemValue)}, assetCount={CsmcKnifeRig.KnifeCount}).");
            state.Variant = variant;
            state.PendingInspect = false;
            string deploy = DeployClip(variant, SilencerOn(variant, itemValue));
            if (deploy == "deploy" && !KnifeQa.Active && HasAlias(variant, "deploy2") && s_random.Next(2) == 0) deploy = "deploy2";
            Start(state, ActionKind.Draw, deploy);
            PlayDrawSound(variant);
            LogActionStart(state, variant);
        }

        // Knife strikes are dispatched by the gameplay subsystem, never inferred from vanilla poke.
        float elapsed = (float)(KnifeClock.Now - state.StartedAt);
        if (state.Action == ActionKind.Idle) {
            // A pistol idles with the slide back while its magazine is empty and
            // drops it the moment a round is chambered. The magazine is written by
            // the behaviour, possibly a frame after this idle began, so the choice
            // is re-made here rather than only when the idle starts.
            if (CsmcKnifeRig.IsGun(variant) && state.ClipAlias is "idle" or "idleEmpty" or "ironsightIdle" or "idleLeftEmpty" or "idleBothEmpty") {
                string wanted = IdleClip(variant, Rounds(variant, itemValue), state.Scoped);
                if (wanted != state.ClipAlias) {
                    Start(state, ActionKind.Idle, wanted);
                    elapsed = 0f;
                }
            }
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, elapsed, true);
            return state.Pose;
        }

        float duration = ActionDuration(state, variant);
        if (state.Action == ActionKind.Grenade) {
            // Preparation clips may contain a single frame (duration zero).
            // Their owner advances phases; reaching the end must never play idle.
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, Math.Clamp(elapsed, 0, duration));
            return state.Pose;
        }
        if (elapsed >= duration) {
            // An inspect asked for during the draw runs now rather than being lost.
            if (state.PendingInspect && !KnifeQa.Active) {
                state.PendingInspect = false;
                Start(state, ActionKind.Inspect, PickInspect(variant));
                LogActionStart(state, variant);
                if (IsBalisong(variant)) AudioManager.PlaySound("Audio/ScCsgoKnives/butterfly_inspect", 1f, 0f, 0f);
                state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, 0f);
                return state.Pose;
            }
            string idle = IdleClip(variant, Rounds(variant, itemValue), state.Scoped);
            if (idle == "idle" && !KnifeQa.Active && HasAlias(variant, "idle2") && s_random.Next(5) == 0) idle = "idle2";
            Start(state, ActionKind.Idle, idle);
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, 0f, true);
        }
        else state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, ClipTime(state, elapsed));
        return state.Pose;
    }

    public static KnifeRigPose GetCurrentPose(ComponentFirstPersonModel model) =>
        model != null && s_states.TryGetValue(model, out State state) ? state.Pose : null;

    public static bool TriggerInspect(ComponentPlayer player) {
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null) return false;
        int value = player.ComponentMiner.ActiveBlockValue;
        int variant = ResolveVariant(value);
        if (variant < 0) return false;

        State state = StateFor(model);
        // Inspect can arrive before the drawing hook observes an inventory switch.
        // Initialize that weapon's deploy first, then queue the inspect behind it.
        if (state.Variant != variant) Update(model, value);

        // Pressed during a draw or a reload: remember it and run it when that ends,
        // instead of swallowing the key. 0.17.0 returned true here and did nothing,
        // so inspecting right after a switch looked dead.
        if (IsBusy(model)) {
            state.PendingInspect = !KnifeQa.Active && !KnifeQa.Armed;
            return true;
        }
        // Already inspecting: keep the clip running. Restarting it reset StartedAt,
        // and a device log showed repeats 0.17 s apart holding the animation at frame 0.
        if (state.Action == ActionKind.Inspect
            && KnifeClock.Now - state.StartedAt < CsmcKnifeRig.GetProfileDuration(variant, state.ClipAlias))
            return true;
        // With the capture armed, the inspect key runs the capture instead (KnifeQa).
        if (KnifeQa.Active) return true;
        if (KnifeQa.Armed) return KnifeQa.Begin(model, variant);
        // Some rigs ship two or three lookat clips; pick from whatever the profile has.
        Start(state, ActionKind.Inspect, PickInspect(variant));
        LogActionStart(state, variant);
        if (IsBalisong(variant)) AudioManager.PlaySound("Audio/ScCsgoKnives/butterfly_inspect", 1f, 0f, 0f);
        return true;
    }

    public static bool TriggerKnifeAttack(ComponentPlayer player, bool heavy) {
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        int variant = ResolveVariant(player.ComponentMiner.ActiveBlockValue);
        if (model is null || variant < 0 || variant >= CsmcKnifeRig.KnifeCount) return false;
        State state = StateFor(model);
        // Input updates can precede rendering after a shot + quick switch. Do not
        // turn the old gun's state into a slash and hide the pending knife deploy.
        if (state.Variant != variant || IsBusy(model)) return false;
        string alias = heavy ? "stab" : s_random.Next(2) == 0 ? "slash1" : "slash2";
        if (!HasAlias(variant, alias)) return false;
        state.PendingInspect = false;
        Start(state, ActionKind.Slash, alias);
        AudioManager.PlaySound("Audio/ScCsgoKnives/knife_slash", .85f, heavy ? -.12f : 0f, 0f);
        return true;
    }
    public static void KnifeHitPose(ComponentPlayer player, bool heavy) {
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null || !s_states.TryGetValue(model, out State state) || state.Action != ActionKind.Slash) return;
        string hit = heavy ? "stabHit" : state.ClipAlias == "slash2" ? "slashHit2" : "slashHit1";
        if (HasAlias(state.Variant, hit)) state.ClipAlias = hit; // preserve elapsed time, don't restart at impact
    }

    // ---- guns --------------------------------------------------------------------

    static State GunState(ComponentPlayer player, out int variant) {
        variant = -1;
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null) return null;
        variant = ResolveVariant(player.ComponentMiner.ActiveBlockValue);
        if (variant < 0 || !CsmcKnifeRig.IsGun(variant)) return null;
        State state = StateFor(model);
        state.Variant = variant;
        return state;
    }

    /// <summary>The silencer attach clip is playing (the silencer must stay drawn while the hands bring it in).</summary>
    public static bool IsAttaching(ComponentFirstPersonModel model) {
        if (model is null || !s_states.TryGetValue(model, out State state) || state.Action != ActionKind.Attach) return false;
        return KnifeClock.Now - state.StartedAt < CsmcKnifeRig.GetProfileDuration(state.Variant, state.ClipAlias);
    }

    /// <summary>A reload, silencer or draw clip is playing: the gun cannot fire, scope or inspect until it ends.</summary>
    public static bool IsBusy(ComponentFirstPersonModel model) {
        if (model is null || !s_states.TryGetValue(model, out State state)) return false;
        if (state.Action is not (ActionKind.Draw or ActionKind.Reload or ActionKind.Attach or ActionKind.Detach or ActionKind.Grenade)) return false;
        return KnifeClock.Now - state.StartedAt < ActionDuration(state, state.Variant);
    }

    /// <summary>How long a reload of this many shells runs: the looped sum where the rig loops, else the clip.</summary>
    public static float ReloadSeconds(int variant, bool magazineEmpty, int shells) {
        string clip = ReloadClip(variant, magazineEmpty);
        if (clip is null) return 0f;
        Cs2Rig.ReloadSections sections = clip == "reload" && Cs2Placement.Active(variant)
            ? Cs2Rig.GetReloadSections(CsmcKnifeRig.GetAssetName(variant)) : null;
        return sections is not null && shells > 0 ? sections.Duration(shells) : CsmcKnifeRig.GetProfileDuration(variant, clip);
    }

    /// <summary>
    /// Fire pressed during a shell-by-shell reload: the loop stops after the shell in
    /// hand and the outro (the pump) plays. Returns how many shells were loaded and
    /// the seconds left until the gun is free, or (-1, 0) when nothing was looping.
    /// </summary>
    public static (int Loaded, float Remaining) FinishReloadEarly(ComponentPlayer player) {
        State state = GunState(player, out int variant);
        if (state is null || state.Action != ActionKind.Reload || state.Sections is null || state.ReloadLoops < 0) return (-1, 0f);
        float elapsed = (float)(KnifeClock.Now - state.StartedAt);
        int loaded = elapsed <= state.Sections.LoopStart ? 0
            : Math.Min(state.ReloadLoops, (int)MathF.Ceiling((elapsed - state.Sections.LoopStart) / state.Sections.LoopLength));
        if (loaded >= state.ReloadLoops) return (state.ReloadLoops, Math.Max(0f, state.Sections.Duration(state.ReloadLoops) - elapsed));
        state.ReloadLoops = loaded;          // the shell in hand finishes, then the outro
        return (loaded, Math.Max(0f, state.Sections.Duration(loaded) - elapsed));
    }

    /// <summary>Plays one of the gun's shot clips (the M4A1-S picks by silencer state); interrupts idle and inspect.</summary>
    /// <summary>
    /// The shot clip this variant plays, from whichever rig is drawing it. Asking
    /// the CS:MC table (0.18.1) gave "idle" for every CS2-only gun - the device log
    /// showed two idle requests per P90 round - because they have no CS:MC clips.
    /// Exposed for the self-test: a gun whose shot resolves to idle is drawn wrong.
    /// </summary>
    internal static string ShootClip(int variant, bool silenced, Func<int, int> pick = null) =>
        ShootClip(variant, silenced, false, pick);

    /// <summary>
    /// With lastRound, the shot that empties the magazine: CS2's pistols lock the
    /// slide back on it (shoot_empty_*), and the rig says whether this gun does.
    /// </summary>
    internal static string ShootClip(int variant, bool silenced, bool lastRound, Func<int, int> pick = null) =>
        ShootClip(variant, silenced, lastRound, false, pick);

    /// <summary>With scoped, a shot from the gun's own scope (ironsight_shoot_*) where the rig has one.</summary>
    public static string ShootClip(int variant, bool silenced, bool lastRound, bool scoped, Func<int, int> pick = null) =>
        ShootClip(variant, silenced, lastRound, scoped, false, -1, pick);

    /// <summary>
    /// The full choice. alternate: the aim key's shot (the R8's fanning, shoot_alt1_*).
    /// roundsBefore: the magazine before this shot, for the Dual Berettas, which fire
    /// left, right, left ... from a full 30 - so an even count is the left gun's turn -
    /// and play each gun's last-round clip when its own last round goes (the left's
    /// with one round remaining, the right's with none).
    /// </summary>
    public static string ShootClip(int variant, bool silenced, bool lastRound, bool scoped, bool alternate, int roundsBefore, Func<int, int> pick = null) {
        if (alternate && HasAlias(variant, "shootAlt")) return "shootAlt";
        if (scoped && HasAlias(variant, "ironsightShoot")) return "ironsightShoot";
        if (roundsBefore >= 0 && HasAlias(variant, "shootLeft")) {
            int after = roundsBefore - 1;
            bool left = roundsBefore % 2 == 0;
            if (after == 1 && left && HasAlias(variant, "shootLeftLast")) return "shootLeftLast";
            if (after == 0 && !left && HasAlias(variant, "shootRightLast")) return "shootRightLast";
            return left ? "shootLeft" : "shoot1";
        }
        if (lastRound && HasAlias(variant, "shootEmpty")) return "shootEmpty";
        if (HasAlias(variant, "shootSilenced")) return silenced ? "shootSilenced" : "shootUnsilenced";
        string[] shots = [.. new[] { "shoot1", "shoot2", "shoot3" }.Where(alias => HasAlias(variant, alias))];
        if (shots.Length == 0) return "idle";
        return shots[(pick ?? s_random.Next)(shots.Length)];
    }

    /// <summary>The reload clip, or null when the drawing rig has none (the Taser).</summary>
    internal static string ReloadClip(int variant) => ReloadClip(variant, false);

    /// <summary>From an empty magazine the pistols run reload_empty_*, which releases the slide.</summary>
    public static string ReloadClip(int variant, bool magazineEmpty) {
        if (magazineEmpty && HasAlias(variant, "reloadEmpty")) return "reloadEmpty";
        return HasAlias(variant, "reload") ? "reload" : null;
    }

    /// <summary>The idle for the magazine state: idle_slide_back_* while empty, where the rig has it.</summary>
    public static string IdleClip(int variant, bool magazineEmpty) => IdleClip(variant, magazineEmpty, false);

    /// <summary>With scoped, the held aim pose (ironsight_fidget_*) where the rig has one.</summary>
    public static string IdleClip(int variant, bool magazineEmpty, bool scoped) {
        if (scoped && HasAlias(variant, "ironsightIdle")) return "ironsightIdle";
        return magazineEmpty && HasAlias(variant, "idleEmpty") ? "idleEmpty" : "idle";
    }

    /// <summary>
    /// By the round count: the Dual Berettas idle with the left gun's slide back at one
    /// round (the right still has it) and both back at none; every other gun reads
    /// "empty" as zero.
    /// </summary>
    public static string IdleClip(int variant, int rounds, bool scoped) {
        if (scoped && HasAlias(variant, "ironsightIdle")) return "ironsightIdle";
        if (rounds == 0 && HasAlias(variant, "idleBothEmpty")) return "idleBothEmpty";
        if (rounds == 1 && HasAlias(variant, "idleLeftEmpty")) return "idleLeftEmpty";
        return IdleClip(variant, rounds <= 0, scoped);
    }

    static int Rounds(int variant, int itemValue) =>
        CsmcKnifeRig.IsGun(variant) ? GunSpec.GetRounds(Terrain.ExtractData(itemValue)) : 1;

    /// <summary>The R8's hammer being drawn before its cocked shot (prepare_shoot_*); false when the rig has none.</summary>
    public static bool TriggerPrepare(ComponentPlayer player) {
        State state = GunState(player, out int variant);
        if (state is null || !HasAlias(variant, "prepareShoot")) return false;
        Start(state, ActionKind.Prepare, "prepareShoot");
        return true;
    }

    /// <summary>The behaviour's zoom state, so the idle and the shot can follow the scope.</summary>
    public static void SetScoped(ComponentPlayer player, bool scoped) {
        ComponentFirstPersonModel model = player?.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null || !s_states.TryGetValue(model, out State state)) return;
        state.Scoped = scoped;
    }

    /// <summary>The draw for the silencer state: draw_silenced_* with the silencer on, where the rig has it.</summary>
    public static string DeployClip(int variant, bool silencerOn) =>
        silencerOn && HasAlias(variant, "deploySilenced") ? "deploySilenced" : "deploy";

    static bool MagazineEmpty(int variant, int itemValue) =>
        CsmcKnifeRig.IsGun(variant) && GunSpec.GetRounds(Terrain.ExtractData(itemValue)) <= 0;

    static bool SilencerOn(int variant, int itemValue) {
        if (!CsmcKnifeRig.IsGun(variant)) return false;
        GunSpec spec = GunSpec.ForAsset(CsmcKnifeRig.GetAssetName(variant));
        return spec is { HasSilencer: true } && !GunSpec.GetSilencerOff(Terrain.ExtractData(itemValue));
    }

    /// <summary>The silencer clip, or null when the drawing rig has none.</summary>
    internal static string SilencerClip(int variant, bool attach) {
        string clip = attach ? "attach" : "detach";
        return HasAlias(variant, clip) ? clip : null;
    }

    public static void TriggerShoot(ComponentPlayer player, bool silenced, bool lastRound = false, bool scoped = false,
                                    bool alternate = false, int roundsBefore = -1) {
        State state = GunState(player, out int variant);
        if (state is null) return;
        state.Scoped = scoped;
        Start(state, ActionKind.Shoot, ShootClip(variant, silenced, lastRound, scoped, alternate, roundsBefore));
    }

    public static void CancelAction(ComponentPlayer player) {
        ComponentFirstPersonModel model = player?.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null || !s_states.TryGetValue(model, out State state)) return;
        state.PendingInspect = false;
        Start(state, ActionKind.Idle, "idle");
    }

    public static void GrenadeAction(ComponentPlayer player, string alias, float elapsed = 0) {
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        int variant = ResolveVariant(player.ComponentMiner.ActiveBlockValue);
        if (model is null || variant < 0 || !CsmcKnifeRig.IsGrenade(variant)) return;
        var state = StateFor(model); state.Variant = variant; state.PendingInspect = false;
        Start(state, ActionKind.Grenade, alias); state.StartedAt -= elapsed;
    }

    public static void TriggerReload(ComponentPlayer player, bool magazineEmpty = false, int shells = 0) {
        State state = GunState(player, out int variant);
        if (state is null || ReloadClip(variant, magazineEmpty) is not string clip) return;
        Start(state, ActionKind.Reload, clip);
        // A shotgun reload loops its shell section once per shell wanted.
        Cs2Rig.ReloadSections sections = clip == "reload" && Cs2Placement.Active(variant)
            ? Cs2Rig.GetReloadSections(CsmcKnifeRig.GetAssetName(variant)) : null;
        if (sections is not null && shells > 0) {
            state.Sections = sections;
            state.ReloadLoops = shells;
            KnifeLog.Information($"[ScCsgoKnives] CS2 reload: asset={CsmcKnifeRig.GetAssetName(variant)} shells={shells} "
                + $"intro {sections.LoopStart:0.###}s + {shells} x {sections.LoopLength:0.###}s + outro {sections.End - sections.OutroStart:0.###}s = {sections.Duration(shells):0.###}s");
        }
        LogActionStart(state, variant);
    }

    public static void TriggerSilencer(ComponentPlayer player, bool attach) {
        State state = GunState(player, out int variant);
        if (state is null || SilencerClip(variant, attach) is not string clip) return;
        Start(state, attach ? ActionKind.Attach : ActionKind.Detach, clip);
        LogActionStart(state, variant);
    }

    /// <summary>The capture run's hooks (KnifeQa): a deterministic draw and inspect, and where the action stands.</summary>
    internal static void QaDraw(ComponentFirstPersonModel model, int variant) {
        State state = StateFor(model);
        state.Variant = variant;
        state.LastPokePhase = 0f;
        Start(state, ActionKind.Draw, "deploy");
    }

    internal static void QaInspect(ComponentFirstPersonModel model, int variant) {
        State state = StateFor(model);
        state.Variant = variant;
        Start(state, ActionKind.Inspect, "inspect");
    }

    internal static bool QaIsIdle(ComponentFirstPersonModel model) =>
        s_states.TryGetValue(model, out State state) && state.Action == ActionKind.Idle;

    internal static string QaClip(ComponentFirstPersonModel model) =>
        s_states.TryGetValue(model, out State state) ? state.ClipAlias : "";

    /// <summary>The clip alias the controller is running for this model, or null.</summary>
    public static string CurrentClip(ComponentFirstPersonModel model) =>
        model is not null && s_states.TryGetValue(model, out State state) ? state.ClipAlias : null;

    internal static float QaClipTime(ComponentFirstPersonModel model) =>
        s_states.TryGetValue(model, out State state) ? (float)(KnifeClock.Now - state.StartedAt) : 0f;

    static State StateFor(ComponentFirstPersonModel model) {
        if (!s_states.TryGetValue(model, out State state)) {
            state = new State();
            s_states.Add(model, state);
        }
        return state;
    }

    static void Start(State state, ActionKind action, string clipAlias) {
        state.Action = action;
        state.ClipAlias = clipAlias;
        state.StartedAt = KnifeClock.Now;
        state.Pose = null;
        state.ReloadLoops = -1;
        state.Sections = null;
        // What the alias actually became. Reading the 0.17.0 log, the only way to
        // tell a second draw from a silent fallback to idle was to cross the CS:MC
        // action lines against the rig's clip list by hand.
        if (state.Variant >= 0 && Cs2Placement.Active(state.Variant)) {
            string asset = CsmcKnifeRig.GetAssetName(state.Variant);
            KnifeLog.Information(
                $"[ScCsgoKnives] CS2 action: asset={asset} requested={clipAlias} "
                + $"resolved={Cs2Rig.ResolvedClip(asset, clipAlias) ?? "(none, drawing idle)"} "
                + $"duration={Cs2Rig.Duration(asset, clipAlias):0.###}s");
        }
    }


    // The flipping sounds were recorded for a balisong and only fit that knife.
    static bool IsBalisong(int variant) => CsmcKnifeRig.GetAssetName(variant) == "butterfly";

    static void PlayDrawSound(int variant) {
        if (CsmcKnifeRig.IsGrenade(variant)) {
            if (variant-CsmcKnifeRig.GrenadeOffset < 6) AudioManager.PlaySound("Audio/ScCsgoKnives/"+CsmcKnifeRig.GetAssetName(variant)+"_draw",1,0,0);
            return;
        }
        if (CsmcKnifeRig.IsGun(variant)) return;          // guns: SubsystemScGunBlockBehavior plays their own files when shipped
        string sound = IsBalisong(variant) ? "Audio/ScCsgoKnives/butterfly_draw" : "Audio/ScCsgoKnives/knife_deploy";
        AudioManager.PlaySound(sound, 1f, 0f, 0f);
    }

    static void LogActionStart(State state, int variant) {
        // Start already logged the CS2 clip and its length; the CS:MC sample below is
        // for the CS:MC chain, and asking a CS2-only variant for it threw out of the
        // draw hook in 0.18.1 (the ssg08 stack in the device log).
        if (CsmcKnifeRig.IsCs2Only(variant)) return;
        float duration = CsmcKnifeRig.GetDuration(variant, state.ClipAlias);
        KnifeRigPose initial = CsmcKnifeRig.Sample(variant, state.ClipAlias, 0f);
        KnifeRigPose middle = CsmcKnifeRig.Sample(variant, state.ClipAlias, duration * 0.5f);
        KnifeRigPose final = CsmcKnifeRig.Sample(variant, state.ClipAlias, duration);
        Log.Information(
            $"[ScCsgoKnives] exact CSMC action={state.Action}, variant={variant}, asset={CsmcKnifeRig.GetAssetName(variant)}, "
            + $"clip={initial.SourceClip}, duration={duration:0.###}s, "
            + $"initialWeapon={KnifeDiagnostics.MatrixSummary(initial.GetBinding("weapon_hand_r"))}, "
            + $"middleWeapon={KnifeDiagnostics.MatrixSummary(middle.GetBinding("weapon_hand_r"))}, "
            + $"hand_r(binding)={Format(initial.GetBindingOrigin("hand_r"))}->{Format(middle.GetBindingOrigin("hand_r"))}->{Format(final.GetBindingOrigin("hand_r"))}, "
            + $"hand_l(bone)={Format(initial.GetAttachment("hand_l"))}->{Format(middle.GetAttachment("hand_l"))}->{Format(final.GetAttachment("hand_l"))}."
        );
    }

    static string Format(Vector3 value) => $"({value.X:0.###},{value.Y:0.###},{value.Z:0.###})";
}
