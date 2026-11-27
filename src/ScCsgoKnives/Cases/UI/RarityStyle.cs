using Engine;

namespace Game;

public static class RarityStyle {
    public static Color Color(CaseRarity rarity) => rarity switch {
        CaseRarity.Common => new Color(150, 160, 170),
        CaseRarity.Uncommon => new Color(75, 165, 95),
        CaseRarity.Rare => new Color(65, 125, 210),
        CaseRarity.Epic => new Color(155, 80, 205),
        CaseRarity.Legendary => new Color(230, 145, 35),
        _ => Engine.Color.White
    };

    public static string Name(CaseRarity rarity) {
        string translated = LanguageControl.Get("ScCsgoBox", "Rarity", rarity.ToString());
        return string.IsNullOrEmpty(translated) ? rarity.ToString() : translated;
    }
}

