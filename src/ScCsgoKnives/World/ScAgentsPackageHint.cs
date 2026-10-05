using Engine;
namespace Game;

/// <summary>Round 12 (2026-10-01 user request: "我只安装了轻量包，没有提示我缺少探员包（当然这只是一个提示，不影响运行，然后不要影响那个
/// 模组冲突或者缺少前置的提示，我们的提示放在他们后面…探员包提示可以给一个超链接：https://files.zh667.cn）").
/// A Lite core without a running agents package tells the player where to get it: once per game start, only after the main
/// menu has shown no other dialog for <see cref="QuietFrames"/> frames, so the engine's own warnings (disabled or conflicting
/// mods, missing prerequisites; queued from MainMenuScreen.Enter, before mods see the screen) come first and are left as
/// they are. Nothing is disabled or changed. The full package carries the agents and never asks. "不再提示" is kept in the
/// engine's ModsManager.Configs. Before round 12 no package had this hint: the agents' own prerequisite hint
/// (TacticalPrerequisites: NekoMeko Model / Neorxna) runs only where the agents code is, so the order in which the full and
/// Lite packages were installed made no difference.</summary>
public static class ScAgentsPackageHint {
    public const string Url = "https://files.zh667.cn";
    /// <summary>ModsManager.Configs key: the player chose not to be reminded.</summary>
    public const string MutedKey = "zh667.ScCsgoKnives.AgentsPackageHintMuted";
    /// <summary>Main-menu frames in a row with no dialog before the hint opens (about half a second).</summary>
    public const int QuietFrames = 30;
    static bool s_started;
    static int s_quiet;

    /// <summary>This core is the Lite edition and no agents package runs with it.</summary>
    public static bool Applies => ScOptionalAgents.Split && !ScOptionalAgents.Available;
    public static bool Muted => ModsManager.Configs is { } c && c.TryGetValue(MutedKey, out var value) && value == "true";
    /// <summary>An agents package file is installed but not running (disabled by the player or the engine, or not the
    /// matching release); the core's data-keeping placeholder has no file.</summary>
    static bool InstalledButOff => ModsManager.ModListAll?.Any(m => m?.modInfo?.PackageName == "zh667.ScCsgoTactical" && !string.IsNullOrEmpty(m.ModFilePath)) == true;

    /// <summary>The main menu was entered: arms the hint once per game start when it applies.</summary>
    public static void OnMainMenu() {
        if (s_started) return;
        s_started = true;
        if (!Applies) return;
        KnifeLog.Warning("[CS_AGENTS] 轻量包未检测到运行中的探员包：同伴、敌对小队和 T/CT 玩家外观等探员内容不可用，枪械和刀具照常。下载：" + Url);
        if (Muted) return;
        Window.Frame += Pump;
    }

    /// <summary>One frame of waiting: true once the main menu has had no dialog for <see cref="QuietFrames"/> frames in a
    /// row. Any dialog, or another screen, starts the count again.</summary>
    public static bool Ready(bool onMainMenu, int dialogs, ref int quiet) {
        quiet = onMainMenu && dialogs == 0 ? quiet + 1 : 0;
        return quiet >= QuietFrames;
    }

    static void Pump() {
        try {
            if (!Ready(ScreensManager.CurrentScreen is MainMenuScreen, DialogsManager.Dialogs.Count, ref s_quiet)) return;
            Window.Frame -= Pump;
            DialogsManager.ShowDialog(null, new ScAgentsPackageHintDialog(InstalledButOff));
        }
        catch (Exception e) {
            Window.Frame -= Pump;
            KnifeLog.Warning("[CS_AGENTS] 探员包提示无法显示：" + e.Message);
        }
    }

    internal static void Mute() {
        try { ModsManager.Configs[MutedKey] = "true"; ModsManager.SaveConfigs(); }
        catch (Exception e) { KnifeLog.Warning("[CS_AGENTS] 无法保存“不再提示”：" + e.Message); }
    }
}

/// <summary>The hint itself: what is missing, what the link is (2026-10-01 user: "这个链接得说明一下是什么，不然玩家都不知道"),
/// the download page as a link, "知道了" / "不再提示".</summary>
public sealed class ScAgentsPackageHintDialog : Dialog {
    readonly LabelWidget m_title, m_text, m_linkCaption;
    readonly LinkWidget m_link;
    readonly ButtonWidget m_ok, m_mute;
    bool m_done;
    public ScAgentsPackageHintDialog(bool installedButOff) {
        HorizontalAlignment = VerticalAlignment = WidgetAlignment.Center;
        Children.Add(ScGunUi.Frame());
        m_title = ScGunUi.Label(installedButOff ? "CS武器：探员包未启用" : "CS武器：未安装探员包", 1f, ScGunUi.Accent);
        Children.Add(m_title);
        m_text = ScGunUi.Note((installedButOff ? "探员包已安装但没有运行（被禁用，或与轻量包不是同一版本）。" : "当前只安装了轻量包。")
            + "同伴、敌对小队和 T/CT 玩家外观需要同版本的探员包；枪械和刀具不受影响。");
        m_text.FontScale = .85f;
        Children.Add(m_text);
        string version = string.IsNullOrEmpty(ScCompatibility.ActiveBuild) ? "" : "（" + ScCompatibility.ActiveBuild + "）";
        m_linkCaption = ScGunUi.Note($"探员包下载页：点击下面的链接用浏览器打开，下载与轻量包同版本{version}的探员包。");
        m_linkCaption.FontScale = .8f;
        Children.Add(m_linkCaption);
        m_link = new LinkWidget { Text = ScAgentsPackageHint.Url, Url = ScAgentsPackageHint.Url, Color = ScGunUi.Accent };
        Children.Add(m_link);
        m_ok = ScGunUi.Button("知道了", 160); m_mute = ScGunUi.Button("不再提示", 160);
        Children.Add(m_ok); Children.Add(m_mute);
    }
    public override void MeasureOverride(Vector2 available) {
        float w = Math.Min(560, Math.Max(280, available.X - 24)), h = Math.Min(360, Math.Max(300, available.Y - 24));
        Size = new Vector2(w, h);
        SetWidgetPosition(m_title, new Vector2(16, 14)); m_title.Size = new Vector2(w - 32, 36);
        SetWidgetPosition(m_text, new Vector2(16, 56)); m_text.Size = new Vector2(w - 32, h - 228);
        SetWidgetPosition(m_linkCaption, new Vector2(16, h - 166)); m_linkCaption.Size = new Vector2(w - 32, 52);
        SetWidgetPosition(m_link, new Vector2(16, h - 110)); m_link.Size = new Vector2(w - 32, 36);
        float bw = Math.Min(170, (w - 44) / 2); m_ok.Size = m_mute.Size = new Vector2(bw, 48);
        SetWidgetPosition(m_mute, new Vector2(16, h - 62)); SetWidgetPosition(m_ok, new Vector2(w - 16 - bw, h - 62));
        base.MeasureOverride(available);
    }
    public override void Update() {
        if (m_done) return;
        if (m_mute.IsClicked) { ScAgentsPackageHint.Mute(); Dismiss(); return; }
        if (m_ok.IsClicked || Input.Cancel || Input.Back) Dismiss();
    }
    void Dismiss() { m_done = true; DialogsManager.HideDialog(this); }
}
