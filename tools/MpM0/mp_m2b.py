"""current-direction-20260929 §6 M2 (part 2): every gun mechanism over the 1.9.3.2_MP engine. TEST-ONLY packages.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_m2b.py <dev tag> [lite|full]
Host + client1 (shooter) + client2 (observer), real input only (held dig for the trigger, held aim for the secondary key).
Checked on the server, the shooter's mirror and the observer: shotgun (one round, pellets shown to the observer), AWP
scope in and out, Glock-18 burst mode (one trigger press, three rounds), R8 fan on the secondary key, M4A1-S silencer
off (saved in the record), Zeus shot and its recharge, and a StatTrak counter installed from the workbench op whose kill
the server counts.
"""
import json, shutil, sys, threading, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, MPBIN, sha, to_menu, join, poll
from mp_m1 import FORCE_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, health, teleport, EYE, near, move_to, spawn_target, aim_at

TRACE = 'return Game.ScNet.TraceText;'
# Turns client1 to look at the target as a player does before clicking (scoped shots and the later rounds of a burst go
# along the camera, as in single player); answers how well the eye now points at it (1 = exactly).
def face(tid): return MAIN + (f'var t = project.Entities.FirstOrDefault(e => e.Id == {tid})?.FindComponent<Game.ComponentBody>(); if (t == null) return "no target"; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var bb = t.BoundingBox; var d = Engine.Vector3.Normalize((bb.Min + bb.Max) * 0.5f - eye); float best = -2f; '
    'foreach (float ys in new[] { 1f, -1f }) foreach (float ps in new[] { 1f, -1f }) { '
    'pl.ComponentBody.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, System.MathF.Atan2(-ys * d.X, -d.Z)); '
    'pl.ComponentLocomotion.LookAngles = new Engine.Vector2(0f, ps * System.MathF.Asin(d.Y)); pl.ComponentCreatureModel.Update(0f); '
    'float dot = Engine.Vector3.Dot(Engine.Matrix.CreateFromQuaternion(pl.ComponentCreatureModel.EyeRotation).Forward, d); '
    'if (dot > 0.9995f) return dot.ToString("0.00000"); best = System.Math.Max(best, dot); } '
    'return "off " + best.ToString("0.00000");')
FACE_AWAY = MAIN + 'pl.ComponentBody.Rotation = Engine.Quaternion.Identity; pl.ComponentLocomotion.LookAngles = Engine.Vector2.Zero; return "ok";'
def give_named(i, name, slot=0): return player(i) + (
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); var spec = Game.GunSpec.All[v]; '
    'int id = Game.ScGunRegistry.Current.Allocate(v, spec.Magazine, false, Game.ScGunDurability.Full(v)); '
    'int gun = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)); '
    'int ammo = Game.ScAmmoBlock.Value(Game.ScReloadTransaction.AmmoKind(spec)); '
    f'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < 6; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); inv.AddSlotItems({slot}, gun, 1); inv.AddSlotItems(1, ammo, 3); '
    'return id + " " + gun + " " + spec.Magazine;')
def snap(id_): return (f'return Game.ScGunRegistry.Current.TryGetSnapshot({id_}, out var s) ? s.Rounds + "/" + s.SilencerOff + "/" + (s.RechargeReadyAt >= 0) + "/" + s.CounterInstalled + "/" + s.KillCount : "none";')
def rounds(g, id_): return int(g.func(snap(id_)).split("/")[0])
def scoped(i): return player(i) + 'return project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true).IsScoped(pl).ToString();'
SCOPED_MAIN = MAIN + 'return project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true).IsScoped(pl).ToString();'
def burst(i): return player(i) + ('var guns = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); '
    'var f = typeof(Game.SubsystemScGunBlockBehavior).GetField("m_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); '
    'var d = (System.Collections.IDictionary)f.GetValue(guns); if (!d.Contains(pl)) return "none"; var st = d[pl]; var fi = st.GetType().GetField("BurstMode"); '
    'object v = fi != null ? fi.GetValue(st) : st.GetType().GetProperty("BurstMode")?.GetValue(st); return v?.ToString() ?? "?";')
