using System.Globalization;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>Loaded rounds and reserve items are different units; never add them together.</summary>
public sealed record ScAmmoReadout(string Main, string Detail, bool Empty, bool Charging, bool Insufficient) {
    public static ScAmmoReadout Read(GunSpec gun, int value, IInventory inventory, bool creative,
        double rechargeRemaining, bool reloading, Func<string, string> text = null) {
        text ??= key => LanguageControl.Get("ScCsgoKnives", "AmmoHud", key);
        string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, text(key), args);
        int rounds = Math.Clamp(GunSpec.GetRounds(Terrain.ExtractData(value)), 0, gun.Magazine);
        if (gun.RechargeSeconds > 0) {
            if (rounds > 0) return new(text("Ready"), text("SingleCharge"), false, false, false);
            double remaining = double.IsFinite(rechargeRemaining) && rechargeRemaining >= 0
                ? Math.Min(rechargeRemaining, gun.RechargeSeconds) : gun.RechargeSeconds;
            // Round upwards so 0.01 seconds remaining does not falsely look ready.
            return new(Format("Charging", Math.Ceiling(remaining * 10) / 10), text("AutoCharge"), true, true, false);
        }
        bool shells = ScReloadTransaction.AmmoKind(gun) == ScAmmoBlock.Shell;
        int reserve = ScInventoryTransaction.Count(inventory, ScAmmoBlock.Value(shells ? ScAmmoBlock.Shell : ScAmmoBlock.Magazine));
        bool tube = ScReloadTransaction.IsTube(gun.Name);
        int cost = ScReloadTransaction.Required(gun);
        string main = Format(shells ? "Shells" : "Magazines", rounds, gun.Magazine,
            creative ? "∞" : reserve.ToString(CultureInfo.InvariantCulture));
        string detail = tube ? text("Tube") : Format(shells ? "ShellMagazine" : "WholeMagazine", cost);
        if (reloading) detail = text(tube ? "Loading" : "Reloading") + " · " + detail;
        return new(main, detail, rounds == 0, false, !creative && reserve < cost);
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
    public ScAmmoHud() { Panel.Children.Add(Main); Panel.Children.Add(Detail); }
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
        Panel.IsVisible = true;
    }
    public void Hide() => Panel.IsVisible = false;
    public void Dispose() => Panel.ParentWidget?.Children.Remove(Panel);
}
