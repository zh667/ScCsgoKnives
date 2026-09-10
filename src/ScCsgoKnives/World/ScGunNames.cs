namespace Game;

/// <summary>Player-facing names only. Never use these to encode a variant or resolve a resource.</summary>
public static class ScGunNames {
    public static string Of(string key) => key switch {
        "ak47"=>"AK-47", "m4a1s"=>"M4A1-S", "awp"=>"AWP", "deagle"=>"沙漠之鹰",
        "glock18"=>"格洛克18", "usp_silencer"=>"USP-S", "m4a4"=>"M4A4", "famas"=>"法玛斯",
        "mp9"=>"MP9", "p90"=>"P90", "ssg08"=>"SSG 08", "fiveseven"=>"FN57", "hkp2000"=>"P2000",
        "p250"=>"P250", "tec9"=>"TEC-9", "cz75a"=>"CZ75", "mac10"=>"MAC-10", "mp7"=>"MP7",
        "ump45"=>"UMP-45", "bizon"=>"PP-野牛", "mp5sd"=>"MP5-SD", "galilar"=>"加利尔",
        "scar20"=>"SCAR-20", "g3sg1"=>"G3SG1", "aug"=>"AUG", "sg556"=>"SG 553", "nova"=>"新星",
        "xm1014"=>"XM1014", "sawedoff"=>"截短霰弹枪", "mag7"=>"MAG-7", "m249"=>"M249",
        "negev"=>"内格夫", "revolver"=>"R8 左轮", "elite"=>"双持贝瑞塔", "taser"=>"电击枪",
        _=>"未知枪械"
    };
    public static string Variant(int variant) => variant>=0 && variant<GunSpec.All.Length ? Of(GunSpec.All[variant].Name) : "未知枪械";
    public static string Item(ScGunSnapshot s) => Variant(s.Variant)
        + (s.SkinId!=ScGunSkinCatalog.None ? " · "+ScGunSkinCatalog.NameOf(s.SkinId) : "")
        + (s.CounterInstalled ? $" · 击杀计数器 Lv{s.Level}" : "");
    public static string Tier(ScSkinTier tier) => tier switch {
        ScSkinTier.Standard=>"标准费用", ScSkinTier.Premium=>"高级费用", ScSkinTier.Special=>"特殊费用", _=>"未知费用"
    };
}
