using System.Security.Cryptography;
using System.Text.Json;
namespace Game;

/// <summary>CS2's bullet penetration in a deathmatch (round 6; the user chose "按方块材质+厚度", then: "木制的都设为容易穿吧",
/// "子弹打穿一个人后，还要继续伤到后面的人"). The core walks the round (ScBulletPenetration); this prices each crossing.
///
///   AS READ      each surface's bulletPenetrationDistanceModifier (Data/dm_cs2_surfaces.json from CS2's
///                scripts/surfaceproperties_game.txt, tools/cs2_dm_surfaces.py), each gun's m_flPenetration
///   AS PUBLISHED the CS:GO SDK's HandleBulletPenetration: 16% of the present damage (5% entering glass), plus 3 / power ×
///                1.25 × 3 / m, plus thickness² / 24 / m, where m is the mean of the entry and exit surfaces' modifiers - 3
///                for glass, and for wood or cardboard entered and left; at most 4 crossings, 90 units at most, a round
///                left with less than 1 stops
///   MEASURED     CS2 1.41.8.8 (2026-10-04, aim_map, an AK-47 through a Wood_Crate's edge, sv_showimpacts_penetration):
///                5.5 / 13.1 / 28.0 / 42.0 cm lost 7.58 / ~8.5 / 9.53 / 11.4, the formula 7.67 / 7.97 / 9.29 / 11.40
///   THE USER'S   wood is easy to cross: a crossing entered and left at wood counts a tenth of its thickness (a whole block
///                then crosses as 3.9 units, a CS2 wooden door's thickness); everything else counts its real thickness
///                (one block = 39.37 units: a stone, earth or iron block stops a round, thin blocks do not)
///   MAPPED       a block's surface: by its class (ice, gravel, sand, clay, carpet, iron and copper blocks, glass, bricks)
///                else by its sound material (Wood, Stone → concrete, Metal, Glass, Dirt, Sand, Snow, Soft → carpet);
///                a body crosses as CS2's flesh; anything else as default
/// </summary>
public static class DmPenetrationRules {
    const string Resource = "dm_cs2_surfaces.json", ExpectedFormat = "ScCsgoDeathmatch.Cs2Surfaces/1";
    public const int MaxCrossings = 4;
    public const float MaxThicknessUnits = 90, UnitsPerMetre = 39.37008f, WoodThicknessScale = .1f, MinDamageLeft = 1;
    public const float DamageLostPercent = .16f, GlassDamageLostPercent = .05f;
    static readonly Dictionary<string, float> s_distance = new(StringComparer.Ordinal);
    public static string LoadError { get; private set; } = "not loaded";
    public static bool Ready => LoadError is null;
    public static string Fingerprint { get; private set; } = "none";

