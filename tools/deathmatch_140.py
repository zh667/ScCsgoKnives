"""The deathmatch package (OpenSpec change deathmatch-addon) on top of the 1.4.0 candidate pipeline.

Usage (Windows, via tools/dev.ps1): python tools/deathmatch_140.py <tag> <step>[,<step>...]|all
The stage is completion_140's (.tmp/completion-140-20260929/<tag>): the core, the agents package and every existing gate
are built and run exactly as there (the core carries the mode interfaces the package uses), and these steps are added:

  dmbuild    src/ScCsgoDeathmatch against this stage's Lite core, stamped with its own gameplay identity (the core's
             identity and the package's sources: both ends of a match must run the same build of both)
  dmpackage  the installable package: modinfo, the assembly, its database entry, the CS2-derived icons and sounds
             (tools/import_cs2_dm_assets.py; their hashes are checked against the provenance record)
  dmcheck    tools/DeathmatchCheck on the staged assemblies: the rules without a game
  dmloop     tools/NetLoopCheck --dmloop: the package on a server and two clients over the platform's own packets, once on
             the Lite core and once on the Full core, each with that edition's packaged adapter
  dmload     tools/TacticalLoadCheck --dm-payload: a world with the package's data through the game's load and save hooks
             with the package, WITHOUT it, and with it again (three processes per edition)
  dmabi      every engine reference of the package's assembly against the 1.9.3.1 game and the 1.9.3.2 multiplayer build

Never writes output/, Mods or worlds; the CS2 install and third-party packages are only read.
"""
import hashlib, json, os, shutil, subprocess, sys, zipfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import completion_140 as cp
base = cp.base
from pack_single_scmods import write_archive

LABEL = "死亡竞赛"
SOURCE = ROOT / "src/ScCsgoDeathmatch"
ASSETS_RECORD = ROOT / "docs/tasks/deathmatch-addon-assets-20261003.json"
# round 2 (R2-6): the pixel sprites rasterised from CS2's own SVG silhouettes (tools/build_dm_pixel_icons.py)
PIXEL_RECORD = ROOT / "docs/tasks/deathmatch-addon-pixel-icons-20261003.json"
FIXTURE = ROOT / "tools/fixtures/migration-120-20260925/world7-guns.xml"
def sha(b): return hashlib.sha256(b).hexdigest()
def dll(S): return S / "dm/source/bin/Release/net10.0/ScCsgoDeathmatch.dll"

def prepare(S):
    ok = cp.STEPS["prepare"](S)
    shutil.copytree(SOURCE, S / "tree/src/ScCsgoDeathmatch", ignore=shutil.ignore_patterns("bin", "obj"))
    return ok

def dmbuild(S):
    src = S / "dm/source"
    if src.exists(): shutil.rmtree(src)
    shutil.copytree(SOURCE, src, ignore=shutil.ignore_patterns("bin", "obj"))
    core = S / "lite/core/source/bin/Release/net10.0/ScCsgoKnives.dll"; assert core.exists(), "build the core first"
    profile = src / "Data/dm_cs2_profile.json"; assert profile.exists(), "run tools/cs2_dm_profile.py first"
    files = sorted((p.relative_to(src).as_posix(), sha(p.read_bytes())) for p in src.rglob("*") if p.is_file() and p.suffix in (".cs", ".json", ".xdb"))
    identity = sha(json.dumps([json.loads((S / "identity.json").read_text("utf8"))["identity"], files]).encode())[:32]
    (src / "ScGameplayIdentity.g.cs").write_text(f'[assembly: System.Reflection.AssemblyMetadata("ScGameplayIdentity", "{identity}")]\n', "utf8")
    version = json.loads((src / "modinfo.json").read_text("utf8"))["Version"]
    (src / "ScCsgoDeathmatch.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoDeathmatch</AssemblyName><RootNamespace>Game</RootNamespace>'
        f'<Version>{version}</Version><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><LangVersion>preview</LangVersion><Deterministic>true</Deterministic><DebugType>none</DebugType>'
        '<GenerateDependencyFile>false</GenerateDependencyFile></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>'
        f'<Reference Include="ScCsgoKnives"><HintPath>{core.as_posix()}</HintPath><Private>false</Private></Reference></ItemGroup>'
        '<ItemGroup><EmbeddedResource Include="Data/*.json"/></ItemGroup></Project>\n', "utf8")
    if not base.run(S, "build-deathmatch", ["dotnet", "build", src / "ScCsgoDeathmatch.csproj", "-c", "Release", "--nologo", "-v:q"]): raise SystemExit(1)
    built = dll(S).read_bytes(); assert identity.encode("ascii") in built
    base.dump(S / "dm.json", {"assembly": sha(built), "bytes": len(built), "identity": identity, "core": sha(core.read_bytes()), "profile": sha(profile.read_bytes()),
                              "cs2": json.loads(profile.read_text("utf8"))["Source"]["steam"], "sources": dict(files)})
    print("deathmatch assembly", sha(built)[:16], len(built), "identity", identity, flush=True)

