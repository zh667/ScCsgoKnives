"""current-direction-20260929 §6 M3 (part 2): the Tactical addon (Full edition) over the 1.9.3.2_MP engine. TEST-ONLY packages.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_m3t.py <dev tag> [full]
Host + two clients. Checked:
  Companion beacon used by client1 through the engine's own interact input: the server creates the companion (owner
  client1) and takes the beacon; both clients get the entity and the server's voice events.
  Orders from client1 (the panel's own call): the server applies them and every client shows the server's orders; an
  order from client2 for client1's companion is refused on the server with a notice to client2.
  Panel open on client1 keeps the server's companion still ("整理装备"), closing releases it.
  Companion combat: an armed companion shoots a predator on the server (record rounds drop there); clients hear the
  shots and show the server's action timeline.
  Enemy squad beacon used by client2; enemies exist on every client; an enemy's reload shows on the clients.
  Enemy bomb planted on the server: both clients show it; client1 defuses it with its own E key and a kit, the server
  resolves it, removes it everywhere and grants the reward.
  Enemy death: loot only on the server (clients hold the same pickables, no local drops).
  Dismiss (client1, own empty companion) and an ownerless shell removed by client2: gone on the server and clients.
"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, MPBIN, sha, to_menu, join, poll, cs_packages, run_label
from mp_m1 import FORCE_ACTIVE, KEEP_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, arena_on_clients, HANDSHAKE, player, health, teleport, EYE, near, move_to

TRACE = 'return Game.ScNet.TraceText;'
TAC = PROJECT + 'var tac = project.FindSubsystem<Game.SubsystemScTactical>(true); '
COMPANIONS = TAC + 'return string.Join(",", tac.Companions.Select(c => c.Entity.Id + ":" + c.OwnerIndex));'
def companion(eid): return TAC + f'var c = tac.Companions.FirstOrDefault(q => q.Entity.Id == {eid}); if (c == null) return "none"; '
def comp_state(eid): return companion(eid) + 'return c.Order + "|" + c.CeaseFire + "|" + c.Status;'
def comp_action(eid): return companion(eid) + 'var a = c.VisualAction; return a.Sequence + " " + a.Kind;'
def comp_held(eid): return companion(eid) + 'return c.Inventory.GetSlotValue(0) + "x" + c.Inventory.GetSlotCount(0);'
def exists(eid): return PROJECT + f'return project.Entities.Any(e => e.Id == {eid}).ToString();'
BEACON_CT = 'return Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScTacticalBeaconBlock>(true), 0, 1).ToString();'
SQUAD = 'return Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScTacticalSquadBlock>(true), 0, 0).ToString();'
KIT = 'return Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScTacticalDefuserBlock>(true), 0, 0).ToString();'
BLANK = 'return Game.ScWeaponMaterialBlock.Value(Game.ScWeaponMaterialBlock.Blank).ToString();'
def give(i, value, count, slot): return player(i) + (f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems({slot}, inv.GetSlotCount({slot})); inv.AddSlotItems({slot}, {value}, {count}); '
    f'return inv.GetSlotValue({slot}) + "x" + inv.GetSlotCount({slot});')
def count_of(i, value): return player(i) + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {value}).ToString();'
def slot(g, i): return g.func(MAIN + f'pl.ComponentMiner.Inventory.ActiveSlotIndex = {i}; return "ok";')
def order(eid, command): return MAIN + (f'var c = project.FindSubsystem<Game.SubsystemScTactical>(true).Companions.FirstOrDefault(q => q.Entity.Id == {eid}); if (c == null) return "none"; '
    f'Game.TacticalNet.Order(pl, c, Game.TacticalNet.Command.{command}); return "sent";')
def panel(eid, open_): return MAIN + (f'var c = project.FindSubsystem<Game.SubsystemScTactical>(true).Companions.FirstOrDefault(q => q.Entity.Id == {eid}); if (c == null) return "none"; '
    + ('Game.TacticalNet.PanelOpen(pl, c);' if open_ else 'Game.TacticalNet.PanelClosed(pl, c);') + ' return "sent";')
# The ground d metres ahead of the camera (horizontally), as an interact ray from the eye.
def ground_ray(d): return MAIN + (f'var cam = pl.GameWidget.ActiveCamera; var v = cam.ViewDirection; v.Y = 0; v = Engine.Vector3.Normalize(v); var eye = cam.ViewPosition; '
    f'var p = pl.ComponentBody.Position + v * {d}f; var at = new Engine.Vector3(System.MathF.Floor(p.X) + 0.5f, System.MathF.Floor(p.Y) - 0.05f, System.MathF.Floor(p.Z) + 0.5f); '  # just under a floor cell's top, at its centre
    'var dir = Engine.Vector3.Normalize(at - eye); '
    'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {dir.X} {dir.Y} {dir.Z}");')
AHEAD = lambda d: MAIN + (f'var cam = pl.GameWidget.ActiveCamera; var v = cam.ViewDirection; v.Y = 0; v = Engine.Vector3.Normalize(v); var at = pl.ComponentBody.Position + v * {d}f; '
    'return System.FormattableString.Invariant($"{at.X} {at.Y} {at.Z}");')
STAND = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'
def sturdy(i): return player(i) + 'pl.ComponentHealth.AttackResilience *= 50; return pl.ComponentHealth.AttackResilience.ToString();'
def predator(p): return PROJECT + (
    'GameEntitySystem.Entity e = null; foreach (var n in new[] { "Wolf_Gray", "Wolf_Coyote", "Werewolf", "Bear_Black", "Bear_Brown", "Tiger" }) { try { e = Game.DatabaseManager.CreateEntity(project, n, true); break; } catch { } } '
    'if (e == null) return "no template"; '
    f'var b = e.FindComponent<Game.ComponentBody>(true); b.Position = new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f); b.Velocity = Engine.Vector3.Zero; '
    'var loco = e.FindComponent<Game.ComponentLocomotion>(); if (loco != null) { loco.WalkSpeed = 0; loco.FlySpeed = 0; loco.SwimSpeed = 0; loco.JumpSpeed = 0; } '
    'var h = e.FindComponent<Game.ComponentHealth>(); if (h != null) { h.m_regenerateLifeEnabled = false; h.AttackResilience *= 20; } '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) { sp.SpawnDuration = 0f; sp.AutoDespawn = false; sp.DespawnTime = null; } '
    'project.AddEntity(e); return e.Id + " " + (e.ValuesDictionary?.DatabaseObject?.Name ?? "?");')
def arm_companion(eid): return companion(eid) + (
    'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"); int id = Game.ScGunRegistry.Current.Allocate(v, 30, false, Game.ScGunDurability.Full(v)); '
    'int gun = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)); '
    'int ammo = Game.ScAmmoBlock.Value(Game.ScReloadTransaction.AmmoKind(Game.GunSpec.All[v])); c.Inventory.AddSlotItems(0, gun, 1); c.Inventory.AddSlotItems(1, ammo, 3); '
    'return id + " " + gun;')
def record(id_): return f'return Game.ScGunRegistry.Current.TryGetSnapshot({id_}, out var s) ? s.Rounds + "/" + s.Durability + "/" + s.Revision : "none";'
ENEMIES = PROJECT + 'return string.Join(",", project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.Select(e => e.Entity.Id).OrderBy(i => i));'
def enemy(eid): return PROJECT + f'var en = project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.FirstOrDefault(q => q.Entity.Id == {eid}); if (en == null) return "none"; '
def enemy_action(eid): return enemy(eid) + 'var a = en.VisualAction; return a.Kind + " " + a.Sequence;'
def make_enemy(p): return PROJECT + (
    'var e = Game.DatabaseManager.CreateEntity(project, "ScTacticalEnemy", true); var en = e.FindComponent<Game.ComponentTacticalEnemy>(true); '
    'var st = new Game.TacticalEnemyState { Squad = "mp-test", Role = Game.TacticalRole.Rifle, Variant = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"), Rounds = 30, Reserve = 90, Source = "manual" }; '
    f'en.Configure(st, new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f)); var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; '
    'project.AddEntity(e); return e.Id.ToString();')
BOMBS = PROJECT + ('var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); '
    'return b.Bombs.Count + " " + string.Join(",", b.Bombs.Select(q => (q.Defuser?.PlayerData.PlayerIndex ?? -1) + "@" + q.Charge.Remaining.ToString("0.0")));')
def plant(p): return PROJECT + f'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); var ok = b.PlantFrom(new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f), 0f, true); return ok + " " + b.Bombs.Count;'
LOOK_AT_BOMB = MAIN + ('var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); if (b.Bombs.Count == 0) return "no bomb"; var target = b.Bombs[0].Charge.Position + new Engine.Vector3(0, 0.1f, 0); '
    'var eye = pl.GameWidget.ActiveCamera.ViewPosition; var d = target - eye; float pitch = System.MathF.Atan2(d.Y, new Engine.Vector2(d.X, d.Z).Length()); '
    'var a = pl.ComponentLocomotion.LookAngles; pl.ComponentLocomotion.LookAngles = new Engine.Vector2(a.X, pitch); pl.Update(0f); pl.ComponentCreatureModel.Update(0f); '
    'return (b.Target(pl) != null) + " pitch " + pitch.ToString("0.00");')
PICKABLES = PROJECT + 'return project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Count.ToString();'
def ownerless(p): return PROJECT + (
    'var e = Game.DatabaseManager.CreateEntity(project, "ScTacticalCT", true); e.FindComponent<Game.ComponentBody>(true).Position = '
    f'new Engine.Vector3({p[0]}f, {p[1]}f, {p[2]}f); var c = e.FindComponent<Game.ComponentTacticalCompanion>(true); c.OwnerIndex = -1; '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; project.AddEntity(e); return e.Id.ToString();')
NOTICE = 'return Game.ScNetFeedback.NoticesReceived + " " + Game.ScNetFeedback.LastNotice;'
VOICES = 'return Game.ScNetFeedback.VoicesReceived.ToString();'
SOUNDS = 'return Game.ScNetGrenades.SoundsReceived + " " + Game.ScNetGrenades.LastSound;'
NET = 'return "npc " + Game.TacticalNet.NpcRecordsSent + "/" + Game.TacticalNet.NpcRecordsApplied + " cmd " + Game.TacticalNet.CommandsApplied + "/" + Game.TacticalNet.CommandsRefused + " bombs " + Game.TacticalNet.BombsSent + "/" + Game.TacticalNet.BombsApplied + " refusal " + Game.TacticalNet.LastRefusal;'


def main(tag, edition):
    server_pkgs, client_pkgs = cs_packages(tag, edition); edition = run_label(edition); ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"m3t-{tag}-{edition}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"m3t-{tag}-{edition}", "package": {p.name: sha(p) for p in {*server_pkgs, *client_pkgs}}, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": [], "checks": []}
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
        c1 = Game("client1", case_dir, [ta, *client_pkgs]); to_menu(c1); join(c1, server)
        c2 = Game("client2", case_dir, [ta, *client_pkgs]); to_menu(c2); join(c2, server)
        for g in [c1, c2]: check(f"{g.name} accepted", poll(g, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;')); i2 = int(c2.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        into_arena(c1); into_arena(c2, 3, 0)
        synced = step("arena cells differing on each client from the server's (first / after the platform and the adapter reconcile)", arena_on_clients(server, [c1, c2], x, y, z, seconds=120))
        check("both clients received the server's pre-join arena without any test resend", all(r[3] is not None for r in synced), synced)
        into_arena(c1); into_arena(c2, 3, 0)
        # client1 stands (its defuse needs footing); client2 stays flying at arena height: it only uses beacons and orders,
        # and one run showed it falling through an arena floor cell its client did not yet have (server edit).
        c1.func(STAND)
        step("sturdy players (server)", [server.func(sturdy(i1)), server.func(sturdy(i2))])
        time.sleep(1.5)

        # 1. A CT companion from client1's beacon, through the engine's own interact input.
        ct = server.func(BEACON_CT)
        step("beacons for client1 (server)", server.func(give(i1, ct, 2, 1)))
        slot(c1, 1); time.sleep(1.5)
        before = set(filter(None, server.func(COMPANIONS).split(",")))
        v0 = [int(g.func(VOICES)) for g in [c1, c2]]
        c1.func(FORCE_ACTIVE); c1.cmd("INTERACT_RAY " + c1.func(ground_ray(2.5))); time.sleep(2.5)
        after = set(filter(None, server.func(COMPANIONS).split(",")))
        made = step("companions (server)", sorted(after - before))
        check("the server made client1's companion", len(made) == 1 and made[0].endswith(f":{i1}"), f"{sorted(before)} -> {sorted(after)} / {c1.func(NOTICE)} / {server.func(TRACE)}")
        eid = made[0].split(":")[0] if made else None
        if eid is None:
            # Keep the later sections covered: a companion for client1 made directly on the server.
            spot = [float(v) for v in c1.func(AHEAD(2.5)).split()]
            eid = step("companion made directly (server)", server.func(PROJECT + (
                'var e = Game.DatabaseManager.CreateEntity(project, "ScTacticalCT", true); '
                f'e.FindComponent<Game.ComponentBody>(true).Position = new Engine.Vector3({spot[0]}f, {spot[1]}f, {spot[2]}f); '
                f'var c = e.FindComponent<Game.ComponentTacticalCompanion>(true); c.OwnerIndex = {i1}; c.GuardPosition = e.FindComponent<Game.ComponentBody>(true).Position; '
                'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; project.AddEntity(e); return e.Id.ToString();')))
            time.sleep(1.5)
        check("the server took one beacon", server.func(count_of(i1, ct)) == "1", server.func(count_of(i1, ct)))
        check("client1's inventory follows", poll(c1, count_of(i1, ct), lambda v: v == "1", 10) == "1")
        check("both clients have the companion", all(poll(g, exists(eid), lambda v: v == "True", 10) == "True" for g in [c1, c2]))

        # Sturdy like the players: the scenario's enemies are real and would otherwise kill it before the later sections.
        server.func(companion(eid) + 'c.Creature.ComponentHealth.AttackResilience *= 50; return "ok";')

        # 2. Orders: client1's own apply on the server and every client shows them; client2's are refused.
        c1.func(order(eid, "Guard")); time.sleep(1)
        check("client1's order applied on the server", server.func(comp_state(eid)).startswith("Guard|"), server.func(comp_state(eid)))
        check("clients show the server's order", all(poll(g, comp_state(eid), lambda v: v.startswith("Guard|"), 10).startswith("Guard|") for g in [c1, c2]),
              [g.func(comp_state(eid)) for g in [c1, c2]])
        check("clients got the server's voice events (spawn, order confirmation)", all(int(g.func(VOICES)) > v for g, v in zip([c1, c2], v0)), [g.func(VOICES) for g in [c1, c2]])
        c1.func(order(eid, "ToggleCeaseFire")); time.sleep(1)
        check("cease fire toggled on the server", server.func(comp_state(eid)).split("|")[1] == "True", server.func(comp_state(eid)))
        c1.func(order(eid, "ToggleCeaseFire")); c1.func(order(eid, "Follow")); time.sleep(1)
        n2 = c2.func(NOTICE)
        c2.func(order(eid, "Guard")); time.sleep(1)
        check("client2's order for client1's companion refused", server.func(comp_state(eid)).startswith("Follow|False"), f"{server.func(comp_state(eid))} / {server.func(NET)}")
        check("client2 told why", poll(c2, NOTICE, lambda v: v != n2 and "其他玩家" in v, 10) != n2, c2.func(NOTICE))

        # 3. Panel open on client1 keeps the companion still on the server.
        c1.func(panel(eid, True)); time.sleep(0.8)
        check("open panel pauses the companion on the server", server.func(comp_state(eid)).endswith("|整理装备"), server.func(comp_state(eid)))
        check("client1 shows it", poll(c1, comp_state(eid), lambda v: v.endswith("|整理装备"), 10).endswith("|整理装备"), c1.func(comp_state(eid)))
        c1.func(panel(eid, False)); time.sleep(0.8)
        check("closed panel releases it", not server.func(comp_state(eid)).endswith("|整理装备"), server.func(comp_state(eid)))

        # 4. Companion combat on the server; clients hear and show it.
        gid, gun = step("companion armed (server)", server.func(arm_companion(eid))).split()
        check("clients see the companion's gun", all(poll(g, comp_held(eid), lambda v: v == f"{gun}x1", 10) == f"{gun}x1" for g in [c1, c2]), [g.func(comp_held(eid)) for g in [c1, c2]])
        # The companion guards a spot in front of both players, so no player stands in its line of fire (it never shoots
        # through a friend, as in single player); the predator appears beyond it.
        server.func(companion(eid) + f'c.Creature.ComponentBody.Position = new Engine.Vector3({float(x) + 0.5}f, {y}f, {float(z) + 4.5}f); return "ok";'); time.sleep(0.5)
        c1.func(order(eid, "Guard")); time.sleep(0.8)
        r0 = server.func(record(gid)); a0 = [g.func(comp_action(eid)) for g in [c1, c2]]; s0 = [int(g.func(SOUNDS).split()[0]) for g in [c1, c2]]
        wolf = step("predator ahead (server)", server.func(predator([float(x) + 0.5, float(y), float(z) + 13.5]))).split()[0]
        shot = poll(server, record(gid), lambda v: v != r0, 20)
        PROBE = companion(eid) + (f'var w = project.Entities.FirstOrDefault(e => e.Id == {wolf}); var wb = w?.FindComponent<Game.ComponentBody>(); var wc = w?.FindComponent<Game.ComponentCreature>(); '
            'var t = typeof(Game.ComponentTacticalCompanion).GetField("threat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(c) as Game.ComponentBody; '
            'var mut = Game.ScGunMutation.Prepare(c.Inventory, 0, Game.ScGunHolders.Key(c.Inventory, 0), out Game.ScGunResult why); '
            'return "threat " + (t?.Entity.Id.ToString() ?? "none") + " active " + c.IsActive + " wolf " + (wc == null ? "gone" : wc.Category + " hp " + wc.ComponentHealth.Health.ToString("0.00") + " d " + Engine.Vector3.Distance(wb.Position, c.Creature.ComponentBody.Position).ToString("0.0")) '
            '+ " mutation " + (mut == null ? why.ToString() : "ok") + " status " + c.Status '
            '+ " hostile " + (wb == null ? "-" : typeof(Game.ComponentTacticalCompanion).GetMethod("Hostile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, new object[] { wb, c.Owner })) '
            '+ " visible " + (wb == null ? "-" : typeof(Game.ComponentTacticalCompanion).GetMethod("Visible", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, new object[] { wb })) '
            '+ " owner " + (c.Owner == null ? "none" : Engine.Vector3.Distance(c.Owner.ComponentBody.Position, wb?.Position ?? Engine.Vector3.Zero).ToString("0.0"));')
        check("the companion fired on the server", shot != r0, f"{r0} -> {shot} / {server.func(PROBE)}")
        time.sleep(1.5)
        check("clients heard the companion's shots", all(int(g.func(SOUNDS).split()[0]) > s for g, s in zip([c1, c2], s0)), [g.func(SOUNDS) for g in [c1, c2]])
        check("clients show the server's actions", all(g.func(comp_action(eid)) != a for g, a in zip([c1, c2], a0)), f"{a0} -> {[g.func(comp_action(eid)) for g in [c1, c2]]} / server {server.func(comp_action(eid))}")
        server.func(PROJECT + f'var h = project.Entities.FirstOrDefault(e => e.Id == {wolf})?.FindComponent<Game.ComponentHealth>(); if (h != null) h.Injure(1f, null, true, "test"); return "ok";')
        c1.func(order(eid, "ToggleCeaseFire")); time.sleep(1)

        # 4b. A late joiner gets the companion's current state (orders, status, held gun) without any change of its own.
        c3 = Game("client3", case_dir, [ta, *client_pkgs], role="client"); to_menu(c3); join(c3, server)
        try:
            check("client3 accepted", poll(c3, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
            want = server.func(comp_state(eid))
            check("the late joiner shows the companion's orders", poll(c3, comp_state(eid), lambda v: v.split("|")[:2] == want.split("|")[:2], 15).split("|")[:2] == want.split("|")[:2],
                  f"{c3.func(comp_state(eid))} / server {want}")
            check("the late joiner sees its gun", poll(c3, comp_held(eid), lambda v: v == f"{gun}x1", 10) == f"{gun}x1", c3.func(comp_held(eid)))
        finally:
            c3.close()

        # 5. Enemy squad beacon from client2; enemies everywhere; a reload shows on the clients.
        sq = server.func(SQUAD)
        step("squad beacon for client2 (server)", server.func(give(i2, sq, 1, 1)))
        slot(c2, 1); time.sleep(1.5)
        e0 = set(filter(None, server.func(ENEMIES).split(",")))
        n2 = c2.func(NOTICE)
        # Aimed just under the top of a floor cell's centre inside the cleared arena, about 6 blocks from client2.
        sq_ray = c2.func(MAIN + (f'var eye = pl.ComponentCreatureModel.EyePosition; var at = new Engine.Vector3({float(x) + 0.5}f, {float(y) - 0.05}f, {float(z) + 6.5}f); var d = Engine.Vector3.Normalize(at - eye); '
            'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");'))
        c2.cmd("INTERACT_RAY " + sq_ray); time.sleep(3)
        e1 = set(filter(None, server.func(ENEMIES).split(",")))
        step("client2's notice", c2.func(NOTICE))
        check("the squad beacon made a 3-member squad on the server", len(e1 - e0) == 3, f"{len(e1 - e0)} enemies / {c2.func(NOTICE)} / {server.func(TRACE)}")
        if not (e1 - e0):
            made_enemy = step("enemy made directly (server)", server.func(make_enemy([float(x) + 0.5, float(y), float(z) + 16.5])))
            time.sleep(1.5); e1 = set(filter(None, server.func(ENEMIES).split(",")))
        check("clients have the server's enemies", all(poll(g, ENEMIES, lambda v: set(filter(None, v.split(","))) == e1, 15) == server.func(ENEMIES) for g in [c1, c2]),
              [server.func(ENEMIES), c1.func(ENEMIES), c2.func(ENEMIES)])
        en = sorted(e1)[0]
        server.func(enemy(en) + 'en.State.ReloadLeft = 2.8f; return "ok";')
        check("an enemy's reload shows on the clients", all(poll(g, enemy_action(en), lambda v: v.startswith("Reload"), 5).startswith("Reload") for g in [c1, c2]),
              [g.func(enemy_action(en)) for g in [c1, c2]])
        step("net counters (server / client1)", [server.func(NET), c1.func(NET)])

        # 6. Enemy bomb: client1 defuses it with a kit and its own E key; the server resolves and rewards.
        c1.func(move_to((float(x) + 0.5, float(y), float(z) + 0.5))); c1.func(STAND); time.sleep(1)
        at = [float(v) for v in c1.func(AHEAD(1.3)).split()]
        step("bomb planted (server)", server.func(plant((at[0], at[1] + 0.1, at[2]))))
        check("both clients show the bomb", all(poll(g, BOMBS, lambda v: v.startswith("1 "), 10).startswith("1 ") for g in [c1, c2]), [g.func(BOMBS) for g in [c1, c2]])
        step("kit for client1 (server)", server.func(give(i1, server.func(KIT), 1, 5)))
        blank = server.func(BLANK); b0 = int(server.func(count_of(i1, blank)))
        slot(c1, 2); time.sleep(1)
        step("client1 looks at the bomb", c1.func(LOOK_AT_BOMB)); time.sleep(0.5)
        n1 = c1.func(NOTICE)
        c1.func(FORCE_ACTIVE); c1.cmd("KEYDOWN E"); time.sleep(1.5)
        check("the server runs client1's defuse", server.func(BOMBS).startswith(f"1 {i1}@"), f"{server.func(BOMBS)} / {server.func(TRACE)}")
        check("client1 shows its defuse", c1.func(BOMBS).startswith(f"1 {i1}@"), c1.func(BOMBS))
        time.sleep(5); c1.cmd("KEYUP E")
        check("the bomb is gone on the server", server.func(BOMBS).startswith("0"), server.func(BOMBS))
        check("and on both clients", all(poll(g, BOMBS, lambda v: v.startswith("0"), 10).startswith("0") for g in [c1, c2]), [g.func(BOMBS) for g in [c1, c2]])
        check("client1 told it is defused", poll(c1, NOTICE, lambda v: v != n1, 10) != n1, c1.func(NOTICE))
        check("the defuse reward reached client1 (server)", poll(server, count_of(i1, blank), lambda v: int(v) >= b0 + 2, 10) == str(b0 + 2), f"{b0} -> {server.func(count_of(i1, blank))}")

        # 7. An enemy's death: loot only from the server.
        server.func(enemy(en) + 'en.Creature.ComponentHealth.Injure(1f, null, true, "test"); return "ok";'); time.sleep(3)
        pk = [server.func(PICKABLES), c1.func(PICKABLES), c2.func(PICKABLES)]
        check("no loot of the clients' own", pk[1] == pk[0] and pk[2] == pk[0], f"server / client1 / client2 pickables {pk}")
        server.func(PROJECT + 'foreach (var e in project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.ToArray()) project.RemoveEntity(e.Entity, true); return "ok";'); time.sleep(1)
        check("the companion is still alive for the dormancy check", server.func(exists(eid)) == "True", server.func(comp_state(eid)))

        # 7b. Dormancy follows every player, not only the host's camera: with the host's own player far away and client1
        # beside its companion, the companion stays; with everyone far it sleeps (gone on every peer); client1 back, it wakes
        # with the same entity id.
        DORMANT = PROJECT + 'return project.FindSubsystem<Game.SubsystemTacticalCompanions>(true).DormantCount.ToString();'
        HOST_X = MAIN + 'return pl.ComponentBody.Position.X.ToString("0");'
        BODY_AT = lambda i: player(i) + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X:0.0} {p.Y:0.0} {p.Z:0.0}");'
        def far(g, i):
            for attempt in range(10):
                g.func(teleport(int(x) + 300, int(y) + 20, z)); time.sleep(0.8)
                if abs(float(server.func(BODY_AT(i)).split()[0]) - float(x)) > 250: return True
            return False
        c1.func(order(eid, "Guard")); time.sleep(0.5)
        server.func(teleport(int(x) + 300, int(y) + 20, z))
        # Dormancy follows cameras (as the native unload does): the host's camera, not only its body, must have left. The
        # server's window is not the focused one; kept active, its camera follows its player (mp17 run 03: a camera left at
        # the arena kept the companion awake, correctly).
        server.func(KEEP_ACTIVE)
        # The camera is stepped from the poll itself: an unfocused server window may not run its camera update for a while
        # (r3i run: the camera stayed 17 m from the arena for 15 s after the teleport), and a camera left behind keeps the
        # companion awake by the product's own rule. Stepping it is the test's setup, not a product change.
        # The server's own camera is put on the first-person camera (test setup, as sp_camera_fire does) and stepped: an
        # unfocused server window's active camera may neither be the player's nor update (r3i/r3j runs: it stayed ~24 m
        # from the arena after the teleport, and the product then keeps the companion awake, correctly).
        HOST_CAM = MAIN + ('var w = pl.GameWidget; var fpp = w.FindCamera<Game.FppCamera>(); if (fpp != null && w.ActiveCamera != fpp) w.ActiveCamera = fpp; w.ActiveCamera.Update(0.05f); '
            'var v = w.ActiveCamera.ViewPosition; return System.FormattableString.Invariant($"{v.X:0.0} {v.Z:0.0}") + " " + w.ActiveCamera.GetType().Name;')
        cam = poll(server, HOST_CAM, lambda v: abs(float(v.split()[0]) - float(x)) > 250, 20)
        check("the host's camera followed it away (setup)", abs(float(cam.split()[0]) - float(x)) > 250, cam)
        time.sleep(3)
        check("a remote owner nearby keeps its companion awake", server.func(exists(eid)) == "True" and server.func(DORMANT) == "0", f"{server.func(exists(eid))} dormant {server.func(DORMANT)}")
        away = [far(c1, i1), far(c2, i2)]
        asleep = poll(server, DORMANT, lambda v: v == "1", 15)
        # What the server's dormancy pass itself sees: the positions it watches from, and each condition of CanSleep.
        SLEEP_INPUTS = PROJECT + (f'var s = project.FindSubsystem<Game.SubsystemTacticalCompanions>(true); '
            'var cams = (Engine.Vector2[])typeof(Game.SubsystemTacticalCompanions).GetMethod("Cameras", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(s, null); '
            f'var c = project.FindSubsystem<Game.SubsystemScTactical>(true).Companions.FirstOrDefault(q => q.Entity.Id == {eid}); '
            'string C = c == null ? "no companion" : System.FormattableString.Invariant($"added {c.Entity.IsAddedToProject} health {c.Creature.ComponentHealth.Health:0.00} deathHandled {c.DeathHandled} panel {c.PanelOpen} remotePanel {c.RemotePanelUntil - Engine.Time.RealTime:0.0} despawning {c.Creature.ComponentSpawn?.IsDespawning} at {c.Creature.ComponentBody.Position.X:0.0},{c.Creature.ComponentBody.Position.Z:0.0} canSleep {Game.SubsystemTacticalCompanions.CanSleep(c)}"); '
            'return "cameras " + string.Join(" ", cams.Select(v => System.FormattableString.Invariant($"{v.X:0.0},{v.Y:0.0}"))) + " | " + C + " | peers " + string.Join(",", Game.ScNet.Peers.Select(q => q.PlayerIndex)) '
            '+ " | players " + string.Join(",", project.FindSubsystem<Game.SubsystemPlayers>(true).ComponentPlayers.Select(q => q.PlayerData.PlayerIndex + "@" + q.ComponentBody.Position.X.ToString("0")));')
        slept = asleep == "1" and server.func(exists(eid)) == "False"
        check("with every player far away it sleeps", slept,
              f"dormant {asleep} exists {server.func(exists(eid))} away {away} players {[server.func(BODY_AT(i)) for i in (i1, i2)]} host {server.func(HOST_X)}"
              + ("" if slept else f" / server's dormancy inputs: {server.func(SLEEP_INPUTS)}"))
        check("and leaves every client", all(poll(g, exists(eid), lambda v: v == "False", 10) == "False" for g in [c1, c2]))
        for attempt in range(10):
            c1.func(teleport(x, y, z)); time.sleep(0.7)
            if near(c1.func(EYE), x, y, z): break
        c1.func(STAND)
        woke = poll(server, exists(eid), lambda v: v == "True", 20)
        check("client1 back: it wakes with the same id", woke == "True" and server.func(DORMANT) == "0", f"exists {woke} dormant {server.func(DORMANT)}")
        check("client1 has it again", poll(c1, exists(eid), lambda v: v == "True", 10) == "True")
        for attempt in range(10):
            c2.func(teleport(x, y, z)); time.sleep(0.7)
            if near(c2.func(EYE), x, y, z): break
        c2.func(move_to((float(x) + 3.5, float(y), float(z) + 0.5)))

        # 8. Dismiss (own, emptied) and an ownerless shell removed by client2.
        server.func(companion(eid) + 'for (int s = 0; s < c.Inventory.SlotsCount; s++) c.Inventory.RemoveSlotItems(s, c.Inventory.GetSlotCount(s)); return "ok";'); time.sleep(1)
        # The server refuses a dismiss from more than 6 m away (its own rule): client1 steps next to the companion first.
        cpos = server.func(companion(eid) + 'var p = c.Creature.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z}");')
        cx, cy, cz = [float(v) for v in cpos.split()]; c1.func(move_to((cx + 1.5, cy, cz))); time.sleep(1.5)
        step("client1 beside the companion (server)", f"companion {cpos} client1 {server.func(BODY_AT(i1))}")
        c1.func(order(eid, "Dismiss")); time.sleep(1.5)
        check("dismissed on the server", server.func(exists(eid)) == "False")
        check("dismissed on both clients", all(poll(g, exists(eid), lambda v: v == "False", 10) == "False" for g in [c1, c2]))
        near2 = [float(v) for v in c2.func(AHEAD(1.5)).split()]
        shell = step("ownerless shell beside client2 (server)", server.func(ownerless(near2)))
        time.sleep(1.5); n2 = c2.func(NOTICE)
        c2.func(order(shell, "RemoveOwnerless")); time.sleep(1.5)
        check("the ownerless shell removed on the server", server.func(exists(shell)) == "False", server.func(NET))
        check("client2 told", poll(c2, NOTICE, lambda v: v != n2, 10) != n2, c2.func(NOTICE))
        step("traces (server / client1 / client2)", [server.func(TRACE), c1.func(TRACE), c2.func(TRACE)])
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
        name = f"m3t-{tag}-{edition}"
        (RESULTS / f"{name}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client1", "client2", "client3"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"{name}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "full"))
