"""Isolated candidates for agent-followup-140 on top of the delivered 1.4.0 packages. Never writes output/, Mods or worlds.

Usage (Windows, via tools/dev.ps1): python tools/followup_140.py <tag> prepare|build|air|appearance|package|checks|motion|hotspots|ui|all
Each tag is a separate stage under .tmp/followup-140-20260928/<tag>. Build stages come from the accepted
release-140 stage (embedded AnimationData/Shaders/ArmData, split shims, refs); current sources are overlaid.
Resource members listed in RESOURCES (if their source file exists) replace the package members byte-for-byte.
"""
import concurrent.futures, hashlib, json, os, shutil, subprocess, sys, zipfile, zlib
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from pack_single_scmods import raw_member, write_archive

REL = ROOT / ".tmp/release-140-20260928"
BASE = ROOT / ".tmp/followup-140-20260928"
GAME = Path(r"D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1")
BASELINES = {
    "全量": "34f11870f152cae665d363c7f4ff0dcfe9f2041cd9ff88a8c7bc53eb3a22957e",
    "轻量": "532986265ead207f43001bf2285ca5fe3b51ab63ea2319717bd0b5ca703a95c1",
    "探员": "d49e4beca0c91e8dfb69141ae3502aa1b2b892f5aadb1274d4222f2f48ea594a",
}
# Tactical resources that later phases may re-derive; packaged only when the staged file exists (agents + full).
RESOURCES = ["Assets/Animations/ScTactical.json", "Assets/Animations/ScTacticalHostage.json", "Assets/Models/ScCsgoTactical/ct.glb", "Assets/Models/ScCsgoTactical/t.glb",
             "Assets/Animations/ScCsgoTactical/ct.scanim", "Assets/Animations/ScCsgoTactical/t.scanim", "Integrations/ScCsgoAppearance.bin"]
NEW_MEMBERS = {"Assets/Animations/ScTacticalHostage.json"}  # the legacy hostage keeps the old gait-only config
# Tactical assets that are new since the baseline packages, as recorded with their hashes: the sub-world airdrop's model, textures
# and attribution (2026-10-06; the first 1.5.0 family build shipped without them and the agents' world failed to load:
# "Not Found Res Models/ScCsgoTactical/airdrop"). Full and agents packages; the Lite carries no agents assets.
TACTICAL_ASSET_RECORDS = [ROOT / "docs/tasks/airdrop-package-members-20261006.json"]
def tactical_assets(edition):
    """(member, bytes) of every recorded tactical asset for an edition ("full" / "agents"), each checked against its record."""
    out = []
    for record in TACTICAL_ASSET_RECORDS:
        for r in json.loads(record.read_text("utf8"))["records"]:
            if edition not in r["editions"]: continue
            data = (ROOT / r["target"]).read_bytes(); assert sha(data) == r["sha256"], f"{r['target']} is not the recorded asset"
            out.append((r["member"], data))
    return out
DENSE = ROOT / ".tmp/actor-freeze-20260926/before"  # preserved dense CT/T actors; proven to re-bake the shipped caches exactly
EXPORTS = {"ct": ROOT / ".tmp/cs2-companions-audit-20260920/export/agents/models/ctm_sas/ctm_sas.glb",
           "t": ROOT / ".tmp/cs2-companions-audit-20260920/export/agents/models/tm_phoenix/tm_phoenix.glb"}
IGNORE = shutil.ignore_patterns("bin", "obj", "__pycache__")

def sha(b): return hashlib.sha256(b).hexdigest()
def dump(p, data): p.parent.mkdir(parents=True, exist_ok=True); p.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", "utf8")
def member(data):
    c = zlib.compressobj(9, zlib.DEFLATED, -15); packed = c.compress(data) + c.flush()
    return (8, zlib.crc32(data), len(data), packed) if len(packed) < len(data) else (0, zlib.crc32(data), len(data), data)
