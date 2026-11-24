"""feedback-1124-170 step 0 (Q7): a world played with the new 1.6.0 candidate, opened and saved with an earlier release (1.4.0-1.5.x, or the internal 1.6.0 of 2026-11-20),
then opened with 1.6.0 again, on the isolated standalone engine copy (M0_ENGINE=131 by default). Observes what the older
reader does with the new survivor cases and keys and with the first-craft ledger (ScFirstCraft group and its world-folder
copy), and what the candidate finds when it comes back.

Three game starts on ONE world (the runtime lock is taken once, so the world folder stays between them):
  1  candidate: new survival world; cases x3 and keys x2 in the inventory, one case dropped on the ground, a first-craft mark
     ("probe" / ak47) in the ledger; save; exit.
  2  the older release: load that world; record the inventory; save; exit.
  3  candidate again: load; record the inventory, the ground case and the ledger.
Usage: sp_switch1124.py <label> <candidate core package> <older core package> [<extra package> ...]
Extra packages (the agents package for a Lite core) load in sessions 1 and 3; give an older agents package by setting
SW_OLD_EXTRA to its path. Package paths should be ASCII. The player's own Mods folder and worlds are never used. Sound and
music are turned off in every session (user requirement for isolated copies).
"""
import json, os, sys, time, xml.etree.ElementTree as ET
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE

m0.firewall_preflight = lambda: {"skipped": "standalone sp131 run; no MP role started", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)

MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
WORLD = MAIN + 'var dir = project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName; return dir + "|" + Engine.Storage.GetSystemPath(dir);'
SAVE = 'Game.GameManager.SaveProject(true, true); return "saved";'
SLOTS = MAIN + ('var inv = pl.ComponentMiner.Inventory; return string.Join(";", System.Linq.Enumerable.Range(0, inv.SlotsCount).Where(i => inv.GetSlotCount(i) > 0)'
                '.Select(i => i + "=" + inv.GetSlotValue(i) + "x" + inv.GetSlotCount(i) + ":" + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(inv.GetSlotValue(i))].GetType().Name));')
GROUND = MAIN + ('var pk = project.FindSubsystem<Game.SubsystemPickables>(true); return string.Join(";", pk.Pickables.Where(p => !p.ToRemove)'
                 '.Select(p => p.Value + "x" + p.Count + ":" + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(p.Value)].GetType().Name));')
# Slots: 3 cases x3 (another item is dragged onto it in the older version), 4 keys x2 (dropped there), 5 cases x2 (moved
# into a chest there), 6 keys x1 (left alone). In the world: a case on the ground 6 blocks away, a vanilla chest holding
# cases x2, an airdrop crate holding a case and a key (Full only), a weapon workbench. The first-craft ledger gets a mark.
SETUP = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; for (int i = 0; i < inv.SlotsCount; i++) inv.RemoveSlotItems(i, inv.GetSlotCount(i)); '
    'int cb = Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true); int kb = Game.BlocksManager.GetBlockIndex<Game.ScCaseKeyBlock>(true); int cv = Game.Terrain.MakeBlockValue(cb, 0, 0), kv = Game.Terrain.MakeBlockValue(kb); '
    'inv.AddSlotItems(3, cv, 3); inv.AddSlotItems(4, kv, 2); inv.AddSlotItems(5, cv, 2); inv.AddSlotItems(6, kv, 1); inv.ActiveSlotIndex = 0; '
    'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var p = pl.ComponentBody.Position; int px = (int)System.MathF.Floor(p.X), pz = (int)System.MathF.Floor(p.Z); '
    'int Top(int x, int z) => t.Terrain.GetTopHeight(x, z) + 1; '
    'project.FindSubsystem<Game.SubsystemPickables>(true).AddPickable(cv, 1, new Engine.Vector3(px + 6.5f, Top(px + 6, pz) + 0.3f, pz + 0.5f), Engine.Vector3.Zero, null, null); '
    'int c1x = px + 2, c1z = pz + 2, c1y = Top(c1x, c1z); t.ChangeCell(c1x, c1y, c1z, Game.Terrain.MakeBlockValue(Game.ChestBlock.Index)); '
    'var be = project.FindSubsystem<Game.SubsystemBlockEntities>(true); var chest = be.GetBlockEntity(c1x, c1y, c1z)?.Entity.FindComponent<Game.ComponentChest>(); if (chest != null) chest.AddSlotItems(0, cv, 2); '
    'int c2x = px - 2, c2z = pz + 2, c2y = Top(c2x, c2z); string crate = "none"; int ab = Game.BlocksManager.GetBlockIndex("ScAirdropBlock", false); '
    'if (ab > 0) { t.ChangeCell(c2x, c2y, c2z, Game.Terrain.MakeBlockValue(ab)); var cc = be.GetBlockEntity(c2x, c2y, c2z)?.Entity.FindComponent<Game.ComponentChest>(); if (cc != null) { cc.AddSlotItems(0, cv, 1); cc.AddSlotItems(1, kv, 1); crate = c2x + "," + c2y + "," + c2z; } } '
    'int wx = px, wz = pz - 2, wy = Top(wx, wz); t.ChangeCell(wx, wy, wz, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScWeaponWorkbenchBlock>(true))); '
    'bool marked = project.FindSubsystem<Game.SubsystemScFirstCraft>(true).Ledger.TryMark("probe", "ak47"); '
    'return cb + "|" + kb + "|" + marked + "|" + c1x + "," + c1y + "," + c1z + "|" + crate + "|" + wx + "," + wy + "," + wz + "|" + (chest != null);')
