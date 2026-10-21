using Mono.Cecil;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

// The binary contract of the delivered DLLs against each standalone engine's own runtime DLLs (api-194-1951-compat-20261016):
// every engine type/member the DLLs reference resolves; every override still has a virtual base; no abstract or interface
// member is left unimplemented; the private engine fields the mod reaches by reflection exist. Also lists the virtuals that
// engine types the mod derives from gained or lost relative to the first engine. Static only: a behaviour change behind an
// unchanged signature needs the gameplay checks (tools/MpM0/sp_engine_compat.py).
//
// Usage: dotnet run --project tools/ApiContractCheck -c Release -- --engine <version>=<runtime dir> [--engine ...]
//          --package <tag>=<.scmod> [--package ...] --work <dir> --out <report.json>
// The first engine is the baseline of the virtual-member comparison. Exit code 1 when any engine breaks the contract.
var engines = new List<(string Version, string Dir)>(); var packages = new List<(string Tag, string Path)>();
string work = null, outPath = null;
for (int i = 0; i + 1 < args.Length; i += 2) {
    string value = args[i + 1];
    switch (args[i]) {
        case "--engine": engines.Add((value[..value.IndexOf('=')], value[(value.IndexOf('=') + 1)..])); break;
        case "--package": packages.Add((value[..value.IndexOf('=')], value[(value.IndexOf('=') + 1)..])); break;
        case "--work": work = value; break;
        case "--out": outPath = value; break;
        default: Console.Error.WriteLine("unknown argument " + args[i]); return 2;
    }
}
if (engines.Count == 0 || packages.Count == 0 || work is null || outPath is null) { Console.Error.WriteLine("usage: --engine v=dir ... --package tag=path ... --work dir --out json"); return 2; }

// Private engine fields read by reflection: ScLazyContent, ScTexturePreparation, ScTextureResidency, ScLiveWorldBackup.
(string Type, string Field)[] reflected = [("Game.ContentManager", "Resources"), ("Game.ContentManager", "Caches"), ("Game.ContentManager", "syncObj"),
    ("Game.TerrainSerializer23", "m_storage"), ("Game.TerrainSerializer23", "m_lock")];
string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
string FileHash(string p) => Hash(File.ReadAllBytes(p));