    static DmPenetrationRules() {
        try {
            var assembly = typeof(DmPenetrationRules).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) { LoadError = "no embedded " + Resource; return; }
            using var stream = assembly.GetManifestResourceStream(name); using var memory = new MemoryStream(); stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.GetProperty("Format").GetString() != ExpectedFormat) { LoadError = Resource + " is not " + ExpectedFormat; return; }
            foreach (var s in doc.RootElement.GetProperty("Surfaces").EnumerateObject()) s_distance[s.Name] = s.Value.GetProperty("distance").GetSingle();
            foreach (string need in new[] { "default", "Wood", "glass", "concrete", "metal", "flesh" }) if (!s_distance.ContainsKey(need)) { LoadError = Resource + " lacks " + need; return; }
            Fingerprint = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
            LoadError = null;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or KeyNotFoundException) { LoadError = e.GetType().Name + ": " + e.Message; }
    }
    public static float DistanceModifier(string surface) => s_distance.TryGetValue(surface ?? "default", out float m) ? m : s_distance.GetValueOrDefault("default", .5f);

    /// <summary>The CS2 surface a block crosses as, from its class name and its sound material (see the class remarks).</summary>
    public static string SurfaceFor(string className, string soundMaterial) {
        switch (className) {
            case "IceBlock": return "ice";
            case "GravelBlock": return "gravel";
            case "SandBlock": return "sand";
            case "ClayBlock": return "clay";
            case "CarpetBlock": return "carpet";
            case "IronBlock" or "CopperBlock": return "solidmetal";
            case "CactusBlock" or "PumpkinBlock" or "RottenPumpkinBlock": return "watermelon";
        }
        if (className is not null && className.Contains("Brick", StringComparison.Ordinal)) return "brick";
        return soundMaterial switch {
            "Wood" => "Wood", "Stone" => "concrete", "Metal" => "metal", "Glass" => "glass", "Dirt" => "dirt", "Sand" => "sand",
            "Snow" => "snow", "Soft" => "carpet", "Leaves" or "Plant" => "foliage", _ => "default" };
    }
    /// <summary>The surface of a block value in the running game (-1: a body, CS2's flesh).</summary>
    public static string SurfaceOf(int value) {
        if (value < 0) return "flesh";
        int contents = Terrain.ExtractContents(value);
        if (contents <= 0 || contents >= BlocksManager.Blocks.Length || BlocksManager.Blocks[contents] is not { } block) return "default";
        string sound;
        try { sound = block.GetSoundMaterialName(null, value); } catch (Exception) { sound = block.DefaultSoundMaterialName; }
        return SurfaceFor(block.GetType().Name, sound);
    }
    static bool Wood(string s) => s is "Wood" or "Wood_Box" or "Wood_Basket" or "Wood_Crate" or "Wood_Plank" or "Wood_Solid" or "Wood_Dense";

    /// <summary>CS2's own loss (the CS:GO SDK formula on CS2's surfaces): the damage a round loses crossing <paramref name="units"/>
    /// of an obstacle entered at <paramref name="entry"/> and left at <paramref name="exit"/>; null: it stops.</summary>
    public static float? Cs2Loss(float penetrationPower, string entry, string exit, float units, float damage) {
        if (!(penetrationPower > 0) || units > MaxThicknessUnits || !(damage > 0)) return null;
        float combined, lostPercent = DamageLostPercent;
        if (entry is "glass" or "glassfloor" or "metalgrate" or "chainlink") { combined = 3; lostPercent = GlassDamageLostPercent; }
        else combined = (DistanceModifier(entry) + DistanceModifier(exit)) * .5f;
        if (entry == exit && (Wood(entry) || entry == "cardboard")) combined = 3;
        else if (Wood(entry) && Wood(exit)) combined = 3;
        float penMod = Math.Max(0, 1 / combined);
        float lost = damage * lostPercent + Math.Max(0, 3 / penetrationPower * 1.25f) * (penMod * 3) + penMod * units * units / 24;
        return damage - lost < MinDamageLeft ? null : lost;
    }
    /// <summary>The deathmatch's loss: CS2's, with a crossing entered and left at wood counted at a tenth of its thickness.</summary>
    public static float? Loss(string gun, string entry, string exit, float thicknessMetres, float damage) {
        float units = thicknessMetres * UnitsPerMetre;
        if (Wood(entry) && Wood(exit)) units *= WoodThicknessScale;
        return Cs2Loss(DmWeapons.Raw(gun, "m_flPenetration", false, 1), entry, exit, units, damage);
    }
}

/// <summary>The deathmatch's penetration for the core (ScMode.Penetration).</summary>
public sealed class DmPenetration : IScPenetration {
    public int MaxCrossings => DmPenetrationRules.MaxCrossings;
    public float MaxThicknessMetres => DmPenetrationRules.MaxThicknessUnits / DmPenetrationRules.UnitsPerMetre;
    public float? Loss(GunSpec spec, int entryValue, int exitValue, float thicknessMetres, float damage) =>
        spec is null ? null : DmPenetrationRules.Loss(spec.Name, DmPenetrationRules.SurfaceOf(entryValue), DmPenetrationRules.SurfaceOf(exitValue), thicknessMetres, damage);
}
