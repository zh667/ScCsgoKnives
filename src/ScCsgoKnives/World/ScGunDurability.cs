namespace Game;

/// <summary>Gun wear (community plan F10 / M4, 0.32.0; six levels since 0.34.0 so v3 items stay recognisable). The item carries a level (5 new, 0 broken);
/// the shots inside the current level are counted per player hotbar slot by SubsystemScGunBlockBehavior.
/// Full lives per class are the plan's first test baseline (C2); the class of every gun is listed
/// explicitly. Nothing here changes damage, rate of fire or accuracy.</summary>
public static class ScGunDurability {
    public enum Class { Pistol, Smg, Rifle, Shotgun, BoltSniper, AutoSniper, MachineGun, Taser }
    public const int Levels = GunSpec.MaxDurability;
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
    /// <summary>Shots that drop the level by one; the last level's last shot still fires, then the gun is broken.</summary>
    public static int ShotsPerLevel(string gun) => Math.Max(1, (int)Math.Round(FullShots(ClassOf(gun)) / (double)Levels));
    public static int Percent(int level) => (int)Math.Round(100.0 * Math.Clamp(level, 0, Levels) / Levels);
    public static bool IsBroken(int data) => GunSpec.GetDurability(data) <= 0;
    /// <summary>Plan C3: at or under 20 % shows orange (level 1 = 20 %).</summary>
    public static bool IsLow(int data) => !IsBroken(data) && Percent(GunSpec.GetDurability(data)) <= 20;
    /// <summary>Advances the per-slot shot counter by one real shot; returns the new level (the caller writes it).</summary>
    public static int Wear(string gun, int level, ref int shotsInLevel) {
        if (level <= 0) return 0;
        shotsInLevel++;
        if (shotsInLevel < ShotsPerLevel(gun)) return level;
        shotsInLevel = 0; return level - 1;
    }
}