def containers(chest, crate): return PROJECT + (
    'var be = project.FindSubsystem<Game.SubsystemBlockEntities>(true); string Show(string at) { if (at == "none") return "none"; var a = at.Split(\',\').Select(int.Parse).ToArray(); '
    'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var c = be.GetBlockEntity(a[0], a[1], a[2])?.Entity.FindComponent<Game.ComponentChest>(); '
    'string cell = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(t.Terrain.GetCellValue(a[0], a[1], a[2]))].GetType().Name; '
    'if (c == null) return cell + " (no inventory)"; return cell + ": " + string.Join(";", Enumerable.Range(0, c.SlotsCount).Where(i => c.GetSlotCount(i) > 0).Select(i => i + "=" + c.GetSlotValue(i) + "x" + c.GetSlotCount(i) + ":" + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(c.GetSlotValue(i))].GetType().Name)); } '
    f'return "chest " + Show("{chest}") + " | crate " + Show("{crate}");')
# The engine's own inventory drag and drop (InventorySlotWidget.HandleDragDrop), whatever its parameter list in this engine.
def drag(src_chest, src_slot, dst_chest, dst_slot, chest_at): return MAIN + (
    'var be = project.FindSubsystem<Game.SubsystemBlockEntities>(true); var a = "' + chest_at + '".Split(\',\').Select(int.Parse).ToArray(); '
    'var chest = be.GetBlockEntity(a[0], a[1], a[2])?.Entity.FindComponent<Game.ComponentChest>(); var inv = pl.ComponentMiner.Inventory; '
    f'Game.IInventory src = {"chest" if src_chest else "inv"}, dst = {"chest" if dst_chest else "inv"}; if (src == null || dst == null) return "no inventory"; '
    'var m = typeof(Game.InventorySlotWidget).GetMethods().FirstOrDefault(x => x.Name == "HandleDragDrop" && x.IsStatic); '
    f'if (m != null) {{ var ps = m.GetParameters(); var args = new object[ps.Length]; args[0] = src; args[1] = {src_slot}; args[2] = Game.DragMode.AllItems; args[3] = dst; args[4] = {dst_slot}; '
    'if (ps.Length > 5) args[5] = false; if (ps.Length > 6) args[6] = pl; return "static result " + m.Invoke(null, args); } '
    # this engine has only the widget's own handler: a slot widget for the target slot, the player's GUI as its owner
    f'var w = new Game.InventorySlotWidget(); w.AssignInventorySlot(dst, {dst_slot}); '
    'var fp = typeof(Game.InventorySlotWidget).GetField("m_componentPlayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public); if (fp != null) fp.SetValue(w, pl); '
    f'w.HandleDragDrop(src, {src_slot}, Game.DragMode.AllItems, dst, {dst_slot}); return "widget handler";')
