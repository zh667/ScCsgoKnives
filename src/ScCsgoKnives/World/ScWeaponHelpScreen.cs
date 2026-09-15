using Engine;
using System.Xml.Linq;
namespace Game;

/// <summary>Keep the API recipe-screen return type, but replace only its recipe content.
/// API 1.9 uses a LEFT navigation rail, with PanoramaWidget behind the entire page.</summary>
public abstract class ScWeaponHelpScreen : RecipaediaRecipesScreen {
    protected readonly CanvasWidget Body = new() { Name = "ScWeaponHelp.Body", Size = new Vector2(float.PositiveInfinity), Margin = new Vector2(12, 12), ClampToBounds = true };
    readonly ButtonWidget m_nativeBack;
    Screen m_returnScreen;
    readonly Screen m_originalRecipeScreen = ScreensManager.FindScreen<Screen>("RecipaediaRecipes");
    Screen m_nativeOwner;
    protected int LastValue;
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
        if (parameters is { Length: > 0 } && parameters[0] is int v) LastValue = v;
        if (previous is not ScWeaponHelpScreen child || child.m_returnScreen != this)
            if (previous != this) m_returnScreen = previous;
        if (m_returnScreen is RecipaediaScreen native) m_nativeOwner = native.m_previousScreen;
        Register(this);
        // Native GetBlockRecipeScreen assigns this temporary object to its global recipe slot.
        // Restore the screen that existed BEFORE construction, including RecipaediaEX's instance.
        if (ReferenceEquals(ScreensManager.FindScreen<Screen>("RecipaediaRecipes"), this) && m_originalRecipeScreen is not null)
            ScreensManager.m_screens["RecipaediaRecipes"] = m_originalRecipeScreen;
        // Do not invoke the vanilla recipe lifecycle: its controls have been detached.
    }

    static string NameOf(ScWeaponHelpScreen screen) => screen is ScGunAttributesScreen ? "ScCsgo.Attributes" : "ScCsgo.Assembly";
    static void Register(ScWeaponHelpScreen screen) {
        string name = NameOf(screen);
        ScreensManager.m_screens[name] = screen;
        var extension = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RecipaediaEX.UI.RecipaediaEXScreen", false)).FirstOrDefault(t => t is not null);
        extension?.GetMethod("RegisterChildScreen", new[] { typeof(string) })?.Invoke(null, new object[] { name });
    }
    public static void Open(bool attributes, int value) {
        if (ScreensManager.CurrentScreen is ScWeaponHelpScreen help && help.m_returnScreen is ScWeaponHelpScreen parent
            && (parent is ScGunAttributesScreen) == attributes) {
            ScreensManager.SwitchScreen(parent, value);
            return;
        }
        ScWeaponHelpScreen screen = attributes ? new ScGunAttributesScreen(value) : new ScAssemblyRecipesScreen();
        Register(screen);
        ScreensManager.SwitchScreen(NameOf(screen), value);
    }
    public static void AfterEnter(Screen screen) {
        if (ScreensManager.PreviousScreen is ScWeaponHelpScreen help && screen == help.m_returnScreen && screen is RecipaediaScreen native)
            native.m_previousScreen = help.m_nativeOwner;
    }

    protected bool BackRequested => Input.Back || Input.Cancel || m_nativeBack.IsClicked;
    protected Screen PrepareBackNavigation() {
        Screen target = m_returnScreen ?? ScreensManager.FindScreen<Screen>("Recipaedia");
        return target;
    }
    protected void GoBack() => ScreensManager.SwitchScreen(PrepareBackNavigation(), LastValue);
    protected static BevelledRectangleWidget NativeArea() => new() {
        Style = ContentManager.Get<XElement>("Styles/Area"), IsHitTestVisible = false
    };

    public override void ArrangeOverride() {
        base.ArrangeOverride();
        if (m_loggedSize != ActualSize) {
            m_loggedSize = ActualSize;
            KnifeLog.Trace($"[UI_LAYOUT native-rail-v2] page={GetType().Name} logical={ActualSize} body={Body.GlobalBounds} back={m_nativeBack.GlobalBounds}");
        }
    }
}
