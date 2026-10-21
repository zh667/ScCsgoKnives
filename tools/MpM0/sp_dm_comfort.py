"""player-suggestions-20261021 in the real game (isolated 1.9.3.1, M0_ENGINE=131, a new survival world): what the offline
checks cannot show - the engine really dispatches the hooks the changes use, and the engine's own save really writes the
world's settings.

  1. an HE blast (the chicken's blast: the HE detonation path) 18 blocks from a cow: the cow runs away
  2. an arena world in map editing: rain and the world's own time; a 0.2 injury gives the game's red screen (the control)
  3. the lobby: static, no weather, noon; food and body temperature put low come back to 0.9 / 12; a save taken now has
     the world's own settings in Project.xml while the running world holds the arena's
  4. a running match: a 0.2 injury of the life (the world's damage) lowers the health and gives no red screen
  5. the host's rule off in the lobby: the world's own settings at once; on again and back to map editing: given back;
     a last save still has the world's own settings

Usage: sp_dm_comfort.py <label> <core package spec> <deathmatch package spec>   (specs as sp_deathmatch.py: stage-<tag>-lite ...)
The player's own Mods folder and worlds are never used (isolated copy, new world). Exit 1 when a check fails.
"""
import json, sys, time

import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, SURVIVAL, build_arena, teleport
from sp_deathmatch import package

DMV = MAIN + 'var dm = project.FindSubsystem<Game.SubsystemScDeathmatch>(false); if (dm == null) return "no deathmatch subsystem"; '
PROBE = DMV + (
    'var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); var w = info.WorldSettings; var tod = project.FindSubsystem<Game.SubsystemTimeOfDay>(true); '
    'var weather = project.FindSubsystem<Game.SubsystemWeather>(true); var v = pl.ComponentVitalStats; var h = pl.ComponentHealth; var me = dm.Enabled ? dm.StateOf(pl) : null; '
    'return System.Text.Json.JsonSerializer.Serialize(new { env = w.EnvironmentBehaviorMode.ToString(), time = w.TimeOfDayMode.ToString(), weather = w.AreWeatherEffectsEnabled, '
    '  tod = tod.TimeOfDay, midday = tod.Midday, rain = weather.PrecipitationIntensity, food = v.Food, temperature = v.Temperature, red = h.m_redScreenFactor, '
    '  redout = pl.ComponentScreenOverlays.RedoutFactor, health = h.Health, phase = dm.Match == null ? "-" : dm.Match.Phase.ToString(), holds = dm.HoldsEnvironment, '
    '  rule = dm.Match == null ? (bool?)null : dm.Match.Rules.FixedEnvironment, me = me == null ? "-" : me.Phase.ToString(), hp = me == null ? -1 : me.Health });')
# The engine's own save, then its Project.xml read back from the world's folder.
SAVED = PROJECT + (
    'var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); Game.GameManager.SaveProject(true, false); '
    'System.Xml.Linq.XElement x; using (var s = Engine.Storage.OpenFile(Engine.Storage.CombinePaths(info.DirectoryName, "Project.xml"), Engine.OpenFileMode.Read)) x = System.Xml.Linq.XElement.Load(s); '
    'var gi = x.Element("Subsystems").Elements("Values").First(e => (string)e.Attribute("Name") == "GameInfo"); '
    'string V(string n) => (string)gi.Elements("Value").First(e => (string)e.Attribute("Name") == n).Attribute("Value"); '
    'return V("EnvironmentBehaviorMode") + "/" + V("TimeOfDayMode") + "/" + V("AreWeatherEffectsEnabled");')
RAIN = PROJECT + ('var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); var weather = project.FindSubsystem<Game.SubsystemWeather>(true); double now = info.TotalElapsedGameTime; '
    'weather.m_precipitationStartTime = now - 60; weather.m_precipitationEndTime = now + 900; weather.PrecipitationIntensity = 1f; return "rain started " + weather.IsPrecipitationStarted;')
