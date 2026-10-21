"""Can the CS packages be played on this standalone engine? (api-194-1951-compat-20261016, the user: "这个从1.9.3.1之后的都要
适配（这些版本都要可用cs枪械）")

One game on the engine M0_ENGINE selects (131 = the 1.9.3.1 baseline, 194, 195, 1951), a new survival world, the game's
own mouse and key input: every gun fires (its rounds drop) and reloads from carried ammunition (rounds rise, ammunition is
spent); a knife hurts a target in reach; an HE, a flashbang and a smoke grenade are thrown and go off (the smoke cloud is
there); a CT companion and a T enemy fight; the weapon workbench crafts a batch of magazines from the recipe's materials; a
fired gun's record and the crafted magazines survive a save and reload; no game errors. Pictures (the window's own,
PrintWindow) of the ammo HUD in first person, the CS model with a gun in third person, the workbench dialog and the CS
settings screen are written for review: from 1.9.5 on the engine draws text with the system's font.

Usage: M0_ENGINE=<engine> python sp_engine_compat.py <label> <package>[+<package>] [section,section...]   (Full, or Lite+Agents;
the sections guns, hud, knife, grenades, agents, workbench, persistence, settings run in that order, all by default)
Isolated engine copy, new world; the player's own Mods and worlds are never used.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, SURVIVAL, build_arena, teleport, STAND, health
from sp_camera_fire import give, look
from sp_gun_visuals import window_of, picture, ORBIT_SIDE
from sp_subworld import STATE as GUN_STATE, DIAG as TRIGGER
from sp_deathmatch import FPP

API = ('return string.Join("; ", ModsManager.ModList.Where(m => m.modInfo != null && m.modInfo.PackageName.StartsWith("zh667.ScCsgo"))'
       '.Select(m => m.modInfo.PackageName + " " + m.modInfo.Version + " api " + m.modInfo.ApiVersion + " fits " + m.modInfo.ApiVersionRange.Satisfies(ModsManager.APINuGetVersion))) '
       '+ " | engine " + ModsManager.APINuGetVersion;')
DURABLE = MAIN + 'pl.ComponentHealth.AttackResilience = 10000; pl.ComponentHealth.FallResilience = 10000; return "durable";'
EMPTY = MAIN + 'foreach (var e in project.Entities.ToArray()) if (e.FindComponent<Game.ComponentCreature>() != null && e.FindComponent<Game.ComponentPlayer>() == null) project.RemoveEntity(e, true); return "empty";'
def wall(x, y, z, block): return MAIN + (f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dx = -4; dx <= 4; dx++) for (int dy = 0; dy <= 5; dy++) '
    f't.ChangeCell({x} + dx, {y} + dy, {z}, {block}); return "wall {block}";')
SPEC = MAIN + ('var inv = pl.ComponentMiner.Inventory; var spec = Game.GunSpec.All[Game.GunSpec.GetVariant(Game.Terrain.ExtractData(inv.GetSlotValue(0)))]; '
    'return Game.ScReloadTransaction.AmmoKind(spec) + " " + spec.RechargeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);')
def put(slot, value, count): return MAIN + (f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems({slot}, inv.GetSlotCount({slot})); '
    f'if ({count} > 0) inv.AddSlotItems({slot}, {value}, {count}); return inv.GetSlotCount({slot}).ToString();')
def count(slot): return MAIN + f'return pl.ComponentMiner.Inventory.GetSlotCount({slot}).ToString();'
def owned(value): return MAIN + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {value}).ToString();'
SELECT0 = MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; return "slot 0";'
KNIFE = 'Game.BlocksManager.Blocks[Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true)].GetCreativeValues().First()'
def grenade(kind): return f'Game.ScGrenadeBlock.Value({kind})'
def active(kind, effect="null"): return PROJECT + f'return project.FindSubsystem<Game.SubsystemScGrenades>(true).ActiveCount({kind}, {effect}).ToString();'
def creature(x, y, z, template): return PROJECT + (
    f'var e = Game.DatabaseManager.CreateEntity(project, "{template}", true); var b = e.FindComponent<Game.ComponentBody>(true); b.Position = new Engine.Vector3({x}f, {y}f, {z}f); '
    'var loco = e.FindComponent<Game.ComponentLocomotion>(); if (loco != null) { loco.WalkSpeed = 0; loco.JumpSpeed = 0; } '
    'var h = e.FindComponent<Game.ComponentHealth>(); if (h != null) h.m_regenerateLifeEnabled = false; '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) { sp.SpawnDuration = 0f; sp.AutoDespawn = false; } project.AddEntity(e); return e.Id.ToString();')
def companion(x, y, z): return MAIN + (
    f'var e = Game.DatabaseManager.CreateEntity(project, "ScTacticalCT", true); e.FindComponent<Game.ComponentBody>(true).Position = new Engine.Vector3({x}f, {y}f, {z}f); '
    'e.FindComponent<Game.ComponentTacticalCompanion>(true).OwnerIndex = pl.PlayerData.PlayerIndex; '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; project.AddEntity(e); return e.Id.ToString();')
def enemy(x, y, z): return MAIN + (
    'var e = Game.DatabaseManager.CreateEntity(project, "ScTacticalEnemy", true); var en = e.FindComponent<Game.ComponentTacticalEnemy>(true); '
    'var st = new Game.TacticalEnemyState { Squad = "engine-compat", Role = Game.TacticalRole.Close, Variant = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"), Rounds = 30, Reserve = 90, Source = "manual" }; '
    f'en.Configure(st, new Engine.Vector3({x}f, {y}f, {z}f)); var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; '
    'project.AddEntity(e); en.Alert(pl.ComponentBody); return e.Id.ToString();')
def enemy_state(eid): return PROJECT + (f'var e = project.Entities.FirstOrDefault(q => q.Id == {eid}); var en = e?.FindComponent<Game.ComponentTacticalEnemy>(); '
    'return en == null ? "gone" : System.FormattableString.Invariant($"{en.Creature.ComponentHealth.Health} {en.State.Rounds} {en.State.Reserve}");')
MATERIALS = [("ironingot", 1), ("copperingot", 2), ("gunpowder", 3)]
def bench_craft(bx, by, bz, batches): return MAIN + (
    'var mag = Game.ScAmmoBlock.Value(Game.ScAmmoBlock.Magazine); '
    f'var op = new Game.ScWorkbenchOp(Game.ScWorkbenchOpKind.Craft, new Engine.Point3({bx}, {by}, {bz}), mag, {batches}); '
    'var r = Game.ScWorkbenchOps.Execute(pl, op); return r.Code + " " + r.Detail;')
def open_bench(bx, by, bz): return MAIN + (
    f'var w = project.FindSubsystem<Game.SubsystemScWeaponWorkbench>(true); var hit = new Game.TerrainRaycastResult {{ CellFace = new Game.CellFace({bx}, {by}, {bz}, 0), '
    f'Value = project.FindSubsystem<Game.SubsystemTerrain>(true).Terrain.GetCellValue({bx}, {by}, {bz}) }}; '
    'bool ok = w.OnInteract(hit, pl.ComponentMiner); return ok + " " + Game.DialogsManager.HasDialogs(pl.GuiWidget);')
# Every dialog, on the player's GUI and on the root widget (a closed dialog can lead to the next one): names, then closed.
CLOSE_DIALOGS = MAIN + ('string Text(object d, string f) => (d.GetType().GetField(f)?.GetValue(d) as Game.LabelWidget)?.Text; '
    'var names = string.Join(" | ", Game.DialogsManager.Dialogs.Select(d => d.GetType().Name + (Text(d, "m_largeLabelWidget") is { } t ? ": " + t + " / " + Text(d, "m_smallLabelWidget") : ""))); '
    'foreach (var d in Game.DialogsManager.Dialogs.ToArray()) Game.DialogsManager.HideDialog(d); return names;')
ANY_DIALOG = MAIN + 'return (Game.DialogsManager.HasDialogs(pl.GuiWidget) || Game.DialogsManager.HasDialogs(Game.ScreensManager.RootWidget)).ToString();'
# A new survival world asks each player for a starter kit (ScStarterDialog, shown again until answered): answer "none",
# as a player would, so no gift changes the inventory under test and no dialog takes the trigger later.
STARTER_NONE = MAIN + 'var s = project.FindSubsystem<Game.SubsystemScStarterEquipment>(true); bool ok = s.Choose(pl, Game.ScStarterPlan.None); return ok + " pending " + s.Pending(pl.PlayerData.PlayerIndex);'
SETTINGS = 'Game.ScCsgoKnivesModLoader.OpenSettings(); return Game.ScreensManager.CurrentScreen?.GetType().Name;'
BACK_TO_GAME = 'Game.ScreensManager.SwitchScreen("Game"); return Game.ScreensManager.CurrentScreen?.GetType().Name;'
SCREEN = 'return Game.ScreensManager.CurrentScreen?.GetType().Name;'
AUTO = {"ak47", "m4a4", "m4a1s", "galilar", "famas", "aug", "sg556", "mp9", "mac10", "mp7", "mp5sd", "ump45", "p90", "bizon", "m249", "negev", "g3sg1", "scar20", "cz75a", "xm1014"}


def main(label, packages, sections="all"):
    pkgs = [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"engine-compat-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case_dir / "pictures").mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "checks": [], "guns": {}, "pictures": []}
    T0 = time.time()
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": None if ok is None else bool(ok), "detail": detail})
        print(("NOT RUN " if ok is None else "PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:300], flush=True)
    def step(name, value=None): print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:300]}", flush=True); return value
    g = None; h = None
    def snap(name):
        if h is None: return
        try:
            im = picture(h); im.save(case_dir / "pictures" / f"{name}.jpg", quality=88); R["pictures"].append(name)
        except Exception as e: R["pictures"].append(f"{name}: {e}")
    def state(): return json.loads(g.func(GUN_STATE))
    def reload_world():
        mark = g.mark(); g.cmd('EXEC var wi = Game.GameManager.WorldInfo; Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("GameLoading", wi, null);')
        g.wait('Entered screen "Game"', 180, mark); poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 120); g.func(KEEP_ACTIVE); g.func(DURABLE)
    wanted = None if sections == "all" else set(sections.replace("+", ",").split(","))
    def section(name, fn):
        if wanted is not None and name.split()[0] not in wanted: return
        try: fn()
        except Exception as e: check(f"{name} (section completed)", False, f"{type(e).__name__}: {e}")
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); step("engine", R["engine"]["engine"]); to_menu(g)
        api = step("mods and API range", g.func(API)); R["api"] = api
        check("the CS packages are loaded and their ApiVersion ranges accept this engine", "zh667.ScCsgoKnives " in api and "zh667.ScCsgoTactical " in api and "fits False" not in api, api)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        step("survival world", poll(g, SURVIVAL, lambda v: v == "Survival", 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); g.func(KEEP_ACTIVE); g.func(DURABLE)
        starter = step("starter kit answered: none", g.func(STARTER_NONE))
        check("the starter-kit question is answered (no kit)", starter.endswith("pending False"), starter)
        x, y, z = [int(v) for v in step("arena", build_arena(g)).split()]
        g.func(EMPTY); g.func(teleport(x, y, z + 12)); g.func(STAND); time.sleep(1.5); g.func(FPP)
        h = window_of(g.proc.pid); step("window", bool(h))
        cx, cy = [int(v) // 2 for v in g.func(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]
        def click(hold):
            g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.05); g.cmd(f"MOUSEDOWN Left {cx} {cy}"); time.sleep(hold); g.cmd(f"MOUSEUP Left {cx} {cy}")
        names = json.loads(g.func('return System.Text.Json.JsonSerializer.Serialize(Game.GunSpec.All.Select(s => s.Name));'))

        def guns():
            g.func(wall(x, y, z + 3, "Game.Terrain.MakeBlockValue(Game.SandstoneBlock.Index)")); g.func(look(0.0, 0.0))
            fired_ok, reload_ok, skipped = [], [], []
            for name in names:
                entry = {}; g.func(give(name)); g.func(SELECT0); time.sleep(1.2)
                kind, recharge = g.func(SPEC).split(); entry["ammoKind"] = int(kind)
                # a press during the draw is not a shot (the CZ75's draw is long): press again after a pause, three presses at most
                before = state(); presses = 0; after = before
                while presses < 3 and after["rounds"] >= before["rounds"]:
                    if presses: time.sleep(1.5)
                    click(.6 if name == "revolver" else .3 if name in AUTO else .08); presses += 1; time.sleep(1.3); after = state()
                entry["rounds"] = [before["rounds"], after["rounds"]]; entry["presses"] = presses
                entry["fired"] = after["usable"] and after["rounds"] < before["rounds"]
                (fired_ok if entry["fired"] else skipped).append(name)
                if float(recharge) > 0: entry["reload"] = "recharges, no ammunition"
                elif entry["fired"]:
                    g.func(put(1, f"Game.ScAmmoBlock.Value({kind})", 20)); time.sleep(.3)
                    g.cmd("KEYDOWN R"); time.sleep(.12); g.cmd("KEYUP R")
                    t0 = time.time(); now = after
                    while time.time() - t0 < 9:
                        time.sleep(.5); now = state()
                        if now["rounds"] > after["rounds"] and int(g.func(count(1))) < 20: break
                    left = int(g.func(count(1))); entry["reloaded"] = [now["rounds"], 20 - left]
                    entry["reload"] = now["rounds"] > after["rounds"] and left < 20
                    if entry["reload"]: reload_ok.append(name)
                    g.func(put(1, 0, 0))
                R["guns"][name] = entry; step(name, entry)
            check(f"every gun fires with the left button ({len(fired_ok)}/{len(names)})", len(fired_ok) == len(names), [n for n in names if n not in fired_ok])
            reloadable = [n for n in fired_ok if R["guns"][n].get("reload") is not None and R["guns"][n]["reload"] != "recharges, no ammunition"]
            check(f"every fired gun reloads from carried ammunition with R ({len(reload_ok)}/{len(reloadable)})", reloadable and len(reload_ok) == len(reloadable), [n for n in reloadable if n not in reload_ok])
            g.func(wall(x, y, z + 3, 0))
        section("guns", guns)

        def hud_pictures():
            g.func(give("ak47")); g.func(SELECT0); g.func(put(1, "Game.ScAmmoBlock.Value(0)", 12)); g.func(FPP); g.func(look(0.0, 0.0)); time.sleep(1.5); snap("hud-first-person-ak47")
            step("orbit", g.func(ORBIT_SIDE)); time.sleep(1.2); snap("third-person-ak47"); g.func(FPP); time.sleep(.5)
        section("hud pictures", hud_pictures)

        def knife():
            g.func(EMPTY); g.func(put(0, KNIFE, 1)); g.func(SELECT0); time.sleep(1)
            tid = step("knife target", g.func(creature(x + .5, y, z + 10.9, "Cow_Brown")))
            g.func(look(0.0, -0.45)); time.sleep(.8); before = float(g.func(health(tid)))
            for _ in range(3):
                click(.08); time.sleep(.9)
                if float(g.func(health(tid))) < before: break
            after = float(g.func(health(tid))); check("a knife hurts a target in reach", after < before, [before, after])
            g.func(EMPTY)
        section("knife", knife)

        def grenades():
            g.func(look(0.0, 0.12))
            for kind, label_ in [(0, "HE"), (1, "flashbang"), (2, "smoke")]:
                g.func(put(0, grenade(kind), 1)); g.func(SELECT0); time.sleep(1.2)
                click(.9); t0 = time.time(); thrown = False
                while time.time() - t0 < 3:
                    time.sleep(.2)
                    if g.func(count(0)) == "0" and int(g.func(active(kind))) > 0: thrown = True; break
                check(f"{label_} is thrown with the left button", thrown, g.func(active(kind)))
                if kind == 2:
                    cloud = poll(g, active(2, "true"), lambda v: v != "0", 12)
                    check("the smoke grenade makes a cloud", cloud != "0", cloud); time.sleep(2.5); snap("smoke-cloud")
                else:
                    gone = poll(g, active(kind), lambda v: v == "0", 10)
                    check(f"{label_} goes off", gone == "0", gone)
        section("grenades", grenades)

        def agents():
            g.func(EMPTY); g.func(teleport(x, y, z + 12)); g.func(STAND); g.func(look(0.0, 0.0))
            cid = step("CT companion", g.func(companion(x + 2.5, y, z + 11.5)))
            eid = step("T enemy", g.func(enemy(x + .5, y, z - 2.5)))
            first = g.func(enemy_state(eid)); step("enemy at start", first); hp0, r0 = float(first.split()[0]), int(first.split()[1])
            t0 = time.time(); now = first
            while time.time() - t0 < 40:
                time.sleep(1); now = g.func(enemy_state(eid))
                if now == "gone" or (float(now.split()[0]) < hp0 and int(now.split()[1]) < r0): break
            time.sleep(1); snap("agents-fight")
            check("the T enemy fires its gun", now == "gone" or int(now.split()[1]) < r0 or int(now.split()[2]) < 90, [first, now])
            check("the CT companion hits the T enemy", now == "gone" or float(now.split()[0]) < hp0, [first, now])
            g.func(EMPTY)
        section("agents", agents)

        def workbench():
            bx, by, bz = x + 2, y, z + 12
            g.func(MAIN + f'project.FindSubsystem<Game.SubsystemTerrain>(true).ChangeCell({bx}, {by}, {bz}, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScWeaponWorkbenchBlock>(true))); return "bench";')
            mag = "Game.ScAmmoBlock.Value(Game.ScAmmoBlock.Magazine)"
            for slot in range(10): g.func(put(slot, 0, 0))
            g.func(put(5, mag, 1))   # the batch stacks here, clear of slot 0 (the next sections put their items there)
            for slot, (cid, n) in enumerate(MATERIALS, start=2): g.func(put(slot, f'Game.ScComponentCrafting.Resolve("{cid}")', n * 3))
            before = int(g.func(owned(mag)))
            result = step("craft 3 batches of magazines at the bench", g.func(bench_craft(bx, by, bz, 3)))
            after = int(g.func(owned(mag))); left = [int(g.func(count(s))) for s in (2, 3, 4)]
            check("the weapon workbench crafts magazines from the recipe's materials (3 batches: +6, materials used up)", after - before == 6 and left == [0, 0, 0] and g.func(count(5)) == "7", [result, before, after, left])
            R["magazines"] = after
            opened = step("open the workbench", g.func(open_bench(bx, by, bz)))
            check("the workbench dialog opens", opened.endswith("True"), opened); time.sleep(1.5); snap("workbench-dialog")
            closed = []
            for _ in range(6):
                closed.append(g.func(CLOSE_DIALOGS)); time.sleep(.8)
                if g.func(ANY_DIALOG) == "False": break
            R["dialogsClosed"] = closed; step("dialogs closed", closed)
            check("no dialog is left open after closing the workbench", g.func(ANY_DIALOG) == "False", closed)
        section("workbench", workbench)

        def persistence():
            # a dialog in front of the game takes the trigger (ScGunBindings.ContextAvailable): note and close any that come up
            seen, quiet = [], 0
            for _ in range(15):
                if g.func(ANY_DIALOG) == "True": seen.append(g.func(CLOSE_DIALOGS)); quiet = 0
                else: quiet += 1
                if quiet >= 3: break
                time.sleep(1)
            R["dialogsBeforeFiring"] = seen; step("dialogs before firing", seen)
            g.func(give("ak47")); g.func(SELECT0); time.sleep(2.5); g.func(look(0.0, 0.0)); full = state()["rounds"]
            for _ in range(3):
                click(.3); time.sleep(1.3)
                if state()["rounds"] < full: break
            mag = "Game.ScAmmoBlock.Value(Game.ScAmmoBlock.Magazine)"
            before = state(); mags = int(g.func(owned(mag))); step("before save", [before, mags])
            if before["rounds"] >= full: R["triggerUnanswered"] = json.loads(g.func(TRIGGER)) | {"gate": g.func(MAIN + 'return Game.ScWeaponActionGate.Blocks(pl).ToString();')}
            check("the gun saved is a fired one (rounds below a full magazine)", before["rounds"] < full, [full, before["rounds"], R.get("triggerUnanswered")])
            reload_world(); after = state(); mags_after = int(g.func(owned(mag)))
            check("a fired gun keeps its record and rounds through save and reload", after["usable"] and after["id"] == before["id"] and after["rounds"] == before["rounds"], [before, after])
            check("crafted magazines survive save and reload", mags > 0 and mags_after == mags, [mags, mags_after])
        section("persistence", persistence)

        def settings():
            screen = step("CS settings screen", g.func(SETTINGS)); time.sleep(1.5); snap("cs-settings")
            check("the CS settings screen opens", screen == "ScGunSettingsScreen", screen)
            m = g.mark(); g.func(BACK_TO_GAME); time.sleep(1.5); step("back", g.func(SCREEN))
        section("settings", settings)

        R["gameErrors"] = g.errors()[:40]
        check("no game errors", not R["gameErrors"], R["gameErrors"][:5])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
        if g is not None: R.setdefault("gameErrors", g.errors()[:40])
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if c["ok"] is False]
    R["seconds"] = round(time.time() - T0)
    out = RESULTS / f"engine-compat-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; engine {R.get('engine', {}).get('engine')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
