"""feedback-1124-170 step 0 (Q7): a world played with the 1.7.0 candidate, opened and saved with an earlier official release,
then opened with 1.7.0 again, on the isolated standalone engine copy (M0_ENGINE=131 by default). Observes what the older
reader does with the new survivor cases and keys and with the first-craft ledger (ScFirstCraft group and its world-folder
copy), and what 1.7.0 finds when it comes back.

Three game starts on ONE world (the runtime lock is taken once, so the world folder stays between them):
  1  1.7.0: new survival world; cases x3 and keys x2 in the inventory, one case dropped on the ground, a first-craft mark
     ("probe" / ak47) in the ledger; save; exit.
  2  the older release: load that world; record the inventory; save; exit.
  3  1.7.0 again: load; record the inventory, the ground case and the ledger.
Usage: sp_switch170.py <label> <1.7.0 core package> <older core package> [<extra package> ...]
Extra packages (the agents package for a Lite core) load in sessions 1 and 3; give an older agents package by setting
SW_OLD_EXTRA to its path. Package paths should be ASCII. The player's own Mods folder and worlds are never used. Sound and
music are turned off in every session (user requirement for isolated copies).
"""
import json, os, sys, time, xml.etree.ElementTree as ET
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE

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
SETUP = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; for (int i = 0; i < inv.SlotsCount; i++) inv.RemoveSlotItems(i, inv.GetSlotCount(i)); '
    'int cb = Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true); int kb = Game.BlocksManager.GetBlockIndex<Game.ScCaseKeyBlock>(true); '
    'inv.AddSlotItems(3, Game.Terrain.MakeBlockValue(cb, 0, 0), 3); inv.AddSlotItems(4, Game.Terrain.MakeBlockValue(kb), 2); inv.ActiveSlotIndex = 0; '
    'project.FindSubsystem<Game.SubsystemPickables>(true).AddPickable(Game.Terrain.MakeBlockValue(cb, 0, 0), 1, pl.ComponentBody.Position + new Engine.Vector3(1.5f, 0.5f, 0), null, null, null); '
    'bool marked = project.FindSubsystem<Game.SubsystemScFirstCraft>(true).Ledger.TryMark("probe", "ak47"); '
    'return cb + "|" + kb + "|" + marked;')
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
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"switch170-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"switch170-{label}", "engine": m0.SP_VERSION, "packages": {p.name: sha(p) for p in [new, old] + extras + old_extra},
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
        g = start("s1-170", [new] + extras)
        m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(MUTE)
        directory, system_dir = step("world", g.func(WORLD)).split("|", 1)
        R["observations"]["s1 setup"] = step("setup", g.func(SETUP))
        R["observations"]["s1 slots"] = step("s1 slots", g.func(SLOTS)); R["observations"]["s1 ground"] = step("s1 ground", g.func(GROUND))
        step("save", g.func(SAVE)); time.sleep(1.5); close(g); g = None
        R["observations"]["s1 disk"] = step("s1 disk", disk(system_dir))

        g = start("s2-old", [old] + old_extra)
        hit, m = enter(g, directory); R["observations"]["s2 entered"] = step("s2 entered", hit)
        if hit == 'Entered screen "Game"':
            spawned(g); time.sleep(1)
            R["observations"]["s2 slots"] = step("s2 slots", g.func(SLOTS)); R["observations"]["s2 ground"] = step("s2 ground", g.func(GROUND))
            step("save", g.func(SAVE)); time.sleep(1.5)
        R["observations"]["s2 errors"] = [l for l in close(g) if "[Error]" in l or "Ignoring invalid" in l or "not found" in l.lower()][:40]; g = None
        R["observations"]["s2 disk"] = step("s2 disk", disk(system_dir))

        g = start("s3-170", [new] + extras)
        hit, m = enter(g, directory); R["observations"]["s3 entered"] = step("s3 entered", hit)
        if hit == 'Entered screen "Game"':
            spawned(g); time.sleep(1)
            R["observations"]["s3 slots"] = step("s3 slots", g.func(SLOTS)); R["observations"]["s3 ground"] = step("s3 ground", g.func(GROUND))
            R["observations"]["s3 ledger"] = step("s3 ledger", g.func(LEDGER))
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
    out = RESULTS / f"switch170-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"engine {R['engine']}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
