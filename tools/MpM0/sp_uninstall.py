"""gun-record-mirror-20261106 (1.5.1): a world played with CS, opened and saved WITHOUT CS, then opened with CS again, on
the isolated standalone engine copy (M0_ENGINE=131 by default; 1951 for the engine of the 2026-11-02 player log).

The player log (D:/下载/Game (2).log): a session without the CS package saved the world, the engine dropped the
ScGunBlockBehavior group (it saves only subsystems it has templates for), and the next start with CS refused the world:
"旧枪仍在，但整个枪械记录表或布局来源缺失". With an optional block mod present while CS is absent, the engine also gives that
mod's block the lowest number no loaded block holds, which is a CS number (BlocksManager.InitializeBlocks).

Three game starts on ONE world (the runtime lock is taken once, so the world folder stays between them):
  1  CS: new survival world; a numbered AK-47 (a record with 17 rounds), a knife, a fresh gun and one item of every other
     CS block; save; exit.
  2  no CS (plus the block mod, if given, with one of its items handed to the player); load that world; save; exit.
  3  CS again (plus the block mod); load. Expected for <expect>: "refused" (the delivered 1.5.0) or "restored" (1.5.1).
Usage: sp_uninstall.py <label> <cs package> <refused|restored> [<block mod package>]
Package arguments are paths (keep them ASCII in job command lines). The player's own Mods folder and worlds are never used.
"""
import json, os, sys, time, xml.etree.ElementTree as ET
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_smoke_view import snap

# Standalone only: the isolated copy's automation binds loopback (ScTestAutomation131) and no MP role is started, so the MP
# firewall preflight (rules for the fixed MP role executables of one checkout) does not apply to this run.
m0.firewall_preflight = lambda: {"skipped": "standalone sp131 run; no MP role started", "problems": []}
# The runtime's state archive lists the MP build's files to leave in place; a checkout without that build (a git worktree)
# has no MP role folders either, so an empty list is exact.
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)
# A checkout without its own unpacked API engines (a git worktree) may name the folder that has them.
if os.environ.get("SC_API_ENGINES") and m0.ENGINE != "131":
    version = m0.SP_ENGINES[m0.ENGINE][0]
    m0.SP_ENGINES = {k: (v, Path(os.environ["SC_API_ENGINES"]) / v if k != "131" else p) for k, (v, p) in m0.SP_ENGINES.items()}
    m0.GAME_SP = m0.SP_ENGINES[m0.ENGINE][1]

GUI_IN_SHOTS = 'Game.SettingsManager.ShowGuiInScreenshots = true; Game.SettingsManager.ShowLogoInScreenshots = false; return "gui in screenshots";'
WORLD = MAIN + 'var dir = project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName; return dir + "|" + Engine.Storage.GetSystemPath(dir);'
SAVE = 'Game.GameManager.SaveProject(true, true); return "saved";'
# Every inventory slot (vanilla only: runs without CS too).
SLOTS = MAIN + ('var inv = pl.ComponentMiner.Inventory; return string.Join(";", System.Linq.Enumerable.Range(0, inv.SlotsCount).Where(i => inv.GetSlotCount(i) > 0)'
                '.Select(i => i + "=" + inv.GetSlotValue(i) + "x" + inv.GetSlotCount(i) + ":" + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(inv.GetSlotValue(i))].GetType().Name));')
