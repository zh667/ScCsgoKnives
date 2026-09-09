using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>The weapon attribute page: a weapon list on the left, the gun and its identity at the top right,
/// and its attributes as labelled bars with exact numbers below.
///
/// The numbers are the ones combat uses, at the level the selected gun is actually carrying. The card
/// deliberately omits current and maximum durability and any crafting materials; the repair page and the trade
/// confirmation still show everything they must. Nothing the mod has not implemented appears here - no rarity,
/// no weight, no armour penetration, no attachments.</summary>
public sealed class ScGunAttributesScreen : RecipaediaRecipesScreen {
    public const string ScreenName = "ScCsgoGunAttributes";

    int m_value;
    int m_variant;
    int m_instanceValue;          // the item the player came from, when they came from one
    int m_levelMode;              // 0 base, 1 current, 2 next
    bool m_built, m_narrow;
    int m_lastRevision = -1;
    int m_initialValue;

    /// <summary>The vertical margin keeps the inherited screen's own top bar - and its Back button - clear.</summary>
    readonly StackPanelWidget m_root = new() { HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(12, 56) };
    readonly ListPanelWidget m_list = new() { Direction = LayoutDirection.Vertical, ItemSize = 46, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    readonly CanvasWidget m_listHost = new();
    readonly StackPanelWidget m_currentHost = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly StackPanelWidget m_right = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    readonly BlockIconWidget m_preview = new() { Size = new Vector2(150), HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center };
    readonly StackPanelWidget m_identity = new() { Direction = LayoutDirection.Vertical, VerticalAlignment = WidgetAlignment.Center, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly StackPanelWidget m_bars = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly ScrollPanelWidget m_barScroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    LabelWidget m_name, m_identityText, m_counter, m_growth, m_description, m_currentInstance;
    ButtonWidget m_level, m_recipe, m_back;

    public ScGunAttributesScreen() {
        m_initialValue = 0;
        Children.Add(m_root);
        m_list.ItemWidgetFactory = item => {
            int variant = (int)item;
            var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(4, 0) };
            row.Children.Add(new BlockIconWidget { Value = ScGunAttributes.TemplateValue(variant), Size = new Vector2(38), Margin = new Vector2(2, 2) });
            row.Children.Add(new LabelWidget {
                Text = DisplayName(variant), FontScale = .78f, VerticalAlignment = WidgetAlignment.Center,
                HorizontalAlignment = WidgetAlignment.Stretch, Color = ScGunUi.Text,
            });
            return row;
        };
        m_list.ItemClicked = item => { if (item is int variant) Select(variant); };
        m_listHost.Children.Add(m_list);
        m_currentInstance = ScGunUi.Note("");
        m_currentHost.Children.Add(m_currentInstance);
    }

    public ScGunAttributesScreen(int value) : this() { m_initialValue = value; }

    static string DisplayName(int variant) {
        int value = ScGunAttributes.TemplateValue(variant);
        return BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null, value);
    }

