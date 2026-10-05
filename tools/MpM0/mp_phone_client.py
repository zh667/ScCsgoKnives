"""deathmatch round 3 (user 2026-10-04: "用usb连接的手机和电脑测试吧（手机当服务器，电脑是客户端）"): this computer joins the
user's phone, which hosts the deathmatch world (driven separately over USB), as a 1.9.3.2_MP client with real input:
waits for the lobby, buys with the wheel and enters, waits for the match, shoots the phone's player with a held trigger,
looks at the feed and the board, waits for the results. Frames and the client's view of the match after every step.

Usage: mp_phone_client.py <label> <the phone's join code (联机码) | host ip:port> <core package spec> <deathmatch package spec> [minutes] [hunt seconds]
Isolated MP role folder; nothing is installed into the user's own game.
"""
import json, re, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import m0
from m0 import Game, MODS, TA_BUILT, RESULTS, RUNS, KEEP_ACTIVE_CLIENT, sha, to_menu, poll
from mp_m1 import MAIN, FORCE_ACTIVE, HANDSHAKE, BRIDGE
from sp_deathmatch import package, GUI_IN_SHOTS, EQUIP
from mp_deathmatch import Side, VIEW, aim_at_player

OTHER = MAIN + 'var o = players.ComponentPlayers.FirstOrDefault(c => c != pl); return o == null ? "-1" : o.PlayerData.PlayerIndex.ToString();'



