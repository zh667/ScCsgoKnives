"""Isolation experiment for the M1 drop step (TEST-ONLY): does the client's native drop reach the server, for a plain block and
for a CS gun? ./tools/dev.ps1 python tools/MpM0/mp_drop.py <dev tag>"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, to_menu, join, poll
from mp_m1 import FORCE_ACTIVE, MAIN, SURVIVAL, ARENA, build_arena, player, inventory, teleport, EYE, near, give_gun, pickable_of, HANDSHAKE

def main(tag):
    pkg = PKG / f"dev-{tag}-lite.scmod"; ta = MODS / TA_BUILT.name
    case_dir = RUNS / f"drop-{tag}-{time.strftime('%H%M%S')}"; case_dir.mkdir(parents=True)
    out = {}
    server = c1 = None
    try:
        server = Game("server", case_dir, [ta, pkg]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        poll(server, SURVIVAL, lambda v: v == "Survival", 20)
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        x, y, z = build_arena(server).split()
        c1 = Game("client1", case_dir, [ta, pkg]); to_menu(c1); join(c1, server)
        poll(c1, HANDSHAKE, lambda v: v == "Accepted", 30)
        idx = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        time.sleep(2)
        for a in range(10):
            c1.func(teleport(x, y, z)); time.sleep(.7)
            if near(c1.func(EYE), x, y, z): break
        for label, give in [("dirt", player(idx) + 'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < 6; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index), 5); return "ok";'),
                            ("gun", give_gun(idx))]:
            out[label + " give"] = server.func(give); time.sleep(1.5)
            out[label + " client before"] = c1.func(inventory(MAIN))
            c1.func(FORCE_ACTIVE); c1.func(MAIN + 'pl.m_lastActionTime = -1000.0; return "ok";')
            m = server.mark(); c1.cmd("DROP_ONCE"); time.sleep(2)
            out[label + " server after"] = server.func(inventory(player(idx)))
            out[label + " pickables"] = server.func('var project = Game.GameManager.Project; return string.Join(" | ", project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Select(p => p.Value + "x" + p.Count));')
            out[label + " client drop flag seen"] = c1.func(MAIN + 'return pl.ComponentInput.PlayerInput.Drop.ToString();')
        out["client errors"] = c1.errors(); out["server errors"] = server.errors()
    except Exception as e:
        out["failure"] = f"{type(e).__name__}: {e}"
    finally:
        for g in [c1, server]:
            if g: g.close()
        for g in ["server", "client1"]: shutil.rmtree(case_dir / g, ignore_errors=True)
    print(json.dumps(out, ensure_ascii=False, indent=1))

if __name__ == "__main__": main(sys.argv[1])
