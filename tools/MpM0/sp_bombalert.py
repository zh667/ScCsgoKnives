"""1.6.0 (user request 2026-11-25): the conspicuous enemy-C4 alert is a switch on the game's mod settings page, off by
default. In-game, isolated and muted (TEST-ONLY):
  - the item is registered under zh667.ScCsgoKnives / Alerts / BombAlert, default off, with the page and item texts from
    the package's Lang (the current language), and pictures of the mod settings page;
  - off: a planted enemy bomb gives no large message, no top line (the 1.5.0 behaviour: plant sound, beeps, defuse prompt);
  - turned on while the bomb is planted: the top line and the large message appear; turned off again: the line goes;
  - on and saved, the game restarted: still on, and the saved bomb is announced after loading the world again.
Usage: M0_ENGINE=131|194|195|1951 python sp_bombalert.py <label> <core package>[+<package>...]"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, SURVIVAL, teleport, STAND
from sp_gun_visuals import window_of, picture
from sp_deathmatch import FPP
from sp_engine_compat import STARTER_NONE, EMPTY, DURABLE, CLOSE_DIALOGS
from sp_round9 import STRIP, MUTE, ALERT, load

m0.firewall_preflight = lambda: {"skipped": "standalone sp run; no MP role started", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)

PATH = '"zh667.ScCsgoKnives", "Alerts", "BombAlert"'
SETTING = f'bool ok = Game.ModSettingsManager.TryGet(out bool v, {PATH}); return ok + " " + v;'
def set_alert(on, save=False): return (f'Game.ModSettingsManager.Set(new[] {{ {PATH} }}, {str(on).lower()}); '
                                      + ('Game.ModSettingsManager.SaveModSettings(); ' if save else '') + f'return Game.ScBombAlertSetting.Enabled.ToString();')
TEXTS = ('var page = Game.ModSettingsManager.GetPages("zh667.ScCsgoKnives").Select(p => p.Id); '
         'string R(string[] chain, string field) => Game.ModSettingLocalizer.ResolveText("zh667.ScCsgoKnives", chain, field, null, false); '
         'return string.Join(",", page) + " | " + R(new[] { "Alerts" }, "Name") + " | " + R(new[] { "Alerts", "BombAlert" }, "Name") + " | " + R(new[] { "Alerts", "BombAlert" }, "Description");')
OPEN_PAGE = ('var s = Game.ScreensManager.CurrentScreen as Game.ModSettingsScreen; if (s == null) return "not on the mod settings screen"; var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
             'var stack = typeof(Game.ModSettingsScreen).GetField("m_pageStack", bf).GetValue(s); '
             'stack.GetType().GetMethod("Push").Invoke(stack, new object[] { System.ValueTuple.Create("zh667.ScCsgoKnives", new[] { "Alerts" }, "CS") }); '
             'typeof(Game.ModSettingsScreen).GetMethod("NavigateCurrent", bf).Invoke(s, null); return "page";')
# The volume the bomb's latest beep was given, divided by the sound setting (set to 0.01 for the measurement: practically
# silent, but above 0 so the game still creates the sound), and the listener's distance to the bomb.
CUE_VOLUME = PROJECT + ('Game.SettingsManager.SoundsVolume = 0.01f; var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); var b = bombs.Bombs[0]; '
    'var f = b.GetType().GetField("Cue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); '
    'var audio = project.FindSubsystem<Game.SubsystemAudio>(true); float d = audio.CalculateListenerDistance(b.Charge.Position); '
    'var cue = f.GetValue(b) as Engine.Audio.BaseSound; return System.FormattableString.Invariant($"{(cue == null ? -1f : cue.Volume / 0.01f):0.000} {d:0.0}");')
# How much of the screen the C4 text takes: the game's large message (earlier builds) and our hotbar message, as shares of
# the game view's height and area.
TEXT_SHARE = MAIN + ('var gui = pl.ComponentGui; var root = pl.GameWidget.ActualSize; float H(Engine.Vector2 v) => v.Y / root.Y; float A(Engine.Vector2 v) => v.X * v.Y / (root.X * root.Y); '
    'var big = gui.m_largeMessageWidget.IsVisible ? gui.m_largeMessageWidget.AllChildren.OfType<Game.LabelWidget>().Where(l => l.IsVisible && l.Text != null && l.Text.Contains("C4")).ToList() : new System.Collections.Generic.List<Game.LabelWidget>(); '
    'var small = gui.m_messageWidget.m_messages.Select(m => m.LabelWidget).Where(l => l.Text != null && l.Text.Contains("C4")).ToList(); '
    'return System.FormattableString.Invariant($"view {root.X:0}x{root.Y:0} | large {big.Sum(l => H(l.ActualSize)):0.000}h {big.Sum(l => A(l.ActualSize)):0.000}a | hotbar {small.Sum(l => H(l.ActualSize)):0.000}h {small.Sum(l => A(l.ActualSize)):0.000}a");')
# How far each bomb's news has gone for this player (the subsystem's own record: 1 first message, 2 the 20 s one, 3 the 10 s one).
STAGES = MAIN + ('var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
    'var alerts = (System.Collections.IDictionary)typeof(Game.SubsystemTacticalBombs).GetField("alerts", bf).GetValue(bombs); if (!alerts.Contains(pl)) return "none"; var view = alerts[pl]; '
    'var stage = (System.Collections.Generic.Dictionary<string, int>)view.GetType().GetField("Stage").GetValue(view); return stage.Count == 0 ? "none" : string.Join(",", stage.Values);')
C4_LINES = MAIN + 'return pl.ComponentGui.m_messageWidget.m_messages.Count(m => m.LabelWidget.Text != null && m.LabelWidget.Text.Contains("C4")).ToString();'
TO_MOD_SETTINGS = ('var f = typeof(Game.ScreensManager).GetField("m_screens", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); '
                   'var all = (System.Collections.IDictionary)f.GetValue(null); var s = all.Values.OfType<Game.ModSettingsScreen>().FirstOrDefault(); if (s == null) return "no screen"; Game.ScreensManager.SwitchScreen(s); return "switched";')


def main(label, packages):
    pkgs = [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"bombalert-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case_dir / "pictures").mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "checks": [], "pictures": [], "data": {}}
    T0 = time.time(); g = None; h = None
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:600], flush=True)
    def step(name, value=None): print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:600]}", flush=True); return value
    def snap(name):
        nonlocal h
        try:
            h = h or window_of(g.proc.pid); im = picture(h); path = case_dir / "pictures" / f"{name}.jpg"; im.save(path, quality=88); R["pictures"].append(str(path)); return str(path)
        except Exception as e: R["pictures"].append(f"{name}: {e}"); return None
    def F(code, timeout=60): return g.func(code, timeout)
    def start(name):
        nonlocal g, h
        g = m0.game(name, case_dir, pkgs); h = None; R.setdefault("engine", g.engine_info()); to_menu(g); F(MUTE)
    def enter(directory=None):
        if directory is None:
            m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
            m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
            poll(g, SURVIVAL, lambda v: v == "Survival", 20)
            m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
            m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        else:
            m = g.mark(); F(load(directory)); g.wait('Entered screen "Game"', 600, m)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); F(KEEP_ACTIVE); F(MUTE); F(DURABLE); time.sleep(1.5); F(STARTER_NONE); F(CLOSE_DIALOGS)
    def alert(): return F(ALERT)
    try:
        start("a")
        R["mods"] = step("mods", F('return string.Join("; ", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));'))
        first = step("setting at first start", F(SETTING)); texts = step("page and texts", F(TEXTS)); R["data"]["texts"] = texts
        check("registered under zh667.ScCsgoKnives/Alerts/BombAlert and off by default", first == "True False", first)
        check("the page and the item have their names and description from the package's Lang", "Alerts" in texts.split(" | ")[0] and all(len(t.strip()) > 1 and t.strip() not in ("Alerts", "BombAlert") for t in texts.split(" | ")[1:]), texts)
        step("to the mod settings", F(TO_MOD_SETTINGS)); time.sleep(1.5); snap("mod-settings-root")
        step("open the Alerts page", F(OPEN_PAGE)); time.sleep(1.2); snap("mod-settings-alerts-off")
        F(set_alert(True)); time.sleep(.8); snap("mod-settings-alerts-on"); F(set_alert(False))
        m = g.mark(); g.cmd('EXEC ScreensManager.SwitchScreen("MainMenu");'); g.wait('Entered screen "MainMenu"', 60, m)

        enter()
        t0 = time.time(); r = F(STRIP)
        while r == "wait" and time.time() - t0 < 120: time.sleep(1); r = F(STRIP)
        x, y, z = [int(v) for v in r.split()]; directory = F(MAIN + 'return project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName;')
        F(EMPTY); F(teleport(x, y, z)); F(STAND); time.sleep(1); F(FPP)
        # a wall between the player and the bomb, so the marker shows through terrain when on
        F(MAIN + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); int g = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index); for (int dx = -2; dx <= 2; dx++) for (int dy = 0; dy <= 3; dy++) t.ChangeCell({x} + dx, {y} + dy, {z} - 6, g); return "wall";')
        planted = F(PROJECT + f'var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); bool ok = bombs.PlantFrom(new Engine.Vector3({x} + .5f, {y}, {z} - 12 + .5f), 0f, false); return ok + " " + bombs.Bombs.Count;')
        step("planted 12 blocks ahead behind a wall", planted); time.sleep(1.5)
        off = step("off", alert()); snap("bomb-off")
        cue = F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true).Bombs[0].Charge; return System.FormattableString.Invariant($"{b.Remaining:0.0} {b.BeepLeft:0.00}");')
        check("off (default): no top line and no large message for a planted enemy bomb; the bomb ticks (beeps)", planted.startswith("True") and off.startswith("- ||") and "C4" not in off and float(cue.split()[0]) < 40, {"alert": off, "fuse/beep": cue})
        F(CUE_VOLUME); time.sleep(1.6); vol_off = step("beep volume, switch off (gain, distance)", F(CUE_VOLUME))
        on = step("turned on while planted", (F(set_alert(True)), time.sleep(.8), alert())[2]); snap("bomb-on")
        R["data"]["textShare"] = share = step("C4 text share of the screen (landscape)", F(TEXT_SHARE))
        check("on: the plant news is an ordinary message above the hotbar, no large centre text", " large 0.000h" in share and " hotbar 0.000h" not in share, share)
        time.sleep(1.6); vol_on = step("beep volume, switch on (gain, distance)", F(CUE_VOLUME)); F(MUTE)
        R["data"]["beepVolume"] = {"off": vol_off, "on": vol_on}
        try:
            (g_off, d_off), (g_on, d_on) = [tuple(map(float, v.split())) for v in (vol_off, vol_on)]
            check("the beeps are louder with the switch on (logged gain at the same distance), 1.5.0 level with it off",
                  g_off > 0 and g_on > g_off * 1.5 and g_on <= 1.0 and abs(d_off - d_on) < .5, R["data"]["beepVolume"])
        except Exception as e: check("the beeps are louder with the switch on", False, f"{vol_off} / {vol_on}: {e}")
        check("turned on while the bomb is planted: the news appears above the hotbar (no line at the top)", on.split(" || ")[0] == "-" and "C4" in on.split(" || ")[1], on)
        F(set_alert(False)); time.sleep(.6); n0 = F(C4_LINES)
        F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); b.Bombs[0].Charge.Remaining = 19.5f; return "19.5";'); time.sleep(1.5)
        off2 = step("turned off again, bomb at 19.5 s", (n0, F(C4_LINES), alert())); time.sleep(3); snap("bomb-off-again")
        check("turned off again: no further news (the 20 s message does not come while off)", int(off2[1]) <= int(off2[0]), off2)
        F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); b.Bombs[0].Charge.Remaining = 35f; return "35";')
        F(set_alert(True)); time.sleep(.6); F(set_alert(False))
        F(MAIN + 'Engine.Window.Size = new Engine.Point2(540, 960); return "portrait";'); time.sleep(1)
        # a second bomb so that its first message shows in the portrait window too
        step("second bomb", F(PROJECT + f'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); return b.PlantFrom(new Engine.Vector3({x} + 3.5f, {y}, {z} - 12 + .5f), 0f, false).ToString();'))
        F(set_alert(True)); time.sleep(1.2); snap("bomb-on-portrait"); R["data"]["textSharePortrait"] = step("C4 text share of the screen (portrait)", F(TEXT_SHARE))
        F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); if (b.Bombs.Count > 1) b.Bombs.RemoveRange(1, b.Bombs.Count - 1); return "one bomb";')
        stored = step("on and saved", F(set_alert(True, save=True)))
        m = g.mark(); g.cmd('EXEC GameManager.SaveProject(true, true);'); time.sleep(2); g.close(); g = None

        start("b")
        again = step("setting after the restart", F(SETTING))
        check("the setting survives a restart (still on)", again == "True True", again)
        enter(directory); F(teleport(x, y, z)); F(STAND); F(FPP); time.sleep(1.5)
        reloaded = step("saved bomb after loading", (F(PROJECT + 'return project.FindSubsystem<Game.SubsystemTacticalBombs>(true).Bombs.Count.ToString();'), alert(), F(STAGES))); snap("bomb-on-after-restart")
        check("after the restart the saved bomb is announced (on): the message was posted (it may already have faded)", reloaded[0] != "0" and reloaded[2] not in ("none", ""), reloaded)
        # once more with 10 s left, and no more than that: count the C4 lines in the hotbar message area
        F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); b.Bombs[0].Charge.Remaining = 10.4f; return "10.4";'); time.sleep(1.5)
        last = step("10 s left", alert()); time.sleep(2.5)
        count = step("C4 lines in the message area", F(C4_LINES))
        snap("bomb-10s-left")
        check("with 10 s left the hotbar message comes once more, and that is all (no repeats)", ("秒爆炸" in last or "explodes in" in last) and int(count) <= 2, {"alert": last, "C4 lines": count})
        F(PROJECT + 'project.FindSubsystem<Game.SubsystemTacticalBombs>(true).Bombs.Clear(); return "cleared";')
        F(set_alert(False, save=True)); final = F(SETTING); step("back to the default", final)
        R["gameErrors"] = g.errors()[:60]; check("no game errors", not R["gameErrors"], R["gameErrors"][:5])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
        if g is not None: R.setdefault("gameErrors", g.errors()[:40])
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    out = RESULTS / f"bombalert-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