SETUP = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; for (int i = 0; i < inv.SlotsCount; i++) inv.RemoveSlotItems(i, inv.GetSlotCount(i)); '
    'int gb = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true); int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"); '
    'int id = Game.ScGunRegistry.Current.Allocate(v, 17, false, Game.ScGunDurability.Full(v)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(v, id)), 1); '
    'int kb = Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true); inv.AddSlotItems(1, Game.BlocksManager.Blocks[kb].GetCreativeValues().First(), 1); '
    'inv.AddSlotItems(2, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(v, Game.GunSpec.FreshFull)), 1); inv.ActiveSlotIndex = 0; '
    'int slot = 3; var added = new System.Collections.Generic.List<string>(); '
    'foreach (var b in Game.BlocksManager.Blocks.Where(b => b != null && b.BlockIndex >= 300 && b.GetType().Assembly.GetName().Name.StartsWith("ScCsgo")).GroupBy(b => b.BlockIndex).Select(x => x.First()).OrderBy(b => b.BlockIndex)) { '
    '  if (b is Game.ScGunBlock || b is Game.ScKnifeBlock) continue; var cv = b.GetCreativeValues().FirstOrDefault(); if (cv == 0) continue; '
    # the hotbar shows 7 to 10 slots by window width (ShortInventoryWidget) and ComponentInventory DROPS the items of slots it
    # hides (VisibleSlotsCount..9) as pickables, so a later start with a narrower window throws them out of their recorded
    # places: never use hotbar slots 7..9, whatever this start shows
    '  int s = slot; while (s < inv.SlotsCount && (inv.GetSlotCount(s) > 0 || inv.GetSlotCapacity(s, cv) < 1 || s >= 7 && s < 10)) s++; if (s >= inv.SlotsCount) continue; '
    '  inv.AddSlotItems(s, cv, 1); if (inv.GetSlotCount(s) > 0) { added.Add(s + ":" + b.GetType().Name + "@" + b.BlockIndex); slot = s + 1; } } '
    'return id + "|" + string.Join(",", added) + " (slots " + inv.SlotsCount + ", visible " + inv.VisibleSlotsCount + ")";')
# The gun in slot 0, its record and the CS block numbers (CS sessions only).
GUN = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; int v = inv.GetSlotValue(0); int d = Game.Terrain.ExtractData(v); var reg = Game.ScGunRegistry.Current; int id = Game.GunSpec.GetId(d); '
    'string rec = reg != null && reg.TryGetSnapshot(id, out var s) ? Game.GunSpec.All[s.Variant].Name + "/" + s.Rounds : "-"; '
    'return System.Text.Json.JsonSerializer.Serialize(new { value = v, block = Game.Terrain.ExtractContents(v), gunBlock = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), id, '
    '  gun = Game.GunSpec.All[System.Math.Max(0, Game.GunSpec.GetVariant(d))].Name, usable = Game.ScGunBlock.IsKnown(v), rounds = Game.GunSpec.GetRounds(d), record = rec, records = reg == null ? -1 : reg.Count });')
RAY = MAIN + ('pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; var c = pl.GameWidget.ActiveCamera; var p = c.ViewPosition; var d = Engine.Vector3.Normalize(c.ViewDirection + new Engine.Vector3(0, 0.3f, 0)); '
              'return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z} {d.X} {d.Y} {d.Z}");')
CAMERA = MAIN + 'return pl.GameWidget.ActiveCamera.GetType().Name;'
TERRARIA = 'TerrariaCraftingTableBlock'
def give(block_name): return MAIN + (   # into the last empty slot
    f'var b = Game.BlocksManager.Blocks.FirstOrDefault(x => x != null && x.GetType().Name == "{block_name}"); if (b == null) return "no {block_name}"; '
    'var inv = pl.ComponentMiner.Inventory; int v = Game.Terrain.MakeBlockValue(b.BlockIndex); int slot = inv.SlotsCount - 1; '
    'while (slot > 0 && (inv.GetSlotCount(slot) > 0 || inv.GetSlotCapacity(slot, v) < 1 || slot >= inv.VisibleSlotsCount && slot < 10)) slot--; '
    'inv.AddSlotItems(slot, v, 1); return slot + "|" + b.BlockIndex + "|" + inv.GetSlotValue(slot) + "x" + inv.GetSlotCount(slot);')
def load(directory): return m0.Call(
    'Game.WorldsManager.UpdateWorldsList(); var w = Game.WorldsManager.WorldInfos.FirstOrDefault(x => x.DirectoryName == $s0); '
    'if (w == null) return "no world " + $s0; Game.ScreensManager.SwitchScreen("GameLoading", w, null); return "loading " + w.DirectoryName;', directory)


