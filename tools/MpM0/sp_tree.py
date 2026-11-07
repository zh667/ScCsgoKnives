"""world-tree-gun-registry-20261107 (1.6.0, phase A): CS guns with sub-world mods on the isolated standalone engine
(M0_ENGINE=131 by default; 1951 for the player's 1.9.5.1), one registry per world tree.

  ghoul | ancient  the provider's own travel function, through sp_subworld.main (fresh world; a main-world record nobody
                   carries, a fresh SCAR-20 fired in the sub-world, home, fired again, out again, saved and loaded again;
                   every check there is about the gun's number, rounds and the main world's own record).
  portal           xianyu.portal 1.5.0 runs its spaces as projects BESIDE the root and moves the player entity between them
                   without saving or loading (SubsystemPortal.MoveSpacePlayer, the call its jade/token use makes). Fresh
                   world: a main-world record nobody carries (#1 sawed-off), a numbered AK-47 (#2) in slot 0; into the
                   own space, fired; home, fired; into the space again; saved and loaded again. Checked each time: the
                   scene the player is in, the gun's number and rounds, the root's #1, and that the root's and the
                   scene's gun subsystems and ScGunRegistry.Current are one registry; after the save the tree file and
                   the stamp in Space_0/SpaceProject.xml.

Usage: sp_tree.py <label> <cs package> <ghoul|ancient|portal> <provider package>   (ASCII paths; isolated copy only)
"""
import json, os, sys, time, xml.etree.ElementTree as ET
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import sp_uninstall   # noqa: F401  the standalone/worktree harness adaptations (firewall preflight, MP build folder, engines)
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_deathmatch import GUI_IN_SHOTS
import sp_subworld
from sp_subworld import put, record_only, RAY

F = 'var f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static; '
GET = 'System.Func<object, string, object> Get = (o, n) => o.GetType().GetField(n, f) != null ? o.GetType().GetField(n, f).GetValue(o) : o.GetType().GetProperty(n, f)?.GetValue(o); '
# The portal's own switch (SubsystemPortal's jade/token handler): into the player's own space, or back to where they came from.
MOVE = MAIN + F + GET + (
    'var portal = pl.Entity.Project.Subsystems.FirstOrDefault(x => x.GetType().Name == "SubsystemPortal"); if (portal == null) return "no portal subsystem"; var t = portal.GetType(); '
    'if (!(bool)t.GetProperty("CanSwitchSpaces", f).GetValue(null)) return "unsupported"; string msg = null; '
    'var allow = t.GetMethod("TryAllowSpaceSwitch", f); if (allow != null && !(bool)allow.Invoke(portal, new object[] { (System.Action<string>)(m => msg = m) })) return "not allowed: " + msg; '
    'int idx = pl.PlayerData.PlayerIndex; int own = (int)t.GetMethod("OwnSpaceDimension", f).Invoke(portal, new object[] { idx }); var st = t.GetMethod("PlayerSpaceState", f).Invoke(portal, new object[] { idx }); '
    'int cur = (int)t.GetMethod("PlayerDimension", f).Invoke(portal, new object[] { pl }); bool inside = cur == own; int dim = inside ? (int)Get(st, "ReturnDimension") : own; '
    'var pos = inside ? (Engine.Vector3)Get(st, "ReturnPosition") : (Engine.Vector3)t.GetMethod("SpawnForDimension", f).Invoke(portal, new object[] { own }); '
    't.GetMethod("MoveSpacePlayer", f).Invoke(portal, new object[] { pl, dim, pos, !inside }); return (inside ? "home to " : "into space ") + dim;')
