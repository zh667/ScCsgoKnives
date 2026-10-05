using Engine;

namespace Game;

public static class KnifeAnimationController {
    enum ActionKind { Idle, Draw, Inspect, Slash, Shoot, Reload, Attach, Detach, Prepare, Grenade }

    sealed class State {
        /// <summary>Whose hands these are: owner of this action's held sounds (ScPresentationSound).</summary>
        public ComponentFirstPersonModel Model;
        public int Variant = -1;
        public readonly ScHeldWeaponSelection Selection = new();
        public ActionKind Action;
        public string ClipAlias = "idle";
        public double StartedAt;
        public double DrawReadyAt;
        public Cs2Rig.Pose InspectFrom;
        public long ActionSequence;
        public float LastPokePhase;
        /// <summary>An inspect asked for while a draw was playing, started when it ends.</summary>
        public bool PendingInspect;
        public bool CzFrontRemoved;
        /// <summary>Aiming down the gun's own scope (AUG, SG 553): idle and shots use the ironsight clips.</summary>
        public bool Scoped;
        /// <summary>A shotgun reload: how many shells the loop section runs for (-1: a one-pass reload).</summary>
        public int ReloadLoops = -1;
        public Cs2Rig.ReloadSections Sections;
        public KnifeRigPose Pose;
        /// <summary>The R8's hammer being drawn: CS2's additive prepare_shoot layered over whatever the hands are
        /// doing (idle, or the recoil of the previous shot). Its time comes from the gameplay clock each frame.</summary>
        public bool PrepareLayer;
        public float PrepareTime;
        /// <summary>The item this action holds has left the hand (a planted C4): the arms keep playing, the prop is not drawn.</summary>
        public bool HeldPropHidden;
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
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScGunRegistry, HashSet<int>> s_czConsumed = new();
    static bool CzConsumed(int value) => ScGunRegistry.Current is {} registry && !GunSpec.IsFresh(Terrain.ExtractData(value))
        && s_czConsumed.GetOrCreateValue(registry).Contains(GunSpec.GetId(Terrain.ExtractData(value)));
    public static void ClearSession() { s_states.Clear(); s_czConsumed.Clear(); }
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
        // Old-format data draws no gun; a multiplayer client's gun still waiting for the server's record draws its model.
        if (contents == BlocksManager.GetBlockIndex<ScGunBlock>(true)) return ScGunBlock.IsShown(itemValue) ? ScGunBlock.AssetIndex(ScGunBlock.GetVariant(itemValue)) : -1;
        if (contents == BlocksManager.GetBlockIndex<ScGrenadeBlock>(true)) return ScGrenadeBlock.AssetIndex(itemValue);
        if (ScC4Block.IsValue(itemValue)) return CsmcKnifeRig.C4Index;
        return -1;
    }

    /// <summary>The drawing hook leaves a non-CS item to the per-frame update of the held item (true). Tests switch it off
    /// once to show the per-frame redraw it prevents (post-mp-bugs-20260930 §4).</summary>
    public static bool FollowHeldItemOnly = true;