LOW_VITALS = MAIN + 'pl.ComponentVitalStats.Food = 0.05f; pl.ComponentVitalStats.Temperature = 2f; return "food 0.05, temperature 2";'
INJURE = MAIN + 'pl.ComponentHealth.Injure(0.2f, null, false, "probe"); return "injured 0.2";'
HEAL = MAIN + 'pl.ComponentHealth.Health = 1f; return "healed";'
def arena_setup(x, y, z): return DMV + (
    f'string r = dm.EnableArena(); if (r != null) return "enable: " + r; '
    f'r = dm.SetCorner(0, new Engine.Point3({x - 4}, {y}, {z - 4})) ?? dm.SetCorner(1, new Engine.Point3({x + 4}, {y}, {z + 8})); if (r != null) return "corners: " + r; '
    f'foreach (var (sx, sz) in new[] {{ (-3, -3), (3, -3), (-3, 7), (3, 7) }}) {{ r = dm.AddSpawn(new Engine.Vector3({x} + sx + 0.5f, {y}f, {z} + sz + 0.5f), 0f); if (r != null) return "spawn: " + r; }} '
    f'r = dm.SetLobby(new Engine.Vector3({x} + 0.5f, {y}f, {z + 12} + 0.5f), 0f); if (r != null) return "lobby: " + r; '
    'var issues = dm.ArenaIssues(); return issues.Count == 0 ? "arena ready" : "issues: " + string.Join(" / ", issues.Select(i => i.Code + "#" + i.SpawnId));')
OPEN_LOBBY = DMV + 'return dm.OpenLobby() ?? "lobby open";'
# The mode refuses a survival backpack that still holds ordinary items (they are never deleted for a kit): the new world's
# starting items are emptied first - this test world only.
ENTER_START = DMV + ('var inv = pl.ComponentMiner.Inventory; int removed = 0; for (int i = 0; i < inv.SlotsCount; i++) { int c = inv.GetSlotCount(i); if (c > 0) removed += inv.RemoveSlotItems(i, c); } '
    'dm.RequestEnter(pl, true); var me = dm.StateOf(pl); return "emptied " + removed + ", entered " + (me != null && me.Entered) + ": " + (dm.StartMatch() ?? "starting");')
STOP = DMV + 'return dm.StopMatch() ?? "stopped";'
# (the automation compiles C# 7.3: no "with"; the record is copied and its init-only property set by reflection)
def rule(on): return DMV + ('var copy = (Game.DmRules)typeof(Game.DmRules).GetMethod("<Clone>$").Invoke(dm.Match.Rules, null); '
    f'typeof(Game.DmRules).GetProperty("FixedEnvironment").SetValue(copy, {"true" if on else "false"}); return dm.SetRules(copy) ?? "rule {"on" if on else "off"}";')
EDIT = DMV + 'return dm.EditMap() ?? "map editing";'
def cow(x, y, z): return PROJECT + (
    'foreach (var n in new[] { "Cow_Brown", "Cow_Black", "Cow_White", "Deer", "Horse_Black" }) { var e = Game.DatabaseManager.CreateEntity(project, n, false); if (e == null) continue; '
    f'e.FindComponent<Game.ComponentBody>(true).Position = new Engine.Vector3({x} + 0.5f, {y}f, {z} + 0.5f); project.AddEntity(e); return n + " " + e.Id; }} return "no animal template";')
def cow_state(eid): return MAIN + (
    f'var e = project.Entities.FirstOrDefault(q => q.Id == {eid}); '
    'if (e == null) return "gone; animals near the player: " + string.Join(", ", project.Entities.Where(q => q.FindComponent<Game.ComponentRunAwayBehavior>() != null && q.FindComponent<Game.ComponentBody>() != null && Engine.Vector3.Distance(q.FindComponent<Game.ComponentBody>().Position, pl.ComponentBody.Position) < 40).Select(q => q.Id + " " + q.ValuesDictionary.DatabaseObject.Name)); '
    'var r = e.FindComponent<Game.ComponentRunAwayBehavior>(); var b = e.FindComponent<Game.ComponentBody>(true); var h = e.FindComponent<Game.ComponentHealth>(); '
    'return System.FormattableString.Invariant($"{r?.m_stateMachine.CurrentState ?? "no run-away behaviour"} {b.Position.X:0.0} {b.Position.Y:0.0} {b.Position.Z:0.0} importance {r?.m_importanceLevel:0} health {h?.Health:0.00}");')
def blast(x, y, z): return PROJECT + f'project.FindSubsystem<Game.SubsystemScGrenades>(true).ChickenBlast(new Engine.Vector3({x} + 0.5f, {y}f, {z} + 0.5f), -1); return "blast";'