    public override void Enter(object[] parameters) {
        // The inherited screen reads parameters[0] as a block value; never hand it an empty array.
        if (parameters is not { Length: > 0 } || parameters[0] is not int)
            parameters = [m_initialValue != 0 ? m_initialValue : ScGunAttributes.TemplateValue(0)];
        base.Enter(parameters);
        m_instanceValue = 0;
        int variant = 0;
        if (parameters[0] is int value && Terrain.ExtractContents(value) == BlocksManager.GetBlockIndex<ScGunBlock>(true)) {
            variant = ScGunBlock.GetVariant(value);
            // Only a real instance record gives a level; a catalogue template is browsed at Lv0.
            if (GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var s) && !s.Fresh) m_instanceValue = value;
        }
        m_levelMode = m_instanceValue != 0 ? 1 : 0;
        m_built = false;
        Select(Math.Clamp(variant, 0, GunSpec.All.Length - 1));
    }

    void Select(int variant) {
        m_variant = Math.Clamp(variant, 0, GunSpec.All.Length - 1);
        m_value = m_instanceValue != 0 && ScGunBlock.GetVariant(m_instanceValue) == m_variant ? m_instanceValue : ScGunAttributes.TemplateValue(m_variant);
        if (m_list.SelectedIndex != m_variant) m_list.SelectedIndex = m_variant;
        Refresh();
    }

    void Build(bool narrow) {
        float listScroll = m_list.ScrollPosition, barScroll = m_barScroll.ScrollPosition;
        // Detach reused widgets from their old nested containers before rebuilding.
        m_listHost.ParentWidget?.Children.Remove(m_listHost);
        m_preview.ParentWidget?.Children.Remove(m_preview);
        m_identity.ParentWidget?.Children.Remove(m_identity);
        m_narrow = narrow; m_built = true;
        m_root.Children.Clear();
        m_root.Direction = narrow ? LayoutDirection.Vertical : LayoutDirection.Horizontal;
        if (m_list.Items.Count == 0) for (int v = 0; v < GunSpec.All.Length; v++) m_list.AddItem(v);

        // Left column: current instance (when opened from an item), then catalogue list.
        var left = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
        m_listHost.HorizontalAlignment = WidgetAlignment.Stretch;
        m_listHost.VerticalAlignment = WidgetAlignment.Stretch;
        m_listHost.Size = narrow ? new Vector2(-1, 250) : new Vector2(-1, -1);
        m_currentHost.IsVisible = m_instanceValue != 0;
        left.Children.Add(m_currentHost);
        left.Children.Add(m_listHost);
        m_description = ScGunUi.Note("");
        m_description.IsVisible = !m_narrow;
        left.Children.Add(m_description);
        var leftHost = new CanvasWidget {
            HorizontalAlignment = narrow ? WidgetAlignment.Stretch : WidgetAlignment.Near,
            VerticalAlignment = WidgetAlignment.Stretch,
            Size = narrow ? new Vector2(-1, 310) : new Vector2(250, -1),
        };
        leftHost.Children.Add(left);
        m_root.Children.Add(leftHost);

        // Right column: preview and identity on top, the attribute bars below.
        m_right.Children.Clear();
        var head = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(6, 0) };
        var previewHost = new CanvasWidget { Size = new Vector2(narrow ? 110 : 170, narrow ? 90 : 130), VerticalAlignment = WidgetAlignment.Center };
        m_preview.Size = new Vector2(narrow ? 100 : 150);
        previewHost.Children.Add(m_preview);
        head.Children.Add(previewHost);
        m_identity.Children.Clear();
        m_name = ScGunUi.Label("", 1.15f);
        m_identityText = ScGunUi.Label("", .78f, ScGunUi.Dim);
        m_counter = ScGunUi.Label("", .78f, ScGunUi.Accent);
        m_identity.Children.Add(m_name); m_identity.Children.Add(m_identityText); m_identity.Children.Add(m_counter);
        m_level = ScGunUi.Button("当前等级", 160);
        m_level.HorizontalAlignment = WidgetAlignment.Near;
        m_identity.Children.Add(m_level);
        head.Children.Add(m_identity);
        m_right.Children.Add(head);
        m_barScroll.Children.Clear(); m_barScroll.Children.Add(m_bars);
        m_right.Children.Add(m_barScroll);
        m_growth = ScGunUi.Note("");
        m_right.Children.Add(m_growth);
        var bar = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 6) };
        m_recipe = ScGunUi.Button("装配配方", 140); m_back = ScGunUi.Button("返回", 120);
        foreach (var b in new[] { m_recipe, m_back }) { b.Margin = new Vector2(5, 0); bar.Children.Add(b); }
        m_right.Children.Add(bar);
        m_root.Children.Add(m_right);
        Refresh();
        m_list.ScrollPosition = listScroll; m_barScroll.ScrollPosition = barScroll;
    }

    /// <summary>One attribute row: label, ten segments, the exact value and its unit. The number is authoritative;
    /// the bar is a fixed-range comparison, never a score and never a progress meter.</summary>
    static Widget BarRow(ScGunAttributes.Row row, bool narrow) {
        var panel = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(6, 3) };
        var top = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch };
        top.Children.Add(new LabelWidget { Text = row.Label, FontScale = .74f, Color = ScGunUi.Dim, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Center });
        top.Children.Add(new LabelWidget { Text = row.Text + " " + row.Unit, FontScale = .8f, Color = ScGunUi.Text, HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Center });
        panel.Children.Add(top);
        var segments = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(0, 2) };
        int filled = (int)MathF.Ceiling(Math.Clamp(row.Fraction, 0, 1) * 10);
        for (int i = 0; i < 10; i++) {
            segments.Children.Add(new BevelledRectangleWidget {
                Size = new Vector2(narrow ? 14 : 18, 8), Margin = new Vector2(1, 0), BevelSize = 0f, AmbientLight = 1f, DirectionalLight = 0f,
                CenterColor = i < filled ? (row.LowerIsBetter ? new Color(235, 190, 90) : ScGunUi.Accent) : new Color(58, 66, 72),
                BevelColor = new Color(0, 0, 0, 0),
            });
        }
        panel.Children.Add(segments);
        if (!string.IsNullOrEmpty(row.Detail)) panel.Children.Add(ScGunUi.Note(row.Detail));
        return panel;
    }

    int Level() {
        int applied = EffectiveGunStats.LevelOf(m_value);
        if (ScGunRegistry.Current?.GrowthMode == ScGunGrowthMode.CountOnly) return applied;
        return m_levelMode switch { 0 => 0, 2 => Math.Min(ScGunGrowth.MaxLevel, applied + 1), _ => applied };
    }

    void Refresh() {
        if (!m_built) return;
        var spec = GunSpec.All[m_variant];
        int level = Level();
        m_preview.Value = m_value;
        m_name.Text = DisplayName(m_variant);
        int skin = ScGunBlock.SkinOf(m_value);
        string identity = $"型号 {spec.Name} · 外观 {ScGunSkinCatalog.NameOf(skin)}";
        if (spec.HasSilencer) identity += GunSpec.GetSilencerOff(Terrain.ExtractData(m_value)) ? " · 消音器已拆" : " · 消音器在位";
        m_identityText.Text = identity;
        bool installed = GunSpec.TryGetSnapshot(Terrain.ExtractData(m_value), out var snap) && snap.CounterInstalled;
        m_lastRevision = snap.Revision;
        m_counter.Text = installed ? $"击杀 {snap.KillCount} · Lv{snap.Level}" : "无击杀计数器";
        m_currentInstance.Text = m_instanceValue != 0
            ? $"当前物品：{ScGunSkinCatalog.NameOf(ScGunBlock.SkinOf(m_instanceValue))}"
                + (installed ? $" · 计数 {snap.KillCount} · Lv{snap.Level}" : "") : "";
        m_currentInstance.IsVisible = m_instanceValue != 0;
        m_level.Text = m_levelMode switch { 0 => "显示：基础 Lv0", 2 => $"显示：下一级 Lv{level}", _ => $"显示：当前 Lv{level}" };
        m_level.IsEnabled = installed && ScGunRegistry.Current?.GrowthMode != ScGunGrowthMode.CountOnly;
        m_bars.Children.Clear();
        var rows = ScGunAttributes.Rows(spec, m_value, level);
        if (m_narrow) foreach (var row in rows) m_bars.Children.Add(BarRow(row, true));
        else for (int i = 0; i < rows.Count; i += 2) {
            var pair = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch };
            for (int k = i; k < Math.Min(i + 2, rows.Count); k++) {
                var cell = new CanvasWidget { Size = new Vector2(-1, -1), HorizontalAlignment = WidgetAlignment.Stretch };
                cell.Children.Add(BarRow(rows[k], false));
                pair.Children.Add(cell);
            }
            m_bars.Children.Add(pair);
        }
        m_growth.Text = ScGunAttributes.GrowthText(m_value, ScGunRegistry.Current?.GrowthMode ?? ScGunGrowthMode.Unset)
            + (level >= ScGunGrowth.MaxLevel ? "\n" + ScGunAttributes.MaxLevelSummary : "");
        m_description.Text = Description(spec);
    }

    /// <summary>Three lines at most: what the weapon is for. No materials, no durability.</summary>
    static string Description(GunSpec spec) {
        string role = ScGunDurability.ClassOf(spec.Name) switch {
            ScGunDurability.Class.Pistol => "手枪：近距应急，携行方便",
            ScGunDurability.Class.Smg => "冲锋枪：近距高射速，移动射击惩罚小",
            ScGunDurability.Class.Rifle => "步枪：中距短点射可靠",
            ScGunDurability.Class.Shotgun => "霰弹枪：贴脸极强，远距迅速衰减",
            ScGunDurability.Class.BoltSniper => "栓动狙击：开镜精确，不开镜不可靠",
            ScGunDurability.Class.AutoSniper => "连发狙击：远距连续压制",
            ScGunDurability.Class.MachineGun => "机枪：大容量持续火力",
            _ => "电击枪：单次高爆发近距武器，靠充能循环使用",
        };
        string mode = spec.ZoomLevels.Length > 0 ? "支持开镜" : spec.HasBurstMode ? "支持三连发" : spec.HasSilencer ? "可拆装消音器"
            : spec.SilencedAlways ? "内置消音" : spec.CycleSecondsAlternate > 0 ? "支持速射副攻" : "无副模式";
        return $"{role}。\n{mode}。\n弹药：{(spec.RechargeSeconds > 0 ? "自动充能，无需弹药" : spec.Pellets > 1 ? "霰弹" : "通用弹匣")}。";
    }

    public override void Update() {
        bool narrow = ActualSize.X > 1 && ActualSize.X < ScGunUi.NarrowWidth;
        if (!m_built || narrow != m_narrow) Build(narrow);
        // Vanilla's own recipe widgets belong to the screen this one replaces.
        m_craftingRecipeWidget.IsVisible = false; m_smeltingRecipeWidget.IsVisible = false;
        m_prevRecipeButton.IsVisible = false; m_nextRecipeButton.IsVisible = false;
        base.Update();
        m_craftingRecipeWidget.IsVisible = false; m_smeltingRecipeWidget.IsVisible = false;
        m_prevRecipeButton.IsVisible = false; m_nextRecipeButton.IsVisible = false;
        if (m_list.SelectedIndex is int index && index != m_variant) Select(index);
        if (GunSpec.TryGetSnapshot(Terrain.ExtractData(m_value), out var current) && current.Revision != m_lastRevision) Refresh();
        if (m_level.IsClicked) { m_levelMode = (m_levelMode + 1) % 3; Refresh(); }
        if (m_recipe.IsClicked) {
            ScreensManager.m_screens["RecipaediaRecipes"] = new ScAssemblyRecipesScreen();
            ScreensManager.SwitchScreen("RecipaediaRecipes", m_value);
            return;
        }
        if (m_back.IsClicked) ScreensManager.SwitchScreen(ScreensManager.PreviousScreen ?? ScreensManager.FindScreen<Screen>("Recipaedia"));
    }
}
