using Engine;

namespace Game;

public static class KnifeAnimationController {
    enum ActionKind { Idle, Draw, Inspect, Slash }

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

    public static KnifeRigPose Update(ComponentFirstPersonModel model, int itemValue) {
        int knifeIndex = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        if (Terrain.ExtractContents(itemValue) != knifeIndex) {
            if (s_states.TryGetValue(model, out State oldState)) {
                oldState.Variant = -1;
                oldState.DrawWhenVisible = false;
                oldState.Pose = null;
            }
            return null;
        }

        int variant = ClampVariant(ScKnifeBlock.GetVariant(itemValue));
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
            Log.Information($"[ScCsgoKnives] controller: state.Variant {state.Variant} -> {variant} (itemValue={itemValue}, rawVariant={ScKnifeBlock.GetVariant(itemValue)}, assetCount={CsmcKnifeRig.AssetCount}).");
            state.Variant = variant;
            string deploy = CsmcKnifeRig.HasClip(variant, "deploy2") && s_random.Next(2) == 0 ? "deploy2" : "deploy";
            Start(state, ActionKind.Draw, deploy);
            PlayDrawSound(variant);
            LogActionStart(state, variant);
            if (!state.ControlsHintShown) {
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
        if (pokePhase > 0f && state.LastPokePhase <= 0f && state.Action != ActionKind.Draw) {
            string slash = CsmcKnifeRig.HasClip(variant, "slash2") && s_random.Next(2) == 1 ? "slash2" : "slash1";
            Start(state, ActionKind.Slash, slash);
            AudioManager.PlaySound("Audio/ScCsgoKnives/knife_slash", 0.85f, (float)s_random.NextDouble() * 0.16f - 0.08f, 0f);
            LogActionStart(state, variant);
        }
        state.LastPokePhase = pokePhase;

        float elapsed = (float)(Time.RealTime - state.StartedAt);
        if (state.Action == ActionKind.Idle) {
            state.Pose = CsmcKnifeRig.Sample(variant, state.ClipAlias, elapsed, true);
            return state.Pose;
        }

        float duration = CsmcKnifeRig.GetDuration(variant, state.ClipAlias);
        if (elapsed >= duration) {
            Start(state, ActionKind.Idle, CsmcKnifeRig.HasClip(variant, "idle2") && s_random.Next(5) == 0 ? "idle2" : "idle");
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
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) return false;

        int variant = ClampVariant(ScKnifeBlock.GetVariant(value));
        if (!s_states.TryGetValue(model, out State state)) {
            state = new State { Variant = variant };
            s_states.Add(model, state);
        }
        state.Variant = variant;
        // Some rigs ship two or three lookat clips; pick from whatever exists.
        string[] available = [.. new[] { "inspect", "inspect2", "inspect3" }
            .Where(alias => CsmcKnifeRig.HasClip(variant, alias))];
        string clip = available.Length > 0 ? available[s_random.Next(available.Length)] : "inspect";
        Start(state, ActionKind.Inspect, clip);
        LogActionStart(state, variant);
        if (IsBalisong(variant)) AudioManager.PlaySound("Audio/ScCsgoKnives/butterfly_inspect", 1f, 0f, 0f);
        return true;
    }

    static void Start(State state, ActionKind action, string clipAlias) {
        state.Action = action;
        state.ClipAlias = clipAlias;
        state.StartedAt = Time.RealTime;
        state.Pose = null;
    }

    static int ClampVariant(int variant) => Math.Clamp(variant, 0, CsmcKnifeRig.AssetCount - 1);

    // The flipping sounds were recorded for a balisong and only fit that knife.
    static bool IsBalisong(int variant) => CsmcKnifeRig.GetAssetName(variant) == "butterfly";

    static void PlayDrawSound(int variant) {
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
