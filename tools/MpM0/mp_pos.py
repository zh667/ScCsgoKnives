"""Diagnostic (test tooling only): how a scripted client move reaches the 1.9.3.2_MP server.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_pos.py <dev tag> [lite|full]
Host + one client. The client is moved several ways; after each the server's copy of that player is sampled for a few
seconds. Prints the positions; no pass/fail beyond reporting.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, to_menu, join, poll
from mp_m1 import FORCE_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, teleport, EYE, near, move_to

def pos_of(i): return player(i) + ('var p = pl.ComponentBody.Position; var v = pl.ComponentBody.Velocity; '
    'return System.FormattableString.Invariant($"{p.X:0.00} {p.Y:0.00} {p.Z:0.00} v {v.Length():0.00} g {pl.ComponentBody.IsGravityEnabled}");')
MY_POS = MAIN + ('var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X:0.00} {p.Y:0.00} {p.Z:0.00} fly {pl.ComponentLocomotion.IsCreativeFlyEnabled} g {pl.ComponentBody.IsGravityEnabled}");')
STAND = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'


def main(tag, edition):
    pkg = PKG / f"dev-{tag}-{edition}.scmod"; ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"pos-{tag}-{stamp}"; case_dir.mkdir(parents=True)
    out = []
    def say(name, value): out.append((name, value)); print(f"{name}: {value}", flush=True); return value
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
        say("accepted", poll(c1, HANDSHAKE, lambda v: v == "Accepted", 30))
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        def sample(label, seconds=4):
            for k in range(int(seconds / 0.5)):
                say(f"{label} t{k * 0.5:.1f}", f"client {c1.func(MY_POS)} | server {server.func(pos_of(i1))}"); time.sleep(0.5)
        time.sleep(2)
        for attempt in range(10):
            c1.func(teleport(x, y, z)); time.sleep(0.7)
            if near(c1.func(EYE), x, y, z): break
        sample("after teleport")
        c1.func(move_to((float(x) + 0.5, float(y), float(z) + 7.5))); sample("after move_to +7 (flying)")
        c1.func(STAND); sample("after STAND")
        c1.func(move_to((float(x) + 0.5, float(y), float(z) + 3.5))); c1.func(STAND); sample("after move_to +3 then STAND")
        c1.func(FORCE_ACTIVE); c1.cmd("MOVE_INPUT 0 0 1 60"); sample("after MOVE_INPUT forward 60 frames")
        c1.func(MAIN + f'pl.ComponentBody.Position = new Engine.Vector3({float(x) + 0.5}f, {y}f, {float(z) + 6.5}f); pl.ComponentBody.Velocity = new Engine.Vector3(0, 0, 0.01f); return "ok";')
        sample("after a bare position write with a small velocity")
        # Active slot: set on the client as the test scripts do, then through the engine's own slot input.
        ACTIVE_SERVER = player(i1) + 'return pl.ComponentMiner.Inventory.ActiveSlotIndex + " / " + pl.ComponentMiner.ActiveBlockValue;'
        ACTIVE_CLIENT = MAIN + 'return pl.ComponentMiner.Inventory.ActiveSlotIndex + " / " + pl.ComponentMiner.ActiveBlockValue;'
        server.func(player(i1) + 'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(4, inv.GetSlotCount(4)); inv.AddSlotItems(4, Game.ScC4Block.Value, 1); return "ok";'); time.sleep(1.5)
        say("slot before", f"client {c1.func(ACTIVE_CLIENT)} | server {server.func(ACTIVE_SERVER)}")
        c1.func(MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 4; return "ok";')
        for k in range(6): say(f"slot after a script write t{k * 0.5:.1f}", f"client {c1.func(ACTIVE_CLIENT)} | server {server.func(ACTIVE_SERVER)}"); time.sleep(0.5)
        c1.func(FORCE_ACTIVE); c1.cmd("KEYDOWN Number3"); time.sleep(0.2); c1.cmd("KEYUP Number3")
        for k in range(6): say(f"slot after key 3 t{k * 0.5:.1f}", f"client {c1.func(ACTIVE_CLIENT)} | server {server.func(ACTIVE_SERVER)}"); time.sleep(0.5)
    finally:
        for g in [c1, server]:
            if g is not None: g.close()
        import shutil
        for g in ["server", "client1"]: shutil.rmtree(case_dir / g, ignore_errors=True)  # the game copies; logs stay
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / f"pos-{tag}.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), "utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
