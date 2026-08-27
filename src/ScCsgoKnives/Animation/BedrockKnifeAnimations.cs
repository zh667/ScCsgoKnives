using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Engine;

namespace Game;

public readonly struct KnifeFramePose {
    public readonly Matrix Weapon;
    public readonly Matrix LeftHand;
    public readonly Matrix RightHand;
    public readonly Matrix ButterflyDown;
    public readonly Matrix ButterflyUp;
    public readonly Matrix ButterflyBlade;

    public KnifeFramePose(Matrix weapon, Matrix leftHand, Matrix rightHand, Matrix down, Matrix up, Matrix blade) {
        Weapon = weapon;
        LeftHand = leftHand;
        RightHand = rightHand;
        ButterflyDown = down;
        ButterflyUp = up;
        ButterflyBlade = blade;
    }

    public static KnifeFramePose Identity => new(
        Matrix.Identity, Matrix.Identity, Matrix.Identity,
        Matrix.Identity, Matrix.Identity, Matrix.Identity
    );
}

/// <summary>
/// Reads the original TaCZ/Bedrock keyframes bundled with the permitted knife
/// port. Unlike the previous whole-item approximation, this preserves the
/// independent weapon, left hand, right hand and butterfly-part channels.
/// </summary>
public static class BedrockKnifeAnimations {
    sealed class KeyTrack {
        public readonly float[] Times;
        public readonly Vector3[] Values;

        public KeyTrack(float[] times, Vector3[] values) {
            Times = times;
            Values = values;
        }

        public Vector3 Sample(float time, Vector3 fallback) {
            if (Times.Length == 0) return fallback;
            if (Times.Length == 1 || time <= Times[0]) return Values[0];
            int last = Times.Length - 1;
            if (time >= Times[last]) return Values[last];
            int hi = Array.BinarySearch(Times, time);
            if (hi >= 0) return Values[hi];
            hi = ~hi;
            int lo = hi - 1;
            float f = (time - Times[lo]) / Math.Max(0.0001f, Times[hi] - Times[lo]);
            return Vector3.Lerp(Values[lo], Values[hi], f);
        }
    }

    sealed class BoneTrack {
        public KeyTrack Position;
        public KeyTrack Rotation;
        public KeyTrack Scale;

        public BonePose Sample(float time) => new(
            Position?.Sample(time, Vector3.Zero) ?? Vector3.Zero,
            Rotation?.Sample(time, Vector3.Zero) ?? Vector3.Zero,
            Scale?.Sample(time, Vector3.One) ?? Vector3.One
        );
    }

