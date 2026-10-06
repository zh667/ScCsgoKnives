"""Resource-memory baseline and comparison (2026-10-01 memory round, ad27 step 1 and 4).

One game session on the isolated 1.9.3.1 copy (M0_ENGINE=131) per call, the same fixed scenario for every package set:
  menu -> new world -> first gun -> a fixed set of guns / skins / knives (round 1, first use) -> the same set again
  (round 2, repeat) -> CT and T agents appear (when the agents' code is loaded) -> exit to the menu -> re-enter the same
  world -> the first gun again.
At every stage the measurement below is taken twice: as it is, and after a measurement-only full GC (the product never
forces one; the second reading separates objects that are still held from garbage the GC has not collected yet).

What is measured (one code path for the packages before and after the change, reflection only, no product hooks):
  streams   ContentManager.Resources entries that come from the CS packages' Assets: count, logical bytes, the bytes the
            stream actually holds (MemoryStream capacity, or a lazy stream's materialized bytes), per category;
  decoded   Engine.Media.Image objects held for CS paths in ContentManager.Caches or as a cached Texture2D's Tag
            (width x height x 4, exact for Rgba32; each object counted once);
  gpu       CS Texture2D objects in the caches (the engine's own size formula, driver overhead not included), all graphics
            resources (Display.GetGpuMemoryUsage, the whole game), CS model vertex/index buffers;
  caches    the mod's own cache counts (ScResourceCaches), texture preparation state, lazy-source state when present;
  process   GC heap / committed / fragmented, working set, private bytes, peak working set (Windows process counters).
Switch timing: the longest frame and the number of frames over 50 ms in the 1.5 s after each switch, plus the mod's own
[CS_RESOURCE] cold-operation lines in that window.

Usage: sp_memory.py <label> <package> [<package> ...]   packages: output-full, output-lite, output-agents, stage-full:<tag>,
       stage-lite:<tag>, stage-agents:<tag>, or a path.
"""
import json, re, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu, PLAYER_SPAWNED, poll
from mp_m1 import MAIN, KEEP_ACTIVE, PROJECT