STATE = MAIN + F + (
    'var inv = pl.ComponentMiner.Inventory; int v = inv.GetSlotValue(0); int d = Game.Terrain.ExtractData(v); var reg = Game.ScGunRegistry.Current; '
    'string Rec(int n) => reg != null && reg.TryGetSnapshot(n, out var s) ? Game.GunSpec.All[s.Variant].Name + "/" + s.Rounds : "-"; '
    'System.Func<GameEntitySystem.Project, object> Reg = p => p?.FindSubsystem<Game.SubsystemScGunBlockBehavior>(false) is { } g ? typeof(Game.SubsystemScGunBlockBehavior).GetField("m_registry", f).GetValue(g) : null; '
    'var scene = pl.Entity.Project; var rootReg = Reg(project); var sceneReg = Reg(scene); '
    'return System.Text.Json.JsonSerializer.Serialize(new { dir = scene.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName, root = project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName, '
    '  value = v, id = Game.GunSpec.GetId(d), gun = Game.GunSpec.All[System.Math.Max(0, Game.GunSpec.GetVariant(d))].Name, usable = Game.ScGunBlock.IsKnown(v), rounds = Game.GunSpec.GetRounds(d), '
    '  rec1 = Rec(1), rec2 = Rec(2), records = reg == null ? -1 : reg.Count, shared = rootReg != null && object.ReferenceEquals(rootReg, sceneReg) && object.ReferenceEquals(rootReg, reg) });')
# The first entry into a new space hands the camera over a moment later (as a world's intro camera does): a shot before
# that is cast from where the camera still is, not from the player.
CAMERA_READY = MAIN + ('var c = pl.GameWidget.ActiveCamera; float d = Engine.Vector3.Distance(c.ViewPosition, pl.ComponentCreatureModel.EyePosition); '
                       'return c.GetType().Name == "FppCamera" && d < 2f ? "ready" : c.GetType().Name + " " + d;')
WORLD_DIR = MAIN + 'var dir = project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName; return Engine.Storage.GetSystemPath(dir);'


def tree_facts(system_root):
    """The tree file and the stamps in the root's Project.xml and Space_0/SpaceProject.xml, read from disk."""
    def gun(path):
        if not path.exists(): return None
        subs = ET.parse(path).getroot().find("Subsystems")
        g = next((e for e in subs.findall("Values") if e.get("Name") == "ScGunBlockBehavior"), None) if subs is not None else None
        if g is None: return {"group": False}
        val = {e.get("Name"): e.get("Value") for e in g.findall("Value")}
        reg = next((e for e in g.findall("Values") if e.get("Name") == "GunRegistry"), None)
        rec = next((e for e in reg.findall("Values") if e.get("Name") == "Records"), None) if reg is not None else None
        return {"group": True, "tree": val.get("GunTreeId"), "generation": val.get("GunTreeGeneration"), "records": len(rec) if rec is not None else 0}
    root = Path(system_root); tf = root / "ScCsgoGuns.xml"; facts = {"treeFile": tf.exists()}
    if tf.exists():
        t = ET.parse(tf).getroot(); facts.update(treeId=t.get("Id"), treeGeneration=t.get("Generation"), bak=(root / "ScCsgoGuns.xml.bak").exists())
    facts["root"] = gun(root / "Project.xml"); facts["space0"] = gun(root / "Space_0" / "SpaceProject.xml")
    return facts


