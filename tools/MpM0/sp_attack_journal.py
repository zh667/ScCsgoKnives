"""The attack journal on real CS attacks, single player (post-mp-bugs-20260930 §3): ready for a third-party target.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/sp_attack_journal.py <package path | output-lite | output-full> <label> [template]
One game in the fixed runtime role "server" (single player), TestAutomation and one CS package. In a cleared arena a target
(default a cow; any entity template name, e.g. a third-party creature) stands 7 m ahead; the player fires an AK-47 and a
Zeus at it and strikes it with a knife from close by. ScAttackJournal (switched on for the run) records each delivered
attack: attack type, attacker and target templates, Projectile context, power submitted and after the ProcessAttackment
hooks, the injury the attack computed and the health actually lost; lines of the game log with "Attack execute error"
(an exception the engine caught inside ProcessAttackment) are collected beside it.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, RESULTS, RUNS, TA_BUILT, sha, to_menu, poll
from mp_m1 import PROJECT, MAIN, SURVIVAL, KEEP_ACTIVE, STAND, build_arena, teleport, EYE, near, aim_at

def mod_type(name): return f'System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("{name}")).FirstOrDefault(x => x != null)'
JOURNAL_ON = f'var t = {mod_type("Game.ScAttackJournal")}; if (t == null) return "absent"; t.GetField("Record").SetValue(null, true); t.GetMethod("Clear").Invoke(null, null); return "on";'
JOURNAL = f'var t = {mod_type("Game.ScAttackJournal")}; return t == null ? "" : (string)t.GetProperty("Text").GetValue(null);'
def target(template, x, y, z): return PROJECT + (
    f'var e = Game.DatabaseManager.CreateEntity(project, "{template}", true); var b = e.FindComponent<Game.ComponentBody>(true); '
    f'b.Position = new Engine.Vector3({x}f + .5f, {y}f, {z}f + .5f); b.Velocity = Engine.Vector3.Zero; '
    'var loco = e.FindComponent<Game.ComponentLocomotion>(); if (loco != null) { loco.WalkSpeed = 0; loco.FlySpeed = 0; loco.SwimSpeed = 0; loco.JumpSpeed = 0; } '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) { sp.SpawnDuration = 0f; sp.AutoDespawn = false; sp.DespawnTime = null; } '
    'project.AddEntity(e); var h = e.FindComponent<Game.ComponentHealth>(); return e.Id + " " + (h == null ? "no-health" : h.Health.ToString("0.###"));')
def give(name): return MAIN + (
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); var spec = Game.GunSpec.All[v]; '
    'int id = Game.ScGunRegistry.Current.Allocate(v, spec.Magazine, false, Game.ScGunDurability.Full(v)); '
    'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)), 1); inv.ActiveSlotIndex = 0; return id.ToString();')
KNIFE = MAIN + ('var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true), 0, 0), 1); inv.ActiveSlotIndex = 0; return "ok";')
def health(eid): return PROJECT + f'var e = project.Entities.FirstOrDefault(x => x.Id == {eid}); var h = e?.FindComponent<Game.ComponentHealth>(); return h == null ? "gone" : h.Health.ToString("0.####");'


def main(package, label, template="Cow_Brown"):
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package); ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"attack-journal-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"attack-journal-{label}", "package": {pkg.name: sha(pkg)}, "template": template, "steps": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    g = None
    try:
        g = Game("server", case_dir, [ta, pkg]); to_menu(g)
        g.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        poll(g, SURVIVAL, lambda v: v == "Survival", 20)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait("Player into playing.", 600, m)
        step("window kept active", g.func(KEEP_ACTIVE))
        x, y, z = step("arena", build_arena(g)).split()
        for attempt in range(10):
            g.func(teleport(x, y, z)); time.sleep(.7)
            if near(g.func(EYE), x, y, z): break
        g.func(STAND); time.sleep(1.5)
        step("attack journal", g.func(JOURNAL_ON))
        # A fresh target per weapon, at a distance inside that weapon's reach (the Zeus is short-range, the knife melee).
        for weapon, distance in [("ak47", 7), ("taser", 3), ("knife", 2)]:
            step(f"{weapon} for the player", g.func(KNIFE if weapon == "knife" else give(weapon))); time.sleep(3)
            eid, h0 = step(f"{template} {distance} m ahead for the {weapon} (id, health)", g.func(target(template, x, y, int(z) + distance))).split(); time.sleep(2)
            ray = g.func(aim_at(eid)); g.cmd(f"HIT_RAY {ray}")
            if weapon != "knife": g.cmd(f"DIG_RAY {ray} 2")
            time.sleep(2.5)
            step(f"target health after the {weapon}", g.func(health(eid)))
            g.func(PROJECT + f'var e = project.Entities.FirstOrDefault(x => x.Id == {eid}); if (e != null) project.RemoveEntity(e, true); return "ok";'); time.sleep(.5)
        R["journal"] = g.func(JOURNAL).splitlines()
        R["attackExecuteErrors"] = [l for l in g.lines if "Attack execute error" in l][:20]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: R["gameErrors"] = g.errors(); g.close()
        m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"attack-journal-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(json.dumps({k: R.get(k) for k in ["case", "failure", "journal", "attackExecuteErrors", "gameErrors"]}, ensure_ascii=False, indent=1)[:9000])
    return 0 if "failure" not in R and R.get("journal") else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
