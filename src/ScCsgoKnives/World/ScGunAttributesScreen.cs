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
public sealed class ScGunAttributesScreen : ScWeaponHelpScreen {
    public const string ScreenName = "ScCsgoGunAttributes";

    int m_value;
    int m_variant;
    int m_entryIndex;
    readonly record struct CatalogueEntry(int Variant, int SkinId) {
        public int Value => SkinId == 0 ? ScGunAttributes.TemplateValue(Variant)
            : Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunSkinTemplateBlock>(true), 0, SkinId);
        public string Name => DisplayName(Variant) + (SkinId == 0 ? "" : " · " + ScGunSkinCatalog.NameOf(SkinId));
    }
    static readonly CatalogueEntry[] Catalogue = Enumerable.Range(0, GunSpec.All.Length).Select(v => new CatalogueEntry(v, 0))
        .Concat(ScGunSkinCatalog.All.Select(s => new CatalogueEntry(Array.FindIndex(GunSpec.All, g => g.Name == s.Gun), s.PaintId))).ToArray();
    int m_instanceValue;          // the item the player came from, when they came from one
    int m_previewLevel = -1;      // -1 follows the real level; never writes to the gun
    readonly List<Widget> m_futureRows = [];
    ButtonWidget m_levelDown, m_levelUp;
    LabelWidget m_previewNotice;
    bool m_built, m_narrow, m_singleBars;
    int m_lastRevision = -1;
    int m_initialValue;

    readonly StackPanelWidget m_root = new() { Name = "ScGunAttributes.Root" };
    readonly ListPanelWidget m_list = new() { Direction = LayoutDirection.Vertical, ItemSize = 46, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    readonly CanvasWidget m_listHost = new() { Name = "ScGunAttributes.ListHost", Size = new Vector2(float.PositiveInfinity), ClampToBounds = true };
    readonly StackPanelWidget m_currentHost = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly StackPanelWidget m_right = new() { Direction = LayoutDirection.Vertical };
    readonly CanvasWidget m_rightHost = new() { Name = "ScGunAttributes.RightHost", Size = new Vector2(float.PositiveInfinity), ClampToBounds = true };
    readonly BlockIconWidget m_preview = new() { Size = new Vector2(150), HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center };
    readonly StackPanelWidget m_identity = new() { Direction = LayoutDirection.Vertical, VerticalAlignment = WidgetAlignment.Center, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly StackPanelWidget m_bars = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly ScrollPanelWidget m_barScroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    LabelWidget m_name, m_identityText, m_counter, m_growth, m_description, m_currentInstance;
    ButtonWidget m_level, m_recipe, m_back;

    public ScGunAttributesScreen() : base("武器属性") {
        m_initialValue = 0;
        Body.Children.Add(m_root);
        m_list.ItemWidgetFactory = item => {
            var entry = Catalogue[(int)item];
            var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(4, 0) };
            row.Children.Add(new BlockIconWidget { Value = entry.Value, Size = new Vector2(38), Margin = new Vector2(2, 2) });
            row.Children.Add(new LabelWidget {
                Text = entry.SkinId == 0 ? entry.Name : DisplayName(entry.Variant) + "\n" + ScGunSkinCatalog.NameOf(entry.SkinId), FontScale = entry.SkinId == 0 ? .78f : .66f, VerticalAlignment = WidgetAlignment.Center,
                HorizontalAlignment = WidgetAlignment.Stretch, Color = ScGunUi.Text, Ellipsis = true, MaxLines = entry.SkinId == 0 ? 1 : 2,
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
        int variant = 0, skin = 0;
        if (parameters[0] is int value && EffectiveGunStats.TrySnapshotValue(value, out var source)) {
            variant = source.Variant; skin = source.SkinId;
            // Only a real instance record gives a level; a catalogue template is browsed at Lv0.
            if (!source.Fresh || source.CounterInstalled) m_instanceValue = value;
        }
        m_previewLevel = -1;
        m_built = false;
        int selected = Array.FindIndex(Catalogue, e => e.Variant == variant && e.SkinId == skin);
        Select(selected >= 0 ? selected : Math.Clamp(variant, 0, GunSpec.All.Length - 1));
    }

    void Select(int index) {
        m_previewLevel = -1;
        m_entryIndex = Math.Clamp(index, 0, Catalogue.Length - 1);
        var entry = Catalogue[m_entryIndex]; m_variant = entry.Variant;
        m_value = m_instanceValue != 0 && EffectiveGunStats.TrySnapshotValue(m_instanceValue, out var origin) && origin.Variant == m_variant
            && origin.SkinId == entry.SkinId ? m_instanceValue : entry.Value;
        if (m_list.SelectedIndex != m_entryIndex) m_list.SelectedIndex = m_entryIndex;
        Refresh();
    }

    void Build(bool narrow, bool singleBars) {
        float listScroll = m_list.ScrollPosition, barScroll = m_barScroll.ScrollPosition;
        // Detach reused widgets from their old nested containers before rebuilding.
        m_listHost.ParentWidget?.Children.Remove(m_listHost);
        m_preview.ParentWidget?.Children.Remove(m_preview);
        m_identity.ParentWidget?.Children.Remove(m_identity);
        m_currentHost.ParentWidget?.Children.Remove(m_currentHost);
        m_rightHost.ParentWidget?.Children.Remove(m_rightHost);
        m_narrow = narrow; m_singleBars = singleBars; m_built = true;
        m_root.Children.Clear();
        m_root.Margin = Vector2.Zero;
        m_root.Direction = narrow ? LayoutDirection.Vertical : LayoutDirection.Horizontal;
        if (m_list.Items.Count == 0) for (int v = 0; v < Catalogue.Length; v++) m_list.AddItem(v);
        m_list.SelectedIndex = m_entryIndex;

        // Left column: current instance (when opened from an item), then catalogue list.
        var left = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
        m_listHost.HorizontalAlignment = WidgetAlignment.Stretch;
        m_listHost.VerticalAlignment = WidgetAlignment.Stretch;
        m_listHost.Size = new Vector2(float.PositiveInfinity);
        m_currentHost.IsVisible = m_instanceValue != 0;
        left.Children.Add(m_currentHost);
        left.Children.Add(m_listHost);
        m_description = ScGunUi.Note("");
        m_description.IsVisible = !m_narrow;
        left.Children.Add(m_description);
        var leftHost = new CanvasWidget {
            HorizontalAlignment = narrow ? WidgetAlignment.Stretch : WidgetAlignment.Near,
            VerticalAlignment = WidgetAlignment.Stretch,
            Name = "ScGunAttributes.LeftHost",
            Size = narrow ? new Vector2(float.PositiveInfinity, 120) : new Vector2(200, float.PositiveInfinity),
            Margin = narrow ? new Vector2(0, 4) : new Vector2(4, 0), ClampToBounds = true,
        };
        leftHost.Children.Add(NativeArea());
        leftHost.Children.Add(left);
        m_root.Children.Add(leftHost);

        // Right column: preview and identity on top, the attribute bars below.
        m_right.Children.Clear();
        var head = new StackPanelWidget { Name = "ScGunAttributes.Header", Direction = LayoutDirection.Horizontal, Margin = new Vector2(6, 4) };
        var previewHost = new CanvasWidget { Size = new Vector2(100, 100), VerticalAlignment = WidgetAlignment.Center };
        m_preview.Size = new Vector2(96);
        previewHost.Children.Add(m_preview);
        head.Children.Add(previewHost);
        m_identity.Children.Clear();
        m_name = ScGunUi.Label("", 1.15f);
        m_identityText = ScGunUi.Label("", .78f, ScGunUi.Dim);
        m_counter = ScGunUi.Label("", .78f, ScGunUi.Accent);
        foreach (var label in new[] { m_name, m_identityText, m_counter }) { label.WordWrap = true; label.HorizontalAlignment = WidgetAlignment.Stretch; }
        m_identity.Children.Add(m_name); m_identity.Children.Add(m_identityText); m_identity.Children.Add(m_counter);
        var levels = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center };
        m_levelDown = ScGunUi.Button("−", 48);
        m_level = ScGunUi.Button("预览 Lv0", 150);
        m_levelUp = ScGunUi.Button("+", 48);
        levels.Children.Add(m_levelDown); levels.Children.Add(m_level); levels.Children.Add(m_levelUp);
        var identityHost = new CanvasWidget { Size = new Vector2(float.PositiveInfinity, -1) };
        identityHost.Children.Add(m_identity);
        head.Children.Add(identityHost);
        var details = new StackPanelWidget { Direction = LayoutDirection.Vertical };
        details.Children.Add(head);
        details.Children.Add(levels);
        m_previewNotice = ScGunUi.Note("");
        details.Children.Add(m_previewNotice);
        m_bars.ParentWidget?.Children.Remove(m_bars);
        details.Children.Add(m_bars);
        m_barScroll.Children.Clear(); m_barScroll.Children.Add(details);
        m_right.Children.Add(m_barScroll);
        m_growth = ScGunUi.Note("");
        details.Children.Add(m_growth);
        var bar = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 6) };
        m_recipe = ScGunUi.Button("装配配方", 132); m_back = ScGunUi.Button("返回", 108);
        foreach (var b in new[] { m_recipe, m_back }) { b.Margin = new Vector2(5, 0); bar.Children.Add(b); }
        m_right.Children.Add(bar);
        m_rightHost.Children.Clear();
        m_rightHost.Children.Add(NativeArea());
        m_rightHost.Children.Add(m_right);
        m_root.Children.Add(m_rightHost);
        Refresh();
        m_list.ScrollPosition = listScroll; m_barScroll.ScrollPosition = barScroll;
    }

    /// <summary>One attribute row: label, ten segments, the exact value and its unit. The number is authoritative;
    /// the bar is a fixed-range comparison, never a score and never a progress meter.</summary>
    static Widget BarRow(ScGunAttributes.Row row, bool narrow) {
        var panel = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(6, 3) };
        var top = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
        top.Children.Add(new LabelWidget { Text = row.Label, FontScale = .74f, Color = ScGunUi.Dim, WordWrap = true });
        top.Children.Add(new LabelWidget { Text = row.Text + " " + row.Unit, FontScale = .8f, Color = ScGunUi.Text, WordWrap = true });
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

    int Level() => m_previewLevel < 0 ? EffectiveGunStats.LevelOf(m_value) : m_previewLevel;

    void PreviewLevel(int level) { m_previewLevel = Math.Clamp(level, 0, ScGunGrowth.MaxLevel); Refresh(); }

    void Refresh() {
        if (!m_built) return;
        var spec = GunSpec.All[m_variant];
        int level = Level();
        m_preview.Value = m_value;
        m_name.Text = Catalogue[m_entryIndex].Name;
        EffectiveGunStats.TrySnapshotValue(m_value, out var snap);
        int skin = snap.SkinId;
        string identity = $"型号 {ScGunNames.Variant(m_variant)} · 外观 {ScGunSkinCatalog.NameOf(skin)}";
        if (spec.HasSilencer) identity += snap.SilencerOff ? " · 消音器已拆" : " · 消音器在位";
        m_identityText.Text = identity;
        bool installed = snap.CounterInstalled;
        m_lastRevision = snap.Revision;
        m_counter.Text = installed ? $"击杀 {snap.KillCount} · Lv{snap.Level}" : "无击杀计数器";
        EffectiveGunStats.TrySnapshotValue(m_instanceValue, out var original);
        m_currentInstance.Text = m_instanceValue != 0
            ? $"当前物品：{ScGunSkinCatalog.NameOf(original.SkinId)}"
                + (original.CounterInstalled ? $" · 计数 {original.KillCount} · Lv{original.Level}" : "") : "";
        m_currentInstance.IsVisible = m_instanceValue != 0;
        int actualLevel = EffectiveGunStats.LevelOf(m_value);
        m_level.Text = $"预览 Lv{level} / {ScGunGrowth.MaxLevel}";
        m_levelDown.IsEnabled = level > 0; m_levelUp.IsEnabled = level < ScGunGrowth.MaxLevel;
        m_previewNotice.Text = ScGunAttributes.CounterUnlockNotice + $"\n实际 Lv{actualLevel} · −/+ 切换，点等级回到实际等级。"
            + (level != actualLevel ? "\n仅预览，不改变枪械等级、弹量或存档。" : "")
            + (level > actualLevel ? " 超出当前等级的变化项会柔和闪烁。" : "")
            + (ScGunRegistry.Current?.GrowthMode == ScGunGrowthMode.CountOnly ? "\n本世界仅计数；预览的成长加成不会生效。" : "");
        if (skin != ScGunSkinCatalog.None) m_previewNotice.Text += "\n皮肤基础伤害 +50%；等级加成在此基础上计算。";
        m_bars.Children.Clear();
        m_futureRows.Clear();
        var rows = ScGunAttributes.Rows(spec, m_value, level);
        var actualRows = ScGunAttributes.Rows(spec, m_value, actualLevel);
        Widget PreviewRow(int index) {
            Widget widget = BarRow(rows[index], m_singleBars);
            if (level > actualLevel && (rows[index].Text != actualRows[index].Text || rows[index].Detail != actualRows[index].Detail)) m_futureRows.Add(widget);
            return widget;
        }
        if (m_singleBars) for (int i = 0; i < rows.Count; i++) m_bars.Children.Add(PreviewRow(i));
        else for (int i = 0; i < rows.Count; i += 2) {
            var pair = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch };
            for (int k = i; k < Math.Min(i + 2, rows.Count); k++) {
                var cell = new CanvasWidget { Size = new Vector2(float.PositiveInfinity, -1), HorizontalAlignment = WidgetAlignment.Stretch };
                cell.Children.Add(PreviewRow(k));
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

    public override void MeasureOverride(Vector2 availableSize) {
        // Layout dimensions are logical units (850 / UIScale), not window pixels.
        float bodyWidth = availableSize.X - 64 - 24;
        bool narrow = bodyWidth < 620;
        bool singleBars = (narrow ? bodyWidth : bodyWidth - 208) < 520;
        if (!m_built || narrow != m_narrow || singleBars != m_singleBars) Build(narrow, singleBars);
        base.MeasureOverride(availableSize);
    }

    public override void Update() {
        if (BackRequested) { GoBack(); return; }
        if (!m_built) return;
        if (m_list.SelectedIndex is int index && index != m_entryIndex) Select(index);
        if (EffectiveGunStats.TrySnapshotValue(m_value, out var current) && current.Revision != m_lastRevision) Refresh();
        if (m_levelDown.IsClicked) PreviewLevel(Level() - 1);
        if (m_levelUp.IsClicked) PreviewLevel(Level() + 1);
        if (m_level.IsClicked) { m_previewLevel = -1; Refresh(); }
        // Slow, shallow alpha pulse: text remains readable at every point, no sharp flashes.
        int alpha = (int)(210 + 45 * Math.Sin(Time.RealTime * Math.PI));
        foreach (var row in m_futureRows) row.ColorTransform = new Color(255, 255, 255, alpha);
        if (m_recipe.IsClicked) {
            ScreensManager.m_screens["RecipaediaRecipes"] = new ScAssemblyRecipesScreen();
            ScreensManager.SwitchScreen("RecipaediaRecipes", m_value);
            return;
        }
        if (m_back.IsClicked) GoBack();
    }
}