GIVE_DIRT = MAIN + 'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(12, inv.GetSlotCount(12)); inv.AddSlotItems(12, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index), 5); return inv.GetSlotCount(12).ToString();'
DROP_KEYS = MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 4; return "active 4";'
def craft_glock(at): return MAIN + (
    'var inv = pl.ComponentMiner.Inventory; for (int s = 13; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
    'var e = Game.ScWeaponCrafting.All.First(q => !q.Knife && q.Name == "glock18"); int slot = 13; foreach (var m in e.Materials()) inv.AddSlotItems(slot++, m.Key, m.Value); pl.PlayerData.Level = 5f; '
    'var a = "' + at + '".Split(\',\').Select(int.Parse).ToArray(); pl.ComponentBody.Position = new Engine.Vector3(a[0] + 0.5f, a[1], a[2] + 2.5f); '
    'var op = new Game.ScWorkbenchOp(Game.ScWorkbenchOpKind.Craft, new Engine.Point3(a[0], a[1], a[2]), e.Value, 1); var r = Game.ScWorkbenchOps.Execute(pl, op); '
    'int cb = Game.BlocksManager.GetBlockIndex("ScCaseBlock", false); int cases = cb > 0 ? Game.ScInventoryTransaction.Count(inv, Game.Terrain.MakeBlockValue(cb, 0, 0)) : -1; '
    'return r.Code + " " + r.Detail + " | cases " + cases;')
LEDGER = MAIN + ('var s = project.FindSubsystem<Game.SubsystemScFirstCraft>(false); return s == null ? "no subsystem" : s.Ledger.Has("probe", "ak47") + "|" + string.Join(",", s.Ledger.Owners);')
def load(directory): return m0.Call(
    'Game.WorldsManager.UpdateWorldsList(); var w = Game.WorldsManager.WorldInfos.FirstOrDefault(x => x.DirectoryName == $s0); '
    'if (w == null) return "no world " + $s0; Game.ScreensManager.SwitchScreen("GameLoading", w, null); return "loading " + w.DirectoryName;', directory)


def disk(system_dir):
    world = Path(system_dir); root = ET.parse(world / "Project.xml").getroot(); subs = root.find("Subsystems")
    def group(parent, name): return next((e for e in parent.findall("Values") if e.get("Name") == name), None) if parent is not None else None
    blocks = {e.get("Name"): e.get("Value") for e in (group(subs, "BlocksManager").findall("Value") if group(subs, "BlocksManager") is not None else [])}
    capsule = next((e.get("Value") for e in (group(subs, "ScCompatibility").findall("Value") if group(subs, "ScCompatibility") is not None else []) if e.get("Name") == "Capsule"), "") or ""
    return {"firstCraftGroup": group(subs, "ScFirstCraft") is not None, "firstCraftCopy": (world / "ScCsgoFirstCraft.xml").exists(),
            "caseBlockName": {k: v for k, v in blocks.items() if v in ("ScCaseBlock", "ScCaseKeyBlock")},
            "capsuleOwnsFirstCraft": 'Name="ScFirstCraft"' in capsule, "capsuleOpaqueFirstCraft": "ScFirstCraft" in capsule.split("<Opaque", 1)[-1] if "<Opaque" in capsule else False}


