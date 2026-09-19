using System.Globalization;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>Loaded rounds and reserve items are different units; never add them together.</summary>
public sealed record ScAmmoReadout(string Main, string Detail, bool Empty, bool Charging, bool Insufficient, string Wear = "", int WearState = 0) {
    public bool Compact { get; init; }
    public string LoadedText { get; init; }
    public string CapacityText { get; init; }
    public string ReserveText { get; init; }
    public string Icon { get; init; }
    public string Status { get; init; }
    public float Fraction { get; init; }
    public string MagazineIcon { get; init; } = "magazine";
    public string ReserveCount { get; init; } = "";
    /// <summary>Pre-0.32.0 shape, kept because tools construct the readout by reflection with five arguments.</summary>
    public ScAmmoReadout(string Main, string Detail, bool Empty, bool Charging, bool Insufficient) : this(Main, Detail, Empty, Charging, Insufficient, "", 0) { }
    /// <summary>M4 durability line: 0 normal, 1 low (orange), 2 broken (red).</summary>
    public static (string Text, int State) WearOf(int value, Func<string, string> text) {
        int data = Terrain.ExtractData(value);
        if (ScGunDurability.IsBroken(data)) return (text("Broken"), 2);
        return (string.Format(CultureInfo.InvariantCulture, text("Durability"), ScGunDurability.PercentText(data).TrimEnd('%')), ScGunDurability.IsLow(data) ? 1 : 0);
    }
    public static ScAmmoReadout Read(GunSpec gun, int value, IInventory inventory, bool creative,
        double rechargeRemaining, bool reloading, Func<string, string> text = null) {
        text ??= key => LanguageControl.Get("ScCsgoKnives", "AmmoHud", key);
        string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, text(key), args);
        int data = Terrain.ExtractData(value);
        // Capacity, charge cycle and reload cost follow this gun's applied level, so the readout can never
        // disagree with what the gun actually holds.
        GunSpec.TryGetSnapshot(data, out var loaded);
        int level = loaded.CounterInstalled ? loaded.Level : 0;
        int capacity = ScGunGrowth.Capacity(GunSpec.GetVariant(data), level);
        float cycle = ScGunGrowth.RechargeSeconds(gun, level);
        int rounds = Math.Clamp(GunSpec.GetRounds(data), 0, capacity);
        var (wear, wearState) = WearOf(value, text);
        if (gun.RechargeSeconds > 0) {
            if (rounds > 0) return new(text("Ready"), text("SingleCharge"), false, false, false, wear, wearState) {
                Compact = true, LoadedText = "1", CapacityText = "/ 1", ReserveText = "", Icon = gun.Name + "_slot", Fraction=1,MagazineIcon="generic_bullet"
            };
            double remaining = double.IsFinite(rechargeRemaining) && rechargeRemaining >= 0
                ? Math.Min(rechargeRemaining, cycle) : cycle;
            // Round upwards so 0.01 seconds remaining does not falsely look ready.
            return new(Format("Charging", Math.Ceiling(remaining * 10) / 10), text("AutoCharge"), true, true, false, wear, wearState) {
                Compact = true, LoadedText = (Math.Ceiling(remaining * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture), CapacityText = "s",
                ReserveText = "", Status = "充能", Icon = gun.Name + "_slot",Fraction=1-(float)(remaining/cycle),MagazineIcon="generic_bullet"
            };
        }
        bool shells = ScReloadTransaction.AmmoKind(gun) == ScAmmoBlock.Shell;
        int reserve = ScInventoryTransaction.Count(inventory, ScAmmoBlock.Value(shells ? ScAmmoBlock.Shell : ScAmmoBlock.Magazine));
        bool tube = ScReloadTransaction.IsTube(gun.Name);
        int cost = ScReloadTransaction.RequiredFor(gun, capacity);
        string main = Format(shells ? "Shells" : "Magazines", rounds, capacity,
            creative ? "∞" : reserve.ToString(CultureInfo.InvariantCulture));
        string detail = tube ? text("Tube") : Format(shells ? "ShellMagazine" : "WholeMagazine", cost);
        // Rounds a shrunk capacity left with this gun are shown, so they never look lost.
        if (loaded.ReserveOverflowRounds > 0) detail += $" · 本枪余弹 {loaded.ReserveOverflowRounds}";
        if (reloading) detail = text(tube ? "Loading" : "Reloading") + " · " + detail;
        bool covered = loaded.ReserveOverflowRounds >= capacity - rounds && capacity > rounds;
        return new(main, detail, rounds == 0, false, !creative && !covered && reserve < cost, wear, wearState) {
            Compact = true, LoadedText = rounds.ToString(CultureInfo.InvariantCulture), CapacityText = "/ " + capacity,
            ReserveText = (shells ? "霰弹 " : "弹匣 ") + (creative ? "∞" : reserve.ToString(CultureInfo.InvariantCulture))
                + (loaded.ReserveOverflowRounds > 0 ? " · 余弹 " + loaded.ReserveOverflowRounds : ""),
            Status = reloading ? "装填" : "", Icon = gun.Name + "_slot",Fraction=capacity>0?(float)rounds/capacity:0,
            ReserveCount=creative?"∞":reserve.ToString(CultureInfo.InvariantCulture),
            MagazineIcon=shells?"shotgun_shell":gun.Name switch {"ak47" or "galilar"=>"banana_mag","bizon"=>"bizon_tube","p90"=>"p90","negev" or "m249"=>"box","revolver"=>"revolver_loader",_=>"magazine"}
        };
    }
}