    public static KnifeRigPose Update(ComponentFirstPersonModel model, int itemValue) {
        int variant = ResolveVariant(itemValue);
        if (variant < 0) {
            if (s_states.TryGetValue(model, out State oldState)) {
                if (oldState.Variant >= 0) { ScPresentationSound.Release(model, "no CS item"); NoteSelection(model, oldState, "no CS item", itemValue); }
                oldState.Variant = -1;
                oldState.Pose = null;
                oldState.Selection.Reset();
            }
            return null;
        }

        State state = StateFor(model);

        // Inventory/dialogs affect gameplay input, not the visual animation clock.
        // Keep sampling real hands every frame, including a switch made in a menu.
        var inventory = model.m_componentPlayer?.ComponentMiner?.Inventory;
        // During vanilla's switch-out, m_value still draws the departing model while the inventory already
        // points at the destination. Sample the outgoing action, but do not start actions or commit
        // animation events for an item that is no longer selected.
        int selected = model.m_componentPlayer?.ComponentMiner?.ActiveBlockValue ?? itemValue;
        if (selected != itemValue) {
            if (state.Variant != variant || state.Pose is null)
                return CsmcKnifeRig.Sample(variant, "idle", 0, true);
            state.PendingInspect = false;
            float departingTime = (float)(KnifeClock.Now - state.StartedAt);
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, ClipTime(state, departingTime), state.Action == ActionKind.Idle);
            return state.Pose;
        }
        bool selectionChanged = selected == itemValue && state.Selection.Observe(inventory, inventory?.ActiveSlotIndex ?? -1, itemValue, CsmcKnifeRig.IsGun(variant));
        if (state.Variant != variant || selectionChanged) {
            state.Scoped = false;
            // Whether a knife has a second draw is a property of its rig, not
            // of it being the butterfly.
            state.Variant = variant;
            state.PendingInspect = false;
            state.CzFrontRemoved = CsmcKnifeRig.GetAssetName(variant) == "cz75a" && CzConsumed(itemValue);
            string deploy = DeployClip(variant, SilencerOn(variant, itemValue));
            if (deploy == "deploy" && !KnifeQa.Active && HasAlias(variant, "deploy2") && s_random.Next(2) == 0) deploy = "deploy2";
            Start(state, ActionKind.Draw, deploy);
            state.DrawReadyAt=KnifeClock.Now+CsmcKnifeRig.GetProfileDuration(variant,deploy);
            // Only the new item's draw is heard: every older held sound of these hands fades out.
            ScPresentationSound.Release(model, "switch", keepAction: state.ActionSequence);
            NoteSelection(model, state, "draw " + deploy, itemValue);
            PlayDrawSound(state, variant);
        }

        // Knife strikes are dispatched by the gameplay subsystem, never inferred from vanilla poke.
        float elapsed = (float)(KnifeClock.Now - state.StartedAt);
        if(CsmcKnifeRig.GetAssetName(variant)=="cz75a" && state.Action==ActionKind.Reload && state.ClipAlias is "reload" or "reloadEmpty"
            && !state.CzFrontRemoved && elapsed >= Cs2Rig.CzFrontDetachTime(state.ClipAlias)) {
            state.CzFrontRemoved=true;
            if (ScGunRegistry.Current is {} registry && !GunSpec.IsFresh(Terrain.ExtractData(itemValue)))
                s_czConsumed.GetOrCreateValue(registry).Add(GunSpec.GetId(Terrain.ExtractData(itemValue)));
        }
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
            if (state.PendingInspect && !state.Scoped && !KnifeQa.Active) {
                state.PendingInspect = false;
                Start(state, ActionKind.Inspect, PickInspect(variant));
                if(CsmcKnifeRig.IsGun(variant)) model.m_componentPlayer?.Project?.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.InspectSound(model.m_componentPlayer,state.ClipAlias);
                if (IsBalisong(variant)) ScPresentationSound.PlayHeld(model, state.ActionSequence, "butterfly_inspect");
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
        if (!ScMinimalEdition.InspectEnabled) return false;
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null) return false;
        int value = player.ComponentMiner.ActiveBlockValue;
        int variant = ResolveVariant(value);
        if (variant < 0) return false;

        State state = StateFor(model);
        if(CsmcKnifeRig.IsGun(variant)&&(state.Scoped||player.Project?.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.IsScoped(player)==true)){
            state.PendingInspect=false;return false;
        }
        // Inspect can arrive before the drawing hook observes an inventory switch.
        // Initialize that weapon's deploy and its readiness deadline before interrupting its visuals.
        Update(model, value);

