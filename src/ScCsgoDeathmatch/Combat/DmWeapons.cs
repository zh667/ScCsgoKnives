using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace Game;

/// <summary>The competitive weapon rules (DM-08, design §7): every number is read from this machine's CS2 files
/// (Data/dm_cs2_profile.json, written by tools/cs2_dm_profile.py with the source hashes) under the vdata's own field
/// name; every conversion from a CS2 value to something this game can apply is in this file, each with its status:
///
///   AS READ      damage, head multiplier, armour ratio, range modifier, range, magazine, cycle time, pellets
///   CONVERTED    distances (Source units → blocks by the core table's units per metre), inaccuracy (dimensionless
///                → cone degrees by atan, the reading the core's own table uses)
///   MODELLED     how inaccuracy grows with fire and recovers (CS2: an exponential decay inside its code; here the
///                core's linear bloom fed with CS2's per-shot value, the steady state of CS2's decay as the ceiling and
///                CS2's recovery time), how moving and jumping add to it (the core's own blend between CS2's standing
///                and moving / jumping values)
///   ROUND 5      CS2's seeded spray patterns (DmRecoil: the table from the vdata, the kick's motion measured in CS2),
///                CS2's hitboxes and hit groups (DmHitboxes, DmCombat), each weapon's running speed (DmMovement)
///   ROUND 6      CS2's bullet penetration through blocks and players (DmPenetration; wood easy, as the user chose)
///
/// "Modelled" and "not done" are listed in the evidence table as unverified against CS2; nothing here claims to be a
/// reproduction of CS2's code. Looks, growth and durability never enter: a gun's model alone decides its numbers (C1).</summary>
public static class DmWeapons {
    const string Resource = "dm_cs2_profile.json", ExpectedFormat = "ScCsgoDeathmatch.Cs2Profile/1";
    sealed class File {
        public string Format { get; set; }
        public JsonElement Source { get; set; }
        public Dictionary<string, Dictionary<string, JsonElement>> Guns { get; set; }
        public Dictionary<string, Dictionary<string, JsonElement>> Equipment { get; set; }
    }
    /// <summary>Null when the profile loaded; the reason otherwise (the mode then refuses to start a match).</summary>
    public static string LoadError { get; private set; } = "not loaded";
    /// <summary>CS2's version as the profile's source recorded it ("1.41.8.8").</summary>
    public static string Cs2Version { get; private set; } = "";
    /// <summary>Changes whenever a number both ends must agree on changes: the profile's bytes and the fixed rules.</summary>
    public static string Fingerprint { get; private set; } = "none";
    static readonly File s_file = Load();
    static readonly Dictionary<(int, bool), EffectiveGunStats> s_stats = [];

