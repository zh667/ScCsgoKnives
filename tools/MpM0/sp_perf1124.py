"""1.6.0 performance round (feedback-1124-170, user 2026-11-25: "static variables not cleaned up make exiting a world lag").
Measures, on the isolated 1.9.3.1 copy (muted, cursor released by the harness), N cycles of: load the world (new the first
time) -> a heavy scene (two five-man squads, 200 drops, an airdrop crate, the player holding an AK) -> 8 s of frames ->
save, dispose and back to the main menu -> measurement-only full GC.
Per cycle: load seconds; frame time avg / p95 / max and managed bytes allocated per frame in the heavy scene; save,
dispose and menu seconds of the exit; after the exit: managed heap, working set and private bytes, how many of the
disposed worlds' Project objects are still alive, and which static fields of the CS assemblies still reach a world
object (Project, Subsystem, Entity, Component - directly, in a collection, or as a delegate target).
Usage: M0_ENGINE=131 python sp_perf1124.py <label> <core package>[+<package>...] [cycles]
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE

m0.firewall_preflight = lambda: {"skipped": "standalone sp131 run; no MP role started", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)

MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
WORLD = MAIN + 'return project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName;'
# A frame sampler installed once per process: frame durations and allocated bytes, collected while "sc-perf-on" is set.
SAMPLER = ('var d = System.AppDomain.CurrentDomain; if (d.GetData("sc-perf-frames") == null) { var list = new System.Collections.Generic.List<double[]>(); d.SetData("sc-perf-frames", list); '
           'long last = System.GC.GetTotalAllocatedBytes(false); double t = Engine.Time.RealTime; '
           'Engine.Window.Frame += () => { long a = System.GC.GetTotalAllocatedBytes(false); double now = Engine.Time.RealTime; '
           'if (d.GetData("sc-perf-on") != null) list.Add(new[] { now - t, a - last }); last = a; t = now; }; } return "sampler";')
START = 'var d = System.AppDomain.CurrentDomain; ((System.Collections.Generic.List<double[]>)d.GetData("sc-perf-frames")).Clear(); d.SetData("sc-perf-on", 1); return "on";'
STOP = ('var d = System.AppDomain.CurrentDomain; d.SetData("sc-perf-on", null); var l = ((System.Collections.Generic.List<double[]>)d.GetData("sc-perf-frames")).ToList(); '
        'if (l.Count == 0) return "{}"; var ms = l.Select(x => x[0] * 1000).OrderBy(x => x).ToArray(); '
        'return System.Text.Json.JsonSerializer.Serialize(new { frames = l.Count, avgMs = ms.Average(), p95Ms = ms[(int)(ms.Length * .95)], maxMs = ms[^1], over50 = ms.Count(x => x > 50), '
        'allocKBPerFrame = l.Average(x => x[1]) / 1024 });')
SCENE = MAIN + (
    'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var p = pl.ComponentBody.Position; int x = (int)System.MathF.Floor(p.X), z = (int)System.MathF.Floor(p.Z); '
    'var dir = project.FindSubsystem<Game.SubsystemTacticalEnemies>(false); int made = 0; string why = ""; '
    'if (dir != null) foreach (var (ox, oz) in new[] { (12, 6), (-12, 6), (8, -10), (-8, -10), (0, 14), (0, -14) }) { if (made >= 10) break; int gx = x + ox, gz = z + oz; '
    '  int got = dir.SpawnManual(new Engine.Point3(gx, t.Terrain.GetTopHeight(gx, gz), gz), 5); made += got; if (got == 0) why = dir.ManualFailure; } '
    'var pk = project.FindSubsystem<Game.SubsystemPickables>(true); foreach (var q in pk.Pickables) q.ToRemove = true; int iron = Game.Terrain.MakeBlockValue(Game.IronIngotBlock.Index); '
    'for (int i = 0; i < 200; i++) { int dx = i % 20 - 10, dz = i / 20 - 12; pk.AddPickable(iron, 1 + i % 3, new Engine.Vector3(x + dx + .5f, t.Terrain.GetTopHeight(x + dx, z + dz) + 1.3f, z + dz + .5f), Engine.Vector3.Zero, null, null); } '
    'int ab = Game.BlocksManager.GetBlockIndex("ScAirdropBlock", false); if (ab > 0) t.ChangeCell(x + 3, t.Terrain.GetTopHeight(x + 3, z + 3) + 1, z + 3, Game.Terrain.MakeBlockValue(ab)); '
    'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"); var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, Game.GunSpec.FreshFull)), 1); inv.ActiveSlotIndex = 0; '
    'pl.ComponentHealth.AttackResilience = 1e6f; return "enemies " + made + " " + why + " drops 200";')
# The same scene without CS (squads, crate and gun are CS): for the engine's own share of frame time and allocations.
VANILLA_SCENE = MAIN + (
    'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var p = pl.ComponentBody.Position; int x = (int)System.MathF.Floor(p.X), z = (int)System.MathF.Floor(p.Z); '
    'var pk = project.FindSubsystem<Game.SubsystemPickables>(true); foreach (var q in pk.Pickables) q.ToRemove = true; int iron = Game.Terrain.MakeBlockValue(Game.IronIngotBlock.Index); '
    'for (int i = 0; i < 200; i++) { int dx = i % 20 - 10, dz = i / 20 - 12; pk.AddPickable(iron, 1 + i % 3, new Engine.Vector3(x + dx + .5f, t.Terrain.GetTopHeight(x + dx, z + dz) + 1.3f, z + dz + .5f), Engine.Vector3.Zero, null, null); } '
    'pl.ComponentHealth.AttackResilience = 1e6f; return "vanilla drops 200";')
# Exit as the game's pause menu does (save, dispose, main menu), each part timed inside the frame.
EXIT = ('var sw = System.Diagnostics.Stopwatch.StartNew(); var d = System.AppDomain.CurrentDomain; '
        'var refs = (System.Collections.Generic.List<System.WeakReference>)d.GetData("sc-perf-projects") ?? new System.Collections.Generic.List<System.WeakReference>(); d.SetData("sc-perf-projects", refs); '
        'refs.Add(new System.WeakReference(Game.GameManager.Project)); '
        'Game.GameManager.SaveProject(true, true); double save = sw.Elapsed.TotalMilliseconds; Game.GameManager.DisposeProject(); double dispose = sw.Elapsed.TotalMilliseconds - save; '
        'Game.ScreensManager.SwitchScreen("MainMenu"); double menu = sw.Elapsed.TotalMilliseconds - save - dispose; '
        'return System.FormattableString.Invariant($"{save:0.0} {dispose:0.0} {menu:0.0}");')
AFTER = ('for (int i = 0; i < 3; i++) { System.GC.Collect(2, System.GCCollectionMode.Forced, true, true); System.GC.WaitForPendingFinalizers(); } '
         'var refs = (System.Collections.Generic.List<System.WeakReference>)System.AppDomain.CurrentDomain.GetData("sc-perf-projects"); var proc = System.Diagnostics.Process.GetCurrentProcess(); proc.Refresh(); '
         'return System.Text.Json.JsonSerializer.Serialize(new { heapMB = System.GC.GetTotalMemory(false) / 1048576.0, workingSetMB = proc.WorkingSet64 / 1048576.0, privateMB = proc.PrivateMemorySize64 / 1048576.0, '
         'projectsAlive = refs == null ? 0 : refs.Count(r => r.IsAlive), projectsDisposed = refs == null ? 0 : refs.Count });')
# Every static field of the CS assemblies that still reaches a world object while no world is loaded.
STATICS = r'''
bool World(object o) => o is GameEntitySystem.Project || o is GameEntitySystem.Subsystem || o is GameEntitySystem.Entity || o is GameEntitySystem.Component;
var found = new System.Collections.Generic.List<string>();
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies().Where(a => (a.GetName().Name ?? "").StartsWith("ScCsgo"))) {
  System.Type[] types; try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); } catch (System.Exception) { continue; }
  foreach (var type in types) {
    try { if (type.ContainsGenericParameters || type.IsByRefLike) continue; } catch (System.Exception) { continue; }
    System.Reflection.FieldInfo[] fields; try { fields = type.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); } catch (System.Exception) { continue; }
    foreach (var f in fields) {
      object v; try { if (f.IsLiteral || f.FieldType.IsPointer || f.FieldType.IsByRefLike || f.FieldType.IsFunctionPointer) continue; v = f.GetValue(null); } catch { continue; } if (v == null) continue;
      int hits = 0; string kind = "";
      void Look(object o, int depth) {
        if (o == null || depth > 2 || hits > 50) return;
        if (World(o)) { hits++; kind = o.GetType().Name; return; }
        if (o is System.Delegate del) { foreach (var x in del.GetInvocationList()) Look(x.Target, depth + 1); return; }
        if (o is string || o.GetType().IsPrimitive || o.GetType().IsEnum) return;
        var ot = o.GetType();
        if (ot.FullName != null && ot.FullName.StartsWith("System.Runtime.CompilerServices.ConditionalWeakTable")) return;
        if (o is System.Collections.IDictionary dict) { int n = 0; foreach (System.Collections.DictionaryEntry e in dict) { if (n++ > 2000) break; Look(e.Key, depth + 1); Look(e.Value, depth + 1); } return; }
        if (o is System.Collections.IEnumerable seq) { int n = 0; foreach (var e in seq) { if (n++ > 2000) break; Look(e, depth + 1); } return; }
        if (depth < 1 && !ot.IsValueType && ot.Namespace == "Game") foreach (var g in ot.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)) { object x; try { x = g.GetValue(o); } catch { continue; } Look(x, depth + 1); }
      }
      try { Look(v, 0); } catch (System.Exception) { found.Add(asm.GetName().Name + ":" + type.FullName + "." + f.Name + " (not readable)"); continue; }
      if (hits > 0) found.Add(asm.GetName().Name + ":" + type.FullName + "." + f.Name + " x" + hits + " " + kind);
    }
  }
}
return string.Join(" | ", found);'''
STATICS = STATICS.replace("\n", " ")
# Managed bytes each CS update and draw path allocates per call, measured in the game by calling it 40 times on the
# game thread: every IUpdateable component of the scene's creatures and every IUpdateable/IDrawable subsystem of the CS
# assemblies, plus each creature model's Animate and DrawExtras. Grouped by type.
# The starter-equipment subsystem's pending checkpoint and what one call of its Update allocates, five times.
STARTER = MAIN + ('var s = project.FindSubsystem<Game.SubsystemScStarterEquipment>(true); var f = s.GetType().GetField("m_pendingCheckpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); '
    'var pend = f == null ? "?" : string.Join(",", ((System.Collections.IEnumerable)f.GetValue(s)).Cast<object>()); var r = new System.Collections.Generic.List<long>(); '
    'for (int i = 0; i < 5; i++) { long a = System.GC.GetAllocatedBytesForCurrentThread(); ((Game.IUpdateable)s).Update(0.016f); r.Add(System.GC.GetAllocatedBytesForCurrentThread() - a); } '
    'var ready = string.Join(",", players.PlayersData.Select(d => d.PlayerIndex + ":" + d.IsReadyForPlaying)); return "pending [" + pend + "] ready [" + ready + "] bytes " + string.Join(",", r);')
ALLOCS = MAIN + r'''
var cam = pl.GameWidget.ActiveCamera; var rows = new System.Collections.Generic.Dictionary<string, double[]>();
void Measure(string key, System.Action call) { for (int i = 0; i < 3; i++) call(); long a = System.GC.GetAllocatedBytesForCurrentThread(); var sw = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 40; i++) call(); sw.Stop();
  long b = System.GC.GetAllocatedBytesForCurrentThread(); if (!rows.TryGetValue(key, out var r)) rows[key] = r = new double[3]; r[0] += (b - a) / 40.0; r[1] += sw.Elapsed.TotalMilliseconds / 40; r[2] += 1; }
foreach (var e in project.Entities.ToArray()) { if (e.FindComponent<Game.ComponentPlayer>() != null || e.FindComponent<Game.ComponentCreature>() == null) continue;
  foreach (var c in e.Components) { if (c is Game.IUpdateable u && (c.GetType().Assembly.GetName().Name ?? "").StartsWith("ScCsgo")) Measure("update " + c.GetType().Name, () => u.Update(0.016f)); }
  var m = e.FindComponent<Game.ComponentCreatureModel>(); if (m != null && (m.GetType().Assembly.GetName().Name ?? "").StartsWith("ScCsgo")) { Measure("animate " + m.GetType().Name, () => m.Animate()); Measure("extras " + m.GetType().Name, () => m.DrawExtras(cam)); } }
foreach (var s in project.Subsystems) { if (!(s.GetType().Assembly.GetName().Name ?? "").StartsWith("ScCsgo")) continue;
  if (s is Game.IUpdateable u) Measure("update " + s.GetType().Name, () => u.Update(0.016f));
  if (s is Game.IDrawable d) foreach (int o in d.DrawOrders) Measure("draw " + s.GetType().Name + "#" + o, () => d.Draw(cam, o)); }
return string.Join(" | ", rows.OrderByDescending(p => p.Value[0] * p.Value[2]).Select(p => System.FormattableString.Invariant($"{p.Key} x{p.Value[2]:0}: {p.Value[0] / p.Value[2]:0} B/call {p.Value[1] / p.Value[2]:0.000} ms")));'''.replace("\n", " ")
def load(directory): return m0.Call(
    'Game.WorldsManager.UpdateWorldsList(); var w = Game.WorldsManager.WorldInfos.FirstOrDefault(x => x.DirectoryName == $s0); '
    'if (w == null) return "no world " + $s0; Game.ScreensManager.SwitchScreen("GameLoading", w, null); return "loading";', directory)


def main(label, packages, cycles="5"):
    vanilla = packages == "none"
    pkgs = [] if vanilla else [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"perf1124-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; case_dir.mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "cycles": []}
    T0 = time.time(); g = None
    def step(name, value=None): print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:900]}", flush=True); return value
    try:
        g = m0.game("sp", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); g.func(MUTE); g.func(SAMPLER)
        R["menuBefore"] = step("menu before", json.loads(g.func(AFTER)))
        R["staticsBefore"] = step("statics at the menu before any world", g.func(STATICS, 300))
        directory = None
        for i in range(int(cycles)):
            c = {"cycle": i}; t = time.time()
            if directory is None:
                m0.enter_world(g)
                directory = g.func(WORLD)
            else:
                m = g.mark(); g.func(load(directory)); g.wait('Entered screen "Game"', 600, m)
                poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)
            c["loadSeconds"] = round(time.time() - t, 2); g.func(KEEP_ACTIVE); g.func(MUTE)
            if vanilla: pass
            else:
              try: g.func('var s = Game.GameManager.Project.FindSubsystem<Game.SubsystemScStarterEquipment>(false); var p = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>(true).ComponentPlayers.FirstOrDefault(); if (s != null && p != null) s.Choose(p, Game.ScStarterPlan.None); foreach (var d in Game.DialogsManager.Dialogs.ToArray()) Game.DialogsManager.HideDialog(d); return "ok";')
              except Exception: pass
            c["scene"] = step(f"cycle {i} scene", g.func(VANILLA_SCENE if vanilla else SCENE)); time.sleep(3)
            g.func(START); time.sleep(8); c["frames"] = step(f"cycle {i} frames", json.loads(g.func(STOP)))
            if not vanilla and i in (0, int(cycles) - 1):
                try: c["starter"] = step(f"cycle {i} starter", g.func(STARTER)); c["allocs"] = step(f"cycle {i} allocations per call", g.func(ALLOCS, 300))
                except Exception as e: c["allocs"] = step("allocations failed", str(e)[:400])
            m = g.mark(); t = time.time(); parts = g.func(EXIT, 300); g.wait('Entered screen "MainMenu"', 300, m)
            save, dispose, menu = [float(v) for v in parts.split()]
            c["exit"] = {"saveMs": save, "disposeMs": dispose, "menuMs": menu, "wallSeconds": round(time.time() - t, 2)}; step(f"cycle {i} exit", c["exit"])
            time.sleep(2); c["after"] = step(f"cycle {i} after exit", json.loads(g.func(AFTER)))
            c["statics"] = step(f"cycle {i} statics", g.func(STATICS, 300))
            R["cycles"].append(c)
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
        if g is not None: R.setdefault("gameErrors", g.errors()[:40])
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"perf1124-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print("SUMMARY", json.dumps([{k: c.get(k) for k in ("loadSeconds", "exit", "after")} | {"frames": {k: c.get("frames", {}).get(k) for k in ("avgMs", "p95Ms", "allocKBPerFrame")}} for c in R["cycles"]], ensure_ascii=False), flush=True)
    print(f"failure {R.get('failure')}; errors {len(R.get('gameErrors', []))}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
