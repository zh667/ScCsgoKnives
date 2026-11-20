using GameEntitySystem;
namespace Game;

/// <summary>feedback-1120-160 #2 (player: "尸鬼的全套灵魂盔甲也免疫CS武器的伤害，站着给匪打都没事"). Vanilla clothing absorbs
/// min(attack × ArmorProtection, the piece's remaining sturdiness); a piece with protection 1 and infinite sturdiness
/// (Ghoul 2.0's soul set, clothing 90-93) therefore swallows every hit forever. Ghoul gives its own creatures a separate
/// rule (protection × 0.7); every other attacker, CS weapons included, did nothing at all.
///
/// After the native pass of a CS attack (bullets of players, squads and companions, knives, grenades, C4, squad bombs),
/// when the target wears at least one piece with protection and infinite sturdiness, the injury is at least
/// <see cref="Floor"/> of what the same attack would do without clothing (resilience and attack resilience as the engine
/// applies them). Finite vanilla armour (iron, diamond: protection 1 but it wears out) and unarmoured targets are left
/// exactly as before; a world mode that settles injuries itself (deathmatch) never gets here. The slot a hit lands on is
/// chosen inside the engine, so a mixed outfit (one unbreakable piece) gets the floor on every hit.</summary>
public static class ScUnbreakableArmor {
    /// <summary>The user's choice (2026-11-20): 30 %, Ghoul's own 0.7 reduction for its creatures.</summary>
    public const float Floor = .3f;

    /// <summary><paramref name="target"/> wears a piece that protects and never wears out.</summary>
    public static bool Worn(Entity target) {
        if (target?.FindComponent<ComponentClothing>() is not { } clothing) return false;
        foreach (var slot in new[] { ClothingSlot.Head, ClothingSlot.Torso, ClothingSlot.Legs, ClothingSlot.Feet })
            foreach (int value in clothing.GetClothes(slot))
                if (Unbreakable(BlocksManager.Blocks[Terrain.ExtractContents(value)]?.GetClothingData(value))) return true;
        return false;
    }
    public static bool Unbreakable(ClothingData data) => data is not null && data.ArmorProtection > 0 && !float.IsFinite(data.Sturdiness);

    /// <summary>The injury to use: <paramref name="native"/> (the engine's amount with clothing), raised to the floor when the
    /// target wears unbreakable armour. <paramref name="nativePass"/> is the same native computation, run once more with
    /// clothing switched off (no clothing is touched then, so nothing wears twice).</summary>
    public static float Apply(Attackment attack, float native, Func<float> nativePass) {
        if (attack is null || ScModes.OwnsInjuries(attack.Target) || !attack.EnableArmorProtection || !(native >= 0) || !(attack.AttackPower > 0) || !Worn(attack.Target)) return native;
        float plain;
        attack.EnableArmorProtection = false;
        try { plain = nativePass(); } finally { attack.EnableArmorProtection = true; }
        return float.IsFinite(plain) && plain * Floor > native ? plain * Floor : native;
    }
}
