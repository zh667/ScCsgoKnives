using System.Globalization;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>Loaded rounds and reserve items are different units; never add them together.</summary>
public sealed record ScAmmoReadout(string Main, string Detail, bool Empty, bool Charging, bool Insufficient, string Wear = "", int WearState = 0) {
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
            if (rounds > 0) return new(text("Ready"), text("SingleCharge"), false, false, false, wear, wearState);
            double remaining = double.IsFinite(rechargeRemaining) && rechargeRemaining >= 0
                ? Math.Min(rechargeRemaining, cycle) : cycle;
            // Round upwards so 0.01 seconds remaining does not falsely look ready.
            return new(Format("Charging", Math.Ceiling(remaining * 10) / 10), text("AutoCharge"), true, true, false, wear, wearState);
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
        return new(main, detail, rounds == 0, false, !creative && !covered && reserve < cost, wear, wearState);
    }
}

/// <summary>Read-only status in the game's bottom stack, following its scale and layout.</summary>
public sealed class ScAmmoHud : IDisposable {
    public readonly StackPanelWidget Panel = new() {
        Name = "ScAmmoHud", Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Center,
        IsHitTestVisible = false, IsVisible = false, Margin = new Vector2(0, 4)
    };
    public readonly LabelWidget Main = new() {
        FontScale = .7f, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center,
        TextAnchor = TextAnchor.HorizontalCenter, IsHitTestVisible = false
    };
    public readonly LabelWidget Detail = new() {
        FontScale = .45f, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center,
        TextAnchor = TextAnchor.HorizontalCenter, IsHitTestVisible = false
    };
    public readonly LabelWidget Wear = new() {
        FontScale = .45f, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center,
        TextAnchor = TextAnchor.HorizontalCenter, IsHitTestVisible = false
    };
    public ScAmmoHud() { Panel.Children.Add(Main); Panel.Children.Add(Detail); Panel.Children.Add(Wear); }
    public bool Attach(ComponentGui gui) {
        var bottom = gui.ShortInventoryWidget?.ParentWidget;
        if (bottom is null) return false;
        var bars = bottom.Children.Find<ContainerWidget>("BottomBarsContainer", false);
        bottom.Children.InsertBefore(bars ?? gui.ShortInventoryWidget, Panel);
        return true;
    }
    public void Show(ScAmmoReadout readout) {
        Main.Text = readout.Main;
        Detail.Text = readout.Detail;
        Main.Color = readout.Charging ? new Color(255, 210, 120) : readout.Empty ? new Color(255, 120, 110) : Color.White;
        Detail.Color = readout.Insufficient ? new Color(255, 190, 120) : new Color(215, 215, 215);
        Wear.Text = readout.Wear ?? "";
        Wear.IsVisible = !string.IsNullOrEmpty(readout.Wear);
        Wear.Color = readout.WearState == 2 ? new Color(255, 90, 80) : readout.WearState == 1 ? new Color(255, 170, 60) : new Color(215, 215, 215);
        Panel.IsVisible = true;
    }
    public void Hide() => Panel.IsVisible = false;
    public void Dispose() => Panel.ParentWidget?.Children.Remove(Panel);
}