def dmpackage(S):
    src = S / "dm/source"; built = dll(S).read_bytes(); info = json.loads((S / "dm.json").read_text("utf8")); assert sha(built) == info["assembly"]
    members = {"modinfo.json": (src / "modinfo.json").read_bytes(), "ScCsgoDeathmatch.dll": built, "Assets/ScDeathmatch.xdb": base.crlf((src / "Assets/ScDeathmatch.xdb").read_text("utf8")).encode("utf8")}
    record = json.loads(ASSETS_RECORD.read_text("utf8"))
    for r in record["records"]:
        data = (ROOT / r["target"]).read_bytes(); assert sha(data) == r["sha256"], f"{r['target']} is not the recorded import"
        members[r["member"]] = data
    for r in json.loads(PIXEL_RECORD.read_text("utf8"))["records"]:
        data = (ROOT / r["target"]).read_bytes(); assert sha(data) == r["sha256"], f"{r['target']} is not the recorded rasterisation"
        members["Assets/" + Path(r["target"]).relative_to("src/ScCsgoDeathmatch/Assets").as_posix()] = data
    # every file the package's Assets folder holds is a recorded member: nothing ships unrecorded, nothing is left behind
    on_disk = {"Assets/" + f.relative_to(SOURCE / "Assets").as_posix() for f in (SOURCE / "Assets").rglob("*") if f.is_file()}
    assert on_disk == {n for n in members if n.startswith("Assets/")}, sorted(on_disk ^ {n for n in members if n.startswith("Assets/")})
    target = S / "candidate" / base.name(LABEL); target.parent.mkdir(parents=True, exist_ok=True)
    write_archive(target, {n: base.member(b) for n, b in sorted(members.items())})
    with zipfile.ZipFile(target) as z:
        assert z.testzip() is None and {n: sha(z.read(n)) for n in z.namelist()} == {n: sha(b) for n, b in members.items()}
        modinfo = json.loads(z.read("modinfo.json").decode("utf8"))
    core_version = json.loads((ROOT / "src/ScCsgoKnives/modinfo.json").read_text("utf8"))["Version"]
    assert modinfo["PackageName"] == "zh667.ScCsgoDeathmatch" and modinfo["Dependencies"] == {"zh667.ScCsgoKnives": core_version}, modinfo
    pk = json.loads((S / "packages.json").read_text("utf8"))
    pk[LABEL] = dict(file=target.name, bytes=target.stat().st_size, sha256=sha(target.read_bytes()), baselineSha256=None, changed={n: sha(b) for n, b in sorted(members.items())}, unchangedMembers=0)
    base.dump(S / "packages.json", pk)
    print(LABEL, target.stat().st_size, pk[LABEL]["sha256"], sorted(members), flush=True)

def report(S, key, least):
    p = S / f"{key}.json"
    if not p.exists(): print(key, "no result", flush=True); return False
    d = json.loads(p.read_text("utf-8-sig")); checks = d.get("checks", [])
    failed = [(c.get("Id") or c.get("Case") or "") + " " + (c.get("Name") or c.get("name") or str(c.get("error", ""))[:300]) for c in checks if not (c.get("Ok") if "Ok" in c else c.get("ok", False))]
    print(key, "failed", d.get("failed"), "of", len(checks), failed, flush=True)
    return d.get("failed") == 0 and len(checks) >= least