MEASURE = r'''
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var cmT = typeof(Game.ContentManager);
var resources = (System.Collections.IDictionary)cmT.GetField("Resources", F).GetValue(null);
var caches = (System.Collections.IDictionary)cmT.GetField("Caches", F).GetValue(null);
var own = new System.Collections.Generic.Dictionary<string, Game.ZipArchiveEntry>();
foreach (var m in ModsManager.ModList) { if (m.modInfo == null || !m.modInfo.PackageName.StartsWith("zh667.")) continue;
  foreach (var kv in m.ModFiles) if (kv.Key.StartsWith("Assets/")) own[kv.Key.Substring(7)] = kv.Value; }
string Cat(string p) { var x = System.IO.Path.GetExtension(p).ToLowerInvariant();
  if (x == ".png" || x == ".webp" || x == ".jpg" || x == ".jpeg" || x == ".astc" || x == ".astcsrgb") return "tex";
  if (p.StartsWith("Animations/") || x == ".scanim") return "anim";
  if (p.StartsWith("Models/") || x == ".dae" || x == ".glb" || x == ".gltf" || x == ".obj" || x == ".scmesh") return "model";
  if (x == ".ogg" || x == ".wav" || x == ".mp3" || x == ".flac") return "audio"; return "other"; }
var cats = new System.Collections.Generic.Dictionary<string, long[]>();
int lazy = 0, missing = 0, closed = 0; var closedNames = new System.Collections.Generic.List<string>();
foreach (var kv in own) {
  var info = resources.Contains(kv.Key) ? resources[kv.Key] as Game.ContentInfo : null;
  if (info == null || info.ContentStream == null) { missing++; continue; }
  var ms = info.ContentStream; long held; var lp = ms.GetType().GetProperty("MaterializedBytes");
  if (lp != null) { held = (long)lp.GetValue(ms); lazy++; }
  else if (ms.CanRead) held = ms.Capacity;
  else { closed++; if (closedNames.Count < 12) closedNames.Add(kv.Key); try { held = ms.GetBuffer().Length; } catch { held = 0; } }
  var c = Cat(kv.Key); if (!cats.TryGetValue(c, out var a)) cats[c] = a = new long[4];
  a[0]++; a[1] += kv.Value.FileSize; a[2] += held; if (!ms.CanRead && lp == null) a[3]++; }
var stems = new System.Collections.Generic.HashSet<string>();
foreach (var k in own.Keys) { int i = k.LastIndexOf('.'); stems.Add(i > 0 ? k.Substring(0, i) : k); }
var images = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
var textures = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
var models = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
int other = 0;
foreach (System.Collections.DictionaryEntry e in caches) { var key = (string)e.Key; if (!own.ContainsKey(key) && !stems.Contains(key)) continue;
  foreach (var o in (System.Collections.Generic.List<object>)e.Value) {
    if (o is Engine.Media.Image) images.Add(o); else if (o is Engine.Graphics.Texture2D) textures.Add(o); else if (o is Engine.Graphics.Model) models.Add(o); else if (o != null) other++; } }
long imgB = 0, tagOnlyB = 0, texB = 0, modelB = 0; int tagOnly = 0, tagged = 0;
foreach (Engine.Media.Image im in images) imgB += (long)im.Width * im.Height * 4;
foreach (Engine.Graphics.Texture2D t in textures) { texB += t.GetGpuMemoryUsage();
  if (t.Tag is Engine.Media.Image ti) { tagged++; if (!images.Contains(ti)) { tagOnly++; tagOnlyB += (long)ti.Width * ti.Height * 4; } } }
foreach (Engine.Graphics.Model md in models) foreach (var mesh in md.Meshes) foreach (var part in mesh.MeshParts) {
  if (part.VertexBuffer != null) modelB += part.VertexBuffer.GetGpuMemoryUsage(); if (part.IndexBuffer != null) modelB += part.IndexBuffer.GetGpuMemoryUsage(); }
string Static(string type, string member) { var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(type)).FirstOrDefault(x => x != null); if (t == null) return null;
  var p = t.GetProperty(member, F); if (p != null) return System.Convert.ToString(p.GetValue(null), System.Globalization.CultureInfo.InvariantCulture);
  var f = t.GetField(member, F); return f == null ? null : System.Convert.ToString(f.GetValue(null), System.Globalization.CultureInfo.InvariantCulture); }
long Held(System.IO.Stream z) { if (z is System.IO.MemoryStream mz) { if (mz.GetType().GetProperty("MaterializedBytes") is var q && q != null) return (long)q.GetValue(mz); if (mz.CanRead) return mz.Capacity; try { return mz.GetBuffer().Length; } catch { return 0; } } return 0; }
var archives = ModsManager.ModListAll.Where(m => m.modInfo != null && m.modInfo.PackageName.StartsWith("zh667.")).Select(m => new { pkg = m.modInfo.PackageName, disabled = m.IsDisabled,
  type = m.ModArchive?.ZipFileStream?.GetType().Name, held = Held(m.ModArchive?.ZipFileStream), file = m.Size,
  dlls = m.ModFiles.Where(f => f.Key.EndsWith(".dll")).Sum(f => (long)f.Value.FileSize) }).ToList();
long otherArchives = ModsManager.ModListAll.Where(m => m.modInfo == null || !m.modInfo.PackageName.StartsWith("zh667.")).Sum(m => Held(m.ModArchive?.ZipFileStream));
var counts = Game.ScResourceCaches.Counts();
var proc = System.Diagnostics.Process.GetCurrentProcess(); proc.Refresh(); var gi = System.GC.GetGCMemoryInfo();
return System.Text.Json.JsonSerializer.Serialize(new {
  streams = cats.ToDictionary(k => k.Key, k => new { n = k.Value[0], bytes = k.Value[1], held = k.Value[2], closed = k.Value[3] }), lazy, missing, closed, closedNames,
  decoded = new { cacheImages = images.Count, cacheImageBytes = imgB, tagOnly, tagOnlyBytes = tagOnlyB, texturesWithTag = tagged },
  gpu = new { csTextures = textures.Count, csTextureBytes = texB, csModels = models.Count, csModelBufferBytes = modelB, allResources = Engine.Graphics.GraphicsResource.m_resources.Count, allBytes = Engine.Graphics.Display.GetGpuMemoryUsage() },
  caches = counts, otherCached = other, archives, otherArchives,
  prep = new { pending = Static("Game.ScTexturePreparation", "PendingCount"), reserved = Static("Game.ScTexturePreparation", "reserved") },
  lazySource = new { materialized = Static("Game.ScLazyContent", "MaterializedBytes"), peak = Static("Game.ScLazyContent", "PeakMaterializedBytes"), loads = Static("Game.ScLazyContent", "Loads"), releases = Static("Game.ScLazyContent", "Releases"), streams = Static("Game.ScLazyContent", "StreamCount") },
  texRelease = new { released = Static("Game.ScTextureResidency", "ReleasedImages"), releasedBytes = Static("Game.ScTextureResidency", "ReleasedBytes"), restored = Static("Game.ScTextureResidency", "Restored") },
  process = new { gcTotal = System.GC.GetTotalMemory(false), heap = gi.HeapSizeBytes, committed = gi.TotalCommittedBytes, fragmented = gi.FragmentedBytes,
    workingSet = proc.WorkingSet64, privateBytes = proc.PrivateMemorySize64, peakWorkingSet = proc.PeakWorkingSet64, gen2 = System.GC.CollectionCount(2), allocated = System.GC.GetTotalAllocatedBytes(false) } });
'''
MEASURE = MEASURE.replace("\n", " ")
# The mod's own caches (ScResourceCaches): reflection deep size of the cached values (arrays exact by element size, objects
# estimated at 8 B per field + 24 B header); GPU buffers they own counted separately; engine models/images/textures excluded.
CACHE_BYTES = r'''var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public; var I = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public; var seen = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance); long gpu = 0; long Size(object o, int depth) {   if (o == null || depth > 8 || !seen.Add(o)) return 0;   var t = o.GetType();   if (o is string s) return 24 + 2L * s.Length;   if (o is Engine.Graphics.GraphicsResource gr) { gpu += gr.GetGpuMemoryUsage(); return 0; }   if (o is Engine.Graphics.Model || o is Engine.Media.Image || o is GameEntitySystem.Project || o is GameEntitySystem.Entity || o is GameEntitySystem.Subsystem || o is System.Type || o is System.Delegate) return 0;   if (t.IsArray) {     var a = (System.Array)o; var et = t.GetElementType(); long n = a.LongLength;     if (et.IsPrimitive || et.IsEnum) return 32 + n * System.Runtime.InteropServices.Marshal.SizeOf(et.IsEnum ? System.Enum.GetUnderlyingType(et) : et);     if (et.IsValueType) { long one; try { one = System.Runtime.InteropServices.Marshal.SizeOf(et); } catch { one = 16; } long sum = 32 + n * one;       if (et.GetFields(I).Any(f => !f.FieldType.IsValueType)) foreach (var e in a) foreach (var f in et.GetFields(I)) if (!f.FieldType.IsValueType) sum += Size(f.GetValue(e), depth + 1);       return sum; }     long r = 32 + n * 8; foreach (var e in a) r += Size(e, depth + 1); return r;   }   long total = 24;   for (var tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)     foreach (var f in tt.GetFields(I | System.Reflection.BindingFlags.DeclaredOnly)) {       if (f.FieldType.IsPrimitive || f.FieldType.IsEnum) { total += 8; continue; }       var v = f.GetValue(o);       if (f.FieldType.IsValueType) { total += 16; if (v != null && f.FieldType.GetFields(I).Any(x => !x.FieldType.IsValueType)) foreach (var x in f.FieldType.GetFields(I)) if (!x.FieldType.IsValueType) total += Size(x.GetValue(v), depth + 1); continue; }       total += 8 + Size(v, depth + 1);     }   return total; } var list = (System.Collections.IList)typeof(Game.ScResourceCaches).GetField("Caches", F).GetValue(null); var result = new System.Collections.Generic.Dictionary<string, object>(); foreach (var w in list) {   var wr = w.GetType().GetMethod("TryGetTarget"); var args = new object[1]; if (!(bool)wr.Invoke(w, args) || args[0] == null) continue;   var cache = args[0]; var name = (string)cache.GetType().GetProperty("Name").GetValue(cache);   var entries = (System.Collections.IDictionary)cache.GetType().GetField("m_entries", I).GetValue(cache);   long before = gpu, bytes = 0; int n = 0;   foreach (System.Collections.DictionaryEntry e in entries) { var node = e.Value; var entry = node.GetType().GetProperty("Value").GetValue(node); var value = entry.GetType().GetProperty("Value").GetValue(entry); bytes += Size(value, 0); n++; }   result[name] = new { n, cpu = bytes, gpu = gpu - before }; } return System.Text.Json.JsonSerializer.Serialize(result); '''
# Ownership view: deep size held by every static field of the CS assemblies (types named Game.* in assemblies ScCsgo*), ranked;
# engine models/images/textures/entities are not followed (counted elsewhere); plus what the engine holds for other mods and
# the base game (ContentManager streams of non-CS members, cached Image/Texture2D/Model/SoundBuffer of non-CS keys).
STATIC_BYTES = r"""
var I = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var SF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly;
var seen = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
long gpu = 0;
long Size(object o, int depth) {
  if (o == null || depth > 12 || !seen.Add(o)) return 0;
  var t = o.GetType();
  if (o is string s) return 24 + 2L * s.Length;
  if (o is Engine.Graphics.GraphicsResource gr) { gpu += gr.GetGpuMemoryUsage(); return 0; }
  if (o is Engine.Graphics.Model || o is Engine.Media.Image || o is GameEntitySystem.Project || o is GameEntitySystem.Entity || o is GameEntitySystem.Subsystem || o is GameEntitySystem.Component || o is System.Type || o is System.Delegate || o is System.Reflection.MemberInfo || o is System.Reflection.Assembly || o is Game.ContentInfo) return 0;
  if (t.IsArray) { var a = (System.Array)o; var et = t.GetElementType(); long n = a.LongLength;
    if (et.IsPointer || et.IsFunctionPointer || et == typeof(System.IntPtr) || et == typeof(System.UIntPtr)) return 32 + n * 8;
    if (et.IsPrimitive || et.IsEnum) { try { return 32 + n * System.Runtime.InteropServices.Marshal.SizeOf(et.IsEnum ? System.Enum.GetUnderlyingType(et) : et); } catch { return 32 + n * 8; } }
    if (et.IsValueType) { long one; try { one = System.Runtime.InteropServices.Marshal.SizeOf(et); } catch { one = 16; } long sum = 32 + n * one;
      if (n < 200000 && et.GetFields(I).Any(f => !f.FieldType.IsValueType)) foreach (var e in a) foreach (var f in et.GetFields(I)) if (!f.FieldType.IsValueType) sum += Size(f.GetValue(e), depth + 1);
      return sum; }
    long r = 32 + n * 8; foreach (var e in a) r += Size(e, depth + 1); return r; }
  long total = 24;
  for (var tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)
    foreach (var f in tt.GetFields(I | System.Reflection.BindingFlags.DeclaredOnly)) {
      if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType.IsPointer) { total += 8; continue; }
      object v; try { v = f.GetValue(o); } catch { continue; }
      if (f.FieldType.IsValueType) { total += 16; continue; }
      total += 8 + Size(v, depth + 1); }
  return total; }
var rows = new System.Collections.Generic.List<(string, long, long)>();
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var engineCaches = typeof(Game.ContentManager).GetField("Caches", F).GetValue(null);
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies().Where(a => (a.GetName().Name ?? "").StartsWith("ScCsgo") && a.GetName().Name != "ScCsgoResourceCodec")) {
  System.Type[] types; try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
  foreach (var t in types) { if (t.IsGenericTypeDefinition) continue;
    foreach (var f in t.GetFields(SF)) { if (f.IsLiteral || f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType.IsPointer) continue; object v; try { v = f.GetValue(null); } catch { continue; }
      if (object.ReferenceEquals(v, engineCaches)) continue;
      long g0 = gpu, b; try { b = Size(v, 0); } catch { continue; } if (b > 256 * 1024 || gpu - g0 > 256 * 1024) rows.Add((t.Name + "." + f.Name, b, gpu - g0)); } } }
var resources = (System.Collections.IDictionary)typeof(Game.ContentManager).GetField("Resources", F).GetValue(null);
var caches = (System.Collections.IDictionary)typeof(Game.ContentManager).GetField("Caches", F).GetValue(null);
var own = new System.Collections.Generic.HashSet<string>();
foreach (var m in ModsManager.ModList) if (m.modInfo != null && m.modInfo.PackageName.StartsWith("zh667.")) foreach (var k in m.ModFiles.Keys) if (k.StartsWith("Assets/")) { var p = k.Substring(7); own.Add(p); int i = p.LastIndexOf('.'); if (i > 0) own.Add(p.Substring(0, i)); }
long otherStreams = 0; int otherN = 0;
foreach (System.Collections.DictionaryEntry e in resources) { var key = (string)e.Key; if (own.Contains(key)) continue; var ms = ((Game.ContentInfo)e.Value).ContentStream; if (ms == null) continue; otherN++;
  try { otherStreams += ms.CanRead ? ms.Capacity : ms.GetBuffer().Length; } catch { } }
long otherImages = 0, otherTex = 0; int otherModels = 0, otherSounds = 0; var seen2 = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
foreach (System.Collections.DictionaryEntry e in caches) { if (own.Contains((string)e.Key)) continue;
  foreach (var o in (System.Collections.Generic.List<object>)e.Value) { if (o == null || !seen2.Add(o)) continue;
    if (o is Engine.Media.Image im) otherImages += (long)im.Width * im.Height * 4; else if (o is Engine.Graphics.Texture2D tx) otherTex += tx.GetGpuMemoryUsage(); else if (o is Engine.Graphics.Model) otherModels++; else if (o is Engine.Audio.SoundBuffer) otherSounds++; } }
return System.Text.Json.JsonSerializer.Serialize(new { statics = rows.OrderByDescending(r => r.Item2 + r.Item3).Take(25).Select(r => new { field = r.Item1, cpu = r.Item2, gpu = r.Item3 }),
  other = new { streams = otherN, streamBytes = otherStreams, imageBytes = otherImages, textureGpu = otherTex, models = otherModels, sounds = otherSounds } });
""".replace("\n", " ")
FULL_GC = 'System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect(); '
# Items: guns with the first skins of the catalogue, factory guns, the first known knives. Created once (gun records in the
# world's registry), kept in AppDomain data and reused in round 2 and after re-entering.
ITEMS = MAIN + r'''
var vals = new System.Collections.Generic.List<string>(); int gunBlock = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true);
int Gun(string name, int skin) { int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == name); if (v < 0) return -1; var spec = Game.GunSpec.All[v];
  int id = Game.ScGunRegistry.Current.Allocate(v, spec.Magazine, false, Game.ScGunDurability.Full(v), -1, skin);
  return Game.Terrain.MakeBlockValue(gunBlock, 0, Game.GunSpec.WithId(v, id)); }
foreach (var name in new[] { "ak47", "awp", "m4a1s", "deagle", "m4a4", "glock18" }) { int x = Gun(name, 0); if (x >= 0) vals.Add(name + ":" + x); }
foreach (var s in Game.ScGunSkinCatalog.Available.Take(10)) { int x = Gun(s.Gun, s.PaintId); if (x >= 0) vals.Add(s.Gun + "/" + s.Key + ":" + x); }
int knifeBlock = Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true); int knives = 0;
for (int k = 0; k < Game.CsmcKnifeRig.KnifeCount && knives < 3; k++) { int x = Game.Terrain.MakeBlockValue(knifeBlock, 0, k); if (Game.ScKnifeBlock.IsKnown(x)) { vals.Add("knife-" + Game.CsmcKnifeRig.GetAssetName(k) + ":" + x); knives++; } }
return string.Join(",", vals);'''.replace("\n", " ")
def hold(value): return MAIN + (
    'var l = new System.Collections.Generic.List<double>(); var d = System.AppDomain.CurrentDomain; var old = d.GetData("mem.handler") as System.Action; if (old != null) Engine.Window.Frame -= old; '
    'd.SetData("mem.frames", l); var sw = System.Diagnostics.Stopwatch.StartNew(); System.Action h = null; '
    'h = () => { l.Add(sw.Elapsed.TotalMilliseconds); sw.Restart(); if (l.Count >= 900) Engine.Window.Frame -= h; }; d.SetData("mem.handler", h); Engine.Window.Frame += h; '
    f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); inv.AddSlotItems(0, {value}, 1); inv.ActiveSlotIndex = 0; return "ok";')
