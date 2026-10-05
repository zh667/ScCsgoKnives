"""Resource residency acceptance (2026-10-01 memory round, ad27 step 4) on the isolated 1.9.3.1 copy (M0_ENGINE=131).

One session with the package(s) under test:
  1  install: the [CS_MEM] residency line (members replaced, package copies released) and the "install" report;
  2  a skinned AK held in first person at noon: the decoded copy of its texture is released after upload (texture alive,
     Tag gone), reference capture A, a second capture A2 (frame-to-frame noise);
  3  device-reset safety net: every CS texture emptied as a device reset leaves it (HandleDeviceLost/Reset; the engine's
     own textures untouched) and captured without restoring (negative control C), then ScTextureResidency's restore and
     capture D; D must match A like A2 does, C must not;
  4  background / resume: the engine's Deactivated and Activated events (desktop simulation; a real Android background
     is not available here): their [CS_MEM] lines, the texture intact afterwards (capture E);
  5  decode failure: a skin texture member made unreadable (wrong CRC on its lazy source): the gun falls back to the
     factory finish, the failure is logged once, no exception escapes;
  6  fast switching: eight guns / skins / knives cycled every two frames for 90 frames, then the AK again (capture F);
  7  exit during loading: background texture preparation requested for six skins, world left at once, re-entered, one of
     those skins held (texture present, no errors).
Usage: sp_residency_check.py <label> <package> [<package> ...]
"""
import json, os, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu, PLAYER_SPAWNED, poll
from mp_m1 import MAIN, KEEP_ACTIVE, PROJECT
from sp_memory import ITEMS, hold, EXIT_WORLD, REENTER

TEX = "Textures/ScCsgoKnives/ak47_hd__cu_ak_island_floral"
FIND = ('var caches = (System.Collections.Generic.IDictionary<string, System.Collections.Generic.List<object>>)typeof(Game.ContentManager).GetField("Caches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null); '
        'Engine.Graphics.Texture2D Tex(string k) { if (!caches.TryGetValue(k, out var l)) return null; foreach (var o in l) if (o is Engine.Graphics.Texture2D t) return t; return null; } ')
STATE = FIND + ('var t = Tex($s0); return System.FormattableString.Invariant($"texture={(t != null)} tag={(t?.Tag != null)} disposed={t?.m_isDisposed} released={Game.ScTextureResidency.ReleasedImages} '
                'restored={Game.ScTextureResidency.Restored} restoreFailures={Game.ScTextureResidency.RestoreFailures} mode={Game.ScLazyContent.Mode} lazy={Game.ScLazyContent.StreamCount} '
                'failures={Game.ScLazyContent.Failures} loads={Game.ScLazyContent.Loads} releases={Game.ScLazyContent.Releases} held={Game.ScLazyContent.MaterializedBytes >> 20}MiB");')
# Every CS texture emptied the way a device reset leaves it (GL texture deleted and allocated again without pixels); the
# engine's own textures are left alone so the frame stays readable.
EMPTY = FIND + ('var seen = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance); int n = 0; '
                'foreach (var kv in caches.ToArray()) { if (!kv.Key.StartsWith("Textures/ScCsgoKnives/") && !kv.Key.StartsWith("Textures/ScCsgoTactical/")) continue; '
                '  foreach (var o in kv.Value.ToArray()) if (o is Engine.Graphics.Texture2D t && t.MipLevelsCount == 1 && !t.m_isDisposed && seen.Add(t)) { t.HandleDeviceLost(); t.HandleDeviceReset(); n++; } } '
                'return "emptied " + n + " CS textures";')
RESTORE = 'typeof(Game.ScTextureResidency).GetMethod("Restore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null); return Game.ScTextureResidency.Restored.ToString();'
EVENT = ('var f = typeof(Engine.Window).GetField($s0, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); '
         'var a = f?.GetValue(null) as System.Action; if (a == null) return "no handlers"; a(); return a.GetInvocationList().Length + " handlers";')
DAY = PROJECT + 'project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.TimeOfDayMode = Game.TimeOfDayMode.Day; return "day";'
# A still scene for frame comparisons: invulnerable (r7b run 1: damage tint and knock-back between A and A2 made the frames
# differ by 27 on their own), 25 blocks up in open air, flying, no velocity.
POSE = MAIN + ('pl.ComponentHealth.IsInvulnerable = true; pl.ComponentHealth.Heal(1f); pl.ComponentLocomotion.IsCreativeFlyEnabled = true; pl.ComponentBody.IsGravityEnabled = false; '
               'pl.ComponentBody.Position = pl.ComponentBody.Position + new Engine.Vector3(0f, 25f, 0f); pl.ComponentBody.Velocity = Engine.Vector3.Zero; '
               'pl.GameWidget.ActiveCamera = pl.GameWidget.FindCamera<Game.FppCamera>(); pl.ComponentLocomotion.LookAngles = new Engine.Vector2(0f, -0.35f); return "ok";')
