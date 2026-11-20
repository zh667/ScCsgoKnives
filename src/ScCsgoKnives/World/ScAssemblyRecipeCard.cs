using Engine;
namespace Game;

/// <summary>What the weapon workbench makes for one item value, and the rows that show it. The assembly recipe page and
/// Neorxna's recipe card (ScNeorxnaRecipes, feedback-1120-160 #1) show the same recipe from here, so the two can never
/// disagree about materials or the route.</summary>
public static class ScAssemblyRecipeCard {
    public sealed record Recipe(int Output, ScWeaponCrafting.Entry Weapon, ScComponentCrafting.Entry Component, ScWorkbenchRecipe Extension) {
        public bool CreativeOnly => Extension is { CreativeOnly: true };
        public Dictionary<int, int> Materials() => Weapon?.Materials() ?? Component?.Materials() ?? Extension.Materials();
        public string Route => CreativeOnly ? "仅创造模式领取 · 无生存制作配方"
            : Component is not null ? "武器装配台 · 配件制作 · 每批产出 1 件"
            : $"武器装配台 · 制作等级 {Weapon?.Level ?? Extension?.Level ?? 1} · 每批产出 {Extension?.ResultCount ?? 1} 件";
        public string Name => BlocksManager.Blocks[Terrain.ExtractContents(Output)].GetDisplayName(null, Output);
        public string Description => BlocksManager.Blocks[Terrain.ExtractContents(Output)].GetDescription(Output);
    }

    /// <summary>The workbench recipe of <paramref name="value"/> (a skin or counter template points at its gun), or null.</summary>
    public static Recipe Find(int value) {
        bool skinTemplate = ScGunSkinTemplateBlock.IsTemplate(value) || ScGunCounterTemplateBlock.IsTemplate(value);
        int recipeValue = skinTemplate && EffectiveGunStats.TrySnapshotValue(value, out var snapshot)
            ? ScGunAttributes.TemplateValue(snapshot.Variant) : value;
        var weapon = ScWeaponCrafting.Find(recipeValue);
        var component = ScComponentCrafting.Find(recipeValue);
        var extension = ScWorkbenchExtension.Find(recipeValue);
        if (weapon is null && component is null && extension is null) return null;
        return new Recipe(weapon?.Value ?? component?.Value ?? extension.Value, weapon, component, extension);
    }

    static LabelWidget Label(string text, float scale) => new() {
        Text = text, FontScale = scale, WordWrap = true, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(4, 5) };

    static StackPanelWidget MaterialRow(int value, int count, float icon) {
        var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center };
        row.Children.Add(new BlockIconWidget { Value = value, Size = new Vector2(icon), Margin = new Vector2(6, 2) });
        row.Children.Add(new LabelWidget { Text = $"{BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null, value)} ×{count}", VerticalAlignment = WidgetAlignment.Center });
        return row;
    }

    /// <summary>The assembly recipe page's rows, in the order and wording that page always had.</summary>
    public static void FillPage(ContainerWidget panel, Recipe recipe) {
        panel.Children.Add(Label(recipe.Name, 1.25f));
        panel.Children.Add(Label(recipe.Route, 1));
        if (recipe.CreativeOnly) { panel.Children.Add(Label(recipe.Description, .8f)); return; }
        foreach (var material in recipe.Materials()) panel.Children.Add(MaterialRow(material.Key, material.Value, 44));
        panel.Children.Add(Label("将材料放入背包，交互装配台并选择此型号。", .8f));
        if (recipe.Extension is not null) panel.Children.Add(Label(recipe.Description, .8f));
        panel.Children.Add(Label(recipe.Weapon is null ? "在装配台选择数量后制作，材料可自动堆叠。" : recipe.Weapon.Knife ? "左键轻刀，右键重刀。" : "交付空枪；弹药另行制作。", .8f));
    }

    /// <summary>A compact card for another mod's recipe browser: name, route and materials, nothing to click.</summary>
    public static Widget Card(Recipe recipe) {
        var card = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Center,
            VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(8, 6) };
        card.Children.Add(Label(recipe.Name, 1));
        card.Children.Add(Label(recipe.Route, .8f));
        if (!recipe.CreativeOnly) foreach (var material in recipe.Materials()) card.Children.Add(MaterialRow(material.Key, material.Value, 36));
        return card;
    }
}
