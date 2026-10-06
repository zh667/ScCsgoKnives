"""subworld-travel-generic-20261003: CS guns carried to and from a sub-world by a sub-world mod that does not call CS, on
the isolated SurvivalcraftAPI 1.9.3.1 copy (M0_ENGINE=131). The mod's own travel function is used - Ancient World's
BeginTravel (what its gate and ages tree call: capture, save, create or open the dimension, switch, restore after load),
Ghoul 2.0's TransPortal (save, copy the player XML into the other world, switch). Only the trigger (walking into the gate)
is replaced by the call.

The reported trip (D:/下载/Game(2) (1).log, 2026-10-03): the main world has a record #1 of its own (a sawed-off); a fresh
SCAR-20 (no record yet) goes into the sub-world and is fired there (its record is made there), comes home, is fired again,
goes out again; then the world is saved and loaded again. After every arrival: the slot, its record, its rounds, the
other world's #1, and the [GUN_TRAVEL] lines.

Usage: sp_subworld.py <label> <core package spec> <ancient | ghoul> <provider package path>
The player's own Mods folder and worlds are never used (isolated copy, new world).
"""
import json, os, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_deathmatch import package, CREATIVE, GUI_IN_SHOTS
from sp_smoke_view import snap

GUNS = 'var guns = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); '
def put(slot, name, record_rounds=None): return MAIN + (
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); int block = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true); '
    + (f'int id = Game.ScGunRegistry.Current.Allocate(v, {record_rounds}, false, Game.ScGunDurability.Full(v)); ' if record_rounds is not None else 'int id = Game.GunSpec.FreshFull; ')
    + f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems({slot}, inv.GetSlotCount({slot})); inv.AddSlotItems({slot}, Game.Terrain.MakeBlockValue(block, 0, Game.GunSpec.WithId(v, id)), 1); '
    f'inv.ActiveSlotIndex = {slot}; return id.ToString();')
def record_only(name, rounds): return MAIN + (   # a record of this world nobody carries (the main world's own #1 in the report)
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); return Game.ScGunRegistry.Current.Allocate(v, {rounds}, false, Game.ScGunDurability.Full(v)).ToString();')
STATE = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; int v = inv.GetSlotCount(0) > 0 ? inv.GetSlotValue(0) : 0; int d = Game.Terrain.ExtractData(v); var reg = Game.ScGunRegistry.Current; '
    'string Rec(int n) => reg != null && reg.TryGetSnapshot(n, out var s) ? Game.GunSpec.All[s.Variant].Name + "/" + s.Rounds : "-"; '
    'return System.Text.Json.JsonSerializer.Serialize(new { dir = project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName, value = v, '
    '  id = v == 0 ? -1 : Game.GunSpec.GetId(d), gun = v == 0 ? "-" : Game.GunSpec.All[System.Math.Max(0, Game.GunSpec.GetVariant(d))].Name, usable = v != 0 && Game.ScGunBlock.IsKnown(v), '
    '  rounds = v == 0 ? -1 : Game.GunSpec.GetRounds(d), records = reg == null ? -1 : reg.Count, rec1 = Rec(1), rec2 = Rec(2), rec3 = Rec(3), active = inv.ActiveSlotIndex, inventory = inv.GetType().Name });')
# Why a trigger would do nothing: the conditions ScGunBindings.Available / ContextAvailable / ControlAllowed read.
DIAG = MAIN + ('var cam = pl.PlayerData?.GameWidget?.ActiveCamera; return System.Text.Json.JsonSerializer.Serialize(new { '
    'available = Game.ScGunBindings.Available(pl), context = Game.ScGunBindings.ContextAvailable(pl), control = Game.ScGunBindings.ControlAllowed(pl), active = Engine.Window.IsActive, '
    'screen = Game.ScreensManager.CurrentScreen?.GetType().Name, animating = Game.ScreensManager.IsAnimating, modal = pl.ComponentGui.ModalPanelWidget?.GetType().Name, '
    'dialogs = Game.DialogsManager.HasDialogs(pl.GuiWidget) || Game.DialogsManager.HasDialogs(Game.ScreensManager.RootWidget), camera = cam?.GetType().Name, entityControl = cam?.IsEntityControlEnabled, '
    'ready = pl.PlayerData?.IsReadyForPlaying, sleep = pl.ComponentSleep?.SleepFactor, health = pl.ComponentHealth.Health, value = pl.ComponentMiner.ActiveBlockValue, '
    'known = Game.ScGunBlock.IsKnown(pl.ComponentMiner.ActiveBlockValue), menu = Game.ScWeaponTouchPanel.MenuActive });')
