"""Diagnostic (test tooling only): the server's hit test with and without lag compensation on one identical ray.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_hitprobe.py <dev tag> [lite]
Host + client1 (fixed runtime). A stationary cow 7 m in front of client1; on the server, from client1's eye to the cow's
centre: ScGunHitTest.RaycastObserved (no compensation) and RaycastCompensated with ScNetGuns.RewindFor(client1), five times.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, to_menu, join, poll
from mp_m1 import PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, teleport, EYE, near, spawn_target, STAND

def probe(i, cow): return player(i) + (
    f'var t = project.Entities.First(e => e.Id == {cow}).FindComponent<Game.ComponentBody>(); var bodies = project.FindSubsystem<Game.SubsystemBodies>(true).Bodies; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var bb = t.BoundingBox; var c = (bb.Min + bb.Max) * 0.5f; var d = Engine.Vector3.Normalize(c - eye); '
    'var plain = Game.ScGunHitTest.RaycastObserved(bodies, pl.ComponentBody, eye, d, 64f, null); '
    'var f = Game.ScNetGuns.RewindFor(pl); var offsets = f == null ? "no rewind" : string.Join(" ", f(t).Select(v => v.Length().ToString("0.000"))); '
    'var comp = Game.ScGunHitTest.RaycastCompensated(bodies, pl.ComponentBody, eye, d, 64f, null, f); '
    'var none = Game.ScGunHitTest.RaycastCompensated(bodies, pl.ComponentBody, eye, d, 64f, null, null); '
    'string H(Game.ScGunHitTest.Hit? h) => h == null ? "miss" : h.Value.Body.Entity.Id + "/" + h.Value.Part + "/" + h.Value.Distance.ToString("0.00") + "/" + h.Value.Reason; '
    'return "plain " + H(plain) + " | compensated " + H(comp) + " | compensated(null) " + H(none) + " | offsets " + offsets;')


def main(tag, edition):
    pkg = PKG / f"dev-{tag}-{edition}.scmod"; ta = MODS / TA_BUILT.name
    case_dir = RUNS / time.strftime(f"hitprobe-{tag}-%Y%m%d-%H%M%S"); case_dir.mkdir(parents=True)
    out = []
    def say(k, v): out.append((k, v)); print(f"{k}: {v}", flush=True); return v
    server = c1 = None
    try:
        server = Game("server", case_dir, [ta, pkg]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        poll(server, SURVIVAL, lambda v: v == "Survival", 20)
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        x, y, z = say("arena", build_arena(server)).split()
        c1 = Game("client1", case_dir, [ta, pkg]); to_menu(c1); join(c1, server)
        say("handshake", poll(c1, HANDSHAKE, lambda v: v == "Accepted", 30))
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        for attempt in range(10):
            c1.func(teleport(x, y, z)); time.sleep(0.7)
            if near(c1.func(EYE), x, y, z): break
        c1.func(STAND); time.sleep(1.5)
        cow = say("cow", server.func(spawn_target(0, 0, 0).replace("new Engine.Vector3(0f + 0.5f, 0f, 0f + 9.5f)", f"new Engine.Vector3({float(x) + 0.5}f, {y}f, {float(z) + 7.5}f)"))).split()[0]
        time.sleep(3)
        for k in range(5): say(f"probe {k}", server.func(probe(i1, cow))); time.sleep(0.5)
    finally:
        for g in [c1, server]:
            if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / f"hitprobe-{tag}.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), "utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
