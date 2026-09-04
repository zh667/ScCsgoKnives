using Engine;

namespace Game;

public static class KnifeAnimationController {
    enum ActionKind { Idle, Draw, Inspect, Slash, Shoot, Reload, Attach, Detach }

    sealed class State {
        public int Variant = -1;
        public ActionKind Action;
        public string ClipAlias = "idle";
        public double StartedAt;
        public float LastPokePhase;
        public bool ControlsHintShown;
        public bool DrawWhenVisible;
        public KnifeRigPose Pose;
    }

    static readonly Dictionary<ComponentFirstPersonModel, State> s_states = [];
    static readonly System.Random s_random = new();

    /// <summary>Rig manifest index of a held item: a knife variant, a gun variant after the knives, or -1.</summary>
    public static int ResolveVariant(int itemValue) {
        int contents = Terrain.ExtractContents(itemValue);
        if (contents == BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) return ClampVariant(ScKnifeBlock.GetVariant(itemValue));
        if (contents == BlocksManager.GetBlockIndex<ScGunBlock>(true)) return ScGunBlock.AssetIndex(ScGunBlock.GetVariant(itemValue));
        return -1;
    }

    public static KnifeRigPose Update(ComponentFirstPersonModel model, int itemValue) {
        int variant = ResolveVariant(itemValue);
        if (variant < 0) {
            if (s_states.TryGetValue(model, out State oldState)) {
                oldState.Variant = -1;
                oldState.DrawWhenVisible = false;
                oldState.Pose = null;
            }
            return null;
        }

        if (!s_states.TryGetValue(model, out State state)) {
            state = new State();
            s_states.Add(model, state);
        }

        bool viewObscured = model.m_componentPlayer.ComponentGui.ModalPanelWidget != null
            || DialogsManager.HasDialogs(model.m_componentPlayer.GuiWidget);
        if (viewObscured) {
            if (state.Variant != variant) {
                Log.Information($"[ScCsgoKnives] obscured: held variant {state.Variant} -> {variant}, deferring draw.");
                state.DrawWhenVisible = true;
            }
            state.Pose = null;
            return null;
        }

        if (state.DrawWhenVisible || state.Variant != variant) {
            state.DrawWhenVisible = false;
            // Whether a knife has a second draw is a property of its rig, not
            // of it being the butterfly.
            Log.Information($"[ScCsgoKnives] controller: state.Variant {state.Variant} -> {variant} (itemValue={itemValue}, rawVariant={ScKnifeBlock.GetVariant(itemValue)}, assetCount={CsmcKnifeRig.KnifeCount}).");
            state.Variant = variant;
            string deploy = !KnifeQa.Active && CsmcKnifeRig.HasClip(variant, "deploy2") && s_random.Next(2) == 0 ? "deploy2" : "deploy";
            Start(state, ActionKind.Draw, deploy);
            PlayDrawSound(variant);
            LogActionStart(state, variant);
            if (!state.ControlsHintShown && !CsmcKnifeRig.IsGun(variant)) {
                state.ControlsHintShown = true;
                model.m_componentPlayer.ComponentGui.DisplaySmallMessage(
                    string.Format(LanguageControl.Get("ScCsgoKnives", "Message", "ControlsHint"), GetEditKeyName()),
                    Color.White,
                    true,
                    false
                );
            }
        }

        float pokePhase = model.m_pokeAnimationTime;
        // Guns have no swing: their attack is the shot (TriggerShoot), never the poke.
        if (pokePhase > 0f && state.LastPokePhase <= 0f && state.Action != ActionKind.Draw && !KnifeQa.Active && !CsmcKnifeRig.IsGun(variant)) {
            string slash = CsmcKnifeRig.HasClip(variant, "slash2") && s_random.Next(2) == 1 ? "slash2" : "slash1";
            Start(state, ActionKind.Slash, slash);
            AudioManager.PlaySound("Audio/ScCsgoKnives/knife_slash", 0.85f, (float)s_random.NextDouble() * 0.16f - 0.08f, 0f);
            LogActionStart(state, variant);
        }
        state.LastPokePhase = pokePhase;

        float elapsed = (float)(KnifeClock.Now - state.StartedAt);
        if (state.Action == ActionKind.Idle) {
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, elapsed, true);
            return state.Pose;
        }

