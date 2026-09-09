using Engine;
using System.Xml.Linq;
namespace Game;

/// <summary>Keep the API recipe-screen return type, but replace only its recipe content.
/// API 1.9 uses a LEFT navigation rail, with PanoramaWidget behind the entire page.</summary>
public abstract class ScWeaponHelpScreen : RecipaediaRecipesScreen {
    protected readonly CanvasWidget Body = new() { Name = "ScWeaponHelp.Body", Size = new Vector2(float.PositiveInfinity), Margin = new Vector2(12, 12), ClampToBounds = true };
    readonly ButtonWidget m_nativeBack;
    Screen m_returnScreen;
    Vector2 m_loggedSize;

    protected ScWeaponHelpScreen(string title) {
        m_nativeBack = Children.Find<ButtonWidget>("TopBar.Back");
        var titleLabel = Children.Find<LabelWidget>("TopBar.Label");
        ContainerWidget rail = m_nativeBack.ParentWidget;
        while (rail.Children.Find<LabelWidget>("TopBar.Label", false) is null) rail = rail.ParentWidget;
        var panorama = Children.OfType<PanoramaWidget>().Single();
        rail.ParentWidget.Children.Remove(rail);
        Children.Clear();
        titleLabel.Text = title;
        Children.Add(panorama);
        var shell = new StackPanelWidget { Name = "ScWeaponHelp.Shell", Direction = LayoutDirection.Horizontal };
        shell.Children.Add(rail);
        shell.Children.Add(Body);
        Children.Add(shell);
    }

    public override void Enter(object[] parameters) {
        Screen previous = ScreensManager.PreviousScreen;
        if (previous is ScWeaponHelpScreen help) m_returnScreen = help.m_returnScreen;
        else if (previous != this) m_returnScreen = previous;
        // Do not invoke the vanilla recipe lifecycle: its controls have been detached.
    }

    protected bool BackRequested => Input.Back || Input.Cancel || m_nativeBack.IsClicked;
    protected Screen PrepareBackNavigation() {
        Screen target = m_returnScreen ?? ScreensManager.FindScreen<Screen>("Recipaedia");
        // SwitchScreen only pops when the destination is the TOP entry. Skipping directly over
        // attribute/recipe pages without unwinding pushes them back into history (Help <-> catalogue loop).
        if (ScreensManager.HistoryStack.Contains(target))
            while (ScreensManager.TopOfHistoryScreen != target) ScreensManager.HistoryStack.Pop();
        // Vanilla Recipaedia.Enter recognizes children by this registered instance, not by type.
        // Switching between our two pages replaces that instance; preserve its outer return owner.
        if (target is RecipaediaScreen) ScreensManager.m_screens["RecipaediaRecipes"] = this;
        return target;
    }
    protected void GoBack() => ScreensManager.SwitchScreen(PrepareBackNavigation());
    protected static BevelledRectangleWidget NativeArea() => new() {
        Style = ContentManager.Get<XElement>("Styles/Area"), IsHitTestVisible = false
    };

    public override void ArrangeOverride() {
        base.ArrangeOverride();
        if (m_loggedSize != ActualSize) {
            m_loggedSize = ActualSize;
            KnifeLog.Information($"[UI_LAYOUT native-rail-v2] page={GetType().Name} logical={ActualSize} body={Body.GlobalBounds} back={m_nativeBack.GlobalBounds}");
        }
    }
}