SHOT = MAIN + ('var size = Engine.Window.Size; Game.ScreenCaptureManager.Capture(size.X, size.Y, $s0); '
               'return Engine.Storage.GetSystemPath(Engine.Storage.CombinePaths(Game.ScreenCaptureManager.ScreenshotDir, $s0));')
# A skin texture member that cannot be read: its lazy source entry with a wrong CRC (as if the package were changed on disk).
def break_member(path): return (
    'var resources = (System.Collections.IDictionary)typeof(Game.ContentManager).GetField("Resources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null); '
    f'var info = new[] {{ ".png", ".webp" }}.Select(x => resources["{path}" + x] as Game.ContentInfo).FirstOrDefault(i => i != null); if (info == null) return "missing"; if (info.ContentStream is not Game.ScLazyContentStream lazy) return "not lazy: " + info.ContentStream?.GetType().Name; '
    'var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; var src = (Game.ScPackageSource)typeof(Game.ScLazyContentStream).GetField("m_source", F).GetValue(lazy); '
    'var e = (Game.ZipArchiveEntry)typeof(Game.ScLazyContentStream).GetField("m_entry", F).GetValue(lazy); '
    'var bad = new Game.ZipArchiveEntry { Method = e.Method, FilenameInZip = e.FilenameInZip, FileSize = e.FileSize, CompressedSize = e.CompressedSize, HeaderOffset = e.HeaderOffset, FileOffset = e.FileOffset, Crc32 = e.Crc32 ^ 1u }; '
    'info.ContentStream = new Game.ScLazyContentStream(src, bad, lazy.ResourcePath); return "broken";')
def skin_gun(gun, paint): return MAIN + (
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{gun}"); var spec = Game.GunSpec.All[v]; '
    f'int id = Game.ScGunRegistry.Current.Allocate(v, spec.Magazine, false, Game.ScGunDurability.Full(v), -1, {paint}); '
    'return Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)).ToString();')
MATERIAL = 'Game.ScGunVisualMaterial.Load($s0, $i1, out var m); return m;'
def fast(values): return MAIN + (
    f'var vals = new[] {{ {", ".join(values)} }}; var inv = pl.ComponentMiner.Inventory; int frame = 0; System.Action h = null; '
    'h = () => { frame++; if (frame % 2 == 0) { int v = vals[(frame / 2) % vals.Length]; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); inv.AddSlotItems(0, v, 1); inv.ActiveSlotIndex = 0; } '
    'if (frame >= 90) { Engine.Window.Frame -= h; System.AppDomain.CurrentDomain.SetData("sc.fast.done", "yes"); } }; Engine.Window.Frame += h; return "installed";')
FAST_DONE = 'return System.Convert.ToString(System.AppDomain.CurrentDomain.GetData("sc.fast.done")) ?? "";'
PREPARE = ('var skins = Game.ScGunSkinCatalog.Available.Skip(10).Take(6).ToArray(); foreach (var s in skins) Game.ScTexturePreparation.Request(s.Material); '
           'var started = Game.ScTexturePreparation.PendingCount; Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("MainMenu"); '
           'return started + " pending; " + string.Join(",", skins.Select(s => s.Gun + ":" + s.PaintId + ":" + s.Material));')