def project_facts(system_dir):
    """What the saved Project.xml and the copy beside it hold (read from disk after the game wrote them)."""
    world = Path(system_dir); facts = {"projectXml": (world / "Project.xml").exists(), "copy": (world / "ScCsgoGunRecords.xml").exists(),
                                       "copyBak": (world / "ScCsgoGunRecords.xml.bak").exists()}
    root = ET.parse(world / "Project.xml").getroot()
    subs = root.find("Subsystems")
    def group(parent, name): return next((e for e in parent.findall("Values") if e.get("Name") == name), None) if parent is not None else None
    def kids(element, tag): return list(element.findall(tag)) if element is not None else []
    gun = group(subs, "ScGunBlockBehavior")
    facts["gunGroup"] = gun is not None
    facts["gunRegistry"] = group(gun, "GunRegistry") is not None
    facts["blocks"] = {e.get("Name"): e.get("Value") for e in kids(group(subs, "BlocksManager"), "Value")}
    if facts["copy"]:
        copy = ET.parse(world / "ScCsgoGunRecords.xml").getroot()
        facts["copyBlocks"] = {b.get("Name"): b.get("Index") for b in kids(copy.find("Blocks"), "Block")}
        facts["copyItems"] = len(kids(copy.find("Items"), "Item"))
        facts["copyRecords"] = len(kids(group(group(group(copy, "ScGunBlockBehavior"), "GunRegistry"), "Records"), "Value"))
    return facts