def dmcheck(S):
    work = S / "dm/check"
    if work.exists(): shutil.rmtree(work)
    shutil.copytree(ROOT / "tools/DeathmatchCheck", work, ignore=shutil.ignore_patterns("bin", "obj"))
    refs = {"ScCsgoKnives": S / "lite/core/source/bin/Release/net10.0/ScCsgoKnives.dll", "ScCsgoDeathmatch": dll(S)}
    items = "".join(f'<Reference Include="{n}"><HintPath>{p.as_posix()}</HintPath></Reference>' for n, p in refs.items())
    (work / "DeathmatchCheck.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        f'<Nullable>disable</Nullable><LangVersion>preview</LangVersion></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{items}</ItemGroup></Project>\n', "utf8")
    if not base.run(S, "build-tool-DeathmatchCheck", ["dotnet", "build", work / "DeathmatchCheck.csproj", "-c", "Release", "--nologo", "-v:q"]): return False
    done = base.run(S, "dmcheck", ["dotnet", work / "bin/Release/net10.0/DeathmatchCheck.dll", S / "dmcheck.json"], cwd=work)
    return report(S, "dmcheck", 190) and done

def dmloop(S):
    pk = json.loads((S / "packages.json").read_text("utf8")); ok = True
    for edition, label, agents in [("lite", "轻量", "探员"), ("full", "全量", "全量")]:
        work = S / f"net/dmloop-{edition}"
        if work.exists(): shutil.rmtree(work)
        work.mkdir(parents=True)
        with zipfile.ZipFile(S / "candidate" / pk[label]["file"]) as z: adapter = z.read(cp.NET_MEMBER); core = z.read("ScCsgoKnives.dll")
        with zipfile.ZipFile(S / "candidate" / pk[agents]["file"]) as z: (work / "ScCsgoTactical.dll").write_bytes(z.read("ScCsgoTactical.dll"))
        with zipfile.ZipFile(S / "candidate" / pk[LABEL]["file"]) as z: package = z.read("ScCsgoDeathmatch.dll")
        (work / "ScCsgoNet.dll").write_bytes(adapter)
        refs = work / "refs"; shutil.copytree(cp.MP_REFS, refs); (refs / "Survivalcraft.CompatNet.dll").unlink()   # the platform's Android build has no CompatNet: the package must not need it
        (refs / "ScCsgoKnives.dll").write_bytes(core); (refs / "ScCsgoDeathmatch.dll").write_bytes(package)
        src = work / "src"; shutil.copytree(ROOT / "tools/NetLoopCheck", src, ignore=shutil.ignore_patterns("bin", "obj"))
        if not base.run(S, f"build-tool-NetLoopCheck-dm-{edition}", ["dotnet", "build", src / "NetLoopCheck.csproj", "-c", "Release", "-o", work / "out", f"-p:Refs={refs}", "--nologo", "-v:q"]): return False
        key = f"netdmloop-{edition}"; os.environ["DM_GROUP_OUT"] = str(S / f"dm/world-group-{edition}.xml")
        try: done = base.run(S, key, ["dotnet", work / "out/NetLoopCheck.dll", refs, work / "ScCsgoNet.dll", "-", S / f"{key}.json", "--dmloop", f"--modules={work / 'ScCsgoTactical.dll'};{refs / 'ScCsgoDeathmatch.dll'}"])
        finally: os.environ.pop("DM_GROUP_OUT", None)
        d = json.loads((S / f"{key}.json").read_text("utf-8-sig")) if (S / f"{key}.json").exists() else {"failed": -1, "total": 0, "checks": []}
        print(key, "failed", d["failed"], "of", d["total"], "handlers", d.get("registered"), [f"[{c['Case']}] {c['Name']}" for c in d["checks"] if not c["Ok"]], flush=True)
        ok &= done and d["failed"] == 0 and d["total"] >= 60 and (S / f"dm/world-group-{edition}.xml").exists()
    return ok

def dmload(S):
    # (built from the present tools/TacticalLoadCheck, not the stage's snapshot: the check may be corrected without rebuilding the candidate; its source hash is recorded)
    work = S / "dm/loadtool"
    if work.exists(): shutil.rmtree(work)
    shutil.copytree(ROOT / "tools/TacticalLoadCheck", work, ignore=shutil.ignore_patterns("bin", "obj"))
    if not base.run(S, "build-tool-TacticalLoadCheck-dm", ["dotnet", "build", work / "TacticalLoadCheck.csproj", "-c", "Release", "--nologo", "-v:q"]): return False
    base.dump(S / "dmload-tool.json", {"Program.cs": sha((work / "Program.cs").read_bytes())})
    pk = json.loads((S / "packages.json").read_text("utf8")); tool = work / "bin/Release/net10.0/TacticalLoadCheck.dll"; content = base.GAME / "Content.zip"; package = S / "candidate" / pk[LABEL]["file"]; ok = True
    for edition, label in [("lite", "轻量"), ("full", "全量")]:
        core = S / "candidate" / pk[label]["file"]; group = S / f"dm/world-group-{edition}.xml"; worlds = [S / f"dm/load-{edition}-{n}.xml" for n in "abc"]
        phases = [("a-with", package, FIXTURE, group, worlds[0]), ("b-without", "-", worlds[0], "-", worlds[1]), ("c-returned", package, worlds[1], "-", worlds[2])]
        for name, dm, world_in, payload, world_out in phases:
            key = f"dmload-{edition}-{name}"
            done = base.run(S, key, ["dotnet", tool, "--dm-payload", core, content, dm, world_in, payload, world_out, S / f"{key}.json"])
            if not (report(S, key, 7) and done): ok = False; break
    return ok

def dmabi(S):
    work = S / "dm/abi"; work.mkdir(parents=True, exist_ok=True)
    if not base.run(S, "build-tool-MpAbiCheck", ["dotnet", "build", ROOT / "tools/MpAbiCheck/MpAbiCheck.csproj", "-c", "Release", "-o", work / "tool", "--nologo", "-v:q"]): return False
    ok = True
    for key, folder in [("dmabi-1931", base.GAME), ("dmabi-1932mp", cp.MP_REFS)]:
        # (the core is given as a second assembly under test: the package references it, and it is not part of an engine build)
        done = base.run(S, key, ["dotnet", work / "tool/MpAbiCheck.dll", S / f"{key}.json", folder, dll(S), S / "lite/core/source/bin/Release/net10.0/ScCsgoKnives.dll"])
        d = json.loads((S / f"{key}.json").read_text("utf-8-sig")) if (S / f"{key}.json").exists() else {}
        print(key, "references", d.get("checkedReferences"), "unresolved", d.get("unresolved"), [p for r in d.get("results", []) for p in r["problems"]][:12], flush=True)
        # the verdict is the package's own: every engine member it binds to exists in the target build, and every assembly it
        # references is the engine's, the framework's or the core's (the core's own references are the core gates' subject)
        mine = [r for r in d.get("results", []) if r["mod"] == "ScCsgoDeathmatch.dll"]
        print(key, "package:", mine[0]["problems"] if mine else "no result", [(a["name"], a["where"]) for a in (mine[0]["assemblyRefs"] if mine else []) if a["where"] not in ("target", "framework")], flush=True)
        ok &= (d.get("checkedReferences") or 0) > 100 and len(mine) == 1 and not mine[0]["problems"] and all(a["where"] != "MISSING" for a in mine[0]["assemblyRefs"])
    return ok

STEPS = dict(cp.STEPS)
STEPS.update({"prepare": prepare, "dmbuild": dmbuild, "dmpackage": dmpackage, "dmcheck": dmcheck, "dmloop": dmloop, "dmload": dmload, "dmabi": dmabi})
ORDER = ["prepare", "build", "clips", "appearance", "package", "dmbuild", "dmpackage", "dmcheck", "dmabi", "netloop", "dmloop", "dmload", "appnet", "baseline", "gates", "motion", "throw", "hotspots", "ui"]
PREREQUISITES = set(cp.PREREQUISITES) | {"dmbuild", "dmpackage"}

if __name__ == "__main__":
    tag, wanted = sys.argv[1], sys.argv[2]; S = base.BASE / tag
    chosen = ORDER if wanted == "all" else wanted.split(",")
    unknown = [s for s in chosen if s not in STEPS]; assert not unknown, unknown
    failed = []
    for step in chosen:
        try: ok = STEPS[step](S) is not False
        except SystemExit as e: ok = not e.code
        if not ok:
            failed.append(step)
            if step in PREREQUISITES: print("stopped after the failed prerequisite", step, "- later steps not run:", chosen[chosen.index(step) + 1:], flush=True); break
    print("failed steps:", failed); raise SystemExit(1 if failed else 0)
