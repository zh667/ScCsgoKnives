namespace Game;

/// <summary>Presentation only: category identity and item/finish IDs never depend on language.</summary>
public static class ScWeaponText {
    public const string Category = "ScCsgoWeapons";

    // Resolve on demand: a locale reload must not retain names cached in a previous language.
    public static string Get(string key, string fallback, params object[] args) {
        string text = LanguageControl.TryGet(out string translated, "ScCsgoKnives", "Items", key)
            && !string.IsNullOrWhiteSpace(translated) ? translated : fallback;
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.CurrentCulture, text, args);
    }
    public static string Counter(int level) => Get("Counter", "击杀计数器 Lv{0}", level);
    public static string Finish(int paintId, string fallback) =>
        LanguageControl.TryGet(out string translated, "ScCsgoKnives", "Finishes", paintId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        && !string.IsNullOrWhiteSpace(translated) ? translated : fallback;
}