// Every DLL of the packages (one level of nested .scmod too), deduplicated by content.
string dllRoot = Path.Combine(work, "dlls"); Directory.CreateDirectory(dllRoot);
var dlls = new Dictionary<string, (string Path, List<string> From)>();
void Take(ZipArchive zip, string from) {
    foreach (var e in zip.Entries) {
        bool dll = e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), nested = e.FullName.EndsWith(".scmod", StringComparison.OrdinalIgnoreCase);
        if (!dll && !nested) continue;
        using var ms = new MemoryStream(); using (var s = e.Open()) s.CopyTo(ms);
        if (nested) { ms.Position = 0; using var inner = new ZipArchive(ms, ZipArchiveMode.Read); Take(inner, from + ":" + e.FullName); continue; }
        var bytes = ms.ToArray(); string h = Hash(bytes);
        if (!dlls.TryGetValue(h, out var d)) {
            string dir = Path.Combine(dllRoot, h[..12]); Directory.CreateDirectory(dir); string p = Path.Combine(dir, Path.GetFileName(e.FullName));
            File.WriteAllBytes(p, bytes); dlls[h] = d = (p, new());
        }
        d.From.Add(from + ":" + e.FullName);
    }
}
var packageInfo = new List<object>();
foreach (var (tag, path) in packages) { packageInfo.Add(new { tag, path, sha256 = FileHash(path) }); using var z = ZipFile.OpenRead(path); Take(z, tag); }
Console.WriteLine($"mod DLLs: {dlls.Count}");
foreach (var d in dlls) Console.WriteLine($"  {d.Key[..12]} {Path.GetFileName(d.Value.Path)} <- {string.Join(", ", d.Value.From)}");
var modDirs = dlls.Values.Select(d => Path.GetDirectoryName(d.Path)!).Distinct().ToArray();
var modNames = dlls.Values.Select(d => Path.GetFileNameWithoutExtension(d.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

string Sig(MethodReference m) => m.ReturnType.FullName + " " + m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";
string Key(MethodReference m) => m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";
var reports = new List<object>(); var baseline = new Dictionary<string, HashSet<string>>(); int broken = 0;
foreach (var (version, dir) in engines) {
    var engineNames = Directory.GetFiles(dir, "*.dll").Select(Path.GetFileNameWithoutExtension).Where(n => !modNames.Contains(n!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    using var resolver = new DefaultAssemblyResolver();
    foreach (var d in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(d);
    resolver.AddSearchDirectory(dir);
    foreach (var d in modDirs) resolver.AddSearchDirectory(d);
    string runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(); resolver.AddSearchDirectory(runtime);
    string desktop = runtime.Replace("Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"); if (Directory.Exists(desktop)) resolver.AddSearchDirectory(desktop);
    bool EngineScope(TypeReference t) { while (t is TypeSpecification s) t = s.ElementType; return t.Scope is AssemblyNameReference a && engineNames.Contains(a.Name); }
    bool EngineDef(TypeDefinition t) => engineNames.Contains(t.Module.Assembly.Name.Name);
    var results = new List<object>(); var derived = new Dictionary<string, TypeDefinition>();
    foreach (var (h, (path, from)) in dlls) {
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
        var missing = new List<string>(); var lost = new List<string>(); var unimplemented = new List<string>(); int types = 0, members = 0, genericBases = 0;
        foreach (var t in module.GetTypeReferences().Where(EngineScope)) { types++; try { if (t.Resolve() is null) missing.Add("type " + t.FullName); } catch (Exception e) { missing.Add("type " + t.FullName + " => " + e.Message); } }
        foreach (var m in module.GetMemberReferences().Where(m => EngineScope(m.DeclaringType))) {
            members++;
            try { if (m switch { MethodReference r => r.Resolve() is null, FieldReference f => f.Resolve() is null, _ => false }) missing.Add("member " + m.FullName); }
            catch (Exception e) { missing.Add("member " + m.FullName + " => " + e.Message); }
        }
        foreach (var t in module.GetTypes()) {
            if (t.IsInterface) continue;
            var chain = new List<TypeDefinition>(); bool generic = false; TypeReference br = t.BaseType;
            try { while (br != null) { generic |= br is GenericInstanceType; var bd = br.Resolve(); if (bd == null) { missing.Add($"base {br.FullName} of {t.FullName}"); break; } chain.Add(bd); br = bd.BaseType; } }
            catch (Exception e) { missing.Add($"base of {t.FullName} => {e.Message}"); }
            if (chain.FirstOrDefault(EngineDef) is { } first) derived[first.FullName] = first;
            if (!chain.Any(EngineDef)) continue;
            // Overrides through a generic base would need argument substitution; they are counted, not compared.
            foreach (var m in t.Methods.Where(m => m.IsVirtual && !m.IsNewSlot && !m.HasOverrides)) {
                if (generic) { genericBases++; continue; }
                if (!chain.SelectMany(b => b.Methods).Any(b => b.IsVirtual && Key(b) == Key(m))) lost.Add(t.FullName + "::" + Sig(m));
            }
            if (t.IsAbstract || generic) continue;
            var all = new[] { t }.Concat(chain).ToList();
            foreach (var b in chain.Where(EngineDef))
                foreach (var a in b.Methods.Where(x => x.IsAbstract))
                    if (!all.TakeWhile(x => x != b).SelectMany(x => x.Methods).Any(x => !x.IsAbstract && x.IsVirtual && Key(x) == Key(a)))
                        unimplemented.Add($"{t.FullName} lacks abstract {b.FullName}::{Sig(a)}");
            foreach (var ir in all.SelectMany(x => x.Interfaces).Select(i => i.InterfaceType).Where(EngineScope)) {
                if (ir is GenericInstanceType) continue;
                var idef = ir.Resolve(); if (idef == null) { missing.Add("interface " + ir.FullName); continue; }
                foreach (var a in idef.Methods.Where(x => x.IsAbstract))
                    if (!all.SelectMany(x => x.Methods).Any(x => (x.IsVirtual && Key(x) == Key(a)) || x.Overrides.Any(o => o.Name == a.Name && o.DeclaringType.FullName == idef.FullName)))
                        unimplemented.Add($"{t.FullName} lacks interface {idef.FullName}::{Sig(a)}");
            }
        }
        broken += missing.Count + lost.Count + unimplemented.Count;
        results.Add(new { dll = Path.GetFileName(path), sha256 = h, from, engineTypeRefs = types, engineMemberRefs = members, missing, lostOverrides = lost, unimplemented, overridesThroughGenericBases = genericBases });
        Console.WriteLine($"[{version}] {Path.GetFileName(path)} {h[..12]}: {types} types, {members} members; missing {missing.Count}, lost overrides {lost.Count}, unimplemented {unimplemented.Count}");
        foreach (var x in missing.Concat(lost).Concat(unimplemented).Take(40)) Console.WriteLine("    " + x);
    }
    var virtuals = new List<object>();
    foreach (var (name, def) in derived.OrderBy(k => k.Key)) {
        var set = new HashSet<string>();
        for (TypeDefinition b = def; b != null && b.FullName != "System.Object"; b = b.BaseType?.Resolve())
            foreach (var m in b.Methods.Where(m => m.IsVirtual && !m.IsFinal)) set.Add((m.IsAbstract ? "abstract " : "") + b.Name + "::" + Sig(m));
        if (version == engines[0].Version) baseline[name] = set;
        else if (baseline.TryGetValue(name, out var basis)) {
            var added = set.Except(basis).OrderBy(x => x).ToArray(); var removed = basis.Except(set).OrderBy(x => x).ToArray();
            if (added.Length + removed.Length == 0) continue;
            virtuals.Add(new { type = name, added, removed });
            Console.WriteLine($"  [{version}] {name}: +{added.Length} -{removed.Length}");
            foreach (var x in added) Console.WriteLine("      + " + x);
            foreach (var x in removed) Console.WriteLine("      - " + x);
        }
    }
    var fields = new List<object>();
    using (var sc = AssemblyDefinition.ReadAssembly(Path.Combine(dir, "Survivalcraft.dll"), new ReaderParameters { AssemblyResolver = resolver }))
    using (var en = AssemblyDefinition.ReadAssembly(Path.Combine(dir, "Engine.dll"), new ReaderParameters { AssemblyResolver = resolver }))
        foreach (var (type, field) in reflected) {
            var f = (sc.MainModule.GetType(type) ?? en.MainModule.GetType(type))?.Fields.FirstOrDefault(x => x.Name == field);
            if (f is null) broken++;
            fields.Add(new { member = type + "." + field, found = f is not null, fieldType = f?.FieldType.FullName, isStatic = f?.IsStatic });
            Console.WriteLine($"  [{version}] reflected {type}.{field}: {(f is null ? "MISSING" : f.FieldType.FullName + (f.IsStatic ? " static" : ""))}");
        }
    reports.Add(new { version, dir, engines = new[] { "Engine", "EntitySystem", "Survivalcraft" }.Select(n => new { n, sha256 = FileHash(Path.Combine(dir, n + ".dll")) }), results, virtuals, reflectedFields = fields });
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, JsonSerializer.Serialize(new { packages = packageInfo, broken, reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"contract breaks: {broken}; {outPath}");
return broken == 0 ? 0 : 1;
