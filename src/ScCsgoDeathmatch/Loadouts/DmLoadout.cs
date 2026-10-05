using System.Text.Json;
using System.Text.Json.Serialization;
namespace Game;

/// <summary>One gun of a loadout: the model and how it looks. Looks never change what the gun does (DM-02, C1).</summary>
public sealed record DmGunChoice(int Variant, int SkinId = 0, bool Counter = false, bool SilencerOff = false);
/// <summary>The knife of a loadout: the model and its finish (ScKnifeSkinCatalog).</summary>
public sealed record DmKnifeChoice(int Variant, int Finish = 0);

/// <summary>What a player wants to carry (DM-02, DM-03). Any part may be absent: nothing is filled in for the player.
/// Throwables are kinds (ScGrenadeBlock), one entry each.</summary>
public sealed record DmLoadout {
    public DmGunChoice Primary { get; init; }
    public DmGunChoice Secondary { get; init; }
    public DmKnifeChoice Knife { get; init; }
    public DmGunChoice Zeus { get; init; }
    public IReadOnlyList<int> Grenades { get; init; } = [];
    public static readonly DmLoadout Empty = new();
    [JsonIgnore] public bool IsEmpty => Primary is null && Secondary is null && Knife is null && Zeus is null && Grenades.Count == 0;
    /// <summary>The guns that need a record from the armoury, with the hotbar slot each goes to.</summary>
    public IEnumerable<(int Slot, DmGunChoice Gun)> Guns() {
        if (Primary is not null) yield return (DmSlots.Primary, Primary);
        if (Secondary is not null) yield return (DmSlots.Secondary, Secondary);
        if (Zeus is not null) yield return (DmSlots.Zeus, Zeus);
    }
    public bool SameAs(DmLoadout other) => other is not null && Equals(Primary, other.Primary) && Equals(Secondary, other.Secondary) && Equals(Knife, other.Knife)
        && Equals(Zeus, other.Zeus) && Grenades.OrderBy(g => g).SequenceEqual(other.Grenades.OrderBy(g => g));
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    public string Encode() => JsonSerializer.Serialize(this, s_json);
    public static bool TryDecode(string text, out DmLoadout loadout) {
        loadout = Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;
        try { loadout = JsonSerializer.Deserialize<DmLoadout>(text, s_json) ?? Empty; if (loadout.Grenades is null) loadout = loadout with { Grenades = [] }; return true; }
        catch (JsonException) { loadout = Empty; return false; }
    }
}

/// <summary>Where each kind of equipment sits in the hotbar. The storage is fixed so a key always means the same thing;
/// what the player is shown (DM-07) is only what is actually there.</summary>
public static class DmSlots {
    public const int Primary = 0, Secondary = 1, Knife = 2, Zeus = 3, FirstGrenade = 4, Count = 10;
    /// <summary>The slot of a throwable kind (0-5: one slot a kind).</summary>
    public static int Grenade(int kind) => FirstGrenade + kind;
}

public enum DmLoadoutError { None, UnknownGun, WrongClass, UnknownSkin, SkinDoesNotFit, UnknownKnife, UnknownFinish, GrenadesOff, UnknownGrenade, TooManyGrenades, TooManyOfKind }

/// <summary>What can be chosen (L1): every gun, knife and finish this build has, free; the classes the wheel shows.</summary>
public static class DmCatalogue {
    public enum Group { Pistols, Smgs, Rifles, Heavy, Knives, Gear }
    public static readonly (Group Group, string Name)[] Groups = [(Group.Pistols, "手枪"), (Group.Smgs, "冲锋枪"), (Group.Rifles, "步枪与狙击"), (Group.Heavy, "霰弹与机枪"), (Group.Knives, "刀具"), (Group.Gear, "装备")];
    public static ScGunDurability.Class ClassOf(int variant) => ScGunDurability.ClassOf(GunSpec.All[variant].Name);
    public static bool ValidGun(int variant) => variant >= 0 && variant < GunSpec.All.Length;
    public static bool IsZeus(int variant) => ValidGun(variant) && ClassOf(variant) == ScGunDurability.Class.Taser;
    public static bool IsSecondary(int variant) => ValidGun(variant) && ClassOf(variant) == ScGunDurability.Class.Pistol;
    public static bool IsPrimary(int variant) => ValidGun(variant) && !IsZeus(variant) && !IsSecondary(variant);
    public static Group GroupOf(int variant) => ClassOf(variant) switch {
        ScGunDurability.Class.Pistol => Group.Pistols, ScGunDurability.Class.Smg => Group.Smgs,
        ScGunDurability.Class.Rifle or ScGunDurability.Class.BoltSniper or ScGunDurability.Class.AutoSniper => Group.Rifles,
        ScGunDurability.Class.Taser => Group.Gear, _ => Group.Heavy };
    /// <summary>The guns of a wheel group, in catalogue order.</summary>
    public static IEnumerable<int> GunsOf(Group group) => Enumerable.Range(0, GunSpec.All.Length).Where(v => GroupOf(v) == group);
    public static int KnifeCount => CsmcKnifeRig.KnifeCount;
    /// <summary>The finishes a knife can wear: none, and its one CS2 finish when it has one.</summary>
    public static IEnumerable<int> KnifeFinishes(int variant) {
        yield return ScKnifeSkinCatalog.None;
        if (ScKnifeSkinCatalog.Finish(variant) is not null) yield return ScKnifeSkinCatalog.ForVariant(variant);
    }
    public const int GrenadeKinds = 6;
    public const int FlashKind = 1;

