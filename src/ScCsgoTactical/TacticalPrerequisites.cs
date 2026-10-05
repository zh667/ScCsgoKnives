using Engine;
using NuGet.Versioning;

namespace Game;

/// <summary>The agents' prerequisite mods (2026-10-01 user request): switching the player's T/CT appearance needs NekoMeko
/// Model 1.1 and Neorxna 1.4 (<see cref="TacticalAppearanceIntegration"/>). Without them nothing is disabled and every other
/// agent feature works; the player gets a hint on the main menu instead of the silent skip, once per game start, until they
/// choose "不再提示". Runs wherever the agent code runs: the agents package and the full package.</summary>
public static class TacticalPrerequisites {
    public static readonly (string Package, string Name, string Minimum)[] Required =
        [("ysf.nekomekomodel", "NekoMeko Model", "1.1"), ("ysf.neorxna", "Neorxna", "1.4")];
    /// <summary>ModsManager.Configs key: the player chose not to be reminded.</summary>
    public const string MutedKey = "zh667.ScCsgoTactical.PrerequisiteHintMuted";
    static bool s_shown;

    /// <summary>One line per prerequisite that is not usable: not installed, disabled, or older than required.</summary>
    public static List<string> Missing() {
        List<string> missing = [];
        foreach (var (package, name, minimum) in Required) {
            var mods = ModsManager.ModListAll?.Where(m => m.modInfo?.PackageName == package).ToList() ?? [];
            var usable = mods.FirstOrDefault(m => !m.IsDisabled && NuGetVersion.TryParse(m.modInfo.Version, out var v) && v >= NuGetVersion.Parse(minimum));
            if (usable != null) continue;
            if (mods.Count == 0) missing.Add($"{name} {minimum} 未安装");
            else if (mods.All(m => m.IsDisabled)) missing.Add($"{name} 已安装但被禁用");
            else missing.Add($"{name} 版本 {mods[0].modInfo.Version} 低于 {minimum}");
        }
        return missing;
    }

    public static bool Muted => ModsManager.Configs is { } c && c.TryGetValue(MutedKey, out var value) && value == "true";

    /// <summary>The main menu was entered: shows the hint once per game start when a prerequisite is not usable.</summary>
    public static void OnMainMenu() {
        if (s_shown) return;
        s_shown = true;
        var missing = Missing();
        if (missing.Count == 0) return;
        KnifeLog.Warning("[CS_AGENTS] 前置模组未就绪：" + string.Join("；", missing) + "。T/CT 玩家外观切换未启用，其他探员功能照常，模组不会被禁用。");
        if (Muted) return;
        string text = "切换 T/CT 玩家外观需要前置模组 NekoMeko Model 1.1 和 Neorxna 1.4。\n当前：" + string.Join("；", missing)
            + "。\n未安装不影响同伴、敌队、盾牌、拆弹和探员语音等其他功能，本模组也不会被禁用；安装后重启游戏即可切换外观。";
        DialogsManager.ShowDialog(null, new MessageDialog("CS武器 · 探员：缺少前置模组", text, "知道了", "不再提示", button => {
            if (button != MessageDialogButton.Button2) return;
            try { ModsManager.Configs[MutedKey] = "true"; ModsManager.SaveConfigs(); }
            catch (Exception e) { KnifeLog.Warning("[CS_AGENTS] 无法保存“不再提示”：" + e.Message); }
        }));
    }
}
