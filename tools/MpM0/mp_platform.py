"""The preview platform as shipped, plus the package under test, with no test-side compensation (round 3 item 4).

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_platform.py <dev tag> [lite|full]
Server + client1 join over loopback from the fixed runtime with dev-<tag>-<edition>.scmod (the release candidate copied by
m0.py candpkg, or a devpkg build) and TestAutomation. The server's CommandCompatNet broadcast timer is NOT held and no
terrain is resent by the test: what the product's own adapter does is what is measured. Checked, before any CS gameplay:
  the adapter reports its platform verdict (build verified, command gate installed, terrain reconciliation on);
  no CompatNet adapter asks to skip the client's own player updates; ComponentPlayer.Update runs on the client;
  the client walks (W held moves its body on the client and on the server), jumps, changes its active slot and the
  server sees the slot; a block placed by the client through the engine's own interact input appears on the server;
  an arena carved on the server before client1 joined is identical on client1 within a bounded wait (the platform's
  chunk sync plus the adapter's hash verification/resend), and the adapter's counters say what it did;
  a client joining later (client2) also receives it; a block changed on the server while client1 is in range reaches it.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, sha, to_menu, join, poll
from mp_m1 import KEEP_ACTIVE, PROJECT, MAIN, SURVIVAL, HANDSHAKE, BRIDGE, player, teleport, EYE, near, build_arena, arena_diff, arena_on_clients, inventory
assert m0.PLATFORM_AS_DESIGNED is False, "this run must not hold the platform's broadcast timer"

def mod_type(name): return f'System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("{name}")).FirstOrDefault(x => x != null)'
PLATFORM = f'var t = {mod_type("Game.ScPlatformCompat")}; return t == null ? "no ScPlatformCompat" : (string)t.GetProperty("Summary").GetValue(null);'
TERRAIN_COUNTERS = f'var a = Game.ScNet.Transport; if (a == null) return "no transport"; var f = a.GetType().GetField("Terrain"); var r = f.GetValue(a); string V(string n) => r.GetType().GetField(n).GetValue(r).ToString(); return "verified " + V("Verified") + " resent " + V("Resent") + " givenUp " + V("GivenUp") + " reported " + V("Reported") + " last " + V("Last");'
ADAPTERS = 'return string.Join(", ", Game.CompatNetAdapterRegistry.Adapters.Select(a => a.AdapterId + ":" + a.GetType().Name));'
SKIPS = MAIN + ('var names = new System.Collections.Generic.List<string>(); foreach (var a in Game.CompatNetAdapterRegistry.Adapters) { bool s = false; try { s = a.ShouldSkipClientUpdate(pl); } catch { } if (s) names.Add(a.GetType().Name); } '
    'return names.Count == 0 ? "none" : string.Join(", ", names);')
UPDATES = MAIN + 'return pl.LastActiveSlot.ToString();'
POKE_LAST = MAIN + 'pl.LastActiveSlot = 200; return pl.LastActiveSlot.ToString();'  # 200: no real slot; a byte on the preview platform build
def body_at(i): return player(i) + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X:0.00} {p.Y:0.00} {p.Z:0.00}");'
MY_BODY = MAIN + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X:0.00} {p.Y:0.00} {p.Z:0.00}");'
def server_slot(i): return player(i) + 'return pl.ComponentMiner.Inventory.ActiveSlotIndex.ToString();'
SET_SLOT = MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 4; return pl.ComponentMiner.Inventory.ActiveSlotIndex.ToString();'
# Inventories are the server's: the dirt is given there for the client's player and arrives through the platform's sync.
def give_dirt(i): return player(i) + 'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(2, inv.GetSlotCount(2)); inv.AddSlotItems(2, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index), 10); return "ok";'
SLOT2 = MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 2; return pl.ComponentMiner.Inventory.ActiveSlotIndex.ToString();'
DIRT_VALUE = 'return Game.Terrain.MakeBlockValue(Game.DirtBlock.Index).ToString();'
def cell(x, y, z): return PROJECT + f'return Game.Terrain.ExtractContents(project.FindSubsystem<Game.SubsystemTerrain>(true).Terrain.GetCellValue({x}, {y}, {z})).ToString();'
def set_cell(x, y, z, v): return PROJECT + f'project.FindSubsystem<Game.SubsystemTerrain>(true).ChangeCell({x}, {y}, {z}, {v}); return "ok";'
# The floor cell two blocks ahead, aimed from the eye: the engine's own interact input (a placement).
PLACE_RAY = MAIN + ('var eye = pl.ComponentCreatureModel.EyePosition; var body = pl.ComponentBody.Position; var at = new Engine.Vector3(System.MathF.Floor(body.X) + .5f, System.MathF.Floor(body.Y) - .05f, System.MathF.Floor(body.Z) + 2.5f); '
    'var d = Engine.Vector3.Normalize(at - eye); return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')


def main(tag, edition="lite"):
    pkg = PKG / f"dev-{tag}-{edition}.scmod"; ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"platform-{tag}-{edition}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"platform-{tag}-{edition}", "package": {pkg.name: sha(pkg)}, "testAutomation": sha(ta), "compensations": "none (broadcast timer not held, no terrain resend)", "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    server = c1 = c2 = None
    try:
        server = Game("server", case_dir, [ta, pkg]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        poll(server, SURVIVAL, lambda v: v == "Survival", 20)
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        step("server platform verdict", server.func(PLATFORM)); step("server adapters", server.func(ADAPTERS))
        x, y, z = [int(v) for v in step("arena carved on the server before any client joins", build_arena(server)).split()]
        server.func(set_cell(x + 3, y + 2, z + 5, "Game.Terrain.MakeBlockValue(Game.DiamondBlock.Index)"))  # a marker block inside the arena
        c1 = Game("client1", case_dir, [ta, pkg]); to_menu(c1); join(c1, server)
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        check("client1 accepted by the CS handshake", poll(c1, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted", c1.func(BRIDGE))
        verdict = step("client1 platform verdict", c1.func(PLATFORM))
        check("the adapter installed the command gate and terrain reconciliation on a verified build", "platform verified" in verdict and "command gate: installed" in verdict and "reconciliation on" in verdict, verdict)
        check("no CompatNet adapter skips the client's own player updates", poll(c1, SKIPS, lambda v: v == "none", 5) == "none", c1.func(SKIPS))
        c1.func(POKE_LAST); time.sleep(.5)
        check("ComponentPlayer.Update runs on the client (LastActiveSlot is reset by it)", c1.func(UPDATES) != "200", c1.func(UPDATES))
        # Movement on the raw platform: W held for 40 frames.
        p0 = c1.func(MY_BODY); c1.cmd("MOVE_INPUT 0 0 1 40"); time.sleep(1.2); p1 = c1.func(MY_BODY); time.sleep(.6); s1 = server.func(body_at(i1))
        def d(a, b): return sum((float(u) - float(v)) ** 2 for u, v in zip(a.split(), b.split())) ** .5
        check("client1 walks: its body moves on the client", d(p0, p1) > .5, f"{p0} -> {p1}")
        check("the server sees client1 where client1 is", d(p1, s1) < 1.5, f"client {p1} server {s1}")
        c1.cmd("KEYDOWN Space"); time.sleep(.1); c1.cmd("KEYUP Space"); time.sleep(.25); py = float(c1.func(MY_BODY).split()[1]); time.sleep(1)
        check("client1 jumps", py > float(p1.split()[1]) + .3, f"{p1.split()[1]} -> {py}")
        step("client1 sets slot 4", c1.func(SET_SLOT))
        check("the server sees client1's active slot", poll(server, server_slot(i1), lambda v: v == "4", 8) == "4", server.func(server_slot(i1)))
        # A placement through the engine's own interact input.
        for attempt in range(8):
            c1.func(teleport(x, y, z)); time.sleep(.7)
            if near(c1.func(EYE), x, y, z): break
        c1.func(MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'); time.sleep(1)
        server.func(give_dirt(i1)); dirt = f"{server.func(DIRT_VALUE)}x10"
        step("client1 sees the dirt the server gave it", poll(c1, inventory(MAIN), lambda v: dirt in v, 10))
        c1.func(SLOT2); step("the server sees slot 2", poll(server, server_slot(i1), lambda v: v == "2", 8))
        c1.cmd("INTERACT_RAY " + c1.func(PLACE_RAY)); time.sleep(2)
        placed = poll(server, cell(x, y, z + 2), lambda v: v != "0", 8)
        check("a block placed by client1 (engine interact input) appears on the server", placed != "0", f"server cell {placed}, client cell {c1.func(cell(x, y, z + 2))}, client inventory {c1.func(inventory(MAIN))}")
        # Late-join terrain: nothing resent by the test.
        synced = step("client1's arena vs the server's (first / final / seconds / server)", arena_on_clients(server, [c1], x, y, z, seconds=120))
        check("client1 received the pre-join arena unchanged, without a test resend", synced[0][3] is not None, synced)
        check("the marker block placed before the join is on client1", poll(c1, cell(x + 3, y + 2, z + 5), lambda v: v != "0", 5) != "0", c1.func(cell(x + 3, y + 2, z + 5)))
        step("server terrain reconciliation counters", server.func(TERRAIN_COUNTERS))
        server.func(set_cell(x - 2, y + 1, z + 3, "Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)"))
        check("a block changed on the server while client1 is in range reaches client1", poll(c1, cell(x - 2, y + 1, z + 3), lambda v: v != "0", 8) != "0", c1.func(cell(x - 2, y + 1, z + 3)))
        c2 = Game("client2", case_dir, [ta, pkg]); to_menu(c2); join(c2, server)
        check("client2 accepted", poll(c2, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
        for attempt in range(8):
            c2.func(teleport(x, y, z)); time.sleep(.7)
            if near(c2.func(EYE), x, y, z): break
        synced2 = step("client2's arena vs the server's", arena_on_clients(server, [c2], x, y, z, seconds=120))
        check("a later joiner receives the arena too, without a test resend", synced2[0][3] is not None, synced2)
        step("server terrain reconciliation counters (final)", server.func(TERRAIN_COUNTERS))
        for g in [server, c1, c2]: check(f"{g.name} logged no errors", not g.errors(), g.errors()[:3])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [c2, c1, server]:
            if g is not None: R[g.name] = {"errors": g.errors(), "net": g.net_lines()[-12:]}; g.close()
        m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    (RESULTS / f"platform-{tag}-{edition}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