AHEAD = lambda d: MAIN + (f'var cam = pl.GameWidget.ActiveCamera; var v = cam.ViewDirection; v.Y = 0; v = Engine.Vector3.Normalize(v); var at = pl.ComponentBody.Position + v * {d}f; '
    'return System.FormattableString.Invariant($"{at.X} {at.Y} {at.Z}");')
def target_at(p): return spawn_target(0, 0, 0).replace("new Engine.Vector3(0f + 0.5f, 0f, 0f + 9.5f)", f"new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f)")
STAND = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'
SHOWN = 'return Game.ScNetGuns.RemoteShotsShown.ToString();'
def counter_setup(i, slot=0): return player(i) + (
    'pl.PlayerData.Level = System.Math.Max(pl.PlayerData.Level, 10); var inv = pl.ComponentMiner.Inventory; '
    f'var q = Game.ScGunCounter.Prepare(inv, {slot}, false); if (q == null) return "no quote"; '
    'return q.Revision + " " + string.Join(",", q.Cost.Select(m => m.Key + ":" + m.Value));')
def give_items(i, pairs): return player(i) + 'var inv = pl.ComponentMiner.Inventory; ' + ' '.join(
    f'for (int n = 0; n < {c}; ) {{ int s = Enumerable.Range(2, inv.SlotsCount - 2).FirstOrDefault(q => inv.GetSlotCount(q) == 0 || inv.GetSlotValue(q) == {v} && inv.GetSlotCount(q) < inv.GetSlotCapacity(q, {v}), -1); if (s < 0) break; int k = System.Math.Min({c} - n, inv.GetSlotCapacity(s, {v}) - inv.GetSlotCount(s)); if (k <= 0) break; inv.AddSlotItems(s, {v}, k); n += k; }}'
    for v, c in pairs) + ' return "ok";'
def place_bench(x, y, z): return PROJECT + (f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    f't.ChangeCell({x} + 2, {y}, {z}, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScWeaponWorkbenchBlock>(true))); return "ok";')
def op_counter(bench, slot, expected, revision):
    bx, by, bz = bench
    return ('Game.ScNetWorkbench.LastResult = null; '
            f'Game.ScNetWorkbench.Run(new Game.ScWorkbenchOp(Game.ScWorkbenchOpKind.Counter, new Engine.Point3({bx}, {by}, {bz}), 0, 1, {slot}, {expected}, {revision}, 0, "", ""), '
            '() => new Game.ScWorkbenchResult(-9, "ran locally"), r => { }); return "sent";')
LAST = 'var r = Game.ScNetWorkbench.LastResult; return r == null ? "pending" : r.Value.Code + " " + r.Value.Detail;'
def pairs(text): return [tuple(map(int, p.split(":"))) for p in text.split(",") if p]