def main(label, new_package, old_package, *extra):
    new = Path(new_package); old = Path(old_package); extras = [Path(p) for p in extra]
    old_extra = [Path(os.environ["SW_OLD_EXTRA"])] if os.environ.get("SW_OLD_EXTRA") else []
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"switch1124-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"switch1124-{label}", "engine": m0.SP_VERSION, "packages": {p.name: sha(p) for p in [new, old] + extras + old_extra},
         "steps": [], "observations": {}, "sessions": []}
    T0 = time.time(); g = None
    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:700]}", flush=True); return value
    def start(name, mods):
        game = m0.game(name, case_dir, mods); R["sessions"].append({"name": name, **game.engine_info()}); to_menu(game); game.func(MUTE); return game
    def enter(game, directory):
        m = game.mark(); step("load", game.func(load(directory)))
        return game.wait(['Entered screen "Game"', "Loading World Failed", "gun load refused", "gun format load refused"], 900, m), m
    def spawned(game):
        step("player", poll(game, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); game.func(KEEP_ACTIVE); game.func(MUTE)
    def close(game):
        lines = list(game.lines); game.close(); return lines
    try:
        g = start("s1-new", [new] + extras)
        m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(MUTE)
        directory, system_dir = step("world", g.func(WORLD)).split("|", 1)
        R["observations"]["s1 setup"] = step("setup", g.func(SETUP))
        parts = R["observations"]["s1 setup"].split("|"); chest_at, crate_at, bench_at = parts[3], parts[4], parts[5]
        R["observations"]["s1 containers"] = step("s1 containers", g.func(containers(chest_at, crate_at)))
        R["observations"]["s1 slots"] = step("s1 slots", g.func(SLOTS)); R["observations"]["s1 ground"] = step("s1 ground", g.func(GROUND))
        step("save", g.func(SAVE)); time.sleep(1.5); close(g); g = None
        R["observations"]["s1 disk"] = step("s1 disk", disk(system_dir))

        g = start("s2-old", [old] + old_extra)
        hit, m = enter(g, directory); R["observations"]["s2 entered"] = step("s2 entered", hit)
        if hit == 'Entered screen "Game"':
            spawned(g); time.sleep(1)
            o = R["observations"]
            o["s2 slots"] = step("s2 slots", g.func(SLOTS)); o["s2 ground"] = step("s2 ground", g.func(GROUND))
            o["s2 containers"] = step("s2 containers", g.func(containers(chest_at, crate_at)))
            # (i) another item dragged onto the slot that holds the cases
            step("dirt", g.func(GIVE_DIRT)); o["s2 drag dirt onto cases"] = step("drag dirt (slot 12) -> slot 3", g.func(drag(False, 12, False, 3, chest_at)))
            o["s2 slots after drag"] = step("slots", g.func(SLOTS))
            # (iii) the slot with cases x2 dragged into the chest
            o["s2 move cases to chest"] = step("drag slot 5 -> chest 5", g.func(drag(False, 5, True, 5, chest_at)))
            # (ii) the keys dropped with the drop key
            step("select keys", g.func(DROP_KEYS)); g.cmd("DROP_ONCE"); time.sleep(1.5)
            o["s2 slots after actions"] = step("slots", g.func(SLOTS)); o["s2 ground after actions"] = step("ground", g.func(GROUND))
            o["s2 containers after actions"] = step("containers", g.func(containers(chest_at, crate_at)))
            if "int160" in label or "1.6.0" in label:
                o["s2 craft glock (pre-feature)"] = step("craft glock in the older version", g.func(craft_glock(bench_at)))
            step("save", g.func(SAVE)); time.sleep(1.5)
        R["observations"]["s2 errors"] = [l for l in close(g) if "[Error]" in l or "Ignoring invalid" in l or "not found" in l.lower()][:40]; g = None
        R["observations"]["s2 disk"] = step("s2 disk", disk(system_dir))

        g = start("s3-new", [new] + extras)
        hit, m = enter(g, directory); R["observations"]["s3 entered"] = step("s3 entered", hit)
        if hit == 'Entered screen "Game"':
            spawned(g); time.sleep(1)
            R["observations"]["s3 slots"] = step("s3 slots", g.func(SLOTS)); R["observations"]["s3 ground"] = step("s3 ground", g.func(GROUND))
            R["observations"]["s3 ledger"] = step("s3 ledger", g.func(LEDGER))
            R["observations"]["s3 containers"] = step("s3 containers", g.func(containers(chest_at, crate_at)))
            R["observations"]["s3 craft glock"] = step("craft glock again in the candidate", g.func(craft_glock(bench_at)))
            R["observations"]["s3 first-craft lines"] = [l for l in g.lines[m:] if "[FIRST_CRAFT]" in l][:10]
        R["observations"]["s3 errors"] = [l for l in close(g) if "[Error]" in l][:40]; g = None
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            try: g.close()
            except Exception: pass
        m0.RUNTIME_OWNER.release()
    o = R["observations"]
    def count(text, name): return sum(int(x.split("x", 1)[1].split(":")[0]) for x in (text or "").split(";") if x.endswith(":" + name))
    R["summary"] = {k: {"cases": count(o.get(k + " slots"), "ScCaseBlock"), "keys": count(o.get(k + " slots"), "ScCaseKeyBlock"), "groundCases": count(o.get(k + " ground"), "ScCaseBlock")}
                    for k in ("s1", "s2", "s3")}
    R["summary"]["ledgerBack"] = (o.get("s3 ledger") or "").startswith("True")
    print("SUMMARY", json.dumps(R["summary"], ensure_ascii=False), flush=True)
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"switch1124-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"engine {R['engine']}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
