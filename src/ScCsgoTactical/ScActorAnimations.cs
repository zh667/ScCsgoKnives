using System.Text;
using Engine;
using Engine.Animation;
using Engine.Graphics;

namespace Game;

// These are native API 1.9.3.1 animation samples baked at build time, not save data.
// Do not run the glTF converter's repeated curve construction on the render thread.
public static class ScActorAnimations {
    public const string MarkerPrefix = "__sc_prebaked_";
    static readonly object sync = new();
    public static void Ensure(Model model) {
        if (model?.Animations is not { Count: 1 } clips) return;
        string role = clips[0].Name switch {
            "__sc_prebaked_ct_v1" => "ct", "__sc_prebaked_t_v1" => "t", _ => null
        };
        if (role == null) return;
        lock (sync) {
            if (!ReferenceEquals(model.Animations, clips)) return;
            // ContentManager owns this shared stream. Never dispose it.
            var stream = ContentManager.GetStream("Animations/ScCsgoTactical/" + role + ".scanim")
                ?? throw new InvalidDataException("CS人物动画缓存缺失，请重新导入完整全量包。");
            var decoded = Read(stream, model.Bones.Select(b => b.Name).ToArray());
            model.ModelData.Animations = decoded;
            model.Animations = decoded;
        }
    }

    public static List<ModelAnimation> Read(Stream stream, IReadOnlyList<string> bones) {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("SCACT001"u8))
            throw new InvalidDataException("Invalid CS actor animation header.");
        int Count(int max) {
            int value = reader.ReadInt32();
            return value >= 0 && value <= max ? value : throw new InvalidDataException("Invalid CS actor animation count.");
        }
        if (Count(4096) != bones.Count) throw new InvalidDataException("CS actor skeleton mismatch.");
        foreach (string bone in bones)
            if (reader.ReadString() != bone) throw new InvalidDataException("CS actor bone order mismatch.");
        int count = Count(512);
        var result = new List<ModelAnimation>(count);
        var names = new HashSet<string>();
        long remainingValues = 10_000_000;
        for (int a = 0; a < count; a++) {
            string name = reader.ReadString();
            float duration = reader.ReadSingle();
            if (!names.Add(name) || !float.IsFinite(duration) || duration < 0)
                throw new InvalidDataException("Invalid CS actor clip.");
            int keys = Count(301);
            if (keys < 1) throw new InvalidDataException("CS actor clip has no keys.");
            var times = new float[keys];
            for (int k = 0; k < keys; k++) {
                times[k] = reader.ReadSingle();
                // The native converter uses duration*i/count, whose final float can
                // round slightly above Duration. Preserve those native bits exactly.
                if (!float.IsFinite(times[k]) || times[k] < 0 || times[k] > duration + Math.Max(1e-6f, duration * 1e-6f) ||
                    k > 0 && times[k] < times[k - 1]) throw new InvalidDataException("Invalid CS actor key time.");
            }
            var clip = new ModelAnimation { Name = name, Duration = duration };
            int channels = Count(12288);
            for (int c = 0; c < channels; c++) {
                int bone = Count(bones.Count - 1), property = reader.ReadByte();
                if (property > 2 || (remainingValues -= keys) < 0) throw new InvalidDataException("Invalid CS actor channel.");
                var sampler = new ModelAnimation.AnimationSampler {
                    KeyTimes = times, Interpolation = ModelAnimation.InterpolationType.Linear
                };
                float Number() {
                    float value = reader.ReadSingle();
                    return float.IsFinite(value) ? value : throw new InvalidDataException("Invalid CS actor sample.");
                }
                if (property == (int)ModelAnimation.AnimationProperty.Rotation) {
                    var values = new Quaternion[keys];
                    for (int k = 0; k < keys; k++) values[k] = new(Number(), Number(), Number(), Number());
                    sampler.Rotations = values;
                } else {
                    var values = new Vector3[keys];
                    for (int k = 0; k < keys; k++) values[k] = new(Number(), Number(), Number());
                    if (property == (int)ModelAnimation.AnimationProperty.Translation) sampler.Translations = values;
                    else sampler.Scales = values;
                }
                clip.Channels.Add(new() { TargetBoneName = bones[bone],
                    Property = (ModelAnimation.AnimationProperty)property, Sampler = sampler });
            }
            result.Add(clip);
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Unexpected CS actor cache data.");
        return result;
    }
}