def main(label, core, dm):
    pkgs = [package(core), package(dm)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"dm-comfort-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"dm-comfort-{label}", "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "checks": []}
    T0 = time.time(); g = None

    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"name": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + (f" :: {detail}" if detail else ""), flush=True)
    def probe(name):
        return step(name, json.loads(g.func(PROBE)))
    def own(p): return (p["env"], p["time"], p["weather"]) == ("Living", "Changing", True)
    def held(p): return (p["env"], p["time"], p["weather"]) == ("Static", "Day", False)

    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        step("survival world", poll(g, SURVIVAL, lambda v: v == "Survival", 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240))
        g.func(KEEP_ACTIVE); time.sleep(2)
        x, y, z = [int(v) for v in step("arena area", build_arena(g)).split()]
        g.func(teleport(x, y, z + 12)); time.sleep(1.5)

        # ---- 1. animals and a real HE blast (the cow 18 blocks from it; nobody within its 6-block radius)
        animal = step("animal", g.func(cow(x, y, z + 4))); eid = animal.split()[-1]; time.sleep(2)
        before = step("animal before", g.func(cow_state(eid)))
        step("blast", g.func(blast(x, y + 2, z - 14)))
        states = []
        for _ in range(12): time.sleep(.25); states.append(g.func(cow_state(eid)))
        step("animal after", states)
        check("an HE blast 18 blocks away makes the animal run away", before.startswith("Inactive") and any(s.startswith("RunningAway") for s in states), f"{before} -> {states}")

        # ---- 2. map editing: the world's own environment; the control injury reddens the screen
        step("arena", g.func(arena_setup(x, y, z))); step("rain", g.func(RAIN)); time.sleep(2)
        p = probe("map editing")
        check("map editing keeps the world's own environment (living, changing time, weather, raining)", own(p) and not p["holds"] and p["rain"] > .9, json.dumps(p))
        step("control injury", g.func(INJURE)); time.sleep(.2); p = probe("after the control injury")
        check("control: outside the mode an injury gives the game's red screen", p["red"] > .3 and p["health"] < .9, f"red {p['red']} health {p['health']}")
        g.func(HEAL); time.sleep(3); saved = step("save in map editing", g.func(SAVED))

        # ---- 3. the lobby
        step("open lobby", g.func(OPEN_LOBBY)); time.sleep(1.2)
        p = probe("lobby")
        check("the lobby holds a static world without weather at noon", held(p) and p["holds"] and abs(p["tod"] - p["midday"]) < 1e-4 and p["rain"] == 0, json.dumps(p))
        step("low vitals", g.func(LOW_VITALS)); time.sleep(.6); p = probe("lobby, vitals")
        check("food and body temperature come back to 0.9 / 12 in the lobby", abs(p["food"] - .9) < .01 and abs(p["temperature"] - 12) < .1, f"food {p['food']} temperature {p['temperature']}")
        saved = step("save in the lobby", g.func(SAVED)); p = probe("after the save")
        check("a save during the hold writes the world's own settings; the world stays held", saved == "Living/Changing/True" and held(p), f"saved {saved}, live {p['env']}/{p['time']}/{p['weather']}")

        # ---- 4. a running match: the life is hurt by the world, no red screen
        step("enter and start", g.func(ENTER_START))
        p = None
        for _ in range(40):
            time.sleep(.5); p = json.loads(g.func(PROBE))
            if p["me"] == "Alive": break
        step("alive", p)
        step("injury in the match", g.func(INJURE)); time.sleep(.4); p = probe("after the match injury")
        check("in a running match an injury lowers the health and gives no red screen", p["hp"] == 80 and abs(p["health"] - .8) < .01 and p["red"] == 0 and p["redout"] == 0, f"hp {p['hp']} health {p['health']} red {p['red']} redout {p['redout']}")
        check("the match holds the arena's environment and the vitals", held(p) and abs(p["food"] - .9) < .01 and abs(p["temperature"] - 12) < .1, json.dumps(p))

        # ---- 5. the rule off and on, back to map editing, a last save
        step("stop", g.func(STOP))
        for _ in range(40):
            time.sleep(.5)
            if json.loads(g.func(PROBE))["phase"] == "Lobby": break
        step("rule off", g.func(rule(False))); time.sleep(.6); p = probe("lobby, rule off")
        check("the host's rule off gives the world its own environment at once", own(p) and not p["holds"] and p["rule"] is False, json.dumps(p))
        step("rule on", g.func(rule(True))); time.sleep(.6); p = probe("lobby, rule on")
        check("the rule on again holds the arena's environment", held(p) and p["holds"], json.dumps(p))
        step("map editing", g.func(EDIT)); time.sleep(.6); p = probe("map editing again")
        check("back to map editing gives the world its own environment", own(p) and not p["holds"], json.dumps(p))
        saved = step("last save", g.func(SAVED))
        check("the last save has the world's own settings", saved == "Living/Changing/True", saved)
        R["gameErrors"] = g.errors()[:40]
        check("no game errors", not R["gameErrors"], "; ".join(R["gameErrors"][:5]))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"dm-comfort-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["name"] for c in R["checks"] if not c["ok"]]
    print(f"checks {len(R['checks'])}, failed {len(failed)}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2], sys.argv[3]))
