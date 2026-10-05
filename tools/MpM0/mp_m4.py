"""current-direction-20260929 §6 M4: edition combination, simulated latency and loss, disconnect and rejoin over the
1.9.3.2_MP engine. TEST-ONLY packages.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_m4.py <dev tag> [full|lite+agents|lite]
Host + two clients of the given edition, then clients of the other editions. Checked:
  Latency: the engine's own transport (LiteNetLib simulation, both ends) delays every packet 120-180 ms each way with 3 %
  loss; under it a burst, a reload and a knife strike from client1 settle to the server's state on every client.
  Disconnect in the middle of a C4 plant and of a grenade throw: the server arms nothing, throws nothing and takes no item.
  Rejoin: the returning client is accepted, keeps its items, protection and gun records.
  Other editions (first-person-eye-shot-20261001: phones on Lite + agents, computers on Full): the other edition with the
  same agents is accepted; from it, a hello claiming another release and one claiming the agents on one side only are
  refused with their reasons and its own hello is accepted again; a client with the agents on one side only (e.g. Lite
  without the agents package on a Full server) is refused with the agents notice; the server keeps serving.
"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, MPBIN, sha, to_menu, join, poll, cs_packages, run_label
from mp_m1 import (FORCE_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, give_gun, record, inventory, health, teleport, EYE, near, move_to,
                   spawn_target, aim_at, DISCONNECT)

TRACE = 'return Game.ScNet.TraceText;'
def latency(on, lo, hi, loss): return ('var nm = (object)Game.NetworkManager.NetworkClient ?? Game.NetworkManager.NetworkServer; if (nm == null) return "no transport"; '
    'var f = nm.GetType().GetField("_netManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var m = f.GetValue(nm); var t = m.GetType(); '
    'string Set(string n, object v) { var fi = t.GetField(n); if (fi != null) { fi.SetValue(m, v); return n; } var p = t.GetProperty(n); if (p != null) { p.SetValue(m, v); return n; } return "no " + n; } '
    f'return string.Join(",", Set("SimulateLatency", {on}), Set("SimulationMinLatency", {lo}), Set("SimulationMaxLatency", {hi}), Set("SimulatePacketLoss", {"true" if loss else "false"}), Set("SimulationPacketLossChance", {loss}));')
def give(i, value, count, slot): return player(i) + (f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems({slot}, inv.GetSlotCount({slot})); inv.AddSlotItems({slot}, {value}, {count}); '
    f'return inv.GetSlotValue({slot}) + "x" + inv.GetSlotCount({slot});')
def count_of(i, value): return player(i) + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {value}).ToString();'
def slot(g, i): return g.func(MAIN + f'pl.ComponentMiner.Inventory.ActiveSlotIndex = {i}; return "ok";')
def armor(i): return PROJECT + f'var a = project.FindSubsystem<Game.SubsystemScArmor>(true); return a.Get(Game.SubsystemScArmor.PlayerKey({i})).Encode();'
def give_armor(i): return PROJECT + (f'var a = project.FindSubsystem<Game.SubsystemScArmor>(true); var key = Game.SubsystemScArmor.PlayerKey({i}); '
    'a.TryCreate(key, Game.ScArmorState.For(Game.ScArmorConfig.Full)); return a.Get(key).Encode();')
def give_knife(i): return player(i) + ('int knife = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true), 0, 0); '
    'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(2, inv.GetSlotCount(2)); inv.AddSlotItems(2, knife, 1); return knife.ToString();')
STAND = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'
CHARGES = 'return Game.GameManager.Project.FindSubsystem<Game.SubsystemScC4>(true).Charges.Count.ToString();'
GRENADES = 'return Game.GameManager.Project.FindSubsystem<Game.SubsystemScGrenades>(true).ActiveCount(-1, null).ToString();'
INDEX = MAIN + 'return pl.PlayerData.PlayerIndex.ToString();'
DETAIL = 'return Game.ScNet.Transport == null ? Game.ScNet.Decision : Game.ScNet.Transport.Handshake + " " + Game.ScNet.Transport.HandshakeDetail;'
AHEAD = lambda d: MAIN + (f'var cam = pl.GameWidget.ActiveCamera; var v = cam.ViewDirection; v.Y = 0; v = Engine.Vector3.Normalize(v); var at = pl.ComponentBody.Position + v * {d}f; '
    'return System.FormattableString.Invariant($"{at.X} {at.Y} {at.Z}");')
def target_at(p): return spawn_target(0, 0, 0).replace("new Engine.Vector3(0f + 0.5f, 0f, 0f + 9.5f)", f"new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f)")


AGENTS = {"full": True, "lite+agents": True, "lite": False}
OTHER = {"full": "lite+agents", "lite+agents": "full", "lite": "full"}        # the other edition (accepted when the agents match)
ONE_SIDED = {"full": "lite", "lite+agents": "lite", "lite": "lite+agents"}    # the agents on one side only: refused
def identity_hello(build, agents): return ('return ((Game.ScCsgoNetAdapter)Game.ScNet.Transport).SendHelloIdentityForTest('
    + ("null" if build is None else f'"{build}"') + ", " + ("null" if agents is None else f'"{agents}"') + ').ToString();')
PEERS = 'return Game.ScNet.Peers.Count.ToString();'


def main(tag, edition):
    other, one_sided = OTHER[edition], ONE_SIDED[edition]
    pkgs = cs_packages(tag, edition)[0]; pkgs_other = cs_packages(tag, other)[0]; pkgs_one_sided = cs_packages(tag, one_sided)[0]; ta = MODS / TA_BUILT.name
    label = run_label(edition)
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"m4-{tag}-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"m4-{tag}-{label}", "package": {p.name: sha(p) for p in {*pkgs, *pkgs_other, *pkgs_one_sided}}, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    def into_arena(g, dx=0.0, dz=0.0):
        time.sleep(2)
        for attempt in range(10):
            g.func(teleport(x, y, z)); time.sleep(0.7)
            if near(g.func(EYE), x, y, z): break
        if dx or dz: g.func(move_to((float(x) + 0.5 + dx, float(y), float(z) + 0.5 + dz)))
    def disconnect(g):
        m = server.mark(); g.cmd("EXEC " + DISCONNECT); server.wait("left (disconnected)", 60, m); g.wait('Entered screen "MainMenu"', 60)
    def rejoin(g):
        m = g.mark()
        g.cmd(f'EXEC ScreensManager.SwitchScreen("GameLoading", null, null, System.Net.IPEndPoint.Parse("127.0.0.1:{server.game_port}"), null);')
        g.wait(f"Connected: 127.0.0.1:{server.game_port}", 300, m)
        seen = g.wait(['Entered screen "Player"', "Player into playing."], 600, m)
        if seen == 'Entered screen "Player"':
            m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait("Player into playing.", 600, m)
        return poll(g, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 40)
    server = c1 = c2 = c3 = c4 = None
    try:
        server = Game("server", case_dir, [ta, *pkgs]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        step("survival mode", poll(server, SURVIVAL, lambda v: v == "Survival", 20))
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        x, y, z = step("arena", build_arena(server)).split()
        c1 = Game("client1", case_dir, [ta, *pkgs]); to_menu(c1); join(c1, server)
        c2 = Game("client2", case_dir, [ta, *pkgs]); to_menu(c2); join(c2, server)
        for g in [c1, c2]: check(f"{g.name} accepted", poll(g, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
        i1 = int(c1.func(INDEX)); i2 = int(c2.func(INDEX))
        into_arena(c1); into_arena(c2, 3, 0)
        for g in [c1, c2]: g.func(STAND)
        step("players sturdy (server)", [server.func(player(i) + 'pl.ComponentHealth.AttackResilience *= 50; return "ok";') for i in (i1, i2)])

        # 1. Simulated latency and loss on the engine's transport, both ends.
        lat = [step(f"{g.name} latency", g.func(latency("true", 120, 180, 3))) for g in [server, c1, c2]]
        check("the engine's transport simulates latency and loss", all("no " not in v and "SimulateLatency" in v for v in lat), lat)
        gid, gun, ammo = step("gun for client1 (server)", server.func(give_gun(i1))).split()
        time.sleep(3)
        check("client1 mirror before firing", poll(c1, record(gid), lambda v: v == server.func(record(gid)), 15) == server.func(record(gid)), c1.func(record(gid)))
        tid = step("target", server.func(target_at([float(x) + 0.5, float(y), float(z) + 9.5]))).split()[0]; time.sleep(3)
        c1.func(FORCE_ACTIVE); aim = step("client1 aim", c1.func(aim_at(tid))); c1.cmd(f"DIG_RAY {aim} 40"); time.sleep(5)
        s1 = step("server record after a burst under latency", server.func(record(gid)))
        check("the server fired under latency", int(s1.split("/")[0]) < 30, s1)
        check("client1 settles to the server's record", poll(c1, record(gid), lambda v: v == server.func(record(gid)), 15) == server.func(record(gid)), c1.func(record(gid)))
        check("client2 settles to the server's target health", poll(c2, health(tid), lambda v: v == server.func(health(tid)), 15) == server.func(health(tid)))
        c1.func(FORCE_ACTIVE); c1.cmd("KEYDOWN R"); time.sleep(0.3); c1.cmd("KEYUP R"); time.sleep(6)
        s2 = step("server record after a reload under latency", server.func(record(gid)))
        check("the reload settled on the server", s2.split("/")[0] == "30", s2)
        check("client1 settles to the reload", poll(c1, record(gid), lambda v: v == s2, 15) == s2, c1.func(record(gid)))
        check("client1 inventory settles", poll(c1, inventory(MAIN), lambda v: v.split(":")[1] == server.func(inventory(player(i1))).split(":")[1], 15).split(":")[1]
              == server.func(inventory(player(i1))).split(":")[1], c1.func(inventory(MAIN)))
        step("knife for client1 (server)", server.func(give_knife(i1)))
        slot(c1, 2); time.sleep(3)
        look = [float(v) for v in c1.func(AHEAD(1.6)).split()]
        cow = step("target in knife reach", server.func(target_at(look))).split()[0]; time.sleep(3)
        hp0 = server.func(health(cow))
        c1.func(FORCE_ACTIVE); c1.cmd("HIT_RAY " + c1.func(aim_at(cow))); time.sleep(3)
        hp1 = step("knife target (server)", server.func(health(cow)))
        check("the knife strike landed under latency", hp1 == "gone" or float(hp1) < float(hp0), f"{hp0} -> {hp1} / {server.func(TRACE)}")
        # A moving target under latency: client1 aims where its screen shows the target (drawn late by the engine's body
        # interpolation); the server's hit-scan compensation should hit it, and without it the same shots mostly miss.
        slot(c1, 0); time.sleep(2.5)
        mover = step("moving target", server.func(target_at([float(x) - 2.5, float(y), float(z) + 9.5]))).split()[0]
        server.func(PROJECT + f'var h = project.Entities.First(e => e.Id == {mover}).FindComponent<Game.ComponentHealth>(); h.AttackResilience = 1000f; return "ok";')
        import threading
        stop = threading.Event(); lock = threading.Lock()
        def sf(code):
            with lock: return server.func(code)
        def move():
            t0 = time.time()
            while not stop.is_set():
                t = time.time() - t0; leg = int(t // 2.0) % 2; f = (t % 2.0) / 2.0
                px = float(x) + 0.5 + (-3.0 + 6.0 * f if leg == 0 else 3.0 - 6.0 * f); vx = 3.0 if leg == 0 else -3.0
                sf(PROJECT + f'var b = project.Entities.First(e => e.Id == {mover}).FindComponent<Game.ComponentBody>(); b.Position = new Engine.Vector3({px}f, {y}f, {float(z) + 9.5}f); b.Velocity = new Engine.Vector3({vx}f, 0, 0); return "ok";')
                time.sleep(0.05)
        th = threading.Thread(target=move, daemon=True); th.start(); time.sleep(2)
        def moving_hits(label):
            hits = 0
            for k in range(6):
                h0 = sf(health(mover)); ray = c1.func(aim_at(mover)); c1.cmd(f"HIT_RAY {ray}"); c1.cmd(f"DIG_RAY {ray} 2"); time.sleep(0.9)
                if sf(health(mover)) != h0: hits += 1
                time.sleep(0.4)
            return step(label, hits)
        on = moving_hits("moving target hits with compensation (of 6)")
        sf('Game.ScNetGuns.Compensate = false; return "off";')
        off = moving_hits("moving target hits without compensation (of 6)")
        sf('Game.ScNetGuns.Compensate = true; return "on";')
        stop.set(); th.join(2)
        check("hit-scan compensation hits a moving target as the client saw it", on >= 4 and on > off, f"with {on}/6, without {off}/6")
        step("latency off", [g.func(latency("false", 0, 0, 0)) for g in [server, c1, c2]])

        # 2. Disconnect in the middle of a C4 plant.
        c4 = server.func('return Game.ScC4Block.Value.ToString();')
        step("C4 for client1 (server)", server.func(give(i1, c4, 1, 4)))
        step("protection for client1 (server)", server.func(give_armor(i1)))
        slot(c1, 4); c1.func(STAND); time.sleep(2.5)
        before = {"c4": server.func(count_of(i1, c4)), "armor": server.func(armor(i1)), "record": server.func(record(gid))}
        c1.func(FORCE_ACTIVE); c1.cmd("KEYDOWN E"); time.sleep(1.5)
        step("server trace in the plant", server.func(TRACE))
        disconnect(c1); c1.cmd("KEYUP E")
        time.sleep(4)
        check("no charge armed after the planter left", server.func(CHARGES) == "0", server.func(CHARGES))
        step("client1 rejoins", rejoin(c1))
        check("rejoined client accepted", c1.func(HANDSHAKE) == "Accepted", c1.func(DETAIL))
        j1 = int(c1.func(INDEX)); step("client1 player index after rejoin", f"{i1} -> {j1}")
        check("the C4 was not taken", server.func(count_of(j1, c4)) == before["c4"], f"{before['c4']} -> {server.func(count_of(j1, c4))}")
        check("protection kept across the rejoin", server.func(armor(j1)) == before["armor"], f"{before['armor']} -> {server.func(armor(j1))}")
        check("gun record kept", server.func(record(gid)) == before["record"] and poll(c1, record(gid), lambda v: v == before["record"], 15) == before["record"], c1.func(record(gid)))
        check("client1's protection mirror after the rejoin", poll(c1, armor(j1), lambda v: v == server.func(armor(j1)), 15) == server.func(armor(j1)), c1.func(armor(j1)))
        i1 = j1

        # 3. Disconnect in the middle of a grenade throw (pin pulled, still held).
        he = server.func('return Game.ScGrenadeBlock.Value(0).ToString();')
        step("HE for client1 (server)", server.func(give(i1, he, 1, 1)))
        into_arena(c1); c1.func(STAND); slot(c1, 1); time.sleep(2.5)
        n0 = server.func(count_of(i1, he))
        c1.func(FORCE_ACTIVE); c1.cmd("DIG_RAY " + c1.func(aim_at(tid) if tid else EYE) + " 600"); time.sleep(1.2)
        step("server trace in the throw", server.func(TRACE))
        disconnect(c1); c1.cmd("DIG_RAY 0 0 0 0 -1 0 1"); time.sleep(3)
        check("no grenade thrown after the thrower left", server.func(GRENADES) == "0", server.func(GRENADES))
        step("client1 rejoins again", rejoin(c1)); i1 = int(c1.func(INDEX))
        check("the grenade was not taken", server.func(count_of(i1, he)) == n0, f"{n0} -> {server.func(count_of(i1, he))}")

        # 4. Other editions.
        def enter(name, packages):
            g = Game(name, case_dir, [ta, *packages], role="client"); to_menu(g)
            m = g.mark()
            g.cmd(f'EXEC ScreensManager.SwitchScreen("GameLoading", null, null, System.Net.IPEndPoint.Parse("127.0.0.1:{server.game_port}"), null);')
            try: seen = g.wait(['Entered screen "Player"', "Player into playing.", 'Entered screen "MainMenu"', "Disconnected"], 300, m)
            except Exception as e: seen = f"no screen: {e}"
            step(f"{name} joining", seen)
            if seen == 'Entered screen "Player"':
                m = g.mark(); g.cmd("CLICK_WIDGET PlayButton")
                try: g.wait("Player into playing.", 300, m)
                except Exception as e: step(f"{name} play", str(e))
            return g, step(f"{name} handshake", poll(g, HANDSHAKE, lambda v: v not in ("Pending",), 30)), step(f"{name} detail", g.func(DETAIL))
        def hello_again(g, build, agents):
            m = g.mark(); g.func(identity_hello(build, agents)); time.sleep(1.5)
            return poll(g, HANDSHAKE, lambda v: v not in ("Pending",), 20), g.func(DETAIL)
        c3, state, detail = enter("client3", pkgs_other)
        matched = AGENTS[edition] == AGENTS[other]
        if matched:
            check(f"a {other} client is accepted on a {edition} server (same release, agents on both sides)", state == "Accepted" and "accepted" in detail, f"{state} / {detail}")
            check("the server counts it among its peers (client1, client2, client3)", poll(server, PEERS, lambda v: v == "3", 10) == "3", server.func(PEERS))
            st, d = hello_again(c3, "another-release", None)
            check("a hello claiming another release is refused with that reason", st == "Rejected" and "不是同一次发布" in d, f"{st} / {d}")
            st, d = hello_again(c3, None, "" if AGENTS[edition] else "agents-of-another-side")
            check("a hello claiming the agents on one side only is refused with the agents notice", st == "Rejected" and "探员" in d, f"{st} / {d}")
            st, d = hello_again(c3, None, None)
            check("its own hello is accepted again", st == "Accepted", f"{st} / {d}")
        else:
            check(f"a {other} client is refused on a {edition} server with the agents notice (agents on one side only)", state != "Accepted" and "探员" in detail, f"{state} / {detail}")
        if one_sided != other:
            R["client3"] = {"errors": c3.errors(), "net": c3.net_lines()[-60:], "logLines": c3.mark()}; c3.close(); c3 = None  # client4 uses the same role folder
            c4, state, detail = enter("client4", pkgs_one_sided)
            check(f"a {one_sided} client is refused on a {edition} server with the agents notice", state != "Accepted" and "探员" in detail, f"{state} / {detail}")
        check("the server still serves client2", poll(c2, HANDSHAKE, lambda v: v == "Accepted", 10) == "Accepted" and server.func(record(gid)) != "none")
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [c4, c3, c2, c1, server]:
            if g is None: continue
            R[g.name] = {"errors": g.errors(), "net": g.net_lines()[-60:], "logLines": g.mark()}
            g.close()
        for g in ["server", "client1", "client2"]:
            check(f"{g} logged no errors", not R.get(g, {}).get("errors"), (R.get(g, {}).get("errors") or [])[:5])
        for g in ["client3", "client4"]:
            if g in R: step(f"{g} errors (other edition, informational)", (R[g].get("errors") or [])[:5])
        RESULTS.mkdir(parents=True, exist_ok=True)
        name = f"m4-{tag}-{label}"
        (RESULTS / f"{name}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client1", "client2", "client3", "client4"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"{name}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "full"))