# The trigger is the game's own dig input held along the camera's line (TestAutomation's DIG_RAY, as the multiplayer
# runs fire). (The mod's fire-button helper lost to the knife subsystem's update order in some runs: a harness flake.)
RAY = MAIN + ('pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; var c = pl.GameWidget.ActiveCamera; var p = c.ViewPosition; var d = Engine.Vector3.Normalize(c.ViewDirection + new Engine.Vector3(0, 0.3f, 0)); '
    'return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z} {d.X} {d.Y} {d.Z}");')
ANCIENT_TRAVEL = MAIN + (
    'var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => { try { return a.GetType("Game.AncientWorldRuntime"); } catch { return null; } }).FirstOrDefault(x => x != null); '
    'if (t == null) return "no Ancient World runtime"; var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public; '
    'string dir = (string)t.GetField("s_currentDirectory", f).GetValue(null); bool ancient = (bool)t.GetField("s_isAncientWorld", f).GetValue(null); '
    'if ((bool)t.GetField("s_transitioning", f).GetValue(null)) return "busy"; '
    't.GetMethod("BeginTravel", f).Invoke(null, new object[] { pl, dir, ancient, null }); return "from " + dir + (ancient ? " (ancient)" : " (main)");')
GHOUL_TRAVEL = ('var s = Game.GameManager.Project.Subsystems.FirstOrDefault(x => x.GetType().FullName == "SAGhoul.Tartareosity.SubsystemTartareosity"); '
    'if (s == null) return "no Ghoul Tartareosity subsystem"; var m = s.GetType().GetMethod("TransPortal", System.Type.EmptyTypes); m.Invoke(s, null); return "TransPortal";')