def main(tag, edition):
    pkg = PKG / f"dev-{tag}-{edition}.scmod"; ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"m2b-{tag}-{edition}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"m2b-{tag}-{edition}", "package": {pkg.name: sha(pkg)}, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": [], "checks": []}
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
    targets = []; faced = []
    def trace_set(): return [t for t in server.func(TRACE).split(" | ") if t]
    def new_shots(before): return [t for t in trace_set() if t not in before and " shot P1 " in t]
    def hit(shot): return shot.split(" hit ")[1].split(" cone ")[0].strip() != ""
    def equip(name, distance=7.0):
        # One target at a time: an earlier target left in place would be stood on (a new body pushed up and moving).
        for old in targets: server.func(PROJECT + f'var e = project.Entities.FirstOrDefault(x => x.Id == {old}); if (e != null) project.RemoveEntity(e, true); return "ok";')
        targets.clear()
        gid, gun, mag = step(f"{name} for client1 (server)", server.func(give_named(i1, name))).split()
        c1.func(MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; return "ok";'); time.sleep(3)  # the draw
        at = [float(x) + 0.5, float(y), float(z) + 0.5 + distance]  # inside the cleared arena (client1 stands at its near end)
        cow = server.func(target_at(at)).split()[0]; targets.append(cow); time.sleep(1.5)
        # Setup, not gunplay: a target that is not standing on the arena floor is not where the shooter aims from above.
        stood = server.func(PROJECT + f'var b = project.Entities.First(e => e.Id == {cow}).FindComponent<Game.ComponentBody>(); '
                            'return System.FormattableString.Invariant($"{b.Position.X} {b.Position.Y} {b.Position.Z}");')
        check(f"{name}: the target stands on the arena floor (setup)", abs(float(stood.split()[1]) - float(y)) < 0.3, stood)
        return gid, int(mag), cow
    def press(kind, cow, frames=2):
        # A left click is the click (Hit: semi-automatic guns fire on it, as in single player) and the hold (Dig:
        # automatic guns fire while it lasts); the secondary key is the aim input.
        faced.append(c1.func(face(cow))); time.sleep(0.3)
        ray = c1.func(aim_at(cow))
        if kind == "DIG_RAY": c1.cmd(f"HIT_RAY {ray}")
        c1.cmd(f"{kind} {ray} {frames}"); time.sleep(frames / 30 + 1.2)
    def mirrored(gid): return poll(c1, snap(gid), lambda v: v == server.func(snap(gid)), 10) == server.func(snap(gid))
    server = c1 = c2 = None
    try:
        server = Game("server", case_dir, [ta, pkg]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        step("survival mode", poll(server, SURVIVAL, lambda v: v == "Survival", 20))
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        x, y, z = step("arena", build_arena(server)).split()
        c1 = Game("client1", case_dir, [ta, pkg]); to_menu(c1); join(c1, server)
        c2 = Game("client2", case_dir, [ta, pkg]); to_menu(c2); join(c2, server)
        for g in [c1, c2]: check(f"{g.name} accepted", poll(g, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        into_arena(c1); into_arena(c2, 3, -2)
        for g in [c1, c2]: g.func(STAND)

        server.func('Game.ScNetGuns.DebugRewind = true; return "ok";')
        # Shotgun: one trigger press, one round, the observer shown the shot.
        gid, mag, cow = equip("nova"); hp0 = server.func(health(cow)); shown = int(c2.func(SHOWN))
        press("DIG_RAY", cow)
        check("nova: one round on the server", rounds(server, gid) == mag - 1, server.func(snap(gid)))
        step("server trace after the nova shot", server.func(TRACE))
        check("nova: the pellets hurt the target", server.func(health(cow)) == "gone" or float(server.func(health(cow))) < float(hp0), f"{hp0} -> {server.func(health(cow))}")
        check("nova: the observer was shown the shot", int(c2.func(SHOWN)) > shown, c2.func(SHOWN))
        check("nova: client1 mirror", mirrored(gid), c1.func(snap(gid)))

        # A/B on a stationary target: the same shot with the server's lag compensation on and off (both must hit).
        ab = {}
        for mode in ["on", "off"]:
            server.func(f'Game.ScNetGuns.Compensate = {"true" if mode == "on" else "false"}; return "ok";')
            gid_ab, mag_ab, cow_ab = equip("ak47", 7.0)
            before = server.func(TRACE); hp_ab = server.func(health(cow_ab))
            press("DIG_RAY", cow_ab, 1)
            ab[mode] = (hp_ab, server.func(health(cow_ab)), server.func(TRACE)[len(before):].strip(" |"),
                        server.func(PROJECT + f'var b = project.Entities.First(e => e.Id == {cow_ab}).FindComponent<Game.ComponentBody>(); return System.FormattableString.Invariant($"{{b.Position.X:0.00}} {{b.Position.Y:0.00}} {{b.Position.Z:0.00}}");'),
                        c1.func(PROJECT + f'var b = project.Entities.First(e => e.Id == {cow_ab}).FindComponent<Game.ComponentBody>(); return System.FormattableString.Invariant($"{{b.Position.X:0.00}} {{b.Position.Y:0.00}} {{b.Position.Z:0.00}}");'))
            step(f"ak47 on a stationary target, compensation {mode}: health, trace, target server/client", ab[mode])
        server.func('Game.ScNetGuns.Compensate = true; return "ok";')
        check("a stationary target is hit with compensation on", ab["on"][1] == "gone" or float(ab["on"][1]) < float(ab["on"][0]), ab["on"])
        check("and with it off", ab["off"][1] == "gone" or float(ab["off"][1]) < float(ab["off"][0]), ab["off"])

        # A click whose release reaches the server in the same server frame: the server is kept busy for 0.7 s while
        # client1 clicks, so both messages are taken together. The release carries the camera's ray (client1 faces -Z,
        # away from the target), the click its own; the shot must go along the click (mp15: a single-tap head shot at
        # 7 m missed although its spread was 1 cm).
        def last_shot(): return ([t for t in server.func(TRACE).split(" | ") if " shot P1 " in t] or [""])[-1]
        gid_sf, mag_sf, cow_sf = equip("ak47", 7.0)
        c1.func(FACE_AWAY); time.sleep(0.3)
        before = last_shot(); hp_sf = server.func(health(cow_sf)); ray = c1.func(aim_at(cow_sf))
        busy = threading.Thread(target=lambda: server.func('System.Threading.Thread.Sleep(700); return "slept";'))
        busy.start(); time.sleep(0.2)
        c1.cmd(f"HIT_RAY {ray}"); c1.cmd(f"DIG_RAY {ray} 1")
        busy.join(); time.sleep(1.5)
        shot = last_shot(); fresh = shot != before and shot != ""
        off = float(shot.split("latest-aim off ")[1].split()[0]) if fresh and "latest-aim off " in shot else None
        step("click and release in one server frame: health, shot", (hp_sf, server.func(health(cow_sf)), shot))
        check("a click whose release arrived in the same server frame fires along the click and hits",
              fresh and (server.func(health(cow_sf)) == "gone" or float(server.func(health(cow_sf))) < float(hp_sf)), (hp_sf, server.func(health(cow_sf)), shot))
        check("and the release's camera ray had already arrived when the shot was taken (the case itself)", off is not None and off > 0.5, off)

        # AWP: the secondary key scopes on the server, twice more back out.
        gid, mag, cow = equip("awp")
        press("AIM_RAY", cow)
        check("awp: scoped on the server", server.func(scoped(i1)) == "True", f"server {server.func(scoped(i1))} client {c1.func(SCOPED_MAIN)} / {server.func(TRACE)}")
        check("awp: scoped on client1", c1.func(SCOPED_MAIN) == "True")
        before = trace_set(); press("DIG_RAY", cow); shots = new_shots(before)
        check("awp: a scoped shot on the server", rounds(server, gid) == mag - 1, server.func(snap(gid)))
        check("awp: the scoped shot (along the camera) hit the target", len(shots) == 1 and hit(shots[0]), shots)
        # The AWP drops the scope for the bolt and zooms back in (CS2): wait for that before stepping out of the scope.
        step("awp: back in the scope after the bolt (server)", poll(server, scoped(i1), lambda v: v == "True", 6))
        # Two zoom levels: two more presses leave the scope. After each press the server and client1 agree.
        agree = []
        for k in range(3):
            if server.func(scoped(i1)) == "False": break
            press("AIM_RAY", cow); time.sleep(0.5)
            agree.append((server.func(scoped(i1)), c1.func(SCOPED_MAIN)))
        check("awp: out of the scope on the server, the client agreeing after every press", server.func(scoped(i1)) == "False" and all(a == b for a, b in agree), agree)

        # Glock-18: burst mode on the secondary key, then one trigger press fires three rounds.
        gid, mag, cow = equip("glock18")
        press("AIM_RAY", cow)
        check("glock18: burst mode on the server", server.func(burst(i1)) == "True", server.func(burst(i1)))
        before = trace_set(); press("DIG_RAY", cow); time.sleep(0.8); shots = new_shots(before)
        check("glock18: one press, three rounds", rounds(server, gid) == mag - 3, server.func(snap(gid)))
        check("glock18: all three rounds of the burst hit the target", len(shots) == 3 and all(hit(t) for t in shots), shots)
        check("glock18: client1 mirror", mirrored(gid), c1.func(snap(gid)))

        # R8: the secondary key fans the hammer (an immediate shot).
        gid, mag, cow = equip("revolver")
        before = trace_set(); press("AIM_RAY", cow); time.sleep(0.8); shots = new_shots(before)
        check("revolver: the fan fired on the server", rounds(server, gid) < mag, server.func(snap(gid)))
        check("revolver: the fanned shot hit the target", bool(shots) and hit(shots[0]), shots)
        check("revolver: client1 mirror", mirrored(gid), c1.func(snap(gid)))

        # M4A1-S: the secondary key takes the silencer off; the record keeps it.
        gid, mag, cow = equip("m4a1s")
        press("AIM_RAY", cow)
        check("m4a1s: silencer off in the server's record", poll(server, snap(gid), lambda v: v.split("/")[1] == "True", 10).split("/")[1] == "True", f"{server.func(snap(gid))} / {server.func(TRACE)}")
        check("m4a1s: client1 mirror", mirrored(gid), c1.func(snap(gid)))

        # Zeus: one shot at close range, then the server's own recharge.
        gid, mag, cow = equip("taser", 2.0); hp0 = server.func(health(cow))
        press("DIG_RAY", cow)
        s1 = step("taser after the shot (server)", server.func(snap(gid)))
        check("taser: fired and recharging on the server", s1.split("/")[0] == "0" and s1.split("/")[2] == "True", s1)
        check("taser: the target hit", server.func(health(cow)) == "gone" or float(server.func(health(cow))) < float(hp0), f"{hp0} -> {server.func(health(cow))}")
        check("taser: client1 mirror while recharging", mirrored(gid), c1.func(snap(gid)))
        time.sleep(11)
        s2 = step("taser after the recharge (server)", server.func(snap(gid)))
        check("taser: recharged on the server", s2.split("/")[0] == "1" and s2.split("/")[2] == "False", s2)
        check("taser: client1 mirror after the recharge", mirrored(gid), c1.func(snap(gid)))

        # StatTrak: the counter installed through the workbench op, then a kill the server counts.
        gid, mag, cow = equip("ak47", 6.0)
        server.func(place_bench(x, y, z)); bench = (int(x) + 2, int(y), int(z)); time.sleep(1)
        rev, cost = step("counter quote (server)", server.func(counter_setup(i1))).split(" ", 1)
        server.func(give_items(i1, pairs(cost))); time.sleep(1.5)
        gun = server.func(player(i1) + 'return pl.ComponentMiner.Inventory.GetSlotValue(0).ToString();')
        c1.func(op_counter(bench, 0, gun, rev))
        done = step("counter (server answer)", poll(c1, LAST, lambda v: v != "pending", 15))
        check("counter installed on the server", done.startswith("0") and server.func(snap(gid)).split("/")[3] == "True", f"{done} / {server.func(snap(gid))}")
        server.func(PROJECT + f'var h = project.Entities.FirstOrDefault(e => e.Id == {cow})?.FindComponent<Game.ComponentHealth>(); if (h != null) h.AttackResilience = 0.2f; return "ok";')
        press("DIG_RAY", cow, 20); time.sleep(2)
        check("the kill counted on the server", poll(server, snap(gid), lambda v: v.split("/")[4] != "0", 10).split("/")[4] != "0", f"{server.func(snap(gid))} target {server.func(health(cow))}")
        check("client1 mirror shows the kill", mirrored(gid), c1.func(snap(gid)))
        step("client1 facing each target before its press (1 = exactly)", faced)
        step("server trace", server.func(TRACE))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [c2, c1, server]:
            if g is None: continue
            R[g.name] = {"errors": g.errors(), "net": g.net_lines()[-60:], "logLines": g.mark()}
            g.close()
        for g in ["server", "client1", "client2"]:
            check(f"{g} logged no errors", not R.get(g, {}).get("errors"), (R.get(g, {}).get("errors") or [])[:5])
        RESULTS.mkdir(parents=True, exist_ok=True)
        name = f"m2b-{tag}-{edition}"
        (RESULTS / f"{name}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client1", "client2"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"{name}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