# The family's version (src/ScCsgoKnives/modinfo.json) names the candidates and is written into their modinfo; the stage builds
# on the delivered packages of BASELINE_VERSION in BASELINE_DIR (since the 1.5.0 family of 2026-10-06 22:50: the 1.5.0
# packages in output/; the 1.4.0 ones moved to output/history-1.4.0/ and stay the compat gate's readers). The deathmatch
# package carries no 包 suffix (the user, 2026-10-06).
VERSION = json.loads((ROOT / "src/ScCsgoKnives/modinfo.json").read_text("utf8"))["Version"]
BASELINE_VERSION = "1.5.0"; BASELINE_DIR = ROOT / "output"
def name(label, version=None): return f"[API1.9]CS武器{version or VERSION}-{label}{'' if label == '死亡竞赛' else '包'}.scmod"
def baseline(label): return BASELINE_DIR / name(label, BASELINE_VERSION)

def run(S, label, args, cwd=None):
    logs = S / "logs"; logs.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy(); env["SC_NMM_CHECK_PACKAGE"] = str(GAME / "Mods/[API1.9]NekoMeko Model-v1.1.scmod")
    with (logs / (label + ".log")).open("w", encoding="utf8") as log:
        r = subprocess.run([str(a) for a in args], cwd=cwd or ROOT, stdout=log, stderr=subprocess.STDOUT, env=env)
    print(label, "PASS" if r.returncode == 0 else f"FAIL ({r.returncode}) see logs/{label}.log", flush=True)
    return r.returncode == 0

def prepare(S):
    if S.exists(): raise SystemExit(f"{S} exists; use a new tag")
    (S / "baseline").mkdir(parents=True)
    for label, expected in BASELINES.items():
        src = baseline(label); assert sha(src.read_bytes()) == expected, f"{label} {BASELINE_VERSION} delivery changed"
    tree = S / "tree"
    for part in ["src/ScCsgoKnives", "src/ScCsgoTactical", "src/ScCsgoVoice", "src/ScCsgoAppearance", "tools"]:
        shutil.copytree(ROOT / part, tree / part, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__", "AnimationData", "Models", "Textures", "Audio", "*.scmod", "*.glb", "*.scanim", "*.png", "*.webp", "*.wav", "*.ogg"))
    for f in ["Directory.Build.targets", "nuget.config"]: shutil.copyfile(ROOT / f, tree / f)
    core_cs = list((tree / "src/ScCsgoKnives").rglob("*.cs")); tactical_cs = list((tree / "src/ScCsgoTactical").glob("*.cs"))
    for edition in ["lite", "full"]:
        for part in ["core", "agents", "voice"]:
            shutil.copytree(REL / edition / part / "source", S / edition / part / "source", ignore=IGNORE)
        if (REL / edition / "core/refs").exists(): shutil.copytree(REL / edition / "core/refs", S / edition / "core/refs")
        dest = S / edition / "core/source"
        for p in core_cs:
            t = dest / p.relative_to(tree / "src/ScCsgoKnives"); t.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, t)
        if edition == "lite": shutil.copyfile(tree / "src/ScCsgoTactical/TacticalBlocks.cs", dest / "TacticalBlocks.cs")
        agents = S / edition / "agents/source"
        for old in agents.glob("*.cs"): old.unlink()
        for p in tactical_cs:
            if not (edition == "lite" and p.name == "TacticalBlocks.cs"): shutil.copyfile(p, agents / p.name)
        voice = S / edition / "voice/source"
        for old in voice.glob("*.cs"): old.unlink()
        for p in (tree / "src/ScCsgoVoice").glob("*.cs"): shutil.copyfile(p, voice / p.name)
        # Re-point project references from the release stage to this stage.
        for proj in (S / edition).rglob("*.csproj"):
            proj.write_text(proj.read_text("utf8").replace(REL.as_posix(), S.as_posix()).replace(str(REL), str(S)), "utf8")
    # Multiplayer gameplay identity (first-person-eye-shot-20261001, Game.ScNetIdentity): one value stamped into every core
    # and agents assembly built from this snapshot, so the Full and split Lite packages of one release accept each other in
    # multiplayer; any change to the gameplay sources gives a new value (and a peer of another release is refused).
    gameplay = sorted((p.relative_to(tree).as_posix(), sha(p.read_bytes())) for part in ["src/ScCsgoKnives", "src/ScCsgoTactical", "src/ScCsgoVoice"]
                      for p in (tree / part).rglob("*") if p.is_file())
    identity = sha(json.dumps(gameplay).encode())[:32]
    for edition in ["lite", "full"]:
        for part in ["core", "agents"]:
            (S / edition / part / "source/ScGameplayIdentity.g.cs").write_text(f'[assembly: System.Reflection.AssemblyMetadata("ScGameplayIdentity", "{identity}")]\n', "utf8")
    dump(S / "identity.json", {"identity": identity, "files": len(gameplay)})
    dump(S / "source-hashes.json", {p.relative_to(tree).as_posix(): sha(p.read_bytes()) for p in tree.rglob("*") if p.is_file() and p.suffix in (".cs", ".xdb", ".xml", ".json", ".py", ".csproj")})
    print("prepared", S, "gameplay identity", identity)