    static File Load() {
        try {
            Assembly assembly = typeof(DmWeapons).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) { LoadError = "no embedded " + Resource; return null; }
            using var stream = assembly.GetManifestResourceStream(name); using var memory = new MemoryStream(); stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            var file = JsonSerializer.Deserialize<File>(bytes);
            if (file?.Format != ExpectedFormat || file.Guns is null) { LoadError = Resource + " is not " + ExpectedFormat; return null; }
            foreach (var spec in GunSpec.All) if (!file.Guns.ContainsKey(spec.Name)) { LoadError = "the CS2 profile has no " + spec.Name; return null; }
            if (file.Source.ValueKind == JsonValueKind.Object && file.Source.TryGetProperty("steam", out var steam) && steam.TryGetProperty("PatchVersion", out var patch)) Cs2Version = patch.GetString() ?? "";
            string rules = $"|hp{DmFixed.Health}|ap{DmFixed.Armour}|prot{DmFixed.ProtectionSeconds}|assist{DmFixed.AssistDamage}/{DmFixed.AssistSeconds}|g{DmFixed.GrenadesPerLife}/{DmFixed.FlashPerLife}/{DmFixed.OtherGrenadeEach}|schema{DmIds.Schema}|model2"
                + $"|{DmRecoilModel.Version}|hit{DmHitboxes.Fingerprint}|stomach{DmCombat.StomachMultiplier}|move1|pen{DmPenetrationRules.Fingerprint}/{DmPenetrationRules.WoodThicknessScale}";
            Fingerprint = Convert.ToHexString(SHA256.HashData([.. bytes, .. System.Text.Encoding.UTF8.GetBytes(rules)]))[..16].ToLowerInvariant();
            LoadError = null;
            return file;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException) { LoadError = e.GetType().Name + ": " + e.Message; return null; }
    }
    public static bool Ready => LoadError is null;

    static float Number(Dictionary<string, JsonElement> block, string field, int index, float fallback) {
        if (block is null || !block.TryGetValue(field, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.Number) return e.GetSingle();
        if (e.ValueKind is JsonValueKind.True or JsonValueKind.False) return e.ValueKind == JsonValueKind.True ? 1 : 0;   // m_bIsFullAuto and the like
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0) return e[Math.Min(index, e.GetArrayLength() - 1)].GetSingle();
        return fallback;
    }
    /// <summary>A gun's raw CS2 value (pairs: 0 = primary, 1 = alternate), or the fallback when the vdata has none.</summary>
    public static float Raw(string gun, string field, bool alternate = false, float fallback = 0) =>
        s_file?.Guns is not null && s_file.Guns.TryGetValue(gun, out var block) ? Number(block, field, alternate ? 1 : 0, fallback) : fallback;
    public static float RawEquipment(string block, string field, float fallback = 0) =>
        s_file?.Equipment is not null && s_file.Equipment.TryGetValue(block, out var b) ? Number(b, field, 0, fallback) : fallback;

    // ---------------------------------------------------------------- conversions
    /// <summary>The longest shot this game traces, in blocks: CS2's 8192 units are 208 m, more than a loaded world shows.</summary>
    public const float MaxRangeBlocks = 128;
    public static float Blocks(float units) => Cs2Weapons.UnitsPerMetre > 0 ? units / Cs2Weapons.UnitsPerMetre : 0;
    /// <summary>CS2's dimensionless inaccuracy as a cone half-angle in degrees (the core table's reading).</summary>
    public static float Degrees(float inaccuracy) => MathF.Atan(Math.Max(0, inaccuracy)) * (180f / MathF.PI);
    /// <summary>Damage left after a distance in blocks: modifier ^ (units / 500).</summary>
    public static float RangeFactor(string gun, float blocks) => DmCombat.AtDistance(1, Raw(gun, "m_flRangeModifier", false, 1), blocks, Cs2Weapons.UnitsPerMetre, Cs2Weapons.FalloffUnits);

    /// <summary>One handling state of a gun from CS2's values (see the class remarks for what is read and what is modelled).</summary>
    public static ScGunHandling.Mode Handling(GunSpec spec, bool alternate) {
        string g = spec.Name;
        float spread = Raw(g, "m_flSpread", alternate), stand = Raw(g, "m_flInaccuracyStand", alternate), crouch = Raw(g, "m_flInaccuracyCrouch", alternate, stand);
        float move = Raw(g, "m_flInaccuracyMove", alternate), jump = Raw(g, "m_flInaccuracyJump", alternate), fire = Raw(g, "m_flInaccuracyFire", alternate);
        float recovery = Raw(g, "m_flRecoveryTimeStand"), cycle = Math.Max(.01f, Raw(g, "m_flCycleTime", false, spec.CycleSeconds));
        float baseCone = Degrees(spread + stand);
        // CS2 lets a shot's penalty decay to a tenth within the recovery time; at a held trigger the penalty settles at
        // fire / (1 - 10^(-cycle / recovery)). That steady state is the ceiling of the core's linear bloom.
        float keep = recovery > 0 ? MathF.Pow(10, -cycle / recovery) : 0;
        float steady = keep < .999f ? fire / (1 - keep) : fire;
        var survival = ScGunHandling.ForMode(g, alternate) ?? ScGunHandling.ForMode(g, false);
        var core = Cs2Weapons.Get(g);
        return new ScGunHandling.Mode {
            BaseCone = baseCone, CrouchingCone = Math.Min(baseCone, Degrees(spread + crouch)),
            MovingExtra = Math.Max(0, Degrees(spread + stand + move) - baseCone), JumpExtra = Math.Max(0, Degrees(spread + stand + jump) - baseCone),
            BloomPerShot = Math.Max(0, Degrees(spread + stand + fire) - baseCone), BloomMax = Math.Max(0, Degrees(spread + stand + steady) - baseCone),
            BloomRecoverySeconds = recovery > 0 ? recovery : .3f,
            KickPitch = core is null ? survival?.KickPitch ?? 0 : alternate ? core.KickPitchDegreesAlternate : core.KickPitchDegrees,
            KickYaw = core?.KickYawDegrees ?? survival?.KickYaw ?? 0,
            CameraRecoveryT90 = recovery > 0 ? recovery : survival?.CameraRecoveryT90 ?? 0 };
    }

    /// <summary>The numbers a gun shoots with in a match: CS2's, for every player alike. False when the profile is not loaded.</summary>
    public static bool TryStats(GunSpec spec, bool alternate, out EffectiveGunStats stats) {
        stats = default;
        if (!Ready || spec is null) return false;
        int variant = Array.IndexOf(GunSpec.All, spec);
        if (variant < 0) return false;
        lock (s_stats) {
            if (s_stats.TryGetValue((variant, alternate), out stats)) return true;
            string g = spec.Name;
            int pellets = Math.Max(1, (int)Raw(g, "m_nNumBullets", false, 1));
            float cycle = Raw(g, "m_flCycleTime", false, spec.CycleSeconds);
            var mode = Handling(spec, alternate);
            stats = new EffectiveGunStats(Raw(g, "m_nDamage") * pellets, Math.Min(MaxRangeBlocks, Blocks(Raw(g, "m_flRange", false, 8192))),
                ScGunGrowth.Capacity(variant, 0), ScGunDurability.Full(variant), ScGunGrowth.ShotInterval(variant, cycle > 0 ? cycle : spec.CycleSeconds, 0), pellets,
                Raw(g, "m_flHeadshotMultiplier", false, 4), mode, 0, false, 1f, ScGunGrowth.RechargeSeconds(spec, 0), variant) {
                ModeFalloff = blocks => RangeFactor(g, blocks),
                HipHandling = spec.ZoomLevels.Length > 0 ? Handling(spec, false) : null };
            s_stats[(variant, alternate)] = stats;
            return true;
        }
    }

    // ---------------------------------------------------------------- what is not a bullet
    /// <summary>The weapon's armour ratio by its asset name (a gun, "knife_…", "grenade_…"); 2 (armour does nothing) when unknown.</summary>
    public static float ArmourRatio(string weapon) {
        if (string.IsNullOrEmpty(weapon)) return 2;
        if (s_file?.Guns is not null && s_file.Guns.ContainsKey(weapon)) return Raw(weapon, "m_flArmorRatio", false, 2);
        if (weapon.StartsWith("knife", StringComparison.Ordinal)) return RawEquipment("weapon_knife", "m_flArmorRatio", 1.7f);
        return weapon switch {
            "grenade_hegrenade" => RawEquipment("weapon_hegrenade", "m_flArmorRatio", 1.2f),
            "grenade_molotov" => RawEquipment("weapon_molotov", "m_flArmorRatio", 1.8f),
            "grenade_incendiary" => RawEquipment("weapon_incgrenade", "m_flArmorRatio", 1.475f),
            _ => 2 };
    }
    /// <summary>Knife damage. CS2's vdata carries no knife damage (the block's m_nDamage is its prefab's placeholder); the
    /// numbers are Counter-Strike's long-published ones - light 40, light from behind 90, heavy 65, heavy from behind
    /// 180 - and are recorded as NOT read from this CS2 build.</summary>
    public static float KnifeDamage(bool heavy, bool fromBehind) => heavy ? fromBehind ? 180 : 65 : fromBehind ? 90 : 40;
    /// <summary>The cosine past which a strike counts as from behind (the published Counter-Strike value, not read here).</summary>
    public const float BackstabDot = .475f;
    /// <summary>The HE grenade's damage at its centre (CS2 m_nDamage) and its radius in blocks (m_flRange). The falloff
    /// between them is the core's own (linear); CS2's is in its code and was not measured.</summary>
    public static float HeDamage => RawEquipment("weapon_hegrenade", "m_nDamage", 99);
    public static float HeRadiusBlocks => Blocks(RawEquipment("weapon_hegrenade", "m_flRange", 350));
    /// <summary>Fire damage per second (CS2 m_nDamage of the molotov / incendiary block, read as per second: the published
    /// figure; the tick pattern is CS2's code and was not measured).</summary>
    public static float FirePerSecond(string weapon) => RawEquipment(weapon == "grenade_incendiary" ? "weapon_incgrenade" : "weapon_molotov", "m_nDamage", 40);
}
