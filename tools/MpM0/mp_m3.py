"""current-direction-20260929 §6 M3 (part 1): grenades and C4 over the 1.9.3.2_MP engine. TEST-ONLY packages.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_m3.py <dev tag> [lite|full]
Host + two clients, real input only (held dig for a throw, the E key for a plant). Checked:
  HE from client1: the server takes the grenade from client1's inventory, owns the flight, both clients see it fly, the
  blast hurts a target on the server and every client sees the same health.
  Flash from client1 at client2: the server measures client2's blindness and client2 is blinded (its own overlay).
  Smoke: both clients show the smoke the server made.
  C4: client1 holds E for the plant with its own 5 s fuse; the server arms the charge (item taken there), both clients
  count it down, the blast hurts a target on the server and reaches both clients.
"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, MPBIN, sha, to_menu, join, poll
from mp_m1 import FORCE_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, health, teleport, EYE, near, move_to, spawn_target

def give(i, value, count, slot=0): return player(i) + (f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems({slot}, inv.GetSlotCount({slot})); inv.AddSlotItems({slot}, {value}, {count}); '
    f'return inv.GetSlotValue({slot}) + "x" + inv.GetSlotCount({slot});')
GRENADE = 'Game.ScGrenadeBlock.Value({kind})'
def grenades(kind=-1, effect="null"): return f'return Game.GameManager.Project.FindSubsystem<Game.SubsystemScGrenades>(true).ActiveCount({kind}, {effect}).ToString();'
CHARGES = 'return Game.GameManager.Project.FindSubsystem<Game.SubsystemScC4>(true).Charges.Count.ToString();'
BLASTS = 'return Game.ScNetC4.BlastsShown.ToString();'  # blasts each client was shown (the drawn blast itself fades after a second)
# The server's live grenades: kind and position (the test moves its target next to one in flight).
LIVE = ('var g = Game.GameManager.Project.FindSubsystem<Game.SubsystemScGrenades>(true); var list = (System.Collections.IEnumerable)typeof(Game.SubsystemScGrenades).GetField("m_active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(g); '
    'var s = list.Cast<Game.ScGrenadeState>().FirstOrDefault(); return s == null ? "none" : System.FormattableString.Invariant($"{s.Kind} {s.Position.X} {s.Position.Y} {s.Position.Z} {s.Remaining}");')
BLINDED = MAIN + 'return project.FindSubsystem<Game.SubsystemScGrenades>(true).IsBodyBlinded(pl.ComponentBody).ToString();'
def count_of(i, value): return player(i) + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {value}).ToString();'
AHEAD = lambda d: MAIN + (f'var cam = pl.GameWidget.ActiveCamera; var v = cam.ViewDirection; v.Y = 0; v = Engine.Vector3.Normalize(v); var at = pl.ComponentBody.Position + v * {d}f; '
    'return System.FormattableString.Invariant($"{at.X} {at.Y} {at.Z}");')
STAND = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'
def target_at(p): return spawn_target(0, 0, 0).replace("new Engine.Vector3(0f + 0.5f, 0f, 0f + 9.5f)", f"new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f)")
TRACE = 'return Game.ScNet.TraceText;'
def slot(g, i): return g.func(MAIN + f'pl.ComponentMiner.Inventory.ActiveSlotIndex = {i}; return "ok";')


def main(tag, edition):
    pkg = PKG / f"dev-{tag}-{edition}.scmod"; ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"m3-{tag}-{edition}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"m3-{tag}-{edition}", "package": {pkg.name: sha(pkg)}, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": [], "checks": []}
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
    def throw(g, frames, pitch=-0.45):
        g.func(FORCE_ACTIVE)
        # A little downward so the throw lands well ahead of the thrower (the view is the throw's aim).
        g.func(MAIN + f'var a = pl.ComponentLocomotion.LookAngles; pl.ComponentLocomotion.LookAngles = new Engine.Vector2(a.X, {pitch}f); pl.Update(0f); pl.ComponentCreatureModel.Update(0f); return "ok";')
        time.sleep(0.3)
        eye = g.func(EYE).split()
        d = g.func(MAIN + 'var v = pl.GameWidget.ActiveCamera.ViewDirection; return System.FormattableString.Invariant($"{v.X} {v.Y} {v.Z}");').split()
        g.cmd(f"DIG_RAY {' '.join(eye)} {' '.join(d)} {frames}")
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
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;')); i2 = int(c2.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        into_arena(c1); into_arena(c2, 3, -3)
        for g in [c1, c2]: g.func(STAND)
        # Own grenades hurt their thrower (as in single player): sturdy players keep the scenario's inventories intact.
        step("sturdy players (server)", [server.func(player(i) + 'pl.ComponentHealth.AttackResilience *= 50; pl.ComponentHealth.FallResilience *= 50; return "ok";') for i in (i1, i2)])
        time.sleep(1.5)

        # 1. HE from client1 at a target 7 m ahead.
        he = server.func('return ' + GRENADE.format(kind=0) + '.ToString();')
        step("HE for client1 (server)", server.func(give(i1, he, 2, 1)))
        WATCH = player(i1) + (f'var inv = pl.ComponentMiner.Inventory; var pk = project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Count(q => q.Value == {he}); '
            'return inv.ActiveSlotIndex + ":" + string.Join(",", Enumerable.Range(0, 6).Select(q => inv.GetSlotValue(q) + "x" + inv.GetSlotCount(q))) + " pickables " + pk;')
        step("server before the slot switch", server.func(WATCH))
        slot(c1, 1)
        for k in range(8): step(f"server after the slot switch +{k * 0.3:.1f}s", server.func(WATCH)); time.sleep(0.3)
        step("client1 inventory", c1.func(MAIN + 'var inv = pl.ComponentMiner.Inventory; return inv.ActiveSlotIndex + ":" + string.Join(",", Enumerable.Range(0, 6).Select(q => inv.GetSlotValue(q) + "x" + inv.GetSlotCount(q)));'))
        step("traces", [server.func(TRACE), c1.func(TRACE)])
        UI = MAIN + ('var modal = pl.ComponentGui.ModalPanelWidget; var dialogs = pl.GuiWidget.AllChildren.OfType<Dialog>().Select(d => d.GetType().Name); '
            'return "modal " + (modal?.GetType().Name ?? "none") + " dialogs " + string.Join(",", dialogs) + " root " + string.Join(",", ScreensManager.RootWidget.AllChildren.OfType<Dialog>().Select(d => d.GetType().Name));')
        step("client1 panels and dialogs", c1.func(UI)); step("client2 panels and dialogs", c2.func(UI))
        ahead = c1.func(AHEAD(5)).split()
        cow = step("target 5 m ahead of client1", server.func(target_at(ahead))).split()[0]
        time.sleep(1.5)
        hp0 = server.func(health(cow)); n0 = int(server.func(count_of(i1, he)))
        seen1 = seen2 = 0; moved_to_grenade = False
        throw(c1, 50, -0.2)
        for k in range(30):
            seen1 = max(seen1, int(c1.func(grenades(0, "false")))); seen2 = max(seen2, int(c2.func(grenades(0, "false"))))
            if k % 3 == 0: step(f"server during the throw +{k * 0.1:.1f}s", server.func(WATCH) + " active " + server.func(grenades()))
            live = server.func(LIVE)
            if live != "none" and not moved_to_grenade:
                # The explosion's damage is the server's: the target is put beside the grenade it owns, before the fuse ends.
                gx, gy, gz = (float(v) for v in live.split()[1:4])
                server.func(PROJECT + f'var b = project.Entities.First(e => e.Id == {cow}).FindComponent<Game.ComponentBody>(); b.Position = new Engine.Vector3({gx + 1.2}f, {gy}f, {gz}f); return "ok";')
                step("target moved beside the server's grenade", live); moved_to_grenade = True
            time.sleep(0.1)
        time.sleep(2.5)
        step("server trace", server.func(TRACE)); step("client1 trace", c1.func(TRACE))
        check("the server took the HE from client1", int(server.func(count_of(i1, he))) == n0 - 1, f"{n0} -> {server.func(count_of(i1, he))}")
        check("client1's inventory follows", poll(c1, count_of(i1, he), lambda v: int(v) == n0 - 1, 10) == str(n0 - 1))
        check("both clients saw the HE fly", seen1 > 0 and seen2 > 0, f"client1 {seen1}, client2 {seen2}")
        hp1 = step("target after the HE (server)", server.func(health(cow)))
        check("the server's HE hurt the target", hp1 == "gone" or float(hp1) < float(hp0), f"{hp0} -> {hp1}")
        check("clients see the same health", all(poll(g, health(cow), lambda v: v == server.func(health(cow)), 10) == server.func(health(cow)) for g in [c1, c2]))

        # 2. Flash from client1 at client2 (client2 stands in front, facing it).
        fl = server.func('return ' + GRENADE.format(kind=1) + '.ToString();')
        step("flash for client1 (server)", server.func(give(i1, fl, 1, 2)))
        slot(c1, 2); time.sleep(2.5)
        c2.func(move_to([float(v) for v in c1.func(AHEAD(5)).split()]))
        c2.func(MAIN + (f'var me = pl.ComponentBody.Position; var other = project.Entities.First(e => e.Id == {server.func(player(i1) + "return pl.Entity.Id;")}).FindComponent<Game.ComponentBody>().Position; '
                        'var d = other - me; pl.ComponentLocomotion.LookAngles = new Engine.Vector2(MathF.Atan2(-d.X, -d.Z), 0); return "ok";'))
        time.sleep(1)
        throw(c1, 6)
        blinded = False
        for k in range(40):
            if c2.func(BLINDED) == "True": blinded = True; break
            time.sleep(0.1)
        check("client2 was blinded by client1's flash", blinded, c2.func(BLINDED))

        # 3. Smoke: both clients show it.
        sm = server.func('return ' + GRENADE.format(kind=2) + '.ToString();')
        step("smoke for client1 (server)", server.func(give(i1, sm, 1, 3)))
        c2.func(move_to((float(x) + 3.5, float(y), float(z) - 2.5)))
        slot(c1, 3); time.sleep(2.5)
        throw(c1, 6)
        smoke1 = smoke2 = 0
        for k in range(80):
            smoke1 = max(smoke1, int(c1.func(grenades(2, "true")))); smoke2 = max(smoke2, int(c2.func(grenades(2, "true"))))
            if smoke1 and smoke2: break
            time.sleep(0.2)
        check("both clients show the server's smoke", smoke1 > 0 and smoke2 > 0 and int(server.func(grenades(2, "true"))) > 0, f"server {server.func(grenades(2, 'true'))}, client1 {smoke1}, client2 {smoke2}")

        # 4. C4 with client1's own 5 s fuse.
        c1.func(f'Game.ScUiSettings.C4Fuses[{i1}] = 5; return "ok";')
        c4 = server.func('return Game.ScC4Block.Value.ToString();')
        step("C4 for client1 (server)", server.func(give(i1, c4, 1, 4)))
        c1.func(move_to((float(x) + 0.5, float(y), float(z) + 0.5))); c1.func(STAND)
        slot(c1, 4); time.sleep(2.5)
        near_cow = step("target beside the C4 spot", server.func(target_at([float(x) + 2.5, float(y), float(z) + 0.5]))).split()[0]
        time.sleep(1)
        hpc0 = server.func(health(near_cow)); nc0 = int(server.func(count_of(i1, c4)))
        WATCH4 = player(i1) + (f'var inv = pl.ComponentMiner.Inventory; var pk = project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Count(q => q.Value == {c4}); '
            'return inv.ActiveSlotIndex + ":" + string.Join(",", Enumerable.Range(0, 6).Select(q => inv.GetSlotValue(q) + "x" + inv.GetSlotCount(q))) + " pickables " + pk;')
        step("server before the plant", server.func(WATCH4))
        c1.func(FORCE_ACTIVE); c1.cmd("KEYDOWN E")
        for k in range(8): step(f"server during the plant +{k * 0.5:.1f}s", server.func(WATCH4) + " / client modal " + c1.func(MAIN + 'return (pl.ComponentGui.ModalPanelWidget?.GetType().Name ?? "none");')); time.sleep(0.5)
        c1.cmd("KEYUP E")
        armed = step("charges (server / client1 / client2)", [server.func(CHARGES), c1.func(CHARGES), c2.func(CHARGES)])
        step("server trace", server.func(TRACE)); step("client1 trace", c1.func(TRACE))
        check("the server armed client1's C4 and took the item", armed[0] == "1" and int(server.func(count_of(i1, c4))) == nc0 - 1, f"{armed} items {nc0}->{server.func(count_of(i1, c4))}")
        check("both clients count the charge down", armed[1] == "1" and armed[2] == "1", str(armed))
        c1.func(move_to((float(x) - 3.0, float(y), float(z) - 3.0))); c2.func(move_to((float(x) - 3.0, float(y), float(z) - 3.5)))
        b1, b2 = int(c1.func(BLASTS)), int(c2.func(BLASTS))
        time.sleep(6.5)
        hpc1 = step("target after the C4 (server)", server.func(health(near_cow)))
        check("the server's C4 blast hurt the target", hpc1 == "gone" or float(hpc1) < float(hpc0), f"{hpc0} -> {hpc1}")
        check("both clients showed the blast", int(c1.func(BLASTS)) > b1 and int(c2.func(BLASTS)) > b2, f"{c1.func(BLASTS)} {c2.func(BLASTS)}")
        check("no charge left anywhere", [server.func(CHARGES), c1.func(CHARGES), c2.func(CHARGES)] == ["0", "0", "0"])
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
        name = f"m3-{tag}-{edition}"
        (RESULTS / f"{name}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client1", "client2"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"{name}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