FRAMES = ('var d = System.AppDomain.CurrentDomain; if (d.GetData("mem.handler") is System.Action h) Engine.Window.Frame -= h; d.SetData("mem.handler", null); '
          'var l = (System.Collections.Generic.List<double>)d.GetData("mem.frames"); if (l == null || l.Count < 2) return "{}"; var f = l.Skip(1).ToList(); '
          'return System.Text.Json.JsonSerializer.Serialize(new { n = f.Count, max = System.Math.Round(f.Max(), 1), over50 = f.Count(x => x > 50), over100 = f.Count(x => x > 100), sum = System.Math.Round(f.Sum(), 0) });')
EXIT_WORLD = 'Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("MainMenu"); return "left";'
REENTER = ('Game.WorldsManager.UpdateWorldsList(); var wi = Game.WorldsManager.WorldInfos.OrderByDescending(w => w.LastSaveTime).First(); '
           'Game.ScreensManager.SwitchScreen("GameLoading", wi, null); return wi.DirectoryName;')
HAS_AGENTS = 'return Game.GameManager.Project.Subsystems.Any(s => s.GetType().Name == "SubsystemTacticalEnemies").ToString();'
# In front of the player's view (drawn, so their models, textures and animations are really used), 4-5 m away, side by side.
AGENTS = MAIN + (
    'var made = new System.Collections.Generic.List<string>(); var at = pl.ComponentBody.Position; int i = 0; '
    'var fwd = Engine.Matrix.CreateFromQuaternion(pl.ComponentCreatureModel.EyeRotation).Forward; fwd.Y = 0f; fwd = Engine.Vector3.Normalize(fwd); var side = new Engine.Vector3(-fwd.Z, 0f, fwd.X); '
    'foreach (var n in new[] { "ScTacticalCT", "ScTacticalT" }) { try { var e = Game.DatabaseManager.CreateEntity(project, n, true); '
    'var b = e.FindComponent<Game.ComponentBody>(true); b.Position = at + fwd * 4.5f + side * (i++ == 0 ? -1.2f : 1.2f); '
    'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) { sp.SpawnDuration = 0f; sp.AutoDespawn = false; } project.AddEntity(e); made.Add(n + "#" + e.Id); } '
    'catch (System.Exception ex) { made.Add(n + " failed: " + ex.GetType().Name + " " + ex.Message); } } return string.Join(", ", made);')