def main(label, host, core, dm, minutes="40", hunt="0"):
    pkgs = [MODS / TA_BUILT.name, *map(package, core.split("+")), package(dm)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"dm-phone-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"dm-phone-{label}", "host": host, "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "checks": [], "frames": []}
    T0 = time.time(); g = None; deadline = T0 + float(minutes) * 60
    progress = case_dir / "progress.txt"   # read while the run goes on: the phone is driven by hand at the same time
    def note(entry):
        with open(progress, "a", encoding="utf-8") as f: f.write(json.dumps(entry, ensure_ascii=False)[:800] + "\n")
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); note(R["checks"][-1]); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True); return ok
    side_step = Side.step
    def step(self, name, value=None):
        v = side_step(self, name, value); note(R["steps"][-1]); return v
    Side.step = step
    def view():
        try: return json.loads(g.func(VIEW))
        except Exception: return {}
    def wait_phase(phase, label_):
        last = None
        while time.time() < deadline:
            v = view()
            if v.get("phase") != last: C.step(f"phase while waiting for {phase}", v); last = v.get("phase")
            if v.get("phase") == phase: return v
            time.sleep(2)
        raise TimeoutError(f"no {phase} before the end of the session ({label_})")
    try:
        g = Game("client1", case_dir, pkgs); to_menu(g); C = Side(g, label, case_dir, R, T0)
        m = g.mark()
        if host.isdigit():
            # the phone's join code (top right of its screen), typed into the platform's own "联机" dialog as a player
            # does: the dialog asks the centre server for the room and joins through the relay. The platform answers a
            # failed lookup with an error dialog (round 3: once, 5 s after typing); its words are kept and the code is
            # typed again, three times at most, as a player would.
            for attempt in range(3):
                C.step(f"join code (attempt {attempt + 1})", g.func('var d = new Game.ConnectServerDialog(); Game.DialogsManager.ShowDialog(null, d); '
                    'var box = (Game.TextBoxWidget)typeof(Game.ConnectServerDialog).GetField("m_ipTextBoxWidget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(d); '
                    f'box.Text = "{host}"; d.ConnectToServer(); return "typed " + box.Text;'))
                # joined: the player screen; failed: back on the main menu (round 3: "Relay disconnected: …, ConnectionFailed"
                # when this computer could not reach the relay the phone's room is on) or an error dialog on it
                said, left = None, False
                for _ in range(90):
                    time.sleep(2)
                    screen = g.func('return Game.ScreensManager.CurrentScreen?.GetType().Name ?? "none";')
                    if screen in ("PlayerScreen", "GameScreen"): break
                    left |= screen != "MainMenuScreen"
                    if screen == "MainMenuScreen":
                        # the platform's community bulletin pops up on the main menu by itself: closed, not an answer
                        g.func('foreach (var d in Game.DialogsManager.Dialogs.Where(d => d.GetType().Name == "BulletinDialog").ToList()) Game.DialogsManager.HideDialog(d); return "ok";')
                        said = g.func('return string.Join(" || ", Game.DialogsManager.Dialogs.OfType<Game.MessageDialog>().Select(d => d.GetType().Name + ": " + string.Join(" / ", d.AllChildren.OfType<Game.LabelWidget>().Select(l => l.Text).Where(t => !string.IsNullOrEmpty(t)))));')
                        if said or left: said = said or "back on the main menu"; break
                if not said: break
                C.step("the platform's answer", said)
                g.func('foreach (var d in Game.DialogsManager.Dialogs.ToList()) Game.DialogsManager.HideDialog(d); return "closed";'); time.sleep(5)
            else: raise RuntimeError(f"the join code did not connect three times: {said}")
        else:
            g.cmd(f'EXEC ScreensManager.SwitchScreen("GameLoading", null, null, System.Net.IPEndPoint.Parse("{host}"), null);')
            g.wait(f"Connected: {host}", 300, m)
        g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait("Player into playing.", 600, m); g.func(KEEP_ACTIVE_CLIENT)
        C.step("handshake", poll(g, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 60))
        check("the computer is accepted by the phone's CS layer", g.func(HANDSHAKE) == "Accepted", g.func(BRIDGE))
        C.step("settings", g.func(GUI_IN_SHOTS))
        C.step("camera", poll(g, MAIN + 'return pl.GameWidget.ActiveCamera.GetType().Name;', lambda v: v == "FppCamera", 60))
        C.view("joined"); C.frame("joined")
        v = wait_phase("Lobby", "the phone opens the lobby"); time.sleep(2); C.frame("lobby")
        C.buy(2, 0); C.frame("entered"); C.view("entered")
        v = wait_phase("Running", "the phone starts the match"); time.sleep(1.5); C.frame("running"); C.step("equip", g.func(EQUIP))
        time.sleep(4)
        host_index = int(g.func(OTHER)); C.step("the phone's player", host_index)
        killed = False
        for burst in range(10):
            aim = g.func(aim_at_player(host_index)); C.step(f"aim {burst}", aim)
            if aim.startswith("no"): time.sleep(2); continue
            g.func(FORCE_ACTIVE); g.cmd(f"DIG_RAY {aim} 40"); time.sleep(40 / 30 + 1.5)
            v = view(); C.step(f"after burst {burst}", v)
            if v.get("feed"): killed = True; break
        check("the computer's shots killed the phone's player (the client's feed has the kill)", killed, view())
        time.sleep(.6); C.frame("kill-feed")
        g.func(FORCE_ACTIVE); g.cmd("KEYDOWN Tab"); time.sleep(.9); C.frame("scoreboard"); g.cmd("KEYUP Tab")
        # then keep hunting for a while: the phone is photographed dying, respawning and reading the feed meanwhile
        hunt_end, deaths = time.time() + float(hunt), None
        while time.time() < hunt_end:
            v = view()
            if v.get("phase") != "Running": break
            row = next((r for r in v.get("rows", "").split(" | ") if r.startswith(f"{host_index}:")), "")
            d = int(re.search(r" D(\d+)", row).group(1)) if re.search(r" D(\d+)", row) else None
            if deaths is not None and d is not None and d > deaths: C.step(f"the phone's player died again (D{d})", row); time.sleep(8)
            deaths = d
            if " Alive " not in row + " ": time.sleep(1); continue
            aim = g.func(aim_at_player(host_index))
            if aim.startswith("no"): time.sleep(1); continue
            g.func(FORCE_ACTIVE); g.cmd(f"DIG_RAY {aim} 40"); time.sleep(40 / 30 + .5)
        v = wait_phase("Results", "the phone ends the match"); time.sleep(1); C.frame("results"); C.view("results")
        R["gameErrors"] = g.errors()[:40]
        R["netLines"] = [l for l in g.lines if "[ScCsgoNet]" in l or "CS_DM" in l][:80]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; note({"failure": R["failure"]}); print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            try: g.close()
            except Exception as e: print("close failed", e, flush=True)
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"dm-phone-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"steps {len(R['steps'])}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; frames {len(R['frames'])}; failure {R.get('failure')}; failed {failed}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