def portal(label, cs_package, provider_package):
    pkgs = [Path(cs_package), Path(provider_package)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"tree-portal-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"tree-portal-{label}", "engine": m0.SP_VERSION, "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "checks": []}
    T0 = time.time(); g = None
    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:600]}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:900], flush=True); return ok
    def state(name): return step(name, json.loads(g.func(STATE)))
    def move(name):
        result = step(name, g.func(MOVE)); g.func(KEEP_ACTIVE)
        step("camera with the player", poll(g, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(2); return result
    def shoot(name):
        time.sleep(1.2); before = json.loads(g.func(STATE)); ray = g.func(RAY); g.cmd(f"DIG_RAY {ray} 12"); time.sleep(2.5); after = state("after firing " + name)
        check(f"{name}: the gun fired (rounds {before.get('rounds')} -> {after.get('rounds')})", after.get("usable") and after.get("rounds", 99) < before.get("rounds", 0), after)
        return after
    try:
        g = m0.game("portal", case_dir, pkgs); R["engine_info"] = g.engine_info(); to_menu(g)
        m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(GUI_IN_SHOTS); time.sleep(3)
        system_root = step("world", g.func(WORLD_DIR))
        step("root's own #1 (a sawed-off nobody carries)", g.func(record_only("sawedoff", 5)))
        gun_id = int(step("a numbered AK-47 in slot 0", g.func(put(0, "ak47", 20))))
        home = state("home before the trip")
        check("fixture: #1 is the root's sawed-off and the AK-47 is #2 with 20 rounds", home["rec1"] == "sawedoff/5" and home["id"] == gun_id == 2 and home["rounds"] == 20, home)

        moved = move("1: into the own space")
        if moved == "unsupported":
            check(f"the portal mod can switch spaces on engine {m0.SP_VERSION}", False, moved); raise RuntimeError("portal spaces unsupported on this engine")
        there = state("in the space")
        check("1: the player is in a space project of the same tree", there["dir"] != there["root"] and there["dir"].startswith(there["root"] + "/Space_"), there)
        check("1: same gun, same number, same rounds; one registry for both projects", there["id"] == gun_id and there["rounds"] == 20 and there["usable"] and there["shared"], there)
        fired = shoot("in the space")
        back = (move("2: home"), state("home again"))[1]
        check("2: home with the rounds spent in the space, same number, root's #1 untouched",
              back["dir"] == back["root"] and back["id"] == gun_id and back["rounds"] == fired["rounds"] and back["rec1"] == "sawedoff/5" and back["shared"], back)
        used = shoot("at home")
        again = (move("3: into the space again"), state("in the space again"))[1]
        check("3: in the space again with the rounds from home, same number", again["dir"] != again["root"] and again["id"] == gun_id and again["rounds"] == used["rounds"] and again["shared"], again)
        last = shoot("in the space again")

        m = g.mark()
        g.cmd('EXEC var info = Game.GameManager.WorldInfo; Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("GameLoading", info, null);')
        hit = g.wait(['Entered screen "Game"', "Loading World Failed", "gun load refused"], 600, m); step("reload", hit)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 120); g.func(KEEP_ACTIVE); time.sleep(4)
        reloaded = state("after save and reload")
        check("4: saved and loaded again: the world loads, same number, the rounds of the last shot, root's #1 untouched",
              hit == 'Entered screen "Game"' and reloaded["id"] == gun_id and reloaded["rounds"] == last.get("rounds") and reloaded["rec1"] == "sawedoff/5", {"last": last, "reloaded": reloaded})
        facts = step("disk", tree_facts(system_root))
        check("4: one tree file; the root's and Space_0's saves carry its id",
              facts["treeFile"] and facts["root"] and facts["root"].get("tree") == facts.get("treeId")
              and facts["space0"] and facts["space0"].get("tree") == facts.get("treeId"), facts)
        R["gunLines"] = [l for l in g.lines if "[GUN_TREE]" in l or "[GUN_TRAVEL]" in l or "unusable gun data" in l or "gun registry:" in l or "refused" in l][:80]
        R["gameErrors"] = g.errors()[:40]
        check("no gun refusal or failed load in the log", not any("refused" in l or "Loading World Failed" in l for l in g.lines), [l for l in g.lines if "refused" in l][:10])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            try: g.close()
            except Exception: pass
        m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"tree-portal-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"engine {R['engine']}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; failure {R.get('failure')}; failed {failed}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


if __name__ == "__main__":
    label, cs, provider, provider_pkg = sys.argv[1:5]
    sys.exit(portal(label, cs, provider_pkg) if provider == "portal" else sp_subworld.main(label, cs, provider, provider_pkg))
