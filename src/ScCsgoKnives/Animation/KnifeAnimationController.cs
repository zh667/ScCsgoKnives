using Engine;

namespace Game;

public static class KnifeAnimationController {
    enum ActionKind { Idle, Draw, Inspect }

    sealed class State {
        public int Variant = -1;
        public ActionKind Action;
        public double StartedAt;
        public float LastPokePhase;
    }

    static readonly Dictionary<ComponentFirstPersonModel, State> s_states = [];
    static readonly float[] s_inspectDurations = [4.85f, 6.12f, 4.79f];
    static readonly System.Random s_random = new();

    public static void Apply(ComponentFirstPersonModel model, int itemValue, ref Matrix matrix) {
        int knifeIndex = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        if (Terrain.ExtractContents(itemValue) != knifeIndex) {
            if (s_states.TryGetValue(model, out State oldState)) oldState.Variant = -1;
            return;
        }

        int variant = Math.Clamp(ScKnifeBlock.GetVariant(itemValue), 0, 2);
        if (!s_states.TryGetValue(model, out State state)) {
            state = new State();
            s_states.Add(model, state);
        }

        if (state.Variant != variant) {
            state.Variant = variant;
            Start(state, ActionKind.Draw);
            PlayDrawSound(variant);
        }

        float pokePhase = model.m_pokeAnimationTime;
        if (pokePhase > 0f && state.LastPokePhase <= 0f) {
            state.Action = ActionKind.Idle;
            AudioManager.PlaySound("Audio/ScCsgoKnives/knife_slash", 0.85f, (float)s_random.NextDouble() * 0.16f - 0.08f, 0f);
        }
        state.LastPokePhase = pokePhase;

        float elapsed = (float)(Time.RealTime - state.StartedAt);
        Matrix animation = state.Action switch {
            ActionKind.Draw => DrawTransform(variant, elapsed, state),
            ActionKind.Inspect => InspectTransform(variant, elapsed, state),
            _ => Matrix.Identity
        };
        matrix = animation * matrix;
    }

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
        if (variant == 2) AudioManager.PlaySound("Audio/ScCsgoKnives/butterfly_inspect", 1f, 0f, 0f);
        return true;
    }

    static void Start(State state, ActionKind action) {
        state.Action = action;
        state.StartedAt = Time.RealTime;
    }

    static Matrix DrawTransform(int variant, float elapsed, State state) {
        const float duration = 0.96f;
        float t = MathUtils.Saturate(elapsed / duration);
        if (t >= 1f) {
            state.Action = ActionKind.Idle;
            return Matrix.Identity;
        }

        float e = Smooth(t);
        float remaining = 1f - e;
        float spin = variant switch { 0 => 340f, 1 => 115f, _ => 520f };
        float tilt = variant switch { 0 => -75f, 1 => -35f, _ => 95f };
        return Matrix.CreateRotationZ(MathUtils.DegToRad(spin * remaining))
             * Matrix.CreateRotationX(MathUtils.DegToRad(tilt * remaining))
             * Matrix.CreateTranslation(0.22f * remaining, -0.72f * remaining, 0.28f * remaining);
    }

    static Matrix InspectTransform(int variant, float elapsed, State state) {
        float duration = s_inspectDurations[variant];
        float t = elapsed / duration;
        if (t >= 1f) {
            state.Action = ActionKind.Idle;
            return Matrix.Identity;
        }

        float envelope = MathUtils.Saturate(t / 0.12f) * MathUtils.Saturate((1f - t) / 0.12f);
        float angle;
        Matrix rotation;
        if (variant == 0) {
            angle = 720f * Smooth(t);
            rotation = Matrix.CreateRotationY(MathUtils.DegToRad(angle)) * Matrix.CreateRotationZ(MathUtils.DegToRad(28f * envelope));
        }
        else if (variant == 1) {
            angle = 185f * MathF.Sin(t * MathF.PI);
            rotation = Matrix.CreateRotationY(MathUtils.DegToRad(angle)) * Matrix.CreateRotationX(MathUtils.DegToRad(-34f * envelope));
        }
        else {
            angle = 1080f * Smooth(t);
            rotation = Matrix.CreateRotationX(MathUtils.DegToRad(angle)) * Matrix.CreateRotationZ(MathUtils.DegToRad(42f * envelope));
        }
        return rotation * Matrix.CreateTranslation(-0.16f * envelope, 0.08f * envelope, -0.18f * envelope);
    }

    static float Smooth(float t) {
        t = MathUtils.Saturate(t);
        return t * t * t * (t * (6f * t - 15f) + 10f);
    }

    static void PlayDrawSound(int variant) {
        string sound = variant == 2 ? "Audio/ScCsgoKnives/butterfly_draw" : "Audio/ScCsgoKnives/knife_deploy";
        AudioManager.PlaySound(sound, 1f, 0f, 0f);
    }
}
