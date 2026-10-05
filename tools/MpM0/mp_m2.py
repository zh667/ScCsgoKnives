"""current-direction-20260929 §6 M2 (part 1): feedback, protection, knife and workbench over the 1.9.3.2_MP engine. TEST-ONLY.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_m2.py <dev tag> [lite|full]
Host + two clients. Checked:
  PvP with CS protection: client1 shoots client2 (body, then head) with friendly fire on; the server settles protection and
  damage, client2's protection mirror and health follow, client2 gets damage marks and the kevlar / helmet sound in the
  victim role, client1 gets the hit confirmation and the sound in the shooter role.
  Knife: client1's own hit input swings; the server strikes a target in reach.
  Workbench commits from client1 (the op a dialog sends): repair (and a stale quote refused without taking anything),
  weapon craft, body protection for itself.
  A client whose handshake was rejected cannot fire: nothing changes on the server.
"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, MPBIN, sha, to_menu, join, poll, cs_packages, run_label
from mp_m1 import (STAND, FORCE_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, give_gun, record, inventory, health, teleport, EYE, near, move_to,
                   spawn_target)

def armor(i): return PROJECT + f'var a = project.FindSubsystem<Game.SubsystemScArmor>(true); return a.Get(Game.SubsystemScArmor.PlayerKey({i})).Encode();'
def give_armor(i): return PROJECT + (f'var a = project.FindSubsystem<Game.SubsystemScArmor>(true); var key = Game.SubsystemScArmor.PlayerKey({i}); '
    'a.TryCreate(key, Game.ScArmorState.For(Game.ScArmorConfig.Full)); return a.Get(key).Encode();')
FRIENDLY = PROJECT + 'var w = project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings; w.IsFriendlyFireEnabled = true; return w.IsFriendlyFireEnabled;'
def player_health(i): return player(i) + 'return pl.ComponentHealth.Health.ToString("R");'
def entity_of(i): return player(i) + 'return pl.Entity.Id;'
def aim_body(eid): return MAIN + (f'var t = project.Entities.FirstOrDefault(e => e.Id == {eid})?.FindComponent<Game.ComponentBody>(); if (t == null) return "no target"; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var bb = t.BoundingBox; var c = new Engine.Vector3((bb.Min.X + bb.Max.X) / 2, bb.Min.Y + (bb.Max.Y - bb.Min.Y) * 0.55f, (bb.Min.Z + bb.Max.Z) / 2); '
    'var d = Engine.Vector3.Normalize(c - eye); return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')
def aim_head(eid): return MAIN + (f'var t = project.Entities.FirstOrDefault(e => e.Id == {eid})?.FindComponent<Game.ComponentCreatureModel>(); if (t == null) return "no target"; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var c = t.EyePosition + new Engine.Vector3(0, 0.05f, 0); var d = Engine.Vector3.Normalize(c - eye); '
    'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')
COUNTERS = 'return "hits " + Game.ScNetFeedback.HitsReceived + " marks " + Game.ScNetFeedback.DamageMarksReceived + " sounds " + Game.ScNetFeedback.HitSoundsReceived + " last " + Game.ScNetFeedback.LastHitSound + " notices " + Game.ScNetFeedback.NoticesReceived;'
FEEDBACK = MAIN + 'var f = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true).FeedbackOf(pl); return f == null ? "none" : f.LastKind + " at " + f.LastAt.ToString("0.00");'
def counter(name, g): return int(g.func(f'return Game.ScNetFeedback.{name};'))

def place_bench(x, y, z): return PROJECT + (f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    f't.ChangeCell({x} + 2, {y}, {z}, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScWeaponWorkbenchBlock>(true))); return "ok";')
def op(kind, bench, value=0, qty=1, slot=-1, expected=0, revision=-1, arg=0, target="", state=""):
    bx, by, bz = bench
    return ('Game.ScNetWorkbench.LastResult = null; '
            f'Game.ScNetWorkbench.Run(new Game.ScWorkbenchOp(Game.ScWorkbenchOpKind.{kind}, new Engine.Point3({bx}, {by}, {bz}), {value}, {qty}, {slot}, {expected}, {revision}, {arg}, "{target}", "{state}"), '
            '() => new Game.ScWorkbenchResult(-9, "ran locally"), r => { }); return "sent";')
LAST = 'var r = Game.ScNetWorkbench.LastResult; return r == null ? "pending" : r.Value.Code + " " + r.Value.Detail;'
def count_of(i, value): return player(i) + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {value});'
def give_items(i, pairs): return player(i) + 'var inv = pl.ComponentMiner.Inventory; ' + ' '.join(
    f'for (int n = 0; n < {c}; ) {{ int s = Enumerable.Range(0, inv.SlotsCount).FirstOrDefault(q => inv.GetSlotCount(q) == 0 || inv.GetSlotValue(q) == {v} && inv.GetSlotCount(q) < inv.GetSlotCapacity(q, {v}), -1); if (s < 0) break; int k = System.Math.Min({c} - n, inv.GetSlotCapacity(s, {v}) - inv.GetSlotCount(s)); if (k <= 0) break; inv.AddSlotItems(s, {v}, k); n += k; }}'
    for v, c in pairs) + ' return "ok";'

def repair_setup(i): return player(i) + (
    'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "glock18"); int full = Game.ScGunDurability.Full(v); '
    'int id = Game.ScGunRegistry.Current.Allocate(v, 5, false, full / 2); '
    'int gun = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)); '
    'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(3, inv.GetSlotCount(3)); inv.AddSlotItems(3, gun, 1); '
    'var c = Game.ScWeaponRepair.Candidates(inv).First(q => q.Slot == 3); '
    'var quote = Game.ScWeaponRepair.Prepare(c, Game.ScWeaponCrafting.Find(c.Value), false, Game.ScWeaponMaterialBlock.Value); '
    'return id + " " + gun + " " + quote.Revision + " " + string.Join(",", quote.Cost.Select(m => m.Key + ":" + m.Value));')
def craft_setup(i): return player(i) + (
    'pl.PlayerData.Level = System.Math.Max(pl.PlayerData.Level, 3); '
    'var e = Game.ScWeaponCrafting.All.FirstOrDefault(q => !q.Knife && q.Name == "p250") ?? Game.ScWeaponCrafting.All.First(q => !q.Knife); '
    'return e.Value + " " + string.Join(",", e.Materials().Select(m => m.Key + ":" + m.Value));')
def armor_setup(i): return player(i) + (
    'pl.PlayerData.Level = System.Math.Max(pl.PlayerData.Level, 3); '
    'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
    'var cost = Game.ScArmorWorkbench.Cost(Game.ScArmorWorkbench.Operation.MakeVest, Game.ScArmorState.None); '
    'return string.Join(",", cost.Select(m => m.Key + ":" + m.Value));')
def give_knife(i): return player(i) + ('int knife = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true), 0, 0); '
    'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(2, inv.GetSlotCount(2)); inv.AddSlotItems(2, knife, 1); return knife.ToString();')
def body_at(i): return player(i) + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z}");'
TRACE = 'return Game.ScNet.TraceText;'
def pairs(text): return [tuple(map(int, p.split(":"))) for p in text.split(",") if p]


def main(tag, edition):
    server_pkgs, client_pkgs = cs_packages(tag, edition); edition = run_label(edition); ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"m2-{tag}-{edition}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"m2-{tag}-{edition}", "package": {p.name: sha(p) for p in {*server_pkgs, *client_pkgs}}, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": [], "checks": []}
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
    def fire(g, aim, frames):
        # A left click: the click (Hit) and the hold (Dig).
        step(f"{g.name} aim", aim); g.cmd(f"HIT_RAY {aim}"); g.cmd(f"DIG_RAY {aim} {frames}"); time.sleep(frames / 30 + 1.5)
    server = c1 = c2 = None
    try:
        server = Game("server", case_dir, [ta, *server_pkgs]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        step("survival mode", poll(server, SURVIVAL, lambda v: v == "Survival", 20))
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        x, y, z = step("arena", build_arena(server)).split()
        step("friendly fire", server.func(FRIENDLY))
        c1 = Game("client1", case_dir, [ta, *client_pkgs]); to_menu(c1); join(c1, server)
        c2 = Game("client2", case_dir, [ta, *client_pkgs]); to_menu(c2); join(c2, server)
        for g in [c1, c2]: check(f"{g.name} accepted", poll(g, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;')); i2 = int(c2.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        gid, gun, ammo = step("gun for client1", server.func(give_gun(i1))).split()
        step("full protection for client2 (server)", server.func(give_armor(i2)))
        into_arena(c1); into_arena(c2, 0, 7)
        for g in [c1, c2]: g.func(STAND)  # standing players (flying is airborne inaccuracy, as in single player)
        # The victim stands 7 m ahead of the shooter (the server's copy of its body is what the shot is traced against).
        want = (float(x) + 0.5, float(y), float(z) + 7.5)
        for attempt in range(8):
            at = server.func(body_at(i2)).split()
            if len(at) == 3 and abs(float(at[0]) - want[0]) < 0.7 and abs(float(at[2]) - want[2]) < 0.7: break
            c2.func(move_to(want)); time.sleep(0.8)
        check("client2 stands 7 m ahead (server)", abs(float(server.func(body_at(i2)).split()[2]) - want[2]) < 0.7, server.func(body_at(i2)))
        c2.func(MAIN + 'pl.ComponentHealth.Health = 1f; return "ok";')
        # One shot at a time on a sturdy target: a death would end the protection (the world's own death rule).
        step("client2 resilience x8 (server)", server.func(player(i2) + 'pl.ComponentHealth.AttackResilience *= 8; return pl.ComponentHealth.AttackResilience.ToString();'))
        check("client2 mirror shows its protection", poll(c2, armor(i2), lambda v: v == server.func(armor(i2)), 10) == server.func(armor(i2)), c2.func(armor(i2)))
        e2 = server.func(entity_of(i2))

        # 1. Body shot on an armoured player.
        a0, h0 = server.func(armor(i2)), server.func(player_health(i2))
        base1, base2 = c1.func(COUNTERS), c2.func(COUNTERS)
        fire(c1, c1.func(aim_body(e2)), 1)
        a1, h1 = step("client2 protection after body shot (server)", server.func(armor(i2))), step("client2 health after body shot (server)", server.func(player_health(i2)))
        check("vest absorbed on the server", a1 != a0 and a1.split("|")[1] != a0.split("|")[1], f"{a0} -> {a1}")
        check("client2 hurt on the server", float(h1) < float(h0), f"{h0} -> {h1}")
        check("client2 mirror follows its protection", poll(c2, armor(i2), lambda v: v == a1, 10) == a1, c2.func(armor(i2)))
        h_client = poll(c2, MAIN + 'return pl.ComponentHealth.Health.ToString("R");', lambda v: abs(float(v) - float(server.func(player_health(i2)))) < 0.01, 10)
        check("client2 health synced", abs(float(h_client) - float(server.func(player_health(i2)))) < 0.01, f"client {h_client} / server {server.func(player_health(i2))}")
        step("counters client1 / client2 after body shot", [c1.func(COUNTERS), c2.func(COUNTERS)])
        check("client2 got a damage mark", counter("DamageMarksReceived", c2) > int(base2.split()[3]), c2.func(COUNTERS))
        check("client2 heard kevlar as the victim", "Kevlar" in c2.func('return Game.ScNetFeedback.LastHitSound;') or "kevlar" in c2.func('return Game.ScNetFeedback.LastHitSound;'), c2.func(COUNTERS))
        check("client1 got its hit confirmation", counter("HitsReceived", c1) > int(base1.split()[1]), f"{c1.func(COUNTERS)} / {c1.func(FEEDBACK)}")
        check("client1 heard the hit as the shooter", counter("HitSoundsReceived", c1) > int(base1.split()[5]), c1.func(COUNTERS))

        # 2. Head shot: the helmet dink.
        c2.func(MAIN + 'pl.ComponentHealth.Health = 1f; return "ok";'); time.sleep(1)
        before_head = server.func(armor(i2))
        EYE_OF = lambda eid: PROJECT + (f'var m = project.Entities.FirstOrDefault(e => e.Id == {eid})?.FindComponent<Game.ComponentCreatureModel>(); var b = m?.Entity.FindComponent<Game.ComponentBody>(); if (m == null) return "none"; '
            'var e = m.EyePosition; var p = b.Position; return System.FormattableString.Invariant($"eye {e.X:0.000} {e.Y:0.000} {e.Z:0.000} feet {p.X:0.000} {p.Y:0.000} {p.Z:0.000} yaw {b.Rotation.ToYawPitchRoll().X:0.00}");')
        step("client2's eye: server / client1's view / client2 itself", [server.func(EYE_OF(e2)), c1.func(EYE_OF(e2)), c2.func(EYE_OF(e2))])
        fire(c1, c1.func(aim_head(e2)), 1)
        after_head = step("client2 protection after head shot (server)", server.func(armor(i2)))
        step("gun record after the head shot (server)", server.func(record(gid)))
        check("helmet absorbed on the server", after_head.split("|")[2] != before_head.split("|")[2], f"{before_head} -> {after_head}")
        check("client2 heard the helmet dink", "helmet_dink" in c2.func('return Game.ScNetFeedback.LastHitSound;'), c2.func(COUNTERS))

        # 3. Knife: client1's own hit input swings, the server strikes a target in reach.
        step("knife for client1", server.func(give_knife(i1)))
        c1.func(MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 2; return "ok";'); time.sleep(2.5)  # the knife's deploy clip
        # The knife strikes along the camera (the swing's own view), so the target stands where client1 looks.
        look = step("client1 camera", c1.func(MAIN + 'var cam = pl.GameWidget.ActiveCamera; var d = cam.ViewDirection; d.Y = 0; d = Engine.Vector3.Normalize(d); var at = pl.ComponentBody.Position + d * 1.6f; '
            'return System.FormattableString.Invariant($"{at.X} {at.Y} {at.Z}");')).split()
        cow = step("target in knife reach", server.func(spawn_target(0, 0, 0).replace("new Engine.Vector3(0f + 0.5f, 0f, 0f + 9.5f)", f"new Engine.Vector3({look[0]}f, {look[1]}f, {look[2]}f)"))).split()[0]
        time.sleep(1.5)
        hp0 = server.func(health(cow))
        aim = c1.func(aim_body(cow)); c1.func(FORCE_ACTIVE); step("client1 knife aim", aim)
        c1.cmd(f"HIT_RAY {aim}"); time.sleep(1.5)
        hp1 = step("knife target health (server)", server.func(health(cow)))
        step("server trace", server.func(TRACE)); step("client1 trace", c1.func(TRACE))
        check("the server's knife strike hurt the target", hp1 == "gone" or float(hp1) < float(hp0), f"{hp0} -> {hp1}")
        check("client1 sees the target health", poll(c1, health(cow), lambda v: v == server.func(health(cow)), 10) == server.func(health(cow)))

        # 4. Workbench commits from client1.
        server.func(place_bench(x, y, z)); bench = (int(x) + 2, int(y), int(z)); time.sleep(1)
        c1.func(move_to((float(x) + 0.5, float(y), float(z) + 0.5))); time.sleep(0.5)
        # Opening through the engine's own interact input: the client sends it, the server replays it for client1 and
        # tells that client to open its own workbench dialog.
        opens0 = int(c1.func('return Game.ScNetWorkbench.OpensReceived.ToString();'))
        ray = c1.func(MAIN + (f'var eye = pl.ComponentCreatureModel.EyePosition; var c = new Engine.Vector3({bench[0]} + 0.5f, {bench[1]} + 0.5f, {bench[2]} + 0.5f); var d = Engine.Vector3.Normalize(c - eye); '
            'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");'))
        c1.cmd("INTERACT_RAY " + ray); time.sleep(2)
        check("the server replayed client1's interaction and asked it to open the workbench", int(c1.func('return Game.ScNetWorkbench.OpensReceived.ToString();')) > opens0, server.func(TRACE))
        check("client1 shows its own workbench dialog", c1.func(MAIN + 'return DialogsManager.HasDialogs(pl.GuiWidget).ToString();') == "True")
        c1.func('DialogsManager.HideAllDialogs(); return "closed";'); time.sleep(0.5)
        rid, rgun, rrev, rcost = step("damaged glock + repair quote (server)", server.func(repair_setup(i1))).split(" ", 3)
        server.func(give_items(i1, pairs(rcost))); time.sleep(1.5)
        mats_before = {v: int(server.func(count_of(i1, v))) for v, _ in pairs(rcost)}
        c1.func(op("Repair", bench, slot=3, expected=rgun, revision=int(rrev) - 1))
        stale = step("stale repair quote (server answer)", poll(c1, LAST, lambda v: v != "pending", 15))
        check("stale repair refused, nothing taken", stale.startswith(str(3)) and all(int(server.func(count_of(i1, v))) == c for v, c in mats_before.items()), f"{stale} (StateChanged=3)")
        c1.func(op("Repair", bench, slot=3, expected=rgun, revision=int(rrev)))
        done = step("repair (server answer)", poll(c1, LAST, lambda v: v != "pending", 15))
        rec = server.func(record(rid))
        check("repair done on the server", done.startswith("0") and rec.split("/")[1] == server.func(f'return Game.ScGunDurability.Full(System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "glock18")).ToString();'), f"{done} / {rec}")
        check("repair materials taken on the server", all(int(server.func(count_of(i1, v))) == mats_before[v] - c for v, c in pairs(rcost)), rcost)
        check("client1 mirror shows the repair", poll(c1, record(rid), lambda v: v == rec, 10) == rec, c1.func(record(rid)))
        pvalue, pcost = step("p250 recipe (server)", server.func(craft_setup(i1))).split(" ", 1)
        server.func(give_items(i1, pairs(pcost))); time.sleep(1.5)
        had = int(server.func(count_of(i1, pvalue)))
        c1.func(op("Craft", bench, value=pvalue))
        craft = step("craft (server answer)", poll(c1, LAST, lambda v: v != "pending", 15))
        check("weapon crafted on the server", craft.startswith("1") and int(server.func(count_of(i1, pvalue))) == had + 1, craft)
        check("client1 inventory has the crafted weapon", poll(c1, MAIN + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {pvalue}).ToString();', lambda v: int(v) == had + 1, 10) == str(had + 1))
        acost = step("vest recipe (server)", server.func(armor_setup(i1)))
        server.func(give_items(i1, pairs(acost))); time.sleep(1.5)
        c1.func(op("Armor", bench, arg=0, target=f"player-{i1}", state="1|0,0,0|0,0,0"))
        step("vest materials on the server", {v: server.func(count_of(i1, v)) for v, _ in pairs(acost)})
        made = step("vest (server answer)", poll(c1, LAST, lambda v: v != "pending", 15))
        check("vest made on the server", made.startswith("1") and server.func(armor(i1)).split("|")[1].startswith("1,150,150"), f"{made} / {server.func(armor(i1))}")
        check("client1 mirror shows its vest", poll(c1, armor(i1), lambda v: v == server.func(armor(i1)), 10) == server.func(armor(i1)), c1.func(armor(i1)))

        # 5. A rejected client fires nothing.
        g2 = step("gun for client2", server.func(give_gun(i2))).split()
        c2.func(MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; return "ok";')
        c2.func('return ((Game.ScCsgoNetAdapter)Game.ScNet.Transport).SendHelloForTest(5);')
        check("client2 rejected", poll(c2, HANDSHAKE, lambda v: v == "Rejected", 15) == "Rejected", c2.func('return Game.ScNet.Transport.HandshakeDetail;'))
        r_before = server.func(record(g2[0]))
        fire(c2, c2.func(aim_body(server.func(entity_of(i1)))), 20)
        check("a rejected client's trigger changes nothing on the server", server.func(record(g2[0])) == r_before, f"{r_before} -> {server.func(record(g2[0]))}")
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [c2, c1, server]:
            if g is None: continue
            R[g.name] = {"errors": g.errors(), "net": g.net_lines()[-80:], "logLines": g.mark()}
            g.close()
        for g in ["server", "client1", "client2"]:
            check(f"{g} logged no errors", not R.get(g, {}).get("errors"), (R.get(g, {}).get("errors") or [])[:5])
        RESULTS.mkdir(parents=True, exist_ok=True)
        name = f"m2-{tag}-{edition}"
        (RESULTS / f"{name}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client1", "client2"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"{name}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