        // Already inspecting: keep the clip running. Restarting it reset StartedAt,
        // and a device log showed repeats 0.17 s apart holding the animation at frame 0.
        if (state.Action == ActionKind.Inspect
            && KnifeClock.Now - state.StartedAt < CsmcKnifeRig.GetProfileDuration(variant, state.ClipAlias))
            return true;
        // Reload and attachment transactions remain locked. Draw visuals can be interrupted.
        if (state.Action != ActionKind.Draw && IsBusy(model)) {
            state.PendingInspect = !KnifeQa.Active && !KnifeQa.Armed;
            return true;
        }
        // With the capture armed, the inspect key runs the capture instead (KnifeQa).
        if (KnifeQa.Active) return true;
        if (KnifeQa.Armed) return KnifeQa.Begin(model, variant);
        // Some rigs ship two or three lookat clips; pick from whatever the profile has.
        var transition=state.Action==ActionKind.Draw?Cs2Rig.Sample(CsmcKnifeRig.GetAssetName(variant),state.ClipAlias,(float)(KnifeClock.Now-state.StartedAt)):null;
        Start(state, ActionKind.Inspect, PickInspect(variant));
        state.InspectFrom=transition;
        state.PendingInspect=false;
        if(CsmcKnifeRig.IsGun(variant))player.Project?.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.InspectSound(player,state.ClipAlias);
        if (IsBalisong(variant)) ScPresentationSound.PlayHeld(model, state.ActionSequence, "butterfly_inspect");
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
        ScPresentationSound.Play("knife_slash", .85f, heavy ? -.12f : 0f);
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
        Update(model, player.ComponentMiner.ActiveBlockValue);
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
        if(KnifeClock.Now<state.DrawReadyAt)return true;
        if (state.Action is not (ActionKind.Draw or ActionKind.Reload or ActionKind.Attach or ActionKind.Detach or ActionKind.Grenade)) return false;
        return KnifeClock.Now - state.StartedAt < ActionDuration(state, state.Variant);
    }

    public static bool IsGrenadeDrawing(ComponentFirstPersonModel model)=>model is not null&&s_states.TryGetValue(model,out var s)
        &&CsmcKnifeRig.IsGrenade(s.Variant)&&(s.Action==ActionKind.Draw||KnifeClock.Now<s.DrawReadyAt);
    /// <summary>Seconds of a grenade's draw still to run in these hands (0: none, or not a grenade).</summary>
    public static double GrenadeDrawRemaining(ComponentFirstPersonModel model){
        if(!IsGrenadeDrawing(model)||!s_states.TryGetValue(model,out var s))return 0;
        double clip=s.Action==ActionKind.Draw?s.StartedAt+ActionDuration(s,s.Variant)-KnifeClock.Now:0;
        return Math.Max(0,Math.Max(clip,s.DrawReadyAt-KnifeClock.Now));
    }
    public static bool CanStartGrenade(ComponentFirstPersonModel model,int value){
        int variant=ResolveVariant(value);
        if(variant<0||!CsmcKnifeRig.IsGrenade(variant))return false;
        return model==null||!s_states.TryGetValue(model,out var s)||s.Variant!=variant||!IsBusy(model)||IsGrenadeDrawing(model);
    }
    public static void ScrubGrenade(ComponentPlayer player,string alias,float elapsed) {
        var model=player.Entity.FindComponent<ComponentFirstPersonModel>();
        if(model is not null&&s_states.TryGetValue(model,out var s)&&s.Action==ActionKind.Grenade&&s.ClipAlias==alias)s.StartedAt=KnifeClock.Now-elapsed;
    }
    static State C4State(ComponentPlayer player,long sequence){
        var model=player?.Entity?.FindComponent<ComponentFirstPersonModel>();
        return sequence>=0&&model is not null&&s_states.TryGetValue(model,out var s)&&s.Variant==CsmcKnifeRig.C4Index&&s.Action==ActionKind.Grenade&&s.ActionSequence==sequence?s:null;
    }
    /// <summary>The plant clip follows the gameplay clock (r2-c4-completion-20260929): its time is the seconds since the
    /// plant began, as the commit, the key presses and the recovery are, not the real-time clock. A paused game or a
    /// slow frame therefore never lets the hands run ahead of the charge.</summary>
    public static void ScrubC4(ComponentPlayer player,long sequence,float elapsed){if(C4State(player,sequence) is {} s)s.StartedAt=KnifeClock.Now-Math.Max(0,elapsed);}
    /// <summary>The charge was committed: from this frame the first-person C4 is on the ground, not in the hand.</summary>
    public static void HideC4Prop(ComponentPlayer player,long sequence){if(C4State(player,sequence) is {} s)s.HeldPropHidden=true;}
    public static bool HeldPropHidden(ComponentFirstPersonModel model)=>model is not null&&s_states.TryGetValue(model,out var s)&&s.HeldPropHidden;
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

    /// <summary>Seconds of prepare_shoot_revolver after which hammer, trigger and cylinder are fully drawn (measured
    /// on the shipped clip: the curves are flat from 0.33 s to its end at 0.97 s).</summary>
    public const float PrepareCockSeconds = 1f/3;

    /// <summary>The R8's hammer starts back (prepare_shoot_*). Presentation only: the cocked shot's timing and
    /// whether it fires belong to the gun behaviour and never depend on the answer. False when the rig has no such
    /// clip. The recoil of a shot that is still playing is kept; an inspect gives way, as CS2's WPN_BLOCK_INSPECT does.</summary>
    public static bool BeginPrepare(ComponentPlayer player) {
        State state = GunState(player, out int variant);
        if (state is null || !HasAlias(variant, "prepareShoot")) return false;
        state.PendingInspect = false;
        if (state.Action is ActionKind.Inspect or ActionKind.Prepare) Start(state, ActionKind.Idle, IdleClip(variant, Rounds(variant, player.ComponentMiner.ActiveBlockValue), state.Scoped));
        state.PrepareLayer = true; state.PrepareTime = 0;
        return true;
    }
    /// <summary>Progress of the cocking on the gameplay clock. The hammer is fully back when the shot is due: at
    /// the clip's own speed when the cocking time allows, faster when growth has shortened it.</summary>
    public static void DrivePrepare(ComponentPlayer player, float elapsed, float cockSeconds) {
        ComponentFirstPersonModel model = player?.Entity?.FindComponent<ComponentFirstPersonModel>();
        if (model is null || !s_states.TryGetValue(model, out State state) || !state.PrepareLayer) return;
        float rate = cockSeconds > 0 ? Math.Max(1, PrepareCockSeconds / cockSeconds) : 1;
        state.PrepareTime = Math.Max(0, elapsed) * rate;
    }
    /// <summary>The hammer falls (the shot) or is let down (released early, menu, switch).</summary>
    public static void EndPrepare(ComponentPlayer player) {
        ComponentFirstPersonModel model = player?.Entity?.FindComponent<ComponentFirstPersonModel>();
        if (model is not null && s_states.TryGetValue(model, out State state)) state.PrepareLayer = false;
    }
    public static bool PrepareShown(ComponentFirstPersonModel model, out float clipSeconds) {
        clipSeconds = 0;
        if (model is null || !s_states.TryGetValue(model, out State state) || !state.PrepareLayer) return false;
        clipSeconds = state.PrepareTime; return true;
    }
    /// <summary>The CS2 pose to draw: the running clip, with the R8's hammer layer on top while it is being drawn.</summary>
    public static Cs2Rig.Pose SampleDrawn(ComponentFirstPersonModel model, string gun, string clipAlias, float time, bool looping) =>
        PrepareShown(model, out float prepare) && Cs2Rig.IsAdditive(gun, "prepareShoot")
            ? Cs2Rig.SampleOver(gun, clipAlias, time, looping, "prepareShoot", prepare)
            : Cs2Rig.Sample(gun, clipAlias, time);
    /// <summary>Kept for callers of the pre-1.4.0 name; see <see cref="BeginPrepare"/>.</summary>
    public static bool TriggerPrepare(ComponentPlayer player) => BeginPrepare(player);

    /// <summary>The behaviour's zoom state, so the idle and the shot can follow the scope.</summary>
    public static void SetScoped(ComponentPlayer player, bool scoped) {
        ComponentFirstPersonModel model = player?.Entity?.FindComponent<ComponentFirstPersonModel>();
        if (model is null || !s_states.TryGetValue(model, out State state)) return;
        state.Scoped = scoped;
        if(scoped){
            state.PendingInspect=false;
            if(state.Action==ActionKind.Inspect)Start(state,ActionKind.Idle,IdleClip(state.Variant,Rounds(state.Variant,player.ComponentMiner.ActiveBlockValue),true));
        }
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
    public static long ReloadActionSequence(ComponentPlayer player) {
        var model = player?.Entity.FindComponent<ComponentFirstPersonModel>();
        return model is not null && s_states.TryGetValue(model, out var state) && state.Action == ActionKind.Reload ? state.ActionSequence : -1;
    }
    public static void CancelReloadAction(ComponentPlayer player, long sequence) {
        var model = player?.Entity.FindComponent<ComponentFirstPersonModel>();
        if (sequence < 0 || model is null || !s_states.TryGetValue(model, out var state)
            || state.Action != ActionKind.Reload || state.ActionSequence != sequence) return;
        state.PendingInspect = false;
        Start(state, ActionKind.Idle, "idle");
    }

    public static void GrenadeAction(ComponentPlayer player, string alias, float elapsed = 0) {
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        int variant = ResolveVariant(player.ComponentMiner.ActiveBlockValue);
        if (model is null || variant < 0 || !CsmcKnifeRig.IsGrenade(variant)) return;
        var state = StateFor(model); state.Variant = variant; state.PendingInspect = false;
        state.DrawReadyAt=KnifeClock.Now;
        var inventory=player.ComponentMiner.Inventory;
        state.Selection.Observe(inventory,inventory.ActiveSlotIndex,player.ComponentMiner.ActiveBlockValue,false);
        Start(state, ActionKind.Grenade, alias); state.StartedAt -= elapsed;
    }
    public static long C4Action(ComponentPlayer player, string alias) {
        var model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null || !ScC4Block.IsValue(player.ComponentMiner.ActiveBlockValue)) return -1;
        var state = StateFor(model); state.Variant = CsmcKnifeRig.C4Index; state.PendingInspect = false;
        var inventory=player.ComponentMiner.Inventory;
        state.Selection.Observe(inventory,inventory.ActiveSlotIndex,player.ComponentMiner.ActiveBlockValue,false);
        Start(state, ActionKind.Grenade, alias);
        return state.ActionSequence;
    }
    public static void CancelC4Action(ComponentPlayer player, long sequence) {
        var model = player?.Entity.FindComponent<ComponentFirstPersonModel>();
        if (sequence < 0 || model is null || !s_states.TryGetValue(model, out var state)
            || state.Variant != CsmcKnifeRig.C4Index || state.Action != ActionKind.Grenade || state.ActionSequence != sequence) return;
        state.PendingInspect = false;
        Start(state, ActionKind.Idle, "idle");
    }

    public static void TriggerReload(ComponentPlayer player, bool magazineEmpty = false, int shells = 0) {
        State state = GunState(player, out int variant);
        if (state is null || ReloadForPlayer(player,variant,magazineEmpty) is not string clip) return;
        Start(state, ActionKind.Reload, clip);
        // A shotgun reload loops its shell section once per shell wanted.
        Cs2Rig.ReloadSections sections = clip == "reload" && Cs2Placement.Active(variant)
            ? Cs2Rig.GetReloadSections(CsmcKnifeRig.GetAssetName(variant)) : null;
        if (sections is not null && shells > 0) {
            state.Sections = sections;
            state.ReloadLoops = shells;
        }
    }
    public static string ReloadForPlayer(ComponentPlayer player,int variant,bool empty) {
        var model=player?.Entity.FindComponent<ComponentFirstPersonModel>();
        if(CsmcKnifeRig.GetAssetName(variant)=="cz75a" && (CzConsumed(player.ComponentMiner.ActiveBlockValue) || model is not null && s_states.TryGetValue(model,out var s) && s.CzFrontRemoved))
            return empty?"reloadFollowupEmpty":"reloadFollowup";
        return ReloadClip(variant,empty);
    }
    public static bool HideCzFront(ComponentFirstPersonModel model)=>s_states.TryGetValue(model,out var s)&&s.CzFrontRemoved;

    public static void TriggerSilencer(ComponentPlayer player, bool attach) {
        State state = GunState(player, out int variant);
        if (state is null || SilencerClip(variant, attach) is not string clip) return;
        Start(state, attach ? ActionKind.Attach : ActionKind.Detach, clip);
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
    public static long ActionToken(ComponentFirstPersonModel model)=>model is not null && s_states.TryGetValue(model,out var state)?state.ActionSequence:-1;
    public static ScWeaponAction ReadAction(ComponentFirstPersonModel model) {
        if(model is null || !s_states.TryGetValue(model,out var s) || s.Variant<0)return default;
        float elapsed=Math.Max(0,(float)(KnifeClock.Now-s.StartedAt));
        return new(CsmcKnifeRig.GetAssetName(s.Variant),(ScWeaponActionKind)s.Action,s.ClipAlias,s.ActionSequence,
            elapsed,ActionDuration(s,s.Variant),ClipTime(s,elapsed),s.Action==ActionKind.Reload&&s.Sections!=null&&s.ReloadLoops>=0);
    }
    public static int CurrentVariant(ComponentFirstPersonModel model)=>model is not null && s_states.TryGetValue(model,out var state)?state.Variant:-1;

    public static Cs2Rig.Pose InspectTransition(ComponentFirstPersonModel model,Cs2Rig.Pose target) {
        if(model is null || !s_states.TryGetValue(model,out var state) || state.InspectFrom is not {} source || state.Action!=ActionKind.Inspect)return target;
        float t=Math.Clamp((float)(KnifeClock.Now-state.StartedAt)/.12f,0,1);
        if(t>=1){state.InspectFrom=null;return target;}
        t=t*t*(3-2*t);
        Matrix Blend(Matrix a,Matrix b) {
            a.Decompose(out Vector3 sa,out Quaternion ra,out Vector3 pa);
            b.Decompose(out Vector3 sb,out Quaternion rb,out Vector3 pb);
            return Matrix.CreateScale(Vector3.Lerp(sa,sb,t))*Matrix.CreateFromQuaternion(Quaternion.Slerp(ra,rb,t))*Matrix.CreateTranslation(Vector3.Lerp(pa,pb,t));
        }
        return new Cs2Rig.Pose{Gun=target.Gun,Clip=target.Clip,Time=target.Time,
            Parts=target.Parts.ToDictionary(p=>p.Key,p=>source.Parts.TryGetValue(p.Key,out var old)?Blend(old,p.Value):p.Value),
            Bones=target.Bones.ToDictionary(p=>p.Key,p=>source.Bones.TryGetValue(p.Key,out var old)?Blend(old,p.Value):p.Value)};
    }

    internal static float QaClipTime(ComponentFirstPersonModel model) =>
        s_states.TryGetValue(model, out State state) ? (float)(KnifeClock.Now - state.StartedAt) : 0f;

    static State StateFor(ComponentFirstPersonModel model) {
        if (!s_states.TryGetValue(model, out State state)) {
            state = new State { Model = model };
            s_states.Add(model, state);
        }
        return state;
    }

    static void Start(State state, ActionKind action, string clipAlias) {
        // An inspect cut short (a shot, a swing, a reload, another inspect) takes its sounds with it. Every other action
        // ending early is a switch, which releases everything anyway; an action that ran to its end keeps its tail.
        if (state.Action == ActionKind.Inspect && state.Model is not null && state.Variant >= 0
            && KnifeClock.Now - state.StartedAt < ActionDuration(state, state.Variant))
            ScPresentationSound.Release(state.Model, "inspect cancelled", onlyAction: state.ActionSequence);
        // The hammer layer rides on idle and on a shot's recoil only; any other action puts it down.
        if (action is not (ActionKind.Idle or ActionKind.Shoot)) state.PrepareLayer = false;
        state.InspectFrom=null;
        state.HeldPropHidden=false;
        state.ActionSequence++;
        state.Action = action;
        state.ClipAlias = clipAlias;
        state.StartedAt = KnifeClock.Now;
        state.Pose = null;
        state.ReloadLoops = -1;
        state.Sections = null;
    }


    // The flipping sounds were recorded for a balisong and only fit that knife.
    static bool IsBalisong(int variant) => CsmcKnifeRig.GetAssetName(variant) == "butterfly";

    static void PlayDrawSound(State state, int variant) {
        var owner = state.Model; long action = state.ActionSequence;
        if (CsmcKnifeRig.IsC4(variant)) { ScPresentationSound.PlayHeld(owner, action, "c4_draw"); return; }
        if (CsmcKnifeRig.IsGrenade(variant)) {
            if (variant-CsmcKnifeRig.GrenadeOffset < 6) ScPresentationSound.PlayHeld(owner, action, CsmcKnifeRig.GetAssetName(variant)+"_draw");
            return;
        }
        if (CsmcKnifeRig.IsGun(variant)) return;          // guns: SubsystemScGunBlockBehavior plays their own files when shipped
        ScPresentationSound.PlayHeld(owner, action, IsBalisong(variant) ? "butterfly_draw" : "knife_deploy");
    }

    /// <summary>Test diagnostics (ScPresentationSound.Record): what the hands hold now, beside the sounds.</summary>
    static void NoteSelection(ComponentFirstPersonModel model, State state, string what, int value) {
        if (!ScPresentationSound.Record) return;
        var player = model.m_componentPlayer;
        ScPresentationSound.Note($"select P{player?.PlayerData?.PlayerIndex ?? -1} slot {player?.ComponentMiner?.Inventory?.ActiveSlotIndex ?? -1} "
            + $"value {value} variant {state.Variant} {(state.Variant >= 0 ? CsmcKnifeRig.GetAssetName(state.Variant) : "-")} action {state.ActionSequence} {what} held {ScPresentationSound.Count(model)}");
    }


}