def air(S):
    """F3 resources: append CS2 jump/in-air clips to the dense actors, bake native caches, keep geometry identical."""
    sys.path[:0] = [str(ROOT / ".tmp/resource-codecs-20260926/deps"), str(ROOT / ".tmp/optimization-deps")]
    import zstandard as zstd
    def envelope(raw):
        packed = zstd.ZstdCompressor(level=19).compress(raw); assert zstd.ZstdDecompressor().decompress(packed) == raw
        return b"SCZSTD01" + __import__("struct").pack("<II", len(raw), len(packed)) + hashlib.sha256(raw).digest() + packed
    work = S / "air"; work.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(S / "tree/tools")); from actor_air_clips import append_to_dense
    if not run(S, "build-tool-ActorLoadCheck", ["dotnet", "build", S / "tree/tools/ActorLoadCheck/ActorLoadCheck.csproj", "-c", "Release", "--nologo", "-v:q"]): raise SystemExit(1)
    tool = next((S / "tree/tools/ActorLoadCheck/bin/Release").rglob("ActorLoadCheck.dll"))
    report = {}
    with zipfile.ZipFile(baseline("全量")) as full, zipfile.ZipFile(baseline("探员")) as agents:
        for role in ["ct", "t"]:
            dense = work / f"{role}-dense.glb"
            report[role] = append_to_dense(DENSE / f"{role}.glb", EXPORTS[role], dense)
            scanim = work / f"{role}.scanim"
            if not run(S, f"bake-{role}", ["dotnet", tool, "bake", dense, scanim, work / f"{role}-bake.json"]): raise SystemExit(1)
            stripped = work / f"{role}.glb"
            if not run(S, f"strip-{role}", [sys.executable, S / "tree/tools/prepare_actor_geometry.py", dense, stripped, role, work / f"{role}-strip.json"], cwd=S / "tree/tools"): raise SystemExit(1)
            glb = f"Assets/Models/ScCsgoTactical/{role}.glb"; cache = f"Assets/Animations/ScCsgoTactical/{role}.scanim"
            assert stripped.read_bytes() == full.read(glb), f"{role} geometry changed; clips must not alter the actor GLB"
            old_lite = agents.read(cache); assert old_lite[:8] == b"SCZSTD01" and envelope(full.read(cache)) == old_lite, "Lite cache is not the Full cache envelope"
            raw = scanim.read_bytes()
            for edition, data in [("full", raw), ("lite", envelope(raw))]:
                t = S / "resources" / edition / cache; t.parent.mkdir(parents=True, exist_ok=True); t.write_bytes(data)
            report[role]["bake"] = json.loads((work / f"{role}-bake.json").read_text("utf-8-sig"))
            report[role]["cacheFull"] = sha(raw); report[role]["cacheLite"] = sha(envelope(raw))
    for edition in ["full", "lite"]:
        for cfg in ["ScTactical.json", "ScTacticalHostage.json"]:
            t = S / "resources" / edition / "Assets/Animations" / cfg; t.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(S / "tree/src/ScCsgoTactical/Assets/Animations" / cfg, t)
    dump(S / "air.json", report); print(json.dumps({r: {k: v for k, v in x.items() if k != "added"} for r, x in report.items()}, ensure_ascii=False)[:3000])

