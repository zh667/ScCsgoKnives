"""current-direction-20260929 §6 M1: one gun (AK-47) over the 1.9.3.2_MP engine, server-authoritative, TEST-ONLY packages.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_m1.py <dev tag> [lite|full]
A host and two clients, each its own copy of the MP build with TestAutomation and the dev package (m0.py devpkg <tag>).
Real input only: the shooter's trigger is TestAutomation's held dig ray (the game's own PlayerInput.Dig), reload is the R key,
the drop is the game's drop action. Checked on every process: gun record (rounds / durability / revision), the target's
health, magazine counts, what observers were shown; late join, drop -> pick-up by another client, reconnect.
Results: .tmp/mp-m0-20260929/results/m1-<tag>-<edition>.json and the three logs.
"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, MPBIN, sha, to_menu, create_world, join, poll, cs_packages, run_label

HANDSHAKE = 'return Game.ScNet.Transport == null ? "none" : Game.ScNet.Transport.Handshake.ToString();'
BRIDGE = 'var t = Game.ScNet.Transport; return t == null ? "no transport: " + Game.ScNet.Decision : t.Role + " | peers " + t.Peers.Count + " | " + t.Handshake + " | " + t.HandshakeDetail;'

FORCE_ACTIVE = ('var windowType = typeof(Engine.Window); var stateType = windowType.GetNestedType("State", System.Reflection.BindingFlags.NonPublic); '
                'var field = windowType.GetField("m_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); '
                'field.SetValue(null, System.Enum.ToObject(stateType, 2)); return Engine.Window.IsActive;')
# The engine zeroes a player's input whenever its window is not the active one (ComponentInput.cs:100) and several test
# windows share one desktop, so a test client keeps itself active: once now, again after any deactivation, and every frame.
KEEP_ACTIVE = ('var windowType = typeof(Engine.Window); var stateType = windowType.GetNestedType("State", System.Reflection.BindingFlags.NonPublic); '
               'var field = windowType.GetField("m_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); '
               'var active = System.Enum.ToObject(stateType, 2); field.SetValue(null, active); '
               'Engine.Window.Deactivated += () => field.SetValue(null, active); Engine.Window.Frame += () => field.SetValue(null, active); '
               # A minimized window (0x0 framebuffer; a focus change on the 1.9.3.1 copy minimized it once) is restored every frame.
               'Engine.Window.Frame += () => { var sz = Engine.Window.Size; if (sz.X == 0 || sz.Y == 0) { var view = typeof(Engine.Window).GetField("m_view", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null); var wsp = view?.GetType().GetProperty("WindowState"); if (wsp != null) wsp.SetValue(view, System.Enum.Parse(wsp.PropertyType, "Normal")); } }; return Engine.Window.IsActive;')
PROJECT = 'var project = Game.GameManager.Project; var players = project.FindSubsystem<Game.SubsystemPlayers>(true); '
def player(i): return PROJECT + f'var pl = players.PlayersData.FirstOrDefault(d => d.PlayerIndex == {i})?.ComponentPlayer; if (pl == null) return "missing player {i}"; '
# The local main player on either engine: the MP snapshot's SubsystemPlayers.MainPlayer (a client's own player), which
# 1.9.3.1 does not have (single player: the first player; its PlayerIndex is not 0 there).
MAIN = PROJECT + ('var mainProp = players.GetType().GetProperty("MainPlayer"); var pl = mainProp != null ? (Game.ComponentPlayer)mainProp.GetValue(players) '
                  ': (players.PlayersData.Count > 0 ? players.PlayersData[0].ComponentPlayer : null); if (pl == null) return "no main player"; ')

SURVIVAL = 'if (ScreensManager.CurrentScreen is not NewWorldScreen s || s.m_worldSettings == null) return "not-new-world"; s.m_worldSettings.GameMode = GameMode.Survival; return s.m_worldSettings.GameMode.ToString();'

# "wait" until every chunk under the arena has generated contents: cells carved into a chunk still to be generated are
# overwritten by its generation (mp15 M2b: the far end of the arena was natural ground again and a target 7 m ahead fell
# 6 blocks into it, below the shooter's line of fire). The floor is read back before the arena is reported.
ARENA = PROJECT + ('var p = players.GlobalSpawnPosition + new Engine.Vector3(22f, 6f, 14f); int x = (int)System.MathF.Floor(p.X), y = (int)System.MathF.Floor(p.Y), z = (int)System.MathF.Floor(p.Z); '
    'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    'for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 14; dz++) { var ch = t.Terrain.GetChunkAtCell(x + dx, z + dz); if (ch == null || ch.State <= Game.TerrainChunkState.InvalidContents4) return "wait chunk " + (x + dx) + "," + (z + dz); } '
    # sand and gravel above the cleared box would fall into it once it is hollow (dmr5b SP 2026-10-05: a sand cliff over the
    # arena crushed the player in an agent-made world): they become sandstone, which stays where it is - before the box is
    # emptied, so no fall is ever started
    'for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 14; dz++) for (int dy = 7; dy <= 64; dy++) { int c = Game.Terrain.ExtractContents(t.Terrain.GetCellValue(x + dx, y + dy, z + dz)); '
    '  if (c == Game.SandBlock.Index || c == Game.GravelBlock.Index) t.ChangeCell(x + dx, y + dy, z + dz, Game.Terrain.MakeBlockValue(Game.SandstoneBlock.Index)); } '
    'for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 14; dz++) { for (int dy = 0; dy <= 6; dy++) t.ChangeCell(x + dx, y + dy, z + dz, 0); t.ChangeCell(x + dx, y - 1, z + dz, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)); } '
    'for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 14; dz++) { if (Game.Terrain.ExtractContents(t.Terrain.GetCellValue(x + dx, y - 1, z + dz)) != Game.GraniteBlock.Index) return "wait floor " + (x + dx) + "," + (z + dz); for (int dy = 0; dy <= 6; dy++) if (Game.Terrain.ExtractContents(t.Terrain.GetCellValue(x + dx, y + dy, z + dz)) != 0) return "wait air " + (x + dx) + "," + (y + dy) + "," + (z + dz); } '
    'return x + " " + y + " " + z;')


def arena_diff(x, y, z): return PROJECT + (f'int x = {x}, y = {y}, z = {z}; var t = project.FindSubsystem<Game.SubsystemTerrain>(true).Terrain; var bad = new System.Collections.Generic.List<string>(); '
    'for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 14; dz++) for (int dy = -1; dy <= 6; dy++) { '
    'int want = dy < 0 ? Game.GraniteBlock.Index : 0, have = Game.Terrain.ExtractContents(t.GetCellValue(x + dx, y + dy, z + dz)); '
    'if (have != want) bad.Add((x + dx) + "," + (y + dy) + "," + (z + dz) + ":" + have); } '
    'return bad.Count + (bad.Count > 0 ? " " + string.Join(" ", bad.Take(400)) : "");')
def arena_set(cells, final): return PROJECT + ('var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    # final: the arena's value (granite floor, air above); otherwise the opposite, so the final write is a real change.
    + " ".join(f't.ChangeCell({cx}, {cy}, {cz}, {"Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)" if (cy == fy) == final else "0"});' for cx, cy, cz, fy in cells)
    + f' return "{len(cells)}";')


def arena_on_clients(server, clients, x, y, z, seconds=25):
    """Every client's copy of the arena, cell by cell, against the server's build, observed only: a client that joined
    after the server carved it must receive the difference through the platform's chunk sync and the product's own
    reconciliation (round 3 item 4), within <seconds>. Nothing is resent by the test.
    Returns [(client, cells differing at first, cells differing at the end, seconds until agreement or None)]."""
    report = []
    # The reference is the server's own copy (blocks placed/changed on the server after the carve are part of it), so a
    # client agrees when its cells differ from the carved shape exactly where the server's do.
    reference = server.func(arena_diff(x, y, z))
    for g in clients:
        first = g.func(arena_diff(x, y, z)); now = first; t0 = time.time(); agreed = None
        while time.time() - t0 < seconds:
            if now == reference: agreed = round(time.time() - t0, 1); break
            time.sleep(1); now = g.func(arena_diff(x, y, z))
        report.append((g.name, first[:300], now[:300], agreed, "server " + reference[:300]))
    return report


def build_arena(g, seconds=60):
    """The cleared arena, once it is really there (ARENA answers "wait ..." until its chunks are generated)."""
    t0 = time.time(); r = g.func(ARENA)
    while r.startswith("wait") and time.time() - t0 < seconds:
        time.sleep(1); r = g.func(ARENA)
    if r.startswith("wait"): raise RuntimeError("arena never ready: " + r)
    return r

def give_gun(i): return player(i) + (
    'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"); '
    'int id = Game.ScGunRegistry.Current.Allocate(v, 30, false, Game.ScGunDurability.Full(v)); '
    'int gun = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)); '
    'int ammo = Game.ScAmmoBlock.Value(Game.ScReloadTransaction.AmmoKind(Game.GunSpec.All[v])); '
    'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < 6; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
    'inv.AddSlotItems(0, gun, 1); inv.AddSlotItems(1, ammo, 3); return id + " " + gun + " " + ammo;')

def record(id_): return f'return Game.ScGunRegistry.Current.TryGetSnapshot({id_}, out var s) ? s.Rounds + "/" + s.Durability + "/" + s.Revision : "none";'
def inventory(prefix): return prefix + 'var inv = pl.ComponentMiner.Inventory; return inv.ActiveSlotIndex + ":" + string.Join(",", Enumerable.Range(0, 6).Select(s => inv.GetSlotValue(s) + "x" + inv.GetSlotCount(s)));'
def health(tid): return PROJECT + f'var e = project.Entities.FirstOrDefault(x => x.Id == {tid}); var h = e?.FindComponent<Game.ComponentHealth>(); return h == null ? "gone" : h.Health.ToString("R");'

def spawn_target(x, y, z): return PROJECT + (
    'GameEntitySystem.Entity e = null; foreach (var n in new[] { "Cow_Brown", "Cow_Black", "Cow_White", "Bull_Brown", "Duck" }) { try { e = Game.DatabaseManager.CreateEntity(project, n, true); break; } catch { } } '
    'if (e == null) return "no template"; '
    f'var b = e.FindComponent<Game.ComponentBody>(true); b.Position = new Engine.Vector3({x}f + 0.5f, {y}f, {z}f + 9.5f); b.Velocity = Engine.Vector3.Zero; '
    'var loco = e.FindComponent<Game.ComponentLocomotion>(); if (loco != null) { loco.WalkSpeed = 0; loco.FlySpeed = 0; loco.SwimSpeed = 0; loco.JumpSpeed = 0; } '
    'var h = e.FindComponent<Game.ComponentHealth>(); if (h != null) { h.m_regenerateLifeEnabled = false; h.IsInvulnerable = false; } '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) { sp.SpawnDuration = 0f; sp.AutoDespawn = false; sp.DespawnTime = null; } '
    'project.AddEntity(e); return e.Id + " " + (e.ValuesDictionary?.DatabaseObject?.Name ?? "?");')

STAND = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'
def teleport(x, y, z): return MAIN + (f'var body = pl.ComponentBody; body.Position = new Engine.Vector3({x}f + 0.5f, {y}f, {z}f + 0.5f); body.Velocity = Engine.Vector3.Zero; '
    'body.Rotation = Engine.Quaternion.Identity; body.IsGravityEnabled = false; body.IsSmoothRiseEnabled = false; pl.ComponentLocomotion.IsCreativeFlyEnabled = true; '
    'pl.ComponentLocomotion.LookAngles = Engine.Vector2.Zero; pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; pl.Update(0f); pl.ComponentCreatureModel.Update(0f); '
    'var eye = pl.ComponentCreatureModel.EyePosition; return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z}");')
# Shooting aims point the eye's line at the target: the bullet leaves the eye along the aim in first person
# (first-person-eye-shot-20261001; the round-4 musket origin beside the eye is history).
def aim_at(tid): return MAIN + (f'var t = project.Entities.FirstOrDefault(e => e.Id == {tid})?.FindComponent<Game.ComponentBody>(); if (t == null) return "no target"; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var bb = t.BoundingBox; var c = (bb.Min + bb.Max) * 0.5f; var d = Engine.Vector3.Normalize(c - eye); '
    'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')
COUNTERS = 'return Game.ScNetGuns.ShotsBroadcast + " broadcast, " + Game.ScNetGuns.RemoteShotsShown + " shown";'
DISCONNECT = 'NetworkManager.Stop(); GameManager.SaveProject(true, true); GameManager.DisposeProject(); ScreensManager.SwitchScreen("MainMenu");'
def pickable_of(value): return PROJECT + f'var pk = project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.FirstOrDefault(q => q.Value == {value}); return pk == null ? "none" : System.FormattableString.Invariant($"{{pk.Position.X}} {{pk.Position.Y}} {{pk.Position.Z}}");'
def move_to(pos): return MAIN + (f'var body = pl.ComponentBody; body.Position = new Engine.Vector3({pos[0]}f, {pos[1]}f, {pos[2]}f); body.Velocity = Engine.Vector3.Zero; '
    'body.IsGravityEnabled = false; pl.ComponentLocomotion.IsCreativeFlyEnabled = true; pl.Update(0f); pl.ComponentCreatureModel.Update(0f); return "ok";')
EYE = MAIN + 'var eye = pl.ComponentCreatureModel.EyePosition; return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z}");'
def near(value, x, y, z): 
    try: ex, ey, ez = map(float, value.split()); return abs(ex - float(x) - .5) < 1.5 and abs(ez - float(z) - .5) < 1.5 and 0 < ey - float(y) < 3
    except Exception: return False


def main(tag, edition):
    server_pkgs, client_pkgs = cs_packages(tag, edition); edition = run_label(edition)
    ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"m1-{tag}-{edition}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"m1-{tag}-{edition}", "package": {p.name: sha(p) for p in {*server_pkgs, *client_pkgs}}, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    server = c1 = c2 = None
    try:
        server = Game("server", case_dir, [ta, *server_pkgs]); to_menu(server)
        step("server probe", server.func('return Game.ScNet.Decision;'))
        # Survival world (rounds, wear and magazines count).
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        step("survival mode", poll(server, SURVIVAL, lambda v: v == "Survival", 20))
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        arena = step("arena", build_arena(server)).split(); x, y, z = arena

        c1 = Game("client1", case_dir, [ta, *client_pkgs]); to_menu(c1); join(c1, server)
        step("client1 handshake", poll(c1, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 30))
        check("client1 accepted", c1.func(HANDSHAKE) == "Accepted", c1.func(BRIDGE))
        c1_index = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        gid, gun, ammo = step("gun given to client1 by the server", server.func(give_gun(c1_index))).split()
        time.sleep(2)  # the joining player finishes spawning before it is moved
        for attempt in range(10):
            eye = c1.func(teleport(x, y, z)); time.sleep(0.7)
            if near(c1.func(EYE), x, y, z): break
        step("client1 eye in the arena", c1.func(EYE))
        check("client1 moved into the arena", near(c1.func(EYE), x, y, z), c1.func(EYE))
        c1.func(STAND)  # a shooter standing on the floor, not flying (flying is airborne inaccuracy, as in single player)
        tid = step("target", server.func(spawn_target(x, y, z))).split()[0]
        time.sleep(2)
        BODY = lambda eid: PROJECT + (f'var b = project.Entities.FirstOrDefault(e => e.Id == {eid})?.FindComponent<Game.ComponentBody>(); '
            'return b == null ? "gone" : System.FormattableString.Invariant($"{b.Position.X:0.00} {b.Position.Y:0.00} {b.Position.Z:0.00}");')
        step("target position: server / client1", [server.func(BODY(tid)), c1.func(BODY(tid))])
        check("client1 mirror has the record", poll(c1, record(gid), lambda v: v.startswith("30/"), 10).startswith("30/"), c1.func(record(gid)))
        check("client1 inventory synced", poll(c1, inventory(MAIN), lambda v: f"{gun}x1" in v and f"{ammo}x3" in v, 10), c1.func(inventory(MAIN)))
        hp0 = step("target health before (server)", server.func(health(tid)))

        def burst(shooter, frames):
            shooter.func(FORCE_ACTIVE)
            aim = shooter.func(aim_at(tid))
            step(f"{shooter.name} aim", aim)
            shooter.cmd(f"DIG_RAY {aim} {frames}")
            time.sleep(frames / 30 + 1.5)

        # 1. Client 1 fires alone.
        burst(c1, 24)
        s1 = step("server record after burst 1", server.func(record(gid)))
        rounds1 = int(s1.split("/")[0])
        check("server fired and counted rounds", rounds1 < 30, s1)
        check("server durability follows rounds (survival)", s1.split("/")[1] == str(int(server.func(f'return Game.ScGunDurability.Full(System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"));')) - (30 - rounds1)), s1)
        check("client1 mirror equals server", poll(c1, record(gid), lambda v: v == s1, 10) == s1, c1.func(record(gid)))
        hp1 = step("target health after burst 1 (server)", server.func(health(tid)))
        check("target hurt by the server", hp1 == "gone" or float(hp1) < float(hp0), f"{hp0} -> {hp1}")
        check("client1 sees the same target health", poll(c1, health(tid), lambda v: v == hp1, 10) == hp1, c1.func(health(tid)))

        # 2. Client 2 joins late: its mirror is the server's without any shot of its own.
        c2 = Game("client2", case_dir, [ta, *client_pkgs]); to_menu(c2); join(c2, server)
        step("client2 handshake", poll(c2, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 30))
        check("late joiner's mirror equals server", poll(c2, record(gid), lambda v: v == s1, 10) == s1, c2.func(record(gid)))
        check("late joiner sees the target health", poll(c2, health(tid), lambda v: v == server.func(health(tid)), 10) == server.func(health(tid)), c2.func(health(tid)))
        time.sleep(2)
        for attempt in range(10):
            c2.func(teleport(x, y, z)); time.sleep(0.7)
            if near(c2.func(EYE), x, y, z): break
        c2.func(move_to((float(x) + 3.5, float(y), float(z) + 12.5)))  # client 2 watches from the side

        # 3. Client 1 fires again; client 2 observes.
        shown0 = int(c2.func('return Game.ScNetGuns.RemoteShotsShown;'))
        tid = step("fresh target for burst 2", server.func(spawn_target(x, y, z))).split()[0]
        time.sleep(1.5)
        burst(c1, 12)
        s2 = step("server record after burst 2", server.func(record(gid)))
        check("client1 mirror equals server (2)", poll(c1, record(gid), lambda v: v == s2, 10) == s2, c1.func(record(gid)))
        check("observer mirror equals server", poll(c2, record(gid), lambda v: v == s2, 10) == s2, c2.func(record(gid)))
        shown = int(c2.func('return Game.ScNetGuns.RemoteShotsShown;')) - shown0
        fired2 = int(s1.split("/")[0]) - int(s2.split("/")[0])
        check("observer was shown every shot of burst 2", shown == fired2 and fired2 > 0, f"shown {shown}, fired {fired2}")
        step("counters server / client2", [server.func(COUNTERS), c2.func(COUNTERS)])

        # 4. Reload with the R key: the server takes one magazine and fills the record.
        mags_before = int(server.func(player(c1_index) + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {ammo});'))
        c1.func(FORCE_ACTIVE); c1.cmd("KEYDOWN R"); time.sleep(0.2); c1.cmd("KEYUP R"); time.sleep(4)
        s3 = step("server record after reload", server.func(record(gid)))
        check("reload filled the magazine on the server", s3.startswith("30/"), s3)
        inv_server = step("server inventory of client1", server.func(inventory(player(c1_index))))
        mags_after = int(server.func(player(c1_index) + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {ammo});'))
        check("one magazine taken on the server", mags_after == mags_before - 1, f"{mags_before} -> {mags_after}")
        check("client1 inventory equals server", poll(c1, inventory(MAIN), lambda v: v.split(":")[1] == inv_server.split(":")[1], 10).split(":")[1] == inv_server.split(":")[1], c1.func(inventory(MAIN)))
        check("client1 mirror after reload", poll(c1, record(gid), lambda v: v == s3, 10) == s3, c1.func(record(gid)))

        # 5. Client 1 drops the gun, client 2 picks it up: the record follows the item.
        # TestAutomation's DROP_ONCE does not produce a drop in this setup even for a plain block (mp_drop.py, job
        # 94a9d9f2...), so the server runs the drop the engine's PlayerDropPacket handler runs: ComponentPlayer.DoDrop().
        step("server drops client1's active item (PlayerDropPacket's own action)", server.func(player(c1_index) + 'pl.DoDrop(); return "dropped";')); time.sleep(0.4)
        c1.func(move_to((float(x) - 3.0, float(y), float(z) - 3.5))); time.sleep(2)
        c1_after = step("client1 inventory after the drop (server)", server.func(inventory(player(c1_index))))
        where = step("dropped gun on the server", server.func(pickable_of(gun)))
        check("the gun left client1 as a pickable", f"{gun}x1" not in c1_after and where != "none", f"{c1_after} / {where}")
        c2_index = int(c2.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        if where != "none":
            # The engine's own pick-up (attraction to a nearby player) is not reproduced by moving the test player there, so
            # the server does what that pick-up does: the pickable's item into client2's inventory, the pickable gone.
            step("server: client2 picks the gun up", server.func(player(c2_index) + f'var pks = project.FindSubsystem<Game.SubsystemPickables>(true); var pk = pks.Pickables.FirstOrDefault(q => q.Value == {gun}); '
                'if (pk == null) return "none"; var inv = pl.ComponentMiner.Inventory; int slot = Enumerable.Range(0, inv.SlotsCount).First(q => inv.GetSlotCount(q) == 0); '
                'inv.AddSlotItems(slot, pk.Value, pk.Count); pk.ToRemove = true; return "slot " + slot;'))
            poll(server, inventory(player(c2_index)), lambda v: f"{gun}x1" in v, 10)
        inv2 = step("server inventory of client2", server.func(inventory(player(c2_index))))
        check("client2 holds the same gun item (server)", f"{gun}x1" in inv2, inv2)
        check("client2 inventory synced", poll(c2, inventory(MAIN), lambda v: f"{gun}x1" in v, 10).split(":")[1] == inv2.split(":")[1], c2.func(inventory(MAIN)))
        check("client2 mirror for the transferred gun", c2.func(record(gid)) == server.func(record(gid)), f"{c2.func(record(gid))} vs {server.func(record(gid))}")

        # 6. Client 1 reconnects: handshake again, full state, same world.
        m = server.mark(); c1.cmd("EXEC " + DISCONNECT); server.wait("left (disconnected)", 30, m)
        c1.wait('Entered screen "MainMenu"', 60)
        # A returning player is restored by the server (its saved entity), so the client goes straight into the game.
        m = c1.mark()
        c1.cmd(f'EXEC ScreensManager.SwitchScreen("GameLoading", null, null, System.Net.IPEndPoint.Parse("127.0.0.1:{server.game_port}"), null);')
        c1.wait(f"Connected: 127.0.0.1:{server.game_port}", 300, m)
        seen = c1.wait(['Entered screen "Player"', "Player into playing."], 600, m)
        if seen == 'Entered screen "Player"':
            m = c1.mark(); c1.cmd("CLICK_WIDGET PlayButton"); c1.wait("Player into playing.", 600, m)
        step("client1 handshake after reconnect", poll(c1, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 30))
        check("reconnected client accepted", c1.func(HANDSHAKE) == "Accepted")
        check("reconnected mirror equals server", poll(c1, record(gid), lambda v: v == server.func(record(gid)), 10) == server.func(record(gid)), c1.func(record(gid)))
        check("reconnected inventory equals server", c1.func(inventory(MAIN)).split(":")[1] == server.func(inventory(player(int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))))).split(":")[1],
              f"{c1.func(inventory(MAIN))}")
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
        name = f"m1-{tag}-{edition}"
        (RESULTS / f"{name}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client1", "client2"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"{name}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