        float duration = CsmcKnifeRig.GetProfileDuration(variant, state.ClipAlias);
        if (elapsed >= duration) {
            Start(state, ActionKind.Idle, !KnifeQa.Active && CsmcKnifeRig.HasClip(variant, "idle2") && s_random.Next(5) == 0 ? "idle2" : "idle");
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, 0f, true);
        }
        else state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, Math.Max(0f, elapsed));
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
        if (IsBusy(model)) return true;

        if (!s_states.TryGetValue(model, out State state)) {
            state = new State { Variant = variant };
            s_states.Add(model, state);
        }
        state.Variant = variant;
        // With the capture armed, the inspect key runs the capture instead (KnifeQa).
        if (KnifeQa.Active) return true;
        if (KnifeQa.Armed) return KnifeQa.Begin(model, variant);
        // Some rigs ship two or three lookat clips; pick from whatever exists.
        string[] available = [.. new[] { "inspect", "inspect2", "inspect3" }
            .Where(alias => CsmcKnifeRig.HasClip(variant, alias))];
        string clip = available.Length > 0 ? available[s_random.Next(available.Length)] : "inspect";
        Start(state, ActionKind.Inspect, clip);
        LogActionStart(state, variant);
        if (IsBalisong(variant)) AudioManager.PlaySound("Audio/ScCsgoKnives/butterfly_inspect", 1f, 0f, 0f);
        return true;
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
        if (state.Action is not (ActionKind.Draw or ActionKind.Reload or ActionKind.Attach or ActionKind.Detach)) return false;
        return KnifeClock.Now - state.StartedAt < CsmcKnifeRig.GetProfileDuration(state.Variant, state.ClipAlias);
    }

    /// <summary>Plays one of the gun's shot clips (the M4A1-S picks by silencer state); interrupts idle and inspect.</summary>
    public static void TriggerShoot(ComponentPlayer player, bool silenced) {
        State state = GunState(player, out int variant);
        if (state is null) return;
        string clip;
        if (CsmcKnifeRig.HasClip(variant, "shootSilenced")) clip = silenced ? "shootSilenced" : "shootUnsilenced";
        else {
            string[] shots = [.. new[] { "shoot1", "shoot2", "shoot3" }.Where(alias => CsmcKnifeRig.HasClip(variant, alias))];
            clip = shots.Length > 0 ? shots[s_random.Next(shots.Length)] : "idle";
        }
        Start(state, ActionKind.Shoot, clip);
    }

    public static void TriggerReload(ComponentPlayer player) {
        State state = GunState(player, out int variant);
        if (state is null || !CsmcKnifeRig.HasClip(variant, "reload")) return;
        Start(state, ActionKind.Reload, "reload");
        LogActionStart(state, variant);
    }

    public static void TriggerSilencer(ComponentPlayer player, bool attach) {
        State state = GunState(player, out int variant);
        string clip = attach ? "attach" : "detach";
        if (state is null || !CsmcKnifeRig.HasClip(variant, clip)) return;
        Start(state, attach ? ActionKind.Attach : ActionKind.Detach, clip);
        LogActionStart(state, variant);
    }

    /// <summary>The capture run's hooks (KnifeQa): a deterministic draw and inspect, and where the action stands.</summary>
    internal static void QaDraw(ComponentFirstPersonModel model, int variant) {
        State state = StateFor(model);
        state.Variant = variant;
        state.DrawWhenVisible = false;
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
    }

    static int ClampVariant(int variant) => Math.Clamp(variant, 0, CsmcKnifeRig.KnifeCount - 1);

    // The flipping sounds were recorded for a balisong and only fit that knife.
    static bool IsBalisong(int variant) => CsmcKnifeRig.GetAssetName(variant) == "butterfly";

    static void PlayDrawSound(int variant) {
        if (CsmcKnifeRig.IsGun(variant)) return;          // guns: SubsystemScGunBlockBehavior plays their own files when shipped
        string sound = IsBalisong(variant) ? "Audio/ScCsgoKnives/butterfly_draw" : "Audio/ScCsgoKnives/knife_deploy";
        AudioManager.PlaySound(sound, 1f, 0f, 0f);
    }

    static void LogActionStart(State state, int variant) {
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

    static string GetEditKeyName() => SettingsManager.GetKeyboardMapping("EditItem", false)?.ToString() ?? "G";
}