def appearance(S):
    """Build the optional appearance adapter against each edition's own core/tactical (as the release chain does)."""
    third = {"neorxna": ROOT / ".tmp/creature-audit-20260917/10/neorxna.dll", "sc-nekomekomodel": ROOT / ".tmp/third-party-refs/sc-nekomekomodel.dll"}
    for n, p in third.items(): assert p.exists(), f"missing read-only reference {n}: {p}"
    for edition in ["lite", "full"]:
        d = S / edition / "appearance/source"; d.mkdir(parents=True, exist_ok=True)
        for p in (S / "tree/src/ScCsgoAppearance").glob("*.cs"): shutil.copyfile(p, d / p.name)
        refs = {"ScCsgoKnives": S / edition / "core/source/bin/Release/net10.0/ScCsgoKnives.dll", "ScCsgoTactical": S / edition / "agents/source/bin/Release/net10.0/ScCsgoTactical.dll", **third}
        items = "".join(f'<Reference Include="{n}"><HintPath>{p.as_posix()}</HintPath></Reference>' for n, p in refs.items())
        (d / "ScCsgoAppearance.csproj").write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoAppearance</AssemblyName><Version>1.2.1</Version><AssemblyVersion>1.1.0.0</AssemblyVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DebugType>none</DebugType></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{items}</ItemGroup></Project>', "utf8")
        if not run(S, f"build-{edition}-appearance", ["dotnet", "build", d / "ScCsgoAppearance.csproj", "-c", "Release", "--nologo", "-v:q"]): raise SystemExit(1)
        t = S / "resources" / edition / "Integrations/ScCsgoAppearance.bin"; t.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(d / "bin/Release/net10.0/ScCsgoAppearance.dll", t)

def native_tool(S, tool):
    """Full-candidate assets (package members plus staged overlays) and a build of tools/<tool> against the stage DLLs."""
    assets = S / "assets-full"
    if not assets.exists():
        with zipfile.ZipFile(baseline("全量")) as z:
            for role in ["ct", "t"]:
                for n in [f"Assets/Models/ScCsgoTactical/{role}.glb", f"Assets/Animations/ScCsgoTactical/{role}.scanim"]:
                    t = assets / n[len("Assets/"):]; t.parent.mkdir(parents=True, exist_ok=True); t.write_bytes(z.read(n))
            (assets / "Animations/ScTactical.json").write_bytes(z.read("Assets/Animations/ScTactical.json"))
        overlay = S / "resources/full/Assets"
        for f in overlay.rglob("*"):
            if f.is_file() and f.suffix in (".scanim", ".json"): t = assets / f.relative_to(overlay); t.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(f, t)
    d = S / "tools" / tool; d.mkdir(parents=True, exist_ok=True); shutil.copyfile(S / "tree/tools" / tool / "Program.cs", d / "Program.cs")
    refs = {"ScCsgoKnives": S / "full/core/source/bin/Release/net10.0/ScCsgoKnives.dll", "ScCsgoTactical": S / "full/agents/source/bin/Release/net10.0/ScCsgoTactical.dll",
            "ScCsgoResources": S / "full/core/refs/ScCsgoResources.dll"}
    items = "".join(f'<Reference Include="{n}"><HintPath>{p.as_posix()}</HintPath></Reference>' for n, p in refs.items())
    (d / f"{tool}.csproj").write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{items}</ItemGroup></Project>', "utf8")
    if not run(S, "build-tool-" + tool, ["dotnet", "build", d / f"{tool}.csproj", "-c", "Release", "--nologo", "-v:q"]): raise SystemExit(1)
    return assets, d / f"bin/Release/net10.0/{tool}.dll"

def motion(S):
    """Offline native renders of jump states and corpses from the Full candidate (evidence, not acceptance)."""
    assets, dll = native_tool(S, "ActorMotionCheck")
    ok = run(S, "motion", ["dotnet", dll, assets, GAME / "Content.zip", S / "motion"])
    if (S / "motion/motion.json").exists(): print(json.dumps(json.loads((S / "motion/motion.json").read_text("utf-8-sig"))["failures"], ensure_ascii=False))
    return ok