    readonly struct BonePose {
        public readonly Vector3 Position;
        public readonly Vector3 Rotation;
        public readonly Vector3 Scale;

        public BonePose(Vector3 position, Vector3 rotation, Vector3 scale) {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    sealed class Clip {
        public float Duration;
        public readonly Dictionary<string, BoneTrack> Bones = new(StringComparer.OrdinalIgnoreCase);

        public BonePose Sample(string bone, float time) =>
            Bones.TryGetValue(bone, out BoneTrack track) ? track.Sample(time) : new BonePose(Vector3.Zero, Vector3.Zero, Vector3.One);
    }

    static readonly string[] s_names = ["karambit", "m9", "butterfly"];
    static readonly string[] s_weaponBones = ["karambit", "m9", "butterfly"];
    static readonly Clip[,] s_clips = new Clip[3, 2];

    static BedrockKnifeAnimations() {
        for (int variant = 0; variant < s_names.Length; variant++) {
            using JsonDocument document = LoadDocument(s_names[variant]);
            JsonElement animations = document.RootElement.GetProperty("animations");
            s_clips[variant, 0] = ParseClip(animations.GetProperty(variant == 2 ? "draw_1" : "draw"));
            s_clips[variant, 1] = ParseClip(animations.GetProperty("inspect"));
        }
    }

    public static float GetDuration(int variant, bool inspect) => s_clips[Math.Clamp(variant, 0, 2), inspect ? 1 : 0].Duration;

    public static KnifeFramePose Sample(int variant, bool inspect, float time) {
        variant = Math.Clamp(variant, 0, 2);
        Clip clip = s_clips[variant, inspect ? 1 : 0];
        Clip rest = s_clips[variant, 0];
        float restTime = rest.Duration;

        BonePose parent = clip.Sample("knife_and_right", time);
        BonePose parentRest = rest.Sample("knife_and_right", restTime);
        BonePose weapon = clip.Sample(s_weaponBones[variant], time);
        BonePose weaponRest = rest.Sample(s_weaponBones[variant], restTime);
        Matrix weaponDelta = CreateDelta(weapon, weaponRest, 0.014f) * CreateDelta(parent, parentRest, 0.014f);

        BonePose left = clip.Sample("lefthand", time);
        BonePose leftRest = rest.Sample("lefthand", restTime);
        BonePose right = clip.Sample("righthand", time);
        BonePose rightRest = rest.Sample("righthand", restTime);
        Matrix leftDelta = CreateDelta(left, leftRest, 0.0105f);
        Matrix rightDelta = CreateDelta(right, rightRest, 0.0105f) * CreateDelta(parent, parentRest, 0.0105f);

        Matrix down = Matrix.Identity;
        Matrix up = Matrix.Identity;
        Matrix blade = Matrix.Identity;
        if (variant == 2) {
            down = CreatePivotRotationDelta(clip.Sample("down", time), rest.Sample("down", restTime), new Vector3(0.014882f, -0.003444f, -0.067859f));
            up = CreatePivotRotationDelta(clip.Sample("up", time), rest.Sample("up", restTime), new Vector3(-0.014881f, -0.107612f, -0.008334f));
            blade = CreatePivotRotationDelta(clip.Sample("blade2", time), rest.Sample("blade2", restTime), new Vector3(-0.007441f, -0.055527f, -0.038098f));
        }
        return new KnifeFramePose(weaponDelta, leftDelta, rightDelta, down, up, blade);
    }

    static Matrix CreateDelta(BonePose pose, BonePose rest, float positionScale) {
        Matrix currentRotation = Rotation(pose.Rotation);
        Matrix restRotation = Rotation(rest.Rotation);
        Vector3 d = pose.Position - rest.Position;
        Vector3 translation = new(d.X * positionScale, -d.Y * positionScale, d.Z * positionScale);
        return currentRotation * Matrix.Invert(restRotation) * Matrix.CreateTranslation(translation);
    }

    static Matrix CreatePivotRotationDelta(BonePose pose, BonePose rest, Vector3 pivot) {
        Matrix rotation = Rotation(pose.Rotation) * Matrix.Invert(Rotation(rest.Rotation));
        return Matrix.CreateTranslation(-pivot) * rotation * Matrix.CreateTranslation(pivot);
    }

    static Matrix Rotation(Vector3 degrees) =>
        Matrix.CreateRotationX(MathUtils.DegToRad(degrees.X))
        * Matrix.CreateRotationY(MathUtils.DegToRad(degrees.Y))
        * Matrix.CreateRotationZ(MathUtils.DegToRad(degrees.Z));

    static JsonDocument LoadDocument(string name) {
        Assembly assembly = typeof(BedrockKnifeAnimations).Assembly;
        string suffix = $"AnimationData.{name}.animation.json";
        string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resource is null) throw new InvalidOperationException($"Missing embedded animation resource {suffix}.");
        using Stream stream = assembly.GetManifestResourceStream(resource);
        return JsonDocument.Parse(stream);
    }

    static Clip ParseClip(JsonElement element) {
        Clip clip = new() {
            Duration = element.TryGetProperty("animation_length", out JsonElement length) && length.ValueKind == JsonValueKind.Number
                ? length.GetSingle()
                : 0f
        };
        if (!element.TryGetProperty("bones", out JsonElement bones)) return clip;
        foreach (JsonProperty boneProperty in bones.EnumerateObject()) {
            BoneTrack bone = new();
            JsonElement value = boneProperty.Value;
            if (value.TryGetProperty("position", out JsonElement position)) bone.Position = ParseTrack(position);
            if (value.TryGetProperty("rotation", out JsonElement rotation)) bone.Rotation = ParseTrack(rotation);
            if (value.TryGetProperty("scale", out JsonElement scale)) bone.Scale = ParseTrack(scale);
            clip.Bones[boneProperty.Name] = bone;
        }
        return clip;
    }

    static KeyTrack ParseTrack(JsonElement element) {
        if (element.ValueKind == JsonValueKind.Array)
            return new KeyTrack([0f], [ReadVector(element)]);
        if (element.ValueKind != JsonValueKind.Object)
            return new KeyTrack([], []);

        List<(float Time, Vector3 Value)> keys = [];
        foreach (JsonProperty property in element.EnumerateObject()) {
            if (!float.TryParse(property.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out float time)) continue;
            JsonElement value = property.Value;
            if (value.ValueKind == JsonValueKind.Object) {
                if (value.TryGetProperty("post", out JsonElement post)) value = post;
                else if (value.TryGetProperty("pre", out JsonElement pre)) value = pre;
            }
            if (value.ValueKind == JsonValueKind.Array) keys.Add((time, ReadVector(value)));
        }
        keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        return new KeyTrack(keys.Select(k => k.Time).ToArray(), keys.Select(k => k.Value).ToArray());
    }

    static Vector3 ReadVector(JsonElement element) {
        float[] values = element.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetSingle() : 0f).ToArray();
        return new Vector3(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1), values.ElementAtOrDefault(2));
    }
}
