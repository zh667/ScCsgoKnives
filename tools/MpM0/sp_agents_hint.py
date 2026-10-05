"""Agents prerequisite hint (2026-10-01 user request: with an agent-related package installed, tell the player to install the
prerequisite mods; when they are missing nothing is force-disabled, it is only a hint).

On the engine M0_ENGINE selects (standalone: 131), one game session per case:
  A  the package(s) under test without NekoMeko Model / Neorxna: the hint dialog is on the main menu, the CS mod and the
     agents' loader are loaded and enabled, the log names what is missing; "不再提示" stores the choice; a second start
     of the same copy shows no dialog (the choice is kept) but still logs the missing prerequisites;
  B  the same package(s) with NekoMeko Model 1.1 and Neorxna 1.4 (read-only copies of the files in the player's
     1.9.3.1 Mods folder, when present): no hint, and the built-in T/CT appearance is enabled.

Usage: sp_agents_hint.py <label> <package> [<package> ...]   (e.g. the full package, or the lite core + the agents package)
"""
import json, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu

PROVIDERS = [Path(r"D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods/[API1.9]NekoMeko Model-v1.1.scmod"),
             Path(r"D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods/[API1.9.2.1]Neorxna-v1.4 (2).scmod")]
STATE = ('var mods = string.Join(",", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName + (m.IsDisabled ? "(disabled)" : ""))); '
         'var loader = ModsManager.ModLoaders.Any(l => l.GetType().FullName == "Game.TacticalModLoader"); '
         'var dialog = DialogsManager.Dialogs.OfType<Game.MessageDialog>().FirstOrDefault(d => d.m_largeLabelWidget.Text.Contains("缺少前置模组")); '
         'var muted = ModsManager.Configs != null && ModsManager.Configs.TryGetValue("zh667.ScCsgoTactical.PrerequisiteHintMuted", out var v) ? v : "-"; '
         'return System.Text.Json.JsonSerializer.Serialize(new { mods, loader, dialog = dialog == null ? null : dialog.m_smallLabelWidget.Text, muted, screen = ScreensManager.CurrentScreen?.GetType().Name });')
UNMUTE = 'ModsManager.Configs.Remove("zh667.ScCsgoTactical.PrerequisiteHintMuted"); ModsManager.SaveConfigs(); return "unmuted";'
MUTE = ('var dialog = DialogsManager.Dialogs.OfType<Game.MessageDialog>().FirstOrDefault(d => d.m_largeLabelWidget.Text.Contains("缺少前置模组")); '
        'if (dialog == null) return "no dialog"; dialog.m_handler(Game.MessageDialogButton.Button2); DialogsManager.HideDialog(dialog); '
        'return ModsManager.Configs.TryGetValue("zh667.ScCsgoTactical.PrerequisiteHintMuted", out var v) ? v : "not stored";')


def main(label, *packages):
    def resolve(p):
        # ASCII tokens for the release stage's candidates (the job submitter passes ASCII only): stage-lite:<tag>, stage-agents:<tag>
        if p.startswith(("stage-lite:", "stage-agents:", "stage-full:")):
            kind, tag = p.split(":", 1); suffix = {"stage-lite": "轻量包.scmod", "stage-agents": "探员包.scmod", "stage-full": "全量包.scmod"}[kind]
            found = sorted((m0.ROOT / ".tmp/completion-140-20260929" / tag / "candidate").glob("*" + suffix))
            if not found: raise FileNotFoundError(f"no {suffix} candidate in stage {tag}")
            return found[0]
        return {"output-lite": m0.LITE, "output-full": m0.FULL}.get(p) or Path(p)
    pkgs = [resolve(p) for p in packages]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"agents-hint-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"agents-hint-{label}", "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "checks": []}
    def step(name, value):
        R["steps"].append({"step": name, "value": value}); print(f"{name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    def session(name, mods):
        g = m0.game("server", case_dir, mods)
        try:
            to_menu(g); time.sleep(3)
            state = json.loads(step(f"{name}: main menu state", g.func(STATE)))
            lines = [l for l in g.lines if "[CS_AGENTS]" in l or "玩家外观" in l]
            return g, state, lines
        except Exception:
            g.close(); raise
    try:
        g, a, lines = session("A without the prerequisites", pkgs)
        try:
            check("A: the CS mod and the agents' loader are loaded, nothing disabled", a["loader"] and "zh667.ScCsgoKnives" in a["mods"] and "(disabled)" not in a["mods"], a["mods"])
            check("A: the hint dialog is on the main menu and names the missing prerequisites", a["dialog"] is not None and "NekoMeko Model" in a["dialog"] and "Neorxna" in a["dialog"] and a["screen"] == "MainMenuScreen", a["dialog"])
            check("A: Game.log names the missing prerequisites ([CS_AGENTS])", any("[CS_AGENTS]" in l and "未安装" in l for l in lines), lines[:3])
            check("A: \"不再提示\" stores the choice", step("A: mute", g.func(MUTE)) == "true")
            R.setdefault("errors", []).extend(g.errors())
        finally:
            g.close()
        # Second start of the same isolated copy: the engine's Configs file is kept between the two sessions of this run.
        g, a2, lines2 = session("A2 restarted after \"不再提示\"", pkgs)
        try:
            check("A2: no dialog after \"不再提示\", the log still names the missing prerequisites", a2["dialog"] is None and a2["muted"] == "true" and any("[CS_AGENTS]" in l for l in lines2), f"{a2} {lines2[:2]}")
            R["errors"].extend(g.errors())
            step("A2: un-mute for case B", g.func(UNMUTE))   # B must show that the providers, not the mute, keep the dialog away
        finally:
            g.close()
        providers = [p for p in PROVIDERS if p.exists()]
        if len(providers) == 2:
            g, b, lines3 = session("B with NekoMeko Model and Neorxna", pkgs + providers)
            g.close()
            # The appearance line is written while the mods initialise, before the automation's log port exists: read the
            # engine's own Game.log (copied into the run folder when the session closes).
            game_log = (case_dir / "server-Game.log").read_text("utf-8", errors="replace") if (case_dir / "server-Game.log").exists() else ""
            # The engine appends every start of the copy to the same Game.log: only B's own part (from its last start-up line).
            game_log = game_log[game_log.rfind("Survivalcraft starting up"):] if "Survivalcraft starting up" in game_log else game_log
            check("B: with NekoMeko Model 1.1 and Neorxna 1.4 (not muted): no dialog, no [CS_AGENTS] warning, the built-in T/CT appearance is enabled",
                  b["dialog"] is None and b["muted"] != "true" and b["loader"] and "[CS_AGENTS]" not in game_log and "已启用内置 T/CT 玩家外观" in game_log,
                  f"{b} appearance line {'present' if '已启用内置 T/CT 玩家外观' in game_log else 'absent'}, agents warning {'present' if '[CS_AGENTS]' in game_log else 'absent'}")
        else:
            check("B: provider mods present on this machine", False, [str(p) for p in PROVIDERS if not p.exists()])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    m0.RUNTIME_OWNER.release()
    failed = [c for c in R["checks"] if not c["ok"]]
    out = RESULTS / f"agents-hint-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed; errors {len(R.get('errors', []))}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