def main(label, cs_package, expect, block_mod=None):
    cs = Path(cs_package); mod = Path(block_mod) if block_mod else None
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"uninstall-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"uninstall-{label}", "engine": m0.SP_VERSION, "expect": expect, "packages": {p.name: sha(p) for p in [cs] + ([mod] if mod else [])},
         "steps": [], "checks": [], "frames": [], "sessions": []}
    T0 = time.time(); g = None
    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:600]}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:900], flush=True); return ok
    def start(name, mods):
        game = m0.game(name, case_dir, mods); R["sessions"].append({"name": name, **game.engine_info()}); to_menu(game); return game
    def enter(game, directory):
        m = game.mark(); step("load", game.func(load(directory)))
        hit = game.wait(['Entered screen "Game"', "Loading World Failed", "gun load refused", "gun format load refused"], 900, m)
        return hit, m
    def spawned(game):
        step("player", poll(game, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); game.func(KEEP_ACTIVE); game.func(GUI_IN_SHOTS)
        step("camera", poll(game, CAMERA, lambda v: v == "FppCamera", 120))
    def frame(game, name):
        try: shot = snap(game, f"uninstall-{label}-{len(R['frames']):02d}-{name}", ((0, 0, 0), (1, 1, 1)))[0]; R["frames"].append(shot); step("frame " + name, shot)
        except Exception as e: step("frame failed " + name, str(e))
    def close(game):
        lines = list(game.lines); game.close(); return lines

    try:
        # ---- 1: CS, new survival world
        g = start("s1-cs", [cs])
        m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(GUI_IN_SHOTS)
        directory, system_dir = step("world", g.func(WORLD)).split("|", 1)
        gun_id, added = step("setup", g.func(SETUP)).split("|", 1)
        s1_slots = step("s1 slots", g.func(SLOTS)); s1_gun = step("s1 gun", json.loads(g.func(GUN)))
        step("save", g.func(SAVE)); time.sleep(1)
        lines = close(g); g = None
        f1 = step("s1 disk", project_facts(system_dir))
        check("1: the saved world holds the gun records", f1["gunRegistry"], f1)
        if expect == "restored":
            check("1: the record copy beside Project.xml was written with the CS block numbers and items",
                  f1["copy"] and f1.get("copyBlocks", {}).get("ScGunBlock") == str(s1_gun["gunBlock"]) and f1.get("copyItems", 0) >= 3, f1)

        # ---- 2: no CS (the block mod, if any); load, save
        g = start("s2-nocs", [mod] if mod else [])
        hit, m = enter(g, directory); step("s2 entered", hit); spawned(g)
        other_slot = step("s2 block mod item", g.func(give(TERRARIA))).split("|")[0] if mod else None
        s2_slots = step("s2 slots", g.func(SLOTS))
        step("save", g.func(SAVE)); time.sleep(1)
        R["s2Errors"] = [l for l in close(g) if "[Error]" in l or "Ignoring invalid" in l][:40]; g = None
        f2 = step("s2 disk", project_facts(system_dir))
        check("2: a save without CS dropped the gun group (the engine's behaviour the fix answers)", not f2["gunGroup"], f2)
        cs_numbers = {k for k, v in f1["blocks"].items() if v and (v.startswith("Sc") and v != "ScTestAutomation131")}
        if mod:
            taken = {k: f2["blocks"].get(k) for k in cs_numbers if f2["blocks"].get(k) == TERRARIA}
            step("2: CS numbers now carrying the block mod's name", taken)
            check("2: the block mod took a CS block number (reported as observed)", bool(taken), {"s1": f1["blocks"], "s2": f2["blocks"]})

        # ---- 3: CS again
        g = start("s3-cs", [cs] + ([mod] if mod else []))
        hit, m = enter(g, directory); step("s3 entered", hit)
        if expect == "refused":
            try: g.wait(["Loading World Failed"], 120, m)
            except Exception as e: step("no failure line", str(e))
            time.sleep(2); refusal = [l for l in g.lines[m:] if "gun load refused" in l or "Loading World Failed" in l or "旧枪仍在" in l]
            step("s3 refusal lines", refusal)
            check("3 (1.5.0): the world is refused with the player's message", any("旧枪仍在，但整个枪械记录表或布局来源缺失" in l for l in refusal), refusal)
        else:
            spawned(g); time.sleep(1.0); frame(g, "notice")
            mirror = [l for l in g.lines[m:] if "[GUN_MIRROR]" in l]; step("s3 mirror lines", mirror)
            s3_slots = step("s3 slots", g.func(SLOTS)); s3_gun = step("s3 gun", json.loads(g.func(GUN)))
            check("3: the world loaded and the copy restored the records", hit == 'Entered screen "Game"' and any("restored" in l and "records" in l for l in mirror), mirror)
            check("3: the numbered AK-47 is usable with its record (17 rounds)", s3_gun["usable"] and s3_gun["gun"] == "ak47" and s3_gun["rounds"] == 17
                  and s3_gun["record"] == "ak47/17" and s3_gun["id"] == s1_gun["id"], {"s1": s1_gun, "s3": s3_gun})
            if mod:
                check("3: the gun now lives on its new CS number", s3_gun["block"] == s3_gun["gunBlock"], s3_gun)
                s1 = dict(x.split("=", 1) for x in s1_slots.split(";") if x); s3 = dict(x.split("=", 1) for x in s3_slots.split(";") if x)
                moved = {k: (s1[k], s3.get(k)) for k in s1}
                same_kind = all(s3.get(k, "").split(":")[-1] == v.split(":")[-1] for k, v in s1.items())
                check("3: every recorded CS item is a CS item again, same kind and count", same_kind, moved)
                check("3: the block mod's own item stayed its own", s3.get(other_slot, "").endswith(":" + TERRARIA), {other_slot: s3.get(other_slot)})
                check("3: the player was told what came back", any("block numbers taken" in l for l in mirror), mirror)
            time.sleep(1.2); before = json.loads(g.func(GUN)); ray = g.func(RAY); g.cmd(f"DIG_RAY {ray} 12"); time.sleep(2.5); after = step("after firing", json.loads(g.func(GUN)))
            check("3: the restored AK-47 fires (rounds go down)", after["usable"] and after["rounds"] < before["rounds"], {"before": before, "after": after})
            step("save", g.func(SAVE)); time.sleep(1)
            f3 = step("s3 disk", project_facts(system_dir))
            check("3: the next save holds the records again", f3["gunRegistry"], f3)
        R["s3Errors"] = [l for l in close(g) if "[Error]" in l][:40]; g = None
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            try: g.close()
            except Exception: pass
        m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"uninstall-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"engine {R['engine']}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; failure {R.get('failure')}; failed {failed}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