def main(label, *packages):
    import numpy as np
    from PIL import Image
    def resolve(p):
        if p.startswith(("stage-lite:", "stage-agents:", "stage-full:")):
            kind, tag = p.split(":", 1); suffix = {"stage-lite": "轻量包.scmod", "stage-agents": "探员包.scmod", "stage-full": "全量包.scmod"}[kind]
            return sorted((m0.ROOT / ".tmp/completion-140-20260929" / tag / "candidate").glob("*" + suffix))[0]
        return {"output-lite": m0.LITE, "output-full": m0.FULL, "output-agents": m0.LITE.with_name("[API1.9]CS武器1.4.0-探员包.scmod")}.get(p) or Path(p)
    pkgs = [resolve(p) for p in packages]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"residency-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"residency-{label}", "packages": {p.name: sha(p) for p in pkgs}, "residencyMode": os.environ.get("SCCS_RESIDENCY", "(default)"), "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:400]}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:400], flush=True)
    def state(): return step("state", g.func(m0.Call(STATE, TEX)))
    def shot(name):
        p = Path(g.func(m0.Call(SHOT, name + ".png"))); time.sleep(1.2)
        dst = case_dir / (name + ".png"); dst.write_bytes(p.read_bytes()); return dst
    def region(path):
        a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32); h, w = a.shape[:2]
        return a[h // 2:, w // 2:]   # first-person weapon and hands
    def diff(p, q): return float(np.abs(region(p) - region(q)).mean())
    def lines(tag, since=0):
        with g.lock: return [l for l in g.lines[since:] if tag in l]
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info()
        to_menu(g); time.sleep(4)
        inst = lines("[CS_MEM] residency"); step("residency lines", inst)
        reports = lines("[CS_MEM] stage=install")
        check("1 install: members replaced and the package copies released, install report written",
              any("now read from" in l for l in inst) and any("residency: mode=" in l for l in inst) and reports, (inst[-1:] + reports[:1]))
        m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(DAY); g.func(POSE); time.sleep(2)
        ak = g.func(skin_gun("ak47", 724)); g.func(hold(ak)); time.sleep(4)
        step("AK skin material", g.func(m0.Call(MATERIAL, "ak47", 724)))
        s = state()
        check("2 the held AK skin texture is on the GPU and its decoded copy was released", "texture=True" in s and "tag=False" in s and "disposed=False" in s, s)
        a = shot("A-held"); a2 = shot("A2-held")
        noise = diff(a, a2); step("noise A/A2", round(noise, 3))
        step("empty CS textures", g.func(EMPTY)); time.sleep(1.5); c = shot("C-emptied")
        step("restore", g.func(RESTORE)); time.sleep(1.5); d = shot("D-restored")
        dc, dd = diff(a, c), diff(a, d)
        s = state()
        check("3 device-reset safety net: the emptied texture differs (C), the restored one matches the reference (D) like two frames do",
              dc > max(4 * noise, 2.0) and dd <= max(2 * noise, 1.0) and "restoreFailures=0" in s, f"A/C {dc:.2f}, A/D {dd:.2f}, noise {noise:.2f}; {s}")
        m = g.mark(); step("deactivated", g.func(m0.Call(EVENT, "Deactivated"))); time.sleep(1); step("activated", g.func(m0.Call(EVENT, "Activated"))); time.sleep(2)
        e = shot("E-resumed"); bg = lines("[CS_MEM] stage=background", m) + lines("[CS_MEM] stage=resume", m)
        check("4 background/resume (engine events): [CS_MEM] lines for both, the texture intact afterwards", len(bg) >= 2 and diff(a, e) <= max(2 * noise, 1.0), f"A/E {diff(a, e):.2f}; {[l[-160:] for l in bg]}")
        m = g.mark(); step("break member", g.func(break_member("Textures/ScCsgoKnives/ak47_hd__am_bamboo_jungle")))
        bamboo = g.func(skin_gun("ak47", 456)); g.func(hold(bamboo)); time.sleep(3)
        material = g.func(m0.Call(MATERIAL, "ak47", 456))
        warned = lines("could not be read again", m); errors_now = [l for l in g.errors() if "bamboo" in l]
        check("5 decode failure: the unreadable skin falls back to the factory finish, logged, nothing thrown to the engine",
              material == "ak47_hd" and warned and not errors_now, f"material {material}; {[l[-200:] for l in warned[:1]]}; errors {errors_now[:1]}")
        items = [x.rsplit(":", 1)[1] for x in g.func(ITEMS).split(",")][:8]
        step("fast", g.func(fast(items))); poll(g, FAST_DONE, lambda v: v == "yes", 30)
        g.func(hold(ak)); time.sleep(3); f = shot("F-after-fast")
        check("6 fast switching of 8 guns/skins/knives every 2 frames: no errors, the AK texture right afterwards", not g.errors() and diff(a, f) <= max(3 * noise, 2.0), f"A/F {diff(a, f):.2f}")
        prep = step("prepare and leave", g.func(PREPARE)); g.wait('Entered screen "MainMenu"', 120); time.sleep(2)
        step("re-enter", g.func(REENTER)); g.wait('Entered screen "Game"', 600); poll(g, PLAYER_SPAWNED, lambda v: v == "True", 240)
        g.func(KEEP_ACTIVE); g.func(DAY); g.func(POSE)
        gun, paint, material = prep.split("; ", 1)[1].split(",")[0].split(":")
        v = g.func(skin_gun(gun, int(paint))); g.func(hold(v)); time.sleep(4)
        s2 = step("prepared skin", g.func(m0.Call(STATE, "Textures/ScCsgoKnives/" + material)))
        check("7 exit during background texture preparation, re-enter: the prepared skin loads, no errors", "texture=True" in s2 and not g.errors(), f"{prep[:120]}; {s2}")
        R["memLines"] = [l[-700:] for l in lines("[CS_MEM]")]
        R["gameErrors"] = g.errors()
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            R.setdefault("gameErrors", g.errors()); g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c for c in R["checks"] if not c["ok"]]
    out = RESULTS / f"residency-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed; errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
