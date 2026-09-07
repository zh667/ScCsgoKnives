namespace Game;

/// <summary>Gun wear (community plan F10 / M4). Since 0.35.0 the durability is an exact shot count kept in the
/// gun's ScGunRecord; a new gun starts at its class's full life and every real shot costs one. Nothing here
/// changes damage, rate of fire or accuracy.</summary>
public static class ScGunDurability {
    public enum Class { Pistol, Smg, Rifle, Shotgun, BoltSniper, AutoSniper, MachineGun, Taser }
    /// <summary>估计 (plan C2): shots from new to broken.</summary>
    public static int FullShots(Class c) => c switch {
        Class.Pistol => 1200, Class.Smg => 2000, Class.Rifle => 1500, Class.Shotgun => 300,
        Class.BoltSniper => 200, Class.AutoSniper => 500, Class.MachineGun => 4000, Class.Taser => 100, _ => 1500 };
    public static Class ClassOf(string gun) => gun switch {
        "glock18" or "hkp2000" or "p250" or "usp_silencer" or "fiveseven" or "tec9" or "cz75a" or "deagle" or "revolver" or "elite" => Class.Pistol,
        "mac10" or "mp9" or "mp7" or "ump45" or "mp5sd" or "p90" or "bizon" => Class.Smg,
        "galilar" or "famas" or "ak47" or "m4a4" or "m4a1s" or "aug" or "sg556" => Class.Rifle,
        "nova" or "xm1014" or "sawedoff" or "mag7" => Class.Shotgun,
        "ssg08" or "awp" => Class.BoltSniper,
        "scar20" or "g3sg1" => Class.AutoSniper,
        "m249" or "negev" => Class.MachineGun,
        "taser" => Class.Taser,
        _ => throw new InvalidOperationException("No durability class for gun " + gun)
    };
    public static int Full(string gun) => FullShots(ClassOf(gun));
    public static int Full(int variant) => variant >= 0 && variant < GunSpec.All.Length ? Full(GunSpec.All[variant].Name) : 1500;
    public static int FullOf(int data) => Full(GunSpec.GetVariant(data));
    public static bool IsBroken(int data) => GunSpec.GetDurability(data) <= 0;
    /// <summary>Plan C3: at or under 20 % shows orange.</summary>
    public static bool IsLow(int data) { int d = GunSpec.GetDurability(data); return d > 0 && d * 5 <= FullOf(data); }
    /// <summary>Plan C5: a positive value never reads as broken and a worn gun never reads as full.</summary>
    public static string PercentText(int durability, int full) {
        if (full <= 0 || durability <= 0) return "0%";
        if (durability >= full) return "100%";
        double percent = 100.0 * durability / full;
        if (percent < 1) return "<1%";
        if (percent > 99) return ">99%";
        return $"{(int)Math.Round(percent)}%";
    }
    public static string PercentText(int data) => PercentText(GunSpec.GetDurability(data), FullOf(data));
    /// <summary>One real shot: the record loses one point (never below zero). Returns the data to write.</summary>
    public static int Wear(int data) {
        int d = GunSpec.GetDurability(data);
        return d <= 0 ? data : GunSpec.SetDurability(data, d - 1);
    }
}