def hotspots(S):
    """F5 frozen-input CPU/allocation baseline of the audited risk points and new costs (not game frame time)."""
    assets, dll = native_tool(S, "TacticalHotspotCheck")
    ok = run(S, "hotspots", ["dotnet", dll, assets, GAME / "Content.zip", S / "hotspots"])
    if (S / "hotspots/hotspots.json").exists(): print(json.dumps(json.loads((S / "hotspots/hotspots.json").read_text("utf-8-sig"))["failures"], ensure_ascii=False))
    return ok

def ui(S):
    """F1/F2/F4 settings semantics and offline renders (settings file stays in the tool's own directory)."""
    _, dll = native_tool(S, "FollowupCheck")
    os.environ["ALSOFT_DRIVERS"] = "null"
    ok = run(S, "ui", ["dotnet", dll, GAME / "Content.zip", S / "ui"])
    if (S / "ui/followup.json").exists(): print(json.dumps(json.loads((S / "ui/followup.json").read_text("utf-8-sig"))["failures"], ensure_ascii=False))
    return ok

def build(S):
    ok = True
    for edition in ["lite", "full"]:
        for part in ["core", "agents", "voice"]:
            proj = next((S / edition / part / "source").glob("*.csproj"))
            if not run(S, f"build-{edition}-{part}", ["dotnet", "build", proj, "-c", "Release", "--nologo", "-v:q"]): raise SystemExit(1)
    for t in ["PackageCheck", "TacticalLoadCheck", "SplitCheck"]:
        ok &= run(S, "build-tool-" + t, ["dotnet", "build", S / "tree/tools" / t / (t + ".csproj"), "-c", "Release", "--nologo", "-v:q"])
    dump(S / "builds.json", {f"{e}/{p}": sha((S / e / p / "source/bin/Release/net10.0" / f"{a}.dll").read_bytes())
                             for e in ["lite", "full"] for p, a in [("core", "ScCsgoKnives"), ("agents", "ScCsgoTactical"), ("voice", "ScCsgoVoice")]})
    if not ok: raise SystemExit(1)

def crlf(text): return text.replace("\r\n", "\n").replace("\n", "\r\n")