    static DmLoadoutError Gun(DmGunChoice gun, Func<int, bool> fits) {
        if (gun is null) return DmLoadoutError.None;
        if (!ValidGun(gun.Variant)) return DmLoadoutError.UnknownGun;
        if (!fits(gun.Variant)) return DmLoadoutError.WrongClass;
        if (gun.SkinId != ScGunSkinCatalog.None) {
            if (ScGunSkinCatalog.Find(gun.SkinId) is not { } skin || !ScGunSkinCatalog.Available.Contains(skin)) return DmLoadoutError.UnknownSkin;
            if (!ScGunSkinCatalog.Fits(skin, gun.Variant)) return DmLoadoutError.SkinDoesNotFit;
        }
        if (gun.SilencerOff && !GunSpec.All[gun.Variant].HasSilencer) return DmLoadoutError.WrongClass;
        return DmLoadoutError.None;
    }
    /// <summary>Whether the whole loadout can be issued under these rules (L2, L3: validated as one; nothing is silently dropped).</summary>
    public static DmLoadoutError Validate(DmLoadout loadout, DmRules rules) {
        if (loadout is null) return DmLoadoutError.UnknownGun;
        var error = Gun(loadout.Primary, IsPrimary); if (error != DmLoadoutError.None) return error;
        error = Gun(loadout.Secondary, IsSecondary); if (error != DmLoadoutError.None) return error;
        error = Gun(loadout.Zeus, IsZeus); if (error != DmLoadoutError.None) return error;
        if (loadout.Knife is { } knife) {
            if (knife.Variant < 0 || knife.Variant >= KnifeCount) return DmLoadoutError.UnknownKnife;
            if (!KnifeFinishes(knife.Variant).Contains(knife.Finish)) return DmLoadoutError.UnknownFinish;
        }
        var grenades = loadout.Grenades ?? [];
        if (grenades.Count > 0) {
            if (!rules.Grenades) return DmLoadoutError.GrenadesOff;
            if (grenades.Any(g => g < 0 || g >= GrenadeKinds || !ScGrenadeBlock.Enabled(g))) return DmLoadoutError.UnknownGrenade;
            if (grenades.Count > DmFixed.GrenadesPerLife) return DmLoadoutError.TooManyGrenades;
            foreach (var kind in grenades.GroupBy(g => g))
                if (kind.Count() > (kind.Key == FlashKind ? DmFixed.FlashPerLife : DmFixed.OtherGrenadeEach)) return DmLoadoutError.TooManyOfKind;
        }
        return DmLoadoutError.None;
    }
    public static string Describe(DmLoadoutError error) => error switch {
        DmLoadoutError.None => "", DmLoadoutError.UnknownGun => "这把枪不在竞技目录里", DmLoadoutError.WrongClass => "这件装备不能放在这个位置",
        DmLoadoutError.UnknownSkin => "这个外观在本版本不可用", DmLoadoutError.SkinDoesNotFit => "这个外观不属于这把枪",
        DmLoadoutError.UnknownKnife => "这把刀不在竞技目录里", DmLoadoutError.UnknownFinish => "这把刀没有这种涂装",
        DmLoadoutError.GrenadesOff => "房主没有开放投掷物", DmLoadoutError.UnknownGrenade => "这种投掷物不可用",
        DmLoadoutError.TooManyGrenades => $"每条命最多 {DmFixed.GrenadesPerLife} 个投掷物", DmLoadoutError.TooManyOfKind => $"同种投掷物数量超出（闪光最多 {DmFixed.FlashPerLife} 个，其他各 {DmFixed.OtherGrenadeEach} 个）",
        _ => error.ToString() };
}
