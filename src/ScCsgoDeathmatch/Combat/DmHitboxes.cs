using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Engine;
namespace Game;

/// <summary>CS2's player hitboxes in a deathmatch (round 5; the user chose "CS2 命中盒"): the "cstrike" capsules of CS2's
/// agent models (Data/dm_cs2_hitboxes.json, written by tools/cs2_dm_hitboxes.py with the source hashes), each with CS2's
/// own hit group - head, neck, chest, stomach, arms, legs. A shot is tested against exactly these; a ray between them
/// (between the legs, past the neck) misses, as in CS2.
///
///   AS READ      every capsule's two centres in its bone, radius and group; the CT and T agents carry the same set
///   MODELLED     the pose: CS2 poses the capsules with the animation a player plays, which is not in these files. The
///                standing pose is fitted to CS2's own hit groups measured down a bot (the tool's notes: 24 of 26 rows
///                agree); the arms hold a rifle; the head stays centred. Placed on this game's body: feet at the body's
///                position, turned with its yaw, CS2's standing eye (64.06 units) on this game's standing eye (0.875 of the
///                body's height, ComponentHumanModel), heights scaled with the eye's present height (this game's crouch:
///                the eye at 45%, CS2's at 72%). Lying down: no capsules (the core's own regions).
/// Placed once per frame and body placement.</summary>
public static class DmHitboxes {
    const string Resource = "dm_cs2_hitboxes.json", ExpectedFormat = "ScCsgoDeathmatch.Cs2Hitboxes/1";
    public readonly record struct Capsule(string Name, int Group, ScHitPart Part, Vector3 A, Vector3 B, float Radius);
    public static string LoadError { get; private set; } = "not loaded";
    public static bool Ready => LoadError is null;
    public static string Fingerprint { get; private set; } = "none";
    /// <summary>CS2's standing eye height, units (the file's EyeStanding).</summary>
    public static float EyeStanding { get; private set; } = 64.0626f;
    public static IReadOnlyList<Capsule> Standing { get; private set; } = [];
    /// <summary>This game's standing eye as a share of the body's height (ComponentHumanModel.CalculateEyePosition).</summary>
    public const float StandingEyeShare = .875f, LyingEyeShare = .3f;

    /// <summary>CS's hit groups on this mod's regions: 1 head, 2 chest, 3 stomach, 4/5 arms, 6/7 legs, 8 neck (CS2).</summary>
    public static ScHitPart PartOf(int group) => group switch {
        1 => ScHitPart.Head, 2 => ScHitPart.Body, 3 => ScHitPart.Stomach, 4 or 5 => ScHitPart.Arm, 6 or 7 => ScHitPart.Leg, 8 => ScHitPart.Neck, _ => ScHitPart.Body };

    static DmHitboxes() {
        try {
            var assembly = typeof(DmHitboxes).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) { LoadError = "no embedded " + Resource; return; }
            using var stream = assembly.GetManifestResourceStream(name); using var memory = new MemoryStream(); stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Format", out var format) || format.GetString() != ExpectedFormat) { LoadError = Resource + " is not " + ExpectedFormat; return; }
            EyeStanding = root.GetProperty("EyeStanding").GetSingle();
            Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
            var list = new List<Capsule>();
            foreach (var c in root.GetProperty("Standing").EnumerateArray()) {
                int group = c.GetProperty("group").GetInt32();
                list.Add(new(c.GetProperty("name").GetString(), group, PartOf(group), V(c.GetProperty("a")), V(c.GetProperty("b")), c.GetProperty("r").GetSingle()));
            }
            if (!(EyeStanding > 1) || !list.Any(c => c.Part == ScHitPart.Head) || !list.Any(c => c.Part == ScHitPart.Neck) || !list.Any(c => c.Part == ScHitPart.Stomach)
                || !list.Any(c => c.Part == ScHitPart.Leg) || !list.Any(c => c.Part == ScHitPart.Arm) || !list.Any(c => c.Part == ScHitPart.Body)) { LoadError = Resource + " lacks a hit group"; return; }
            Standing = list;
            Fingerprint = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
            LoadError = null;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or KeyNotFoundException or FormatException) { LoadError = e.GetType().Name + ": " + e.Message; }
    }

    /// <summary>The capsules of a body standing at <paramref name="feet"/>, facing <paramref name="forward"/> (horizontal),
    /// <paramref name="height"/> tall, its eye at <paramref name="eyeHeight"/> above the feet; null while lying down.</summary>
    public static ScHitCapsule[] Place(Vector3 feet, Vector3 forward, float height, float eyeHeight) {
        if (!Ready || !(height > .2f) || !float.IsFinite(feet.X + feet.Y + feet.Z + eyeHeight) || eyeHeight < LyingEyeShare * height) return null;
        forward = new Vector3(forward.X, 0, forward.Z);
        if (!(forward.LengthSquared() > 1e-8f)) return null;
        forward = Vector3.Normalize(forward);
        Vector3 left = Vector3.Cross(Vector3.UnitY, forward);
        float standing = StandingEyeShare * height, scale = standing / EyeStanding, crouch = Math.Clamp(eyeHeight / standing, LyingEyeShare, 1.05f);
        Vector3 At(Vector3 p) => feet + forward * (p.X * scale) + left * (p.Y * scale) + Vector3.UnitY * (p.Z * scale * crouch);
        var result = new ScHitCapsule[Standing.Count];
        for (int i = 0; i < result.Length; i++) { var c = Standing[i]; result[i] = new(At(c.A), At(c.B), c.Radius * scale, c.Part); }
        return result;
    }

    sealed class Cache { public int Frame = -1; public Matrix Body; public Vector3 Eye; public ScHitCapsule[] Capsules; }
    static readonly ConditionalWeakTable<ComponentBody, Cache> s_cache = new();
    /// <summary>A player's capsules now: the body's placement and the eye its model computes (crouch and lying included).</summary>
    public static ScHitCapsule[] For(ComponentBody body) {
        if (body?.Entity?.FindComponent<ComponentCreatureModel>() is not { } model) return null;
        Vector3 eye = model.EyePosition;
        var cache = s_cache.GetOrCreateValue(body);
        if (cache.Frame == Time.FrameIndex && cache.Body == body.Matrix && cache.Eye == eye) return cache.Capsules;
        cache.Frame = Time.FrameIndex; cache.Body = body.Matrix; cache.Eye = eye;
        return cache.Capsules = Place(body.Position, body.Rotation.GetForwardVector(), body.BoxSize.Y, eye.Y - body.Position.Y);
    }
}
