"""Two processes on the API 1.9.3.2_MP engine (TEST-ONLY): the conspicuous enemy-C4 alert follows each process's own mod
setting. Host off / client on, then host on / client off, with the host's bomb planted in front of both players.
Usage: python mp_bombalert.py <label> <core package>[+<package>...]"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import poll, to_menu, join, RESULTS, RUNS, sha
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, HANDSHAKE, build_arena, teleport
from sp_gun_visuals import window_of, picture

MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
ALERT = MAIN + ('var w = pl.ComponentGui.ControlsContainerWidget.AllChildren.OfType<Game.LabelWidget>().FirstOrDefault(l => l.IsVisible && l.Text != null && l.Text.Contains("C4") && l.Text.Contains("·")); '
                'var msg = pl.ComponentGui.m_message; return (w == null ? "-" : w.Text) + " || " + (msg == null ? "-" : msg.LargeText + " / " + msg.SmallText);')
PATH = '"zh667.ScCsgoKnives", "Alerts", "BombAlert"'
def set_alert(on): return f'Game.ModSettingsManager.Set(new[] {{ {PATH} }}, {str(on).lower()}); return Game.ScBombAlertSetting.Enabled.ToString();'
BOMBS = PROJECT + 'return project.FindSubsystem<Game.SubsystemTacticalBombs>(true).Bombs.Count.ToString();'


def main(label, packages):
    pkgs = [Path(p) for p in packages.split("+")]
    case = RUNS / f"bombalert-mp-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case / "pictures").mkdir(parents=True)
    R = {"packages": {p.name: sha(p) for p in pkgs}, "checks": [], "pictures": [], "data": {}}; server = client = None
    def check(name, ok, detail=None):
        R["checks"].append(dict(name=name, ok=bool(ok), detail=detail)); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:500], flush=True)
    def snap(g, name):
        try: im = picture(window_of(g.proc.pid)); p = case / "pictures" / f"{name}.jpg"; im.save(p, quality=88); R["pictures"].append(str(p))
        except Exception as e: R["pictures"].append(f"{name}: {e}")
    try:
        server = m0.game("server", case, pkgs); R["serverEngine"] = server.engine_info(); to_menu(server); server.func(MUTE)
        m0.enter_world(server); server.func(KEEP_ACTIVE); server.func(MUTE)
        x, y, z = map(int, build_arena(server).split()); server.func(teleport(x, y, z))
        server.func(MAIN + 'foreach (var p in players.ComponentPlayers) p.ComponentHealth.AttackResilience = 10000; return "durable";')
        client = m0.game("client1", case, pkgs); to_menu(client); client.func(MUTE); join(client, server)
        check("client handshake accepted", poll(client, HANDSHAKE, lambda s: s == "Accepted", 30) == "Accepted")
        for g in [server, client]: g.func(KEEP_ACTIVE)
        client.func(teleport(x + 1, y, z)); time.sleep(2)
        R["data"]["set1"] = {"server": server.func(set_alert(False)), "client": client.func(set_alert(True))}
        planted = server.func(PROJECT + f'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); bool ok = b.PlantFrom(new Engine.Vector3({x}f + .5f, {y}f, {z + 8}f + .5f), 0f, false); return ok + " " + b.Bombs.Count;')
        poll(client, BOMBS, lambda v: v != "0", 15); time.sleep(1.5)
        s1, c1 = server.func(ALERT), client.func(ALERT); R["data"]["hostOffClientOn"] = {"planted": planted, "server": s1, "client": c1}
        snap(server, "host-off"); snap(client, "client-on")
        check("host off / client on: the client shows the alert, the host does not", planted.startswith("True") and "C4" in c1.split(" || ")[0] and s1.startswith("- ||"), R["data"]["hostOffClientOn"])
        R["data"]["set2"] = {"server": server.func(set_alert(True)), "client": client.func(set_alert(False))}; time.sleep(1)
        s2, c2 = server.func(ALERT), client.func(ALERT); R["data"]["hostOnClientOff"] = {"server": s2, "client": c2}
        snap(server, "host-on"); snap(client, "client-off")
        check("host on / client off: the host shows the alert, the client does not", "C4" in s2.split(" || ")[0] and c2.startswith("- ||"), R["data"]["hostOnClientOff"])
        R["clientErrors"] = client.errors()[:20]; R["serverErrors"] = server.errors()[:20]
        check("no game errors", not R["clientErrors"] and not R["serverErrors"], (R["clientErrors"][:3], R["serverErrors"][:3]))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [client, server]:
            if g:
                try: g.close()
                except Exception: pass
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"bombalert-mp-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["name"] for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