def package(S):
    builds = json.loads((S / "builds.json").read_text("utf8")); result = {}
    tree = S / "tree"
    xdb = crlf((tree / "src/ScCsgoTactical/Assets/ScTactical.xdb").read_text("utf8"))
    manifest_src = (tree / "src/ScCsgoKnives/Assets/ScCompatibilityManifest.xml").read_text("utf8")
    wanted = [l.strip() for l in manifest_src.splitlines() if l.strip().startswith("<Subsystem ")]
    for label in ["全量", "轻量", "探员"]:
        source = baseline(label); assert sha(source.read_bytes()) == BASELINES[label]
        edition = "full" if label == "全量" else "lite"; changes = {}
        def dll(part, assembly):
            b = (S / edition / part / "source/bin/Release/net10.0" / f"{assembly}.dll").read_bytes(); assert sha(b) == builds[f"{edition}/{part}"]; return b
        if label != "探员": changes["ScCsgoKnives.dll"] = dll("core", "ScCsgoKnives")
        if label != "轻量": changes["ScCsgoTactical.dll"] = dll("agents", "ScCsgoTactical"); changes["ScCsgoVoice.dll"] = dll("voice", "ScCsgoVoice")
        with zipfile.ZipFile(source) as z:
            entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
            hashes = {n: sha(z.read(n)) for n in z.namelist()}
            if "Assets/ScTactical.xdb" in hashes and z.read("Assets/ScTactical.xdb").decode("utf8") != xdb: changes["Assets/ScTactical.xdb"] = xdb.encode("utf8")
            if "Assets/ScCompatibilityManifest.xml" in hashes:
                text = z.read("Assets/ScCompatibilityManifest.xml").decode("utf8"); nl = "\r\n" if "\r\n" in text else "\n"
                missing = [w for w in wanted if w not in text]
                for line in missing:  # keep edition-specific attributes/extra entries; append missing subsystems in place
                    anchor = text.rindex("  <Subsystem Name=\"Tactical"); end = text.index(nl, anchor) + len(nl)
                    text = text[:end] + "  " + line + nl + text[end:]
                if missing: changes["Assets/ScCompatibilityManifest.xml"] = text.encode("utf8")
            if label != "轻量":
                for res in RESOURCES:
                    staged = S / "resources" / edition / res
                    if staged.exists(): assert res in hashes or res in NEW_MEMBERS, res; changes[res] = staged.read_bytes()
                for m, data in tactical_assets("full" if label == "全量" else "agents"):
                    if hashes.get(m) != sha(data): changes[m] = data; NEW_MEMBERS.add(m)
            if VERSION != BASELINE_VERSION:   # the family's version in every member that names it (as the 1.5.0 Full delivery did)
                for n in ["modinfo.json", "Integrations/ScCsgoKnives.modinfo.json", "Integrations/ScCsgoTactical.modinfo.json"]:
                    if n in hashes: info = json.loads(z.read(n).decode("utf-8-sig")); info["Version"] = VERSION; changes[n] = (json.dumps(info, ensure_ascii=False, indent=2) + "\n").encode("utf8")
                for n in ["INSTALL.txt", "Integrations/ScCsgoBundle.json"]:
                    if n in hashes: changes[n] = z.read(n).decode("utf-8-sig").replace(BASELINE_VERSION, VERSION).encode("utf8")
            for n, b in changes.items(): assert n in hashes or n in NEW_MEMBERS, "unexpected new member " + n; entries[n] = member(b); hashes[n] = sha(b)
            target = S / "candidate" / name(label); target.parent.mkdir(parents=True, exist_ok=True); write_archive(target, entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and {n: sha(out.read(n)) for n in out.namelist()} == hashes
                for n in z.namelist():
                    if n not in changes: assert raw_member(out, out.getinfo(n)) == raw_member(z, z.getinfo(n))
        result[label] = dict(file=target.name, bytes=target.stat().st_size, sha256=sha(target.read_bytes()), baselineSha256=BASELINES[label],
                             changed={n: hashes[n] for n in sorted(changes)}, unchangedMembers=len(hashes) - len(changes))
        print(label, target.stat().st_size, sorted(changes), flush=True)
    dump(S / "packages.json", result)

def checks(S):
    pk = json.loads((S / "packages.json").read_text("utf8")); c = lambda l: S / "candidate" / pk[l]["file"]
    content = GAME / "Content.zip"; tool = lambda t: S / "tree/tools" / t / "bin/Release/net10.0" / (t + ".dll")
    full, lite, agents = c("全量"), c("轻量"), c("探员")
    jobs = [("ai-full", ["dotnet", tool("PackageCheck"), "--scmod", full, "--tactical-package", full, "--tactical-ai-only", "--vanilla-content", content, "--json", S / "ai-full.json"]),
            ("ai-lite", ["dotnet", tool("PackageCheck"), "--scmod", lite, "--tactical-package", agents, "--tactical-ai-only", "--vanilla-content", content, "--json", S / "ai-lite.json"]),
            ("split-core", ["dotnet", tool("SplitCheck"), lite, agents, content, "core", S / "split-core.json"]),
            ("split-agents", ["dotnet", tool("SplitCheck"), lite, agents, content, "agents", S / "split-agents.json"]),
            ("native-full", ["dotnet", tool("TacticalLoadCheck"), "--world-resource-gate", full, S / "native-full.json"])]
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results = dict(zip([j[0] for j in jobs], pool.map(lambda j: run(S, *j), jobs)))
    dump(S / "checks.json", results)
    for key in ["ai-full", "ai-lite"]:
        if (S / f"{key}.json").exists():
            d = json.loads((S / f"{key}.json").read_text("utf-8-sig"))
            print(key, "failed", d["failed"], "of", len(d["checks"]), [x["Name"] for x in d["checks"] if not x["Ok"]])
    return all(results.values())

if __name__ == "__main__":
    tag, step = sys.argv[1], sys.argv[2]; S = BASE / tag
    steps = {"prepare": prepare, "build": build, "air": air, "appearance": appearance, "package": package, "checks": checks, "motion": motion, "hotspots": hotspots, "ui": ui}
    failed = [s for s in (["prepare", "build", "air", "appearance", "package", "checks", "motion", "hotspots", "ui"] if step == "all" else [step]) if steps[s](S) is False]
    print("failed steps:", failed); raise SystemExit(1 if failed else 0)
