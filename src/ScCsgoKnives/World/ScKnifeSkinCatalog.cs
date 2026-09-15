namespace Game;
public static class ScKnifeSkinCatalog {
    public const int None = 0, GammaDoppler = 1, Fade = 2, Butcher = 3;
    // Stable variants; pairs verified against CS2's default_generated icons and paint kits.
    static readonly string[] s_finishes = [
        "am_gamma_doppler_phase4", "am_gamma_doppler_phase1", "am_gamma_doppler_phase2",
        "am_ruby_marbleized", "am_gamma_doppler_phase4", "aa_fade", "aa_fade", "aa_fade",
        null, null, "am_gamma_doppler_phase2", "am_gamma_doppler_phase3", "am_gamma_doppler_phase4",
        "aa_fade", "aa_fade", "aa_fade", "am_gamma_doppler_phase4", "aa_fade", "aa_fade",
        "am_gamma_doppler_phase3", "aa_fade", "aa_fade"
    ];
    public static string Finish(int variant) => variant >= 0 && variant < s_finishes.Length ? s_finishes[variant] : null;
    public static int Get(int value) => (Terrain.ExtractData(value) >> 5) & 3;
    public static int With(int variant, int skin) => (variant & 31) | ((skin & 3) << 5);
    public static int ForVariant(int variant) => Finish(variant) is not {} finish ? None : finish == "aa_fade" ? Fade : GammaDoppler;
    public static int Phase(int variant) => Finish(variant)?.StartsWith("am_gamma_doppler_phase") == true ? Finish(variant)[^1] - '0' : 0;
    public static string Name(int skin, int variant) => skin == None ? "原厂外观" : Finish(variant) is null
        ? "旧预览外观（无官方涂装）" : Finish(variant) == "am_ruby_marbleized" ? "多普勒 · 红宝石"
        : Phase(variant) > 0 ? $"伽马多普勒 P{Phase(variant)}" : "渐变之色";
    // Unsupported Gamma in the old preview used skin 1. Preserve stored values
    // and knife identities while resolving the selected supported finish visually.
    public static string Texture(string asset, int skin, int variant) => skin != None && Finish(variant) is {} finish
        ? $"{asset}_finish__{finish}" : $"{asset}_cs2";
    public static string Icon(string asset, int skin, int variant) => skin != None && Finish(variant) is {} finish
        ? $"{asset}_slot__{finish}" : $"{asset}_slot";
    // Use the exact item being drawn, including during a switch-out.
    public static string MaterialForRender(string asset, int variant, int itemValue) =>
        variant >= 0 && variant < CsmcKnifeRig.KnifeCount && ScKnifeBlock.GetVariant(itemValue) == variant
            ? Texture(asset, ScKnifeBlock.SkinOf(itemValue), variant) : asset + "_cs2";
}
