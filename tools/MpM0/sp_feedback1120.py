"""feedback-1120-160 in the real game (docs/tasks/feedback-1120-160.md): the three player reports with the real third-party mods.

1. Neorxna 1.4.3: CS offered its workbench recipes (ScNeorxnaRecipes "registered"); Neorxna's own recipe walk shows the
   universal magazine's workbench card; the recipe dialog is opened with it and pictured.
2. Ghoul 2.0's soul set (clothing 90-93, the package's own clothing data): CS bullets keep 30 % of the unclothed injury,
   a finite protection-1 set (vanilla 19-22) still absorbs everything, an unclothed player takes the whole hit; then a
   T squad member shoots the player wearing the soul set and the player's health falls.
3. The recoil setting: an AK-47 held for 0.75 s under each setting; how far the view rose while firing and after.

Usage: M0_ENGINE=<engine> python sp_feedback1120.py <label> <core package>[+<package>...] <Neorxna package> <Ghoul package>
Isolated engine copy, new survival world; the player's own Mods and worlds are never used.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, SURVIVAL, build_arena, teleport, STAND
from sp_camera_fire import give, look
from sp_gun_visuals import window_of, picture
from sp_subworld import STATE as GUN_STATE
from sp_deathmatch import FPP
from sp_engine_compat import STARTER_NONE, EMPTY, SELECT0, DURABLE, CLOSE_DIALOGS, enemy, creature

NEO_STATUS = 'return Game.ScNeorxnaRecipes.Status;'
# Neorxna's own walks over its provider list (internal static (int, bool) -> IEnumerable<Widget>, not a provider itself):
# how many workbench cards each gives for the magazine.
NEO_WALK = ('var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Neorxna.NUI.RecipaediaRecipesNeo", false)).First(x => x != null); '
    'var providers = ((System.Collections.IList)t.GetField("RecipeWidgetProviders").GetValue(null)).Cast<System.Delegate>().ToList(); int v = Game.ScAmmoBlock.Value(0); '
    'var parts = new System.Collections.Generic.List<string>(); '
    'foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)) { '
    'var ps = m.GetParameters(); if (m.ReturnType != typeof(System.Collections.Generic.IEnumerable<Game.Widget>) || ps.Length != 2 || ps[0].ParameterType != typeof(int) || ps[1].ParameterType != typeof(bool)) continue; '
    'if (providers.Any(d => d.Method == m)) continue; '
    'var cards = ((System.Collections.Generic.IEnumerable<Game.Widget>)m.Invoke(null, new object[] { v, false })).ToList(); '
    'parts.Add(m.Name + "=" + cards.Count(c => c is Game.ContainerWidget cw && cw.AllChildren.OfType<Game.LabelWidget>().Any(l => l.Text != null && l.Text.Contains("武器装配台")))); } '
    'return providers.Count + " providers: " + string.Join(", ", providers.Select(d => d.Method.DeclaringType.FullName + "." + d.Method.Name)) + " | walks " + string.Join(", ", parts);')
# Neorxna's recipe dialog for the magazine (its show method (int, bool, List<Widget>)), with the cards its walk gives.
NEO_DIALOG = ('var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Neorxna.NUI.RecipaediaRecipesNeo", false)).First(x => x != null); int v = Game.ScAmmoBlock.Value(0); '
    'var cards = Game.ScNeorxnaRecipes.Provide(v, false).ToList(); '
    'var show = t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).First(m => m.ReturnType == typeof(void) && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int), typeof(bool), typeof(System.Collections.Generic.List<Game.Widget>) })); '
    'show.Invoke(null, new object[] { v, false, cards }); return Game.DialogsManager.Dialogs.Select(d => d.GetType().FullName).LastOrDefault() ?? "none";')


def wear(indices): return MAIN + (
    # (level 5: the engine takes off clothing above the player's level the next frame - vanilla diamond needs 2, a new world is 1)
    'pl.PlayerData.Level = 5f; var c = pl.Entity.FindComponent<Game.ComponentClothing>(true); var block = Game.BlocksManager.Blocks[Game.ClothingBlock.Index]; '
    'foreach (var slot in new[] { Game.ClothingSlot.Head, Game.ClothingSlot.Torso, Game.ClothingSlot.Legs, Game.ClothingSlot.Feet }) c.SetClothes(slot, new int[0]); '
    f'var parts = new System.Collections.Generic.List<string>(); foreach (int i in new int[] {{ {", ".join(str(i) for i in indices)} }}) {{ '
    'int value = Game.Terrain.MakeBlockValue(Game.ClothingBlock.Index, 0, i); var d = block.GetClothingData(value); '
    'c.SetClothes(d.Slot, new[] { value }); parts.Add(i + ":" + d.Slot.Name + " AP " + d.ArmorProtection + " St " + d.Sturdiness); } '
    'return string.Join(", ", parts) + " | unbreakable worn " + Game.ScUnbreakableArmor.Worn(pl.Entity);')
# One CS bullet of power 10 on the body from a squad member (or the player's own entity when none is given), on a player of
# attack resilience 100 and full health: the engine's own clothing pass, then the injury as the engine applies it.
def bullet(shooter): return MAIN + (
    'pl.ComponentHealth.AttackResilience = 100; pl.ComponentHealth.Health = 1; '
    f'var shooter = project.Entities.FirstOrDefault(e => e.Id == {shooter}) ?? pl.Entity; '
    'var p = pl.ComponentBody.Position + new Engine.Vector3(0, 1, 0); var hits = new Game.ScShotHits(); hits.Add(Game.ScHitPart.Body, 10f, p, -Engine.Vector3.UnitZ); '
    'var a = new Game.ScSurvivalBalance.BulletAttack(pl.ComponentBody, shooter, p, -Engine.Vector3.UnitZ, hits.Total) { Hits = hits }; '
    'Game.ScDamageIndicator.AttackBody(a); float taken = 1 - pl.ComponentHealth.Health; pl.ComponentHealth.Health = 1; '
    'var cl = pl.Entity.FindComponent<Game.ComponentClothing>(true); '
    'return taken.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(",", new[] { Game.ClothingSlot.Head, Game.ClothingSlot.Torso, Game.ClothingSlot.Legs, Game.ClothingSlot.Feet }.Select(s => cl.GetClothes(s).Count));')
BENCH = 'return Game.ScWorkbenchExtension.IsRecipe(Game.ScAmmoBlock.Value(0)) + " " + Game.ScWorkbenchExtension.All.Count();'
RECOIL = lambda mode: MAIN + f'Game.ScUiSettings.GunRecoil = Game.ScRecoilMode.{mode}; return Game.ScRecoilRules.Current.ToString();'
PITCH = MAIN + 'return pl.ComponentLocomotion.LookAngles.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);'
HP = MAIN + 'return pl.ComponentHealth.Health.ToString("R", System.Globalization.CultureInfo.InvariantCulture);'
NORMAL = MAIN + 'pl.ComponentHealth.AttackResilience = 20; pl.ComponentHealth.Health = 1; return "resilience 20";'
FULL = MAIN + 'pl.ComponentHealth.Health = 1; return "1";'


def main(label, packages, neorxna, ghoul):
    pkgs = [Path(p) for p in packages.split("+")] + [Path(neorxna), Path(ghoul)]
    case_dir = RUNS / f"feedback1120-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case_dir / "pictures").mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "checks": [], "pictures": [], "recoil": {}}
    T0 = time.time()
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": None if ok is None else bool(ok), "detail": detail})
        print(("NOT RUN " if ok is None else "PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:400], flush=True)
    def step(name, value=None): print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:400]}", flush=True); return value
    g = None; h = None
    def snap(name):
        if h is None: return
        try: im = picture(h); im.save(case_dir / "pictures" / f"{name}.jpg", quality=88); R["pictures"].append(name)
        except Exception as e: R["pictures"].append(f"{name}: {e}")
    def section(name, fn):
        try: fn()
        except Exception as e: check(f"{name} (section completed)", False, f"{type(e).__name__}: {e}")
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); step("engine", R["engine"]["engine"]); to_menu(g)
        mods = step("mods", g.func('return string.Join("; ", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));'))
        R["mods"] = mods
        check("CS, Neorxna and Ghoul are loaded together", "zh667.ScCsgoKnives " in mods and "neorxna" in mods.lower() and "ghoul" in mods.lower(), mods)
        # Whether the engine's loading loop reached CS, and if not, that CS finished its own loading at the main menu.
        time.sleep(2)
        with g.lock: lines = list(g.lines)
        skipped = any("never called the CS packages' OnLoadingFinished" in l for l in lines)
        R["loadingFinishedLate"] = skipped
        step("CS loading", "finished at the main menu (an earlier mod ended the loop)" if skipped else "finished by the engine")
        check("CS initialized (by the engine, or at the main menu after another mod ended the loading loop)", any("[ScCsgoKnives] 1.6.0 initialized" in l for l in lines), "late" if skipped else "engine")
        bench = step("workbench supplies", g.func(BENCH))
        check("the weapon workbench has the universal magazine recipe", bench.startswith("True"), bench)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        step("survival world", poll(g, SURVIVAL, lambda v: v == "Survival", 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); g.func(KEEP_ACTIVE); g.func(DURABLE)
        step("starter kit answered: none", g.func(STARTER_NONE)); step("dialogs", g.func(CLOSE_DIALOGS))
        x, y, z = [int(v) for v in step("arena", build_arena(g)).split()]
        g.func(EMPTY); g.func(teleport(x, y, z + 12)); g.func(STAND); time.sleep(1.5); g.func(FPP)
        h = window_of(g.proc.pid)
        cx, cy = [int(v) // 2 for v in g.func(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]

        def neorxna_recipes():
            status = step("CS provider", g.func(NEO_STATUS))
            walk = step("Neorxna walk", g.func(NEO_WALK))
            counts = [int(p.split("=")[1]) for p in walk.split(" | walks ")[1].split(", ") if "=" in p] if " | walks " in walk else []
            check("CS offered its workbench recipes to Neorxna", status == "registered", status)
            check("Neorxna's own recipe walk shows the universal magazine's workbench card", counts and all(c == 1 for c in counts), walk)
            dialog = step("Neorxna dialog", g.func(NEO_DIALOG)); time.sleep(1.5); snap("neorxna-magazine-recipe")
            check("Neorxna's recipe dialog opens with the card", "NUIRecipaediaRecipesDialog" in dialog, dialog)
            step("closed", g.func(CLOSE_DIALOGS))
        section("neorxna", neorxna_recipes)

        def soul_armor():
            # The shooter is a creature that does not fire back (a squad member would wear the armour between the shots here).
            eid = step("a shooter entity (a cow)", g.func(creature(x + .5, y, z - 6, "Cow_Brown")))
            # Level 5 first: vanilla diamond needs level 2, and the player's resilience factor follows the level a frame later.
            step("player level", g.func(MAIN + 'pl.PlayerData.Level = 5f; return pl.PlayerData.Level.ToString();')); time.sleep(1.5)
            results = {}
            for name, indices in [("bare", []), ("soul set", [90, 91, 92, 93]), ("diamond set", [19, 20, 21, 22])]:
                worn = step(f"wear {name}", g.func(wear(indices)))
                shots = [g.func(bullet(eid)) for _ in range(4)]
                takes = [float(v.split()[0]) for v in shots]
                results[name] = {"worn": worn, "taken": takes, "clothesAfter": [v.split()[1] for v in shots]}; step(name, shots)
            R["armor"] = results
            soul = results["soul set"]; bare = results["bare"]["taken"]; plain = bare[0]
            check("Ghoul's soul set is protection 1 with infinite sturdiness and counts as unbreakable",
                  soul["worn"].count(" AP 1 ") == 4 and "unbreakable worn True" in soul["worn"], soul["worn"])
            # (the player's own resilience factor applies as for every attack: unclothed is 10/100 divided by it)
            check("an unclothed player takes the bullet with the engine's own resilience", plain > 0 and all(abs(t - plain) < 1e-6 for t in bare), bare)
            check("on the soul set a CS bullet keeps 30 % of the unclothed injury", all(abs(t - .3 * plain) < 1e-5 for t in soul["taken"]), f"{soul['taken']} vs 0.3 x {plain}")
            check("a finite protection-1 set still absorbs it all", all(t == 0 for t in results["diamond set"]["taken"]), results["diamond set"]["taken"])
            # Stand and be shot at, wearing the soul set (the report: "站着给匪打都没事").
            g.func(wear([90, 91, 92, 93])); g.func(NORMAL); g.func(EMPTY)
            sid = step("T squad member", g.func(enemy(x + .5, y, z + 4)))
            lost, t0 = 0.0, time.time()
            while time.time() - t0 < 15:
                time.sleep(.25); hp = float(g.func(HP))
                if hp < 1: lost += 1 - hp; g.func(FULL)
            R["shotAt"] = {"seconds": 15, "healthLost": lost}
            check("standing in front of a shooting T in the soul set costs health", lost > 0, f"{lost:.3f} health lost in 15 s")
            g.func(EMPTY); g.func(wear([])); g.func(DURABLE); g.func(FULL)
        section("soul armor", soul_armor)

        def recoil():
            g.func(teleport(x, y, z + 12)); g.func(STAND)
            for mode in ["Standard", "Off", "Realistic"]:
                current = g.func(RECOIL(mode)); g.func(give("ak47")); g.func(SELECT0); time.sleep(1.3); g.func(look(0.0, 0.0)); time.sleep(.6)
                before = json.loads(g.func(GUN_STATE))["rounds"]; p0 = float(g.func(PITCH))
                g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.05); g.cmd(f"MOUSEDOWN Left {cx} {cy}"); time.sleep(.75)
                p1 = float(g.func(PITCH)); snap(f"recoil-{mode.lower()}-firing")
                g.cmd(f"MOUSEUP Left {cx} {cy}"); time.sleep(1.5); p2 = float(g.func(PITCH))
                after = json.loads(g.func(GUN_STATE))["rounds"]
                R["recoil"][mode] = {"current": current, "rounds": before - after, "riseDeg": round((p1 - p0) * 57.29578, 3), "afterDeg": round((p2 - p0) * 57.29578, 3)}
                step(mode, R["recoil"][mode])
            r = R["recoil"]
            check("every setting fired a burst", all(v["rounds"] >= 5 for v in r.values()), {k: v["rounds"] for k, v in r.items()})
            check("off: the view does not move while firing", abs(r["Off"]["riseDeg"]) < .05 and abs(r["Off"]["afterDeg"]) < .05, r["Off"])
            check("realistic rises clearly more than standard while firing, and both settle afterwards",
                  r["Realistic"]["riseDeg"] > max(1.0, r["Standard"]["riseDeg"] + .5) and abs(r["Realistic"]["afterDeg"]) < .5 and abs(r["Standard"]["afterDeg"]) < .5, r)
            g.func(RECOIL("Standard"))
        section("recoil", recoil)

        # Neorxna 1.4.3 is built for API 1.9.4: on 1.9.3.1 its own settings screen and key mapping log errors that have nothing
        # to do with CS (they happen without CS too). They are recorded apart; every other error fails.
        errors = g.errors()[:60]
        third = [e for e in errors if "InvokeAsBoolForYsf" in e or "NUIConsle" in e]
        R["gameErrors"] = [e for e in errors if e not in third]; R["neorxnaOwnErrors"] = third
        check("no game errors besides Neorxna's own", not R["gameErrors"], R["gameErrors"][:5])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
        if g is not None: R.setdefault("gameErrors", g.errors()[:40])
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if c["ok"] is False]
    R["seconds"] = round(time.time() - T0)
    out = RESULTS / f"feedback1120-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; engine {R.get('engine', {}).get('engine')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