def main(label, core, provider, provider_pkg):
    pkgs = [package(core), Path(provider_pkg)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"subworld-{provider}-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"subworld-{provider}-{label}", "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "checks": [], "frames": []}
    T0 = time.time(); g = None
    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True); return ok
    def state(name):
        try: return step(name, json.loads(g.func(STATE)))
        except Exception as e: return step(name, {"error": str(e)})
    def frame(name):
        try:
            shot = snap(g, f"subworld-{provider}-{label}-{len(R['frames']):02d}-{name}", ((0, 0, 0), (1, 1, 1)))[0]; R["frames"].append(shot); step("frame " + name, shot)
        except Exception as e: step("frame failed " + name, str(e))
    def travel(name):
        m = g.mark(); step(name, g.func(ANCIENT_TRAVEL if provider == "ancient" else GHOUL_TRAVEL))
        if provider == "ancient": g.wait("旅行者数据已恢复，切换完成", 600, m)
        else: g.wait('Entered screen "Game"', 600, m)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 120); g.func(KEEP_ACTIVE)
        # a world entered for the first time opens with the engine's intro camera, which has no character control (the
        # trigger does nothing until it hands over, as for a player)
        step("camera", poll(g, MAIN + 'return pl.GameWidget.ActiveCamera.GetType().Name;', lambda v: v == "FppCamera", 120))
        time.sleep(4)   # the arrival check waits for the inventory to settle (ScTravelArrival.Quiet), then maps
        return state("after " + name)
    def shoot(name, frames=12):
        time.sleep(1.2); before = json.loads(g.func(STATE)); step("trigger conditions " + name, g.func(DIAG)); ray = g.func(RAY); step("fire " + name, ray); g.cmd(f"DIG_RAY {ray} {frames}"); time.sleep(2.5); after = state("after firing " + name)
        check(f"{name}: the gun fired (rounds {before.get('rounds')} -> {after.get('rounds')})", isinstance(after, dict) and after.get("usable") and after.get("rounds", 99) < before.get("rounds", 0), after)
        return after
    def gun_lines(since): return [l for l in g.lines[since:] if "[GUN_TRAVEL]" in l or "unusable gun data" in l]

    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        mode = "Survival" if os.environ.get("SC_TRAVEL_SURVIVAL") == "1" else "Creative"
        step("world mode", poll(g, CREATIVE.replace("GameMode.Creative", "GameMode." + mode), lambda v: v == mode, 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240))
        g.func(KEEP_ACTIVE); step("settings", g.func(GUI_IN_SHOTS)); time.sleep(3)

        # ---- the main world: its own #1 (a sawed-off nobody carries), a fresh SCAR-20 in slot 1
        step("main world's own record", g.func(record_only("sawedoff", 5)))
        step("fresh SCAR-20 in slot 1", g.func(put(0, "scar20")))
        home = state("main before the trip")
        check("fixture: actual inventory matches requested mode", home.get("inventory") == ("ComponentCreativeInventory" if mode == "Creative" else "ComponentInventory"), home)
        check("fixture: the main world's #1 is a sawed-off and the SCAR-20 is fresh", home.get("rec1", "").startswith("sawedoff") and home.get("id") == 0, home)

        # ---- out, fired there (its record is made in the sub-world)
        mark = len(g.lines); there = travel("trip 1: into the sub-world")
        check("trip 1 reached another world", there.get("dir") != home.get("dir"), there)
        fired = shoot("in the sub-world")
        check("in the sub-world the first shot made a record there", fired.get("id", 0) not in (0, 1023, -1), fired)
        sub_id, sub_rounds = fired.get("id"), fired.get("rounds")

        # ---- home: the reported fault was "unusable gun data" here
        mark = len(g.lines); back = travel("trip 2: home")
        lines = step("gun lines after trip 2", gun_lines(mark))
        check("home again: the carried SCAR-20 is usable with the rounds it had in the sub-world",
              back.get("dir") == home.get("dir") and back.get("usable") and back.get("gun") == "scar20" and back.get("rounds") == sub_rounds, back)
        check("home again: the main world's own #1 is still the sawed-off with 5 rounds", back.get("rec1") == "sawedoff/5", back)
        check("home again: no 'unusable gun data'", not any("unusable gun data" in l for l in lines), lines)
        frame("home-after-trip-2")
        used = shoot("at home")

        # ---- out again: back to its number in the sub-world, with the rounds it has now
        mark = len(g.lines); again = travel("trip 3: into the sub-world again")
        lines3 = step("gun lines after trip 3", gun_lines(mark))
        check("out again: rounds from home and stable existing identity", again.get("usable") and again.get("id") == sub_id and again.get("rounds") == used.get("rounds"), again)
        check("out again: no 'unusable gun data'", not any("unusable gun data" in l for l in lines3), lines3)

        if os.environ.get("SC_TRAVEL_SURVIVAL") == "1":
            survival_fired = shoot("survival sub-world")
            survival_back = travel("trip 4: survival home")
            check("survival home: spent rounds preserved", survival_back.get("usable") and survival_back.get("rounds") == survival_fired.get("rounds"), survival_back)
            again = shoot("survival home")

        # ---- saved and loaded again (the arrival is not taken twice; the state is on disk)
        m = g.mark()
        g.cmd('EXEC var info = Game.GameManager.WorldInfo; Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("GameLoading", info, null);')
        g.wait('Entered screen "Game"', 600, m); poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 120); g.func(KEEP_ACTIVE); time.sleep(4)
        reloaded = state("after save and reload")
        check("after save and reload: the same gun, number and rounds", reloaded.get("usable") and reloaded.get("id") == again.get("id") and reloaded.get("rounds") == again.get("rounds") and reloaded.get("records") == again.get("records"), reloaded)
        R["gunLines"] = [l for l in g.lines if "[GUN_TRAVEL]" in l or "unusable gun data" in l or "gun registry:" in l][:80]
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"subworld-{provider}-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"steps {len(R['steps'])}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; failure {R.get('failure')}; failed {failed}; errors {len(R.get('gameErrors', []))}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]))