def main(label, *packages):
    def resolve(p):
        if p.startswith(("stage-lite:", "stage-agents:", "stage-full:")):
            kind, tag = p.split(":", 1); suffix = {"stage-lite": "轻量包.scmod", "stage-agents": "探员包.scmod", "stage-full": "全量包.scmod"}[kind]
            found = sorted((m0.ROOT / ".tmp/completion-140-20260929" / tag / "candidate").glob("*" + suffix))
            if not found: raise FileNotFoundError(f"no {suffix} candidate in stage {tag}")
            return found[0]
        return {"output-lite": m0.LITE, "output-full": m0.FULL, "output-agents": m0.AGENTS}.get(p) or Path(p)
    pkgs = [resolve(p) for p in packages]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"memory-{label}-{stamp}"; case_dir.mkdir(parents=True)
    import os
    R = {"case": f"memory-{label}", "packages": {p.name: sha(p) for p in pkgs}, "residencyMode": os.environ.get("SCCS_RESIDENCY", "(default)"), "stages": [], "switches": [], "steps": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:300]}", flush=True); return value
    def measure(stage):
        now = json.loads(g.func(MEASURE, timeout=120)); after = json.loads(g.func(FULL_GC + MEASURE, timeout=120))
        caches = json.loads(g.func(CACHE_BYTES, timeout=120)) if stage in ("round2", "agents", "reenter") else None
        statics = json.loads(g.func(STATIC_BYTES, timeout=180)) if stage in ("menu", "agents") else None
        R["stages"].append({"stage": stage, "t": round(time.time() - T0, 1), "now": now, "afterGc": after, "cacheBytes": caches, "statics": statics})
        if statics: print("  statics: " + ", ".join(f"{r['field']} {r['cpu'] >> 20}/{r['gpu'] >> 20} MiB" for r in statics["statics"][:10]) + f" | other: {json.dumps(statics['other'])}", flush=True)
        if caches: print(f"  caches: " + ", ".join(f"{k} {v['n']}/{v['cpu'] >> 20} MiB cpu/{v['gpu'] >> 20} MiB gpu" for k, v in caches.items()), flush=True)
        s = lambda d: sum(v["held"] for v in d["streams"].values()) >> 20
        arch = lambda d: sum(a["held"] for a in d.get("archives") or []) >> 20
        print(f"[{round(time.time() - T0, 1)}] MEM {stage}: package copies {arch(now)} MiB, streams held {s(now)} MiB (after GC {s(after)}), decoded {(now['decoded']['cacheImageBytes'] + now['decoded']['tagOnlyBytes']) >> 20} MiB, "
              f"cs gpu {now['gpu']['csTextureBytes'] >> 20} MiB, heap {now['process']['heap'] >> 20}/{after['process']['heap'] >> 20} MiB, "
              f"private {now['process']['privateBytes'] >> 20}/{after['process']['privateBytes'] >> 20} MiB, ws {now['process']['workingSet'] >> 20} MiB, peak ws {now['process']['peakWorkingSet'] >> 20} MiB", flush=True)
    def switch(round_, name, value):
        m = g.mark(); g.func(hold(value)); time.sleep(1.5); frames = json.loads(g.func(FRAMES))
        with g.lock: cold = [l.split("[CS_RESOURCE]", 1)[1].strip()[:160] for l in g.lines[m:] if "[CS_RESOURCE]" in l]
        R["switches"].append({"round": round_, "item": name, "frames": frames, "cold": cold})
        print(f"  {round_} {name}: max {frames.get('max')} ms, >50ms {frames.get('over50')}, cold {len(cold)}", flush=True)
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info()
        t = time.time(); to_menu(g); R["startupSeconds"] = round(time.time() - t, 1); time.sleep(5)
        step("engine", g.func('return System.Reflection.Assembly.GetAssembly(typeof(Game.GameManager)).GetName().Version.ToString() + " | " + string.Join(", ", ModsManager.ModList.Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));'))
        measure("menu")
        m0.enter_world(g); g.func(KEEP_ACTIVE); time.sleep(5)
        measure("world")
        items = [tuple(x.rsplit(":", 1)) for x in step("items", g.func(ITEMS)).split(",")]
        switch(0, items[0][0], items[0][1]); measure("first-gun")
        for name, value in items: switch(1, name, value)
        measure("round1")
        for name, value in items: switch(2, name, value)
        measure("round2")
        if g.func(HAS_AGENTS) == "True":
            step("agents", g.func(AGENTS)); time.sleep(4)
            measure("agents")
        step("exit", g.func(EXIT_WORLD)); g.wait('Entered screen "MainMenu"', 120); time.sleep(4)
        measure("exit")
        step("re-enter", g.func(REENTER)); g.wait('Entered screen "Game"', 600)
        poll(g, PLAYER_SPAWNED, lambda v: v == "True", 240); g.func(KEEP_ACTIVE); time.sleep(4)
        switch(3, items[0][0], items[0][1]); measure("reenter")
        R["gameErrors"] = g.errors()
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            R.setdefault("gameErrors", g.errors())
            with g.lock: R["csLines"] = [l[:900] for l in g.lines if "[CS_MEM]" in l or "[CS_PERF]" in l][:200]
            g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"memory-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"stages {len(R['stages'])}, switches {len(R['switches'])}, errors {len(R.get('gameErrors', []))}, failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