/// <summary>Passive lower-right ammunition readout, independent of the touch overlay.</summary>
public sealed class ScAmmoHud : IDisposable {
    public readonly StackPanelWidget Panel = new() {
        Name = "ScAmmoHud", Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Far,
        VerticalAlignment = WidgetAlignment.Far, IsHitTestVisible = false, IsVisible = false, Margin = new Vector2(0, 128)
    };
    public readonly LabelWidget Main = new() {
        FontScale = .65f, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center,
        TextAnchor = TextAnchor.HorizontalCenter, IsHitTestVisible = false
    };
    public readonly LabelWidget Detail = new() {
        FontScale = .42f, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center,
        TextAnchor = TextAnchor.HorizontalCenter, IsHitTestVisible = false
    };
    public readonly LabelWidget Wear = new() {
        FontScale = .48f, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center,
        TextAnchor = TextAnchor.HorizontalCenter, IsHitTestVisible = false
    };
    ContainerWidget host;
    public readonly ScMagazineWidget Magazine = new();
    static IEnumerable<Widget> Obstacles(ContainerWidget container) {
        foreach (var w in container.Children) {
            if (!w.IsVisibleGlobal || w.Name == "ScAmmoHud") continue;
            if (w is ButtonWidget || w.Name is "ShortInventory" or "BottomBarsContainer" or "MoveButtonsContainer" or "MovePadContainer" or "LookPadContainer") yield return w;
            else if (w is ContainerWidget c) foreach (var child in Obstacles(c)) yield return child;
        }
    }
    public static Vector2 FindCenter(Vector2 area, Vector2 size, BoundingRectangle[] obstacles) {
        float x=Math.Max(0,(area.X-size.X)/2),y=Math.Max(0,area.Y-size.Y-12);
        // Stay centred, directly above the highest overlapping control in the lower HUD.
        foreach(var r in obstacles.OrderByDescending(r=>r.Max.Y))
            if(x<r.Max.X+6 && x+size.X>r.Min.X-6 && y<r.Max.Y+6 && y+size.Y>r.Min.Y-6)
                y=Math.Max(0,r.Min.Y-size.Y-6);
        return new Vector2(x,y);
    }
    public static Vector2 FindCorner(Vector2 area,Vector2 size,BoundingRectangle[] obstacles) {
        float x=Math.Max(0,area.X-size.X-12),y=Math.Max(0,area.Y-size.Y-12);
        foreach(var r in obstacles.OrderByDescending(r=>r.Max.Y))
            if(x<r.Max.X+6&&x+size.X>r.Min.X-6&&y<r.Max.Y+6&&y+size.Y>r.Min.Y-6)y=Math.Max(0,r.Min.Y-size.Y-6);
        return new(x,y);
    }
    void Position() {
        if (host is null || host.ActualSize.X <= 1) return;
        Vector2 size = new(Math.Max(100, Panel.ActualSize.X), Math.Max(92, Panel.ActualSize.Y));
        var obstacles = Obstacles(host).Select(w => new BoundingRectangle(host.ScreenToWidget(w.GlobalBounds.Min), host.ScreenToWidget(w.GlobalBounds.Max))).ToArray();
        Vector2 corner = FindCorner(host.ActualSize, size, obstacles);
        Panel.MarginRight = Math.Max(0,host.ActualSize.X-corner.X-size.X); Panel.MarginLeft = 0;
        Panel.MarginBottom = Math.Max(0, host.ActualSize.Y - corner.Y - size.Y); Panel.MarginTop = 0;
    }
    public ScAmmoHud() {
        Panel.Children.Add(Magazine);Panel.Children.Add(Main); Panel.Children.Add(Detail); Panel.Children.Add(Wear);
        ApplyDeviceScale(ScMobileControls.IsMobileDevice);
    }
    public void ApplyDeviceScale(bool mobile) { Main.FontScale=mobile?.56f:.65f;Detail.FontScale=mobile?.38f:.42f;Wear.FontScale=mobile?.42f:.48f; }
    public bool Attach(ComponentGui gui) {
        host = gui.ControlsContainerWidget;
        if (host is null) return false;
        host.Children.Add(Panel);
        return true;
    }
    public void Show(ScAmmoReadout readout) {
        Main.Text = readout.Compact ? readout.LoadedText+" "+readout.CapacityText : readout.Main;
        Magazine.Fraction=readout.Fraction;Magazine.Icon=readout.MagazineIcon;Magazine.Count.Text=readout.ReserveCount;Magazine.IsVisible=readout.Compact;
        Detail.Text = readout.Compact ? readout.Status ?? "" : readout.Detail;
        Detail.IsVisible = !string.IsNullOrEmpty(Detail.Text);
        Main.Color = readout.Charging ? new Color(255, 210, 120) : readout.Empty ? new Color(255, 120, 110) : Color.White;
        Detail.Color = readout.Insufficient ? new Color(255, 190, 120) : new Color(215, 215, 215);
        Wear.Text = readout.Wear ?? "";
        Wear.IsVisible = false; // inventory slot now owns the durability percentage
        Wear.Color = readout.WearState == 2 ? new Color(255, 90, 80) : readout.WearState == 1 ? new Color(255, 170, 60) : new Color(215, 215, 215);
        Panel.IsVisible = true;
        Position();
    }
    public void Hide() => Panel.IsVisible = false;
    public void Dispose() => Panel.ParentWidget?.Children.Remove(Panel);
}
