using Engine;

namespace Game;

public static class KnifeAnimationController {
    enum ActionKind { Idle, Draw, Inspect }

    sealed class State {
        public int Variant = -1;
        public ActionKind Action;
        public double StartedAt;
        public float LastPokePhase;
        public bool ControlsHintShown;
        public bool DrawWhenVisible;
        public KnifeFramePose Pose = KnifeFramePose.Identity;
    }

    static readonly Dictionary<ComponentFirstPersonModel, State> s_states = [];
    static readonly System.Random s_random = new();

    public static KnifeFramePose Update(ComponentFirstPersonModel model, int itemValue) {
        int knifeIndex = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        if (Terrain.ExtractContents(itemValue) != knifeIndex) {
            if (s_states.TryGetValue(model, out State oldState)) {
                oldState.Variant = -1;
                oldState.DrawWhenVisible = false;
                oldState.Pose = KnifeFramePose.Identity;
            }
            return KnifeFramePose.Identity;
        }

        int variant = Math.Clamp(ScKnifeBlock.GetVariant(itemValue), 0, 2);
        if (!s_states.TryGetValue(model, out State state)) {
            state = new State();
            s_states.Add(model, state);
        }

        bool viewObscured = model.m_componentPlayer.ComponentGui.ModalPanelWidget != null
            || DialogsManager.HasDialogs(model.m_componentPlayer.GuiWidget);
        if (viewObscured) {
            if (state.Variant != variant) state.DrawWhenVisible = true;
            state.Pose = KnifeFramePose.Identity;
            return state.Pose;
        }

        if (state.DrawWhenVisible || state.Variant != variant) {
            state.DrawWhenVisible = false;
            state.Variant = variant;
            Start(state, ActionKind.Draw);
            PlayDrawSound(variant);
            Log.Information($"[ScCsgoKnives] deploy started, variant={variant}.");
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
            state.Action = ActionKind.Idle;
            state.Pose = KnifeFramePose.Identity;
            AudioManager.PlaySound("Audio/ScCsgoKnives/knife_slash", 0.85f, (float)s_random.NextDouble() * 0.16f - 0.08f, 0f);
        }
        state.LastPokePhase = pokePhase;

        if (state.Action == ActionKind.Idle) {
            state.Pose = KnifeFramePose.Identity;
            return state.Pose;
        }

        float elapsed = (float)(Time.RealTime - state.StartedAt);
        bool inspect = state.Action == ActionKind.Inspect;
        float duration = BedrockKnifeAnimations.GetDuration(variant, inspect);
        if (elapsed >= duration) {
            state.Action = ActionKind.Idle;
            state.Pose = KnifeFramePose.Identity;
        }
        else {
            state.Pose = BedrockKnifeAnimations.Sample(variant, inspect, Math.Max(0f, elapsed));
        }
        return state.Pose;
    }

    public static KnifeFramePose GetCurrentPose(ComponentFirstPersonModel model) =>
        model != null && s_states.TryGetValue(model, out State state) ? state.Pose : KnifeFramePose.Identity;

    public static bool TriggerInspect(ComponentPlayer player) {
        ComponentFirstPersonModel model = player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (model is null) return false;
        int value = player.ComponentMiner.ActiveBlockValue;
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) return false;

        int variant = Math.Clamp(ScKnifeBlock.GetVariant(value), 0, 2);
        if (!s_states.TryGetValue(model, out State state)) {
            state = new State { Variant = variant };
            s_states.Add(model, state);
        }
        state.Variant = variant;
        Start(state, ActionKind.Inspect);
        Log.Information($"[ScCsgoKnives] inspect started, variant={variant}.");
        if (variant == 2) AudioManager.PlaySound("Audio/ScCsgoKnives/butterfly_inspect", 1f, 0f, 0f);
        return true;
    }

    static void Start(State state, ActionKind action) {
        state.Action = action;
        state.StartedAt = Time.RealTime;
        state.Pose = KnifeFramePose.Identity;
    }

    static void PlayDrawSound(int variant) {
        string sound = variant == 2 ? "Audio/ScCsgoKnives/butterfly_draw" : "Audio/ScCsgoKnives/knife_deploy";
        AudioManager.PlaySound(sound, 1f, 0f, 0f);
    }

    static string GetEditKeyName() => SettingsManager.GetKeyboardMapping("EditItem", false)?.ToString() ?? "G";
}
