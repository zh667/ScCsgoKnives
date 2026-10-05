using System.Runtime.CompilerServices;
using Engine;
namespace Game;

/// <summary>Each weapon's own running speed in a deathmatch (round 5; the user chose "按比例"): the knife runs at this game's
/// own walking speed (3.1 m/s, Database.xml), every other weapon at that times its CS2 m_flMaxSpeed over the knife's 250,
/// scoped included (the vdata pair's second value: the AWP 200 → 100). Grenades read CS2's equipment blocks (245), the
/// Zeus its own (230). The speed of the weapon in hand is set on the player this process moves, every frame while the
/// player fights; outside a life the player's own speed comes back. The values are CS2's as read; CS2 itself was NOT
/// measured running in-game (the attempt on 2026-10-04 read stale positions and was discarded).</summary>
public static class DmMovement {
    /// <summary>CS2's knife speed (weapon_knife m_flMaxSpeed): the reference this game's walking speed stands for.</summary>
    public const float ReferenceUnits = 250;
    static readonly ConditionalWeakTable<ComponentLocomotion, StrongBox<float>> s_own = new();

    /// <summary>CS2's running speed in units for the item <paramref name="value"/> in hand (0: nothing).</summary>
    public static float UnitsFor(int value, bool scoped) {
        int contents = Terrain.ExtractContents(value);
        Block block = contents > 0 && contents < BlocksManager.Blocks.Length ? BlocksManager.Blocks[contents] : null;
        if (block is ScGunBlock && ScGunBlock.HasReliableModel(value)) return UnitsForGun(GunSpec.All[ScGunBlock.GetVariant(value)].Name, scoped);
        if (block is ScKnifeBlock) return DmWeapons.RawEquipment("weapon_knife", "m_flMaxSpeed", ReferenceUnits);
        if (block is ScGrenadeBlock) return DmWeapons.RawEquipment(ScGrenadeBlock.Kind(value) switch {
            1 => "weapon_flashbang", 2 => "weapon_smokegrenade", 3 => "weapon_molotov", 4 => "weapon_incgrenade", 5 => "weapon_decoy", _ => "weapon_hegrenade" }, "m_flMaxSpeed", 245);
        return ReferenceUnits;
    }
    /// <summary>A gun's CS2 running speed: the vdata pair's scoped value while scoped; the Zeus's from its equipment block.</summary>
    public static float UnitsForGun(string gun, bool scoped) {
        float units = DmWeapons.Raw(gun, "m_flMaxSpeed", scoped, 0);
        if (units > 0) return units;
        return gun == "taser" ? DmWeapons.RawEquipment("weapon_taser", "m_flMaxSpeed", 230) : ReferenceUnits;
    }
    public static float Ratio(int value, bool scoped) => Math.Clamp(UnitsFor(value, scoped) / ReferenceUnits, .2f, 1.5f);

    /// <summary>The player's walking speed for the weapon in hand (<paramref name="ratio"/>), or its own again (null). The
    /// player's own speed is taken the first time and given back exactly.</summary>
    public static void Apply(ComponentPlayer player, float? ratio) {
        if (player?.ComponentLocomotion is not { } locomotion) return;
        if (ratio is not { } r) {
            if (s_own.TryGetValue(locomotion, out var own)) { locomotion.WalkSpeed = own.Value; s_own.Remove(locomotion); }
            return;
        }
        float wanted = s_own.GetValue(locomotion, l => new StrongBox<float>(l.WalkSpeed)).Value * r;
        if (MathF.Abs(locomotion.WalkSpeed - wanted) > 1e-4f) locomotion.WalkSpeed = wanted;
    }
}
