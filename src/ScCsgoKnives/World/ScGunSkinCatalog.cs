namespace Game;

/// <summary>What a finish costs at the workbench. The tier is the only place a number lives; the
/// workbench and the transaction both read it, so a rebalance is one table edit.</summary>
public enum ScSkinTier { Standard, Premium, Special }

/// <summary>One CS2 weapon finish the mod can apply. Identified by CS2's own paint ID, which is stable
/// across CS2 updates and is what the world record stores; the string key is for assets and logs only.</summary>
public sealed record ScGunSkin(int PaintId, string Key, string Name, string Gun, ScSkinTier Tier, bool Approximate) {
    /// <summary>Texture stem for this finish on its gun: <c>ak47_hd__cu_fireserpent_ak47_bravo</c>.</summary>
    public string Material => $"{Gun}_hd__{Key}";
    public string Icon => $"{Gun}_slot__{Key}";
}

/// <summary>The approved finishes (audit of 2026-09-08, manifest sha256 8857dbe2...). Paint IDs, keys and
/// names come from CS2's own item schema; the tiers are the plan's opening balance.
///
/// Skins are a record field, never a gun variant: <see cref="GunSpec"/>'s 35 models and the v5 item layout
/// are unchanged, and a world that has never seen a finish stores nothing new.
///
/// <c>Approximate</c> retains the catalogue's historical quality classification. Legacy finishes now
/// use their native geometry and UVs, but this is still not Valve's wear/pearlescence compositor.</summary>
public static class ScGunSkinCatalog {
    /// <summary>No finish. Stored as 0, which is what every schema-1 record converts to.</summary>
    public const int None = 0;

    public static readonly ScGunSkin[] All = [
        new(51,   "am_lightning_awp",          "雷击",     "awp",   ScSkinTier.Standard, false),
        new(756,  "gs_awp_gungnir",            "永恒之枪", "awp",   ScSkinTier.Special,  true),
        new(344,  "cu_medieval_dragon_awp",    "巨龙传说", "awp",   ScSkinTier.Premium,  true),
        new(724,  "cu_ak_island_floral",       "野荷",     "ak47",  ScSkinTier.Premium,  true),
        new(180,  "cu_fireserpent_ak47_bravo", "火蛇",     "ak47",  ScSkinTier.Standard, true),
        new(456,  "am_bamboo_jungle",          "水栽竹",   "ak47",  ScSkinTier.Standard, false),
        new(302,  "cu_ak47_rubber",            "火神",     "ak47",  ScSkinTier.Standard, true),
        new(984,  "cu_m4a1s_printstream",      "印花集",   "m4a1s", ScSkinTier.Premium,  true),
        new(946,  "cu_m4a1s_csgo2048",         "二号玩家", "m4a1s", ScSkinTier.Standard, true),
        new(1177, "aa_fade_m4a1s",             "渐变之色", "m4a1s", ScSkinTier.Special,  true),
        new(497,  "gs_m4a1s_snakebite_gold",   "金蛇缠绕", "m4a1s", ScSkinTier.Standard, true),
    ];

    /// <summary>Material cost by tier: blanks, mechanisms, paint. Indexes are ScWeaponMaterialBlock kinds.</summary>
    public static readonly Dictionary<ScSkinTier, (int Blank, int Mechanism, int Paint)> Cost = new() {
        [ScSkinTier.Standard] = (2, 1, 1),
        [ScSkinTier.Premium] = (4, 2, 2),
        [ScSkinTier.Special] = (6, 3, 3),
    };
    /// <summary>Stripping a finish costs half a Standard application, rounded up, whatever the finish was.</summary>
    public static (int Blank, int Mechanism, int Paint) RemovalCost {
        get { var s = Cost[ScSkinTier.Standard]; return ((s.Blank + 1) / 2, (s.Mechanism + 1) / 2, (s.Paint + 1) / 2); }
    }

    static readonly Dictionary<int, ScGunSkin> s_byId = All.ToDictionary(s => s.PaintId);

    public static ScGunSkin Find(int paintId) => paintId != None && s_byId.TryGetValue(paintId, out var skin) ? skin : null;
    /// <summary>A paint ID this build can render: 0 (none) or one of the catalogue. Anything else is a
    /// record from a version that knew more finishes and must not be guessed at.</summary>
    public static bool IsKnown(int paintId) => paintId == None || s_byId.ContainsKey(paintId);
    /// <summary>The finishes offered for a gun variant, in catalogue order.</summary>
    public static IEnumerable<ScGunSkin> For(int variant) {
        string gun = variant >= 0 && variant < GunSpec.All.Length ? GunSpec.All[variant].Name : null;
        return gun is null ? [] : All.Where(s => s.Gun == gun);
    }
    public static bool Fits(ScGunSkin skin, int variant) =>
        skin is not null && variant >= 0 && variant < GunSpec.All.Length && GunSpec.All[variant].Name == skin.Gun;
    public static string NameOf(int paintId) => Find(paintId)?.Name ?? "原厂外观";

    /// <summary>Cost of moving this gun from one finish to another, as item value → count. Re-applying the
    /// finish a gun already wears is free and refused earlier; there is no partial charge.</summary>
    public static Dictionary<int, int> CostOf(ScGunSkin skin, Func<int, int> materialValue) {
        var (blank, mechanism, paint) = skin is null ? RemovalCost : Cost[skin.Tier];
        var cost = new Dictionary<int, int>();
        if (blank > 0) cost[materialValue(ScWeaponMaterialBlock.Blank)] = blank;
        if (mechanism > 0) cost[materialValue(ScWeaponMaterialBlock.Mechanism)] = mechanism;
        if (paint > 0) cost[materialValue(ScWeaponMaterialBlock.Paint)] = paint;
        return cost;
    }

    /// <summary>The colour/ORM/normal stem a draw call should sample for this gun and finish, and the icon.
    /// Falls back to the factory material when the finish is unknown or does not belong to this gun, so a
    /// record from a newer build draws the plain gun instead of nothing.</summary>
    public static string Material(string asset, int paintId) {
        var skin = Find(paintId);
        return skin is not null && skin.Gun == asset ? skin.Material : $"{asset}_hd";
    }
    public static string Icon(string asset, int paintId) {
        var skin = Find(paintId);
        return skin is not null && skin.Gun == asset ? skin.Icon : $"{asset}_slot";
    }
}
