"""1.4.0 Full / Lite / Agents from the accepted 1.3.0 deliveries (agent-feedback-20260928).

Inputs are fixed: the three current 1.3.0 packages (exact SHA-256), the accepted capacity core build stages and the
feedback-fixes tactical/voice stages. Only gameplay DLLs, the tactical database, the compatibility manifest and
version text change; every other member keeps its original compressed bytes. Never installs to Mods or touches
worlds. Steps: prepare -> build -> package -> checks -> deliver.
"""
import argparse, concurrent.futures, hashlib, json, os, shutil, subprocess, sys, zipfile, zlib
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from pack_single_scmods import raw_member, write_archive

VERSION = "1.4.0"
REVISION = "agent-feedback-20260928"
S = ROOT / ".tmp/release-140-20260928"
CAP = ROOT / ".tmp/capacity-fix-20260927"
FB = ROOT / ".tmp/feedback-fixes-130-20260927"
GAME = Path(r"D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1")
BASELINES = {
    "全量": "dc902a45775d82682ddc0cc17d6aa94a38872ba26311770d96c5ac932a109a97",
    "轻量": "3399b7025fc011ff037ef97bc5f491bea381424a29da260f7430504456d0f332",
    "探员": "06a441b95ccb97fd99e1806db83d75b151b7e5bb4ac4d3f0e5465552769934bd",
}
COMPANIONS_GUID = "46bc6693-04ff-5a6f-a3e8-6658bbcc217b"
COMPANIONS_CLASS_GUID = "6de1e09f-27f7-5699-93d0-2f0afa51ca2a"
RESILIENCE_GUIDS = ["71cd2557-08cd-599b-8414-f93cc05196df", "9248012a-f94a-55b3-9248-16d14c83dd97", "66f825fb-7aa0-5f15-a34d-3fc8fe252cb9"]

def sha(b): return hashlib.sha256(b).hexdigest()
def dump(p, data):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", "utf8")
def member(data):
    c = zlib.compressobj(9, zlib.DEFLATED, -15); packed = c.compress(data) + c.flush()
    return (8, zlib.crc32(data), len(data), packed) if len(packed) < len(data) else (0, zlib.crc32(data), len(data), data)
def old_name(label): return f"[API1.9]CS武器1.3.0-{label}包.scmod"
def new_name(label): return f"[API1.9]CS武器{VERSION}-{label}包.scmod"
IGNORE = shutil.ignore_patterns("bin", "obj", "__pycache__")

def csproj(assembly, constants, references, extra_props="", embedded=""):
    refs = "".join(f'<Reference Include="{Path(p).stem}"><HintPath>{Path(p).as_posix()}</HintPath></Reference>' for p in references)
    return (f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Game</RootNamespace>'
            f'<GenerateDependencyFile>false</GenerateDependencyFile><Nullable>disable</Nullable><LangVersion>preview</LangVersion>'
            f'<DefineConstants>{constants}</DefineConstants><DebugType>none</DebugType><AssemblyName>{assembly}</AssemblyName>{extra_props}</PropertyGroup>'
            f'<ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{refs}{embedded}</ItemGroup></Project>')

def prepare():
    if S.exists(): raise SystemExit(f"{S} exists; use a new stage for a new input set")
    (S / "baseline").mkdir(parents=True)
    for label, expected in BASELINES.items():
        src = ROOT / "output" / old_name(label); dst = S / "baseline" / old_name(label)
        shutil.copyfile(src, dst); assert sha(dst.read_bytes()) == expected, f"{label} delivery changed"
    # Fixed source snapshot of the synced tree (code, tools, text assets). Large resources are not used from here.
    tree = S / "tree"
    for part in ["src/ScCsgoKnives", "src/ScCsgoTactical", "src/ScCsgoVoice", "src/ScCsgoAppearance", "tools"]:
        shutil.copytree(ROOT / part, tree / part, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__", "AnimationData", "Assets", "*.scmod"))
    for f in ["Directory.Build.targets", "nuget.config"]: shutil.copyfile(ROOT / f, tree / f)
    shutil.copytree(ROOT / "src/ScCsgoTactical/Assets", tree / "src/ScCsgoTactical/Assets", dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("*.glb", "*.png", "*.webp", "*.wav", "*.ogg", "*.scanim", "*.bin", "*.dds"))
    hashes = {p.relative_to(tree).as_posix(): sha(p.read_bytes()) for p in tree.rglob("*") if p.is_file() and p.suffix in (".cs", ".csproj", ".xdb", ".xml", ".json", ".py")}
    core_cs = [p for p in (tree / "src/ScCsgoKnives").rglob("*.cs")]
    tactical_cs = [p for p in (tree / "src/ScCsgoTactical").glob("*.cs")]
    # Core: accepted capacity stage (embedded AnimationData/Shaders, split shims, refs) + current shared sources.
    for edition, stage, constants, refs in [("lite", CAP / "lite", "SC_SPLIT;SC_RESOURCE_ZSTD", ["ScCsgoResources.dll", "ScCsgoResourceCodec.dll"]),
                                            ("full", CAP / "full", "", ["ScCsgoResources.dll"])]:
        dest = S / edition / "core/source"
        shutil.copytree(stage / "source", dest, ignore=IGNORE)
        shutil.copytree(stage / "refs", S / edition / "core/refs")
        for p in core_cs:
            t = dest / p.relative_to(tree / "src/ScCsgoKnives"); t.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, t)
        if edition == "lite": shutil.copyfile(tree / "src/ScCsgoTactical/TacticalBlocks.cs", dest / "TacticalBlocks.cs")
        (dest / "ScCsgoKnives.csproj").write_text(csproj("ScCsgoKnives", constants, [f"../refs/{r}" for r in refs],
            "<GenerateAssemblyInfo>false</GenerateAssemblyInfo>", '<EmbeddedResource Include="AnimationData/*.json;Shaders/*.vsh;Shaders/*.psh"/>'), "utf8")
        # Report (do not silently change) embedded data that differs from the live source tree.
        live = ROOT / "src/ScCsgoKnives"
        drift = [p.relative_to(dest).as_posix() for p in list(dest.glob("AnimationData/*.json")) + list(dest.glob("Shaders/*"))
                 if (live / p.relative_to(dest)).exists() and sha((live / p.relative_to(dest)).read_bytes()) != sha(p.read_bytes())]
        hashes[f"{edition}-embedded-drift"] = drift
    # Tactical and voice: accepted feedback-fixes stages (embedded ArmData) + current sources.
    for edition, stage, constants, skip in [("lite", FB / "agents/source", "SC_SPLIT;SC_RESOURCE_ZSTD", {"TacticalBlocks.cs"}), ("full", FB / "full/agents/source", "", set())]:
        dest = S / edition / "agents/source"
        shutil.copytree(stage, dest, ignore=IGNORE)
        for old in dest.glob("*.cs"): old.unlink()
        for p in tactical_cs:
            if p.name not in skip: shutil.copyfile(p, dest / p.name)
        core = (S / edition / "core/source/bin/Release/net10.0/ScCsgoKnives.dll")
        (dest / "ScCsgoTactical.csproj").write_text(csproj("ScCsgoTactical", constants, [core],
            "<AssemblyVersion>1.2.0.0</AssemblyVersion><Version>1.3.0</Version><ImplicitUsings>enable</ImplicitUsings>", '<EmbeddedResource Include="ArmData/*"/>'), "utf8")
        voice = S / edition / "voice/source"; voice.mkdir(parents=True)
        for p in (tree / "src/ScCsgoVoice").glob("*.cs"): shutil.copyfile(p, voice / p.name)
        (voice / "ScCsgoVoice.csproj").write_text(csproj("ScCsgoVoice", "", [core], "<Version>1.0.0</Version><ImplicitUsings>enable</ImplicitUsings>"), "utf8")
    dump(S / "source-hashes.json", hashes)
    print("prepared", S, "embedded drift:", hashes["lite-embedded-drift"], hashes["full-embedded-drift"])

def refresh_tools():
    """Re-snapshot tools only (check fixes after prepare); gameplay sources stay as prepared."""
    tree = S / "tree/tools"; shutil.rmtree(tree)
    shutil.copytree(ROOT / "tools", tree, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__", "*.scmod"))
    ok = all([run("build-tool-" + t, ["dotnet", "build", tree / t / (t + ".csproj"), "-c", "Release", "--nologo", "-v:q"]) for t in ["PackageCheck", "TacticalLoadCheck", "SplitCheck", "CompatibilityCheck", "InventoryCheck"]])
    dump(S / "tools-hashes.json", {p.relative_to(tree).as_posix(): sha(p.read_bytes()) for p in tree.rglob("*.cs")})
    raise SystemExit(0 if ok else 1)

def run(label, args, cwd=None):
    logs = S / "logs"; logs.mkdir(exist_ok=True)
    env = os.environ.copy(); env["SC_NMM_CHECK_PACKAGE"] = str(GAME / "Mods/[API1.9]NekoMeko Model-v1.1.scmod")
    with (logs / (label + ".log")).open("w", encoding="utf8") as log:
        # Check tools resolve fixtures (tools/fixtures, src/*/Assets) relative to the project root.
        r = subprocess.run([str(a) for a in args], cwd=cwd or ROOT, stdout=log, stderr=subprocess.STDOUT, env=env)
    print(label, "PASS" if r.returncode == 0 else f"FAIL ({r.returncode}) see logs/{label}.log", flush=True)
    return r.returncode == 0

def build():
    ok = True
    for edition in ["lite", "full"]:
        for part in ["core", "agents", "voice"]:
            proj = next((S / edition / part / "source").glob("*.csproj"))
            ok &= run(f"build-{edition}-{part}", ["dotnet", "build", proj, "-c", "Release", "--nologo", "-v:q"])
            if not ok: raise SystemExit(1)
    tools = S / "tree/tools"
    for t in ["PackageCheck", "TacticalLoadCheck", "SplitCheck", "CompatibilityCheck", "InventoryCheck"]:
        ok &= run("build-tool-" + t, ["dotnet", "build", tools / t / (t + ".csproj"), "-c", "Release", "--nologo", "-v:q"])
    dlls = {f"{e}/{p}": sha((S / e / p / "source/bin/Release/net10.0" / f"{a}.dll").read_bytes())
            for e in ["lite", "full"] for p, a in [("core", "ScCsgoKnives"), ("agents", "ScCsgoTactical"), ("voice", "ScCsgoVoice")]}
    dump(S / "builds.json", dlls); print(json.dumps(dlls, indent=1))
    raise SystemExit(0 if ok else 1)

def patch_xdb(text):
    nl = "\r\n" if "\r\n" in text else "\n"
    anchor = '      <Parameter Name="Class" Guid="db6bfd08-3d65-5956-a943-314062a63f2d" Value="Game.SubsystemTacticalBombs" Type="string" />' + nl + "    </MemberSubsystemTemplate>" + nl
    assert text.count(anchor) == 1 and COMPANIONS_GUID not in text
    add = (f'    <MemberSubsystemTemplate Name="TacticalCompanions" Guid="{COMPANIONS_GUID}" InheritanceParent="fefb9590-4972-4893-b02a-76063611b745">{nl}'
           f'      <Parameter Name="Class" Guid="{COMPANIONS_CLASS_GUID}" Value="Game.SubsystemTacticalCompanions" Type="string" />{nl}    </MemberSubsystemTemplate>{nl}')
    text = text.replace(anchor, anchor + add)
    for g in RESILIENCE_GUIDS:
        old = f'<Parameter Name="AttackResilience" Guid="{g}" Value="60" Type="float" />'
        assert text.count(old) == 1; text = text.replace(old, old.replace('Value="60"', 'Value="180"'))
    return text

def patch_manifest(text):
    nl = "\r\n" if "\r\n" in text else "\n"
    anchor = '  <Subsystem Name="TacticalEnemies"'
    assert text.count(anchor) == 1 and "TacticalCompanions" not in text
    return text.replace(anchor, f'  <Subsystem Name="TacticalCompanions" Guid="{COMPANIONS_GUID}" />{nl}' + anchor)

FEATURES = ("1.4.0探员改进：同伴走远后回来保留主人、指令和全部装备；玩家的手雷、燃烧、闪光和C4不再伤到自己的同伴（世界开启友伤时除外），敌方的也不再伤敌方；"
            "敌队下包后会离开爆炸范围，手动召唤的敌队在18格以外出现并有3秒预警；同伴和敌人能跳上一格台阶；自己放的C4可以拆除，创造模式直接5秒拆弹，"
            "生存拆除敌队C4有一次性材料奖励；投掷道具和指挥同伴时自动语音（语音设置中可关闭）。同伴和敌队更耐打。")

def install_text(label, old):
    if label == "全量":
        assert "CS武器1.3.0总包（含战术1.5.1和中英探员语音1.1.0）" in old
        text = old.replace("CS武器1.3.0总包（含战术1.5.1和中英探员语音1.1.0）", f"CS武器{VERSION}全量包（含探员与中英探员语音）")
    else:
        note = "本次为1.3.0无损资源压缩版。已有探员包的玩家请同时更新本次轻量包与探员包；公开版本相同不代表旧轻量包具备新资源解码器。模型、512颜色贴图和全部动作不变。"
        assert note in old and "CS武器 1.3.0 分体版" in old
        text = old.replace(note, f"{VERSION}轻量包与{VERSION}探员包必须一起更新；旧1.3.0探员包不能搭配{VERSION}轻量包，反之亦然。模型、贴图和动作不变。")
        text = text.replace("CS武器 1.3.0 分体版", f"CS武器 {VERSION} 分体版").replace("本次新的1.3.0轻量包", f"{VERSION}轻量包")
    assert "1.3.0" not in text.replace("旧1.3.0", "")
    return text.rstrip() + "\n" + FEATURES + "\n"

def package():
    builds = json.loads((S / "builds.json").read_text("utf8"))
    def dll(edition, part, assembly):
        b = (S / edition / part / "source/bin/Release/net10.0" / f"{assembly}.dll").read_bytes(); assert sha(b) == builds[f"{edition}/{part}"]; return b
    from package_display import presentation
    result = {}
    for label in ["全量", "轻量", "探员"]:
        source = S / "baseline" / old_name(label); assert sha(source.read_bytes()) == BASELINES[label]
        edition = "full" if label == "全量" else "lite"
        changes = {}
        if label in ("全量", "轻量"): changes["ScCsgoKnives.dll"] = dll(edition, "core", "ScCsgoKnives")
        if label in ("全量", "探员"):
            changes["ScCsgoTactical.dll"] = dll(edition, "agents", "ScCsgoTactical")
            changes["ScCsgoVoice.dll"] = dll(edition, "voice", "ScCsgoVoice")
        with zipfile.ZipFile(source) as z:
            entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
            hashes = {n: sha(z.read(n)) for n in z.namelist()}
            def text(n): return z.read(n).decode("utf-8")
            if "Assets/ScTactical.xdb" in hashes: changes["Assets/ScTactical.xdb"] = patch_xdb(text("Assets/ScTactical.xdb")).encode("utf-8")
            if "Assets/ScCompatibilityManifest.xml" in hashes: changes["Assets/ScCompatibilityManifest.xml"] = patch_manifest(text("Assets/ScCompatibilityManifest.xml")).encode("utf-8")
            core_sha = sha(changes["ScCsgoKnives.dll"]) if "ScCsgoKnives.dll" in changes else None
            for n in ["modinfo.json", "Integrations/ScCsgoKnives.modinfo.json", "Integrations/ScCsgoBundle.json", "Integrations/CompatibilityFamily.json",
                      "Integrations/ScSplit.json", "Integrations/ScMobileCommon.json"]:
                if n not in hashes: continue
                meta = json.loads(z.read(n))
                if n.endswith("modinfo.json") and label != "探员":
                    meta["Version"] = VERSION; meta["Name"], meta["Description"] = presentation(VERSION, edition)
                elif n == "modinfo.json":
                    meta["Version"] = VERSION; meta["Name"] = f"CS武器 · {VERSION}探员包"
                    meta["Description"] = meta["Description"].replace("配套新1.3.0轻量包", f"配套{VERSION}轻量包")
                    assert "1.3.0" not in meta["Description"]
                    meta["Dependencies"] = {"zh667.ScCsgoKnives": VERSION}
                elif n.endswith("ScCsgoBundle.json"): meta.update(version=VERSION, core=VERSION, coreSha256=core_sha)
                elif n.endswith("CompatibilityFamily.json"): meta.update(version=VERSION, core_sha256=core_sha, build_revision=REVISION)
                elif n.endswith("ScSplit.json"): meta.update(version=VERSION, build_revision=REVISION)
                else: meta.update(version=VERSION, revision=REVISION)
                changes[n] = (json.dumps(meta, ensure_ascii=False, indent=2) + ("\n" if label == "探员" and n == "modinfo.json" else "")).encode("utf8")
            changes["INSTALL.txt"] = install_text(label, text("INSTALL.txt")).encode("utf-8")
            for n, b in changes.items(): assert n in hashes, "unexpected new member " + n; entries[n] = member(b); hashes[n] = sha(b)
            target = S / "candidate" / new_name(label); target.parent.mkdir(exist_ok=True); write_archive(target, entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and {n: sha(out.read(n)) for n in out.namelist()} == hashes
                for n in z.namelist():
                    if n not in changes: assert raw_member(out, out.getinfo(n)) == raw_member(z, z.getinfo(n))
        if label != "全量": assert target.stat().st_size < 40_000_000
        result[label] = dict(file=target.name, bytes=target.stat().st_size, sha256=sha(target.read_bytes()), baseline=source.name,
                             baselineSha256=BASELINES[label], changed={n: hashes[n] for n in sorted(changes)}, unchangedMembers=len(hashes) - len(changes))
        print(label, target.name, target.stat().st_size, flush=True)
    dump(S / "packages.json", result)

def checks():
    pk = json.loads((S / "packages.json").read_text("utf8")); c = lambda l: S / "candidate" / pk[l]["file"]
    content = GAME / "Content.zip"; tools = S / "tree/tools"
    tool = lambda t: tools / t / "bin/Release/net10.0" / (t + ".dll")
    full, lite, agents = c("全量"), c("轻量"), c("探员")
    jobs = [("package-full", ["dotnet", tool("PackageCheck"), "--scmod", full, "--sha256", pk["全量"]["sha256"], "--vanilla-content", content, "--json", S / "package-full.json"]),
            ("package-lite", ["dotnet", tool("PackageCheck"), "--scmod", lite, "--sha256", pk["轻量"]["sha256"], "--vanilla-content", content, "--json", S / "package-lite.json"]),
            ("native-full", ["dotnet", tool("TacticalLoadCheck"), "--world-resource-gate", full, S / "native-full.json"]),
            ("native-lite", ["dotnet", tool("TacticalLoadCheck"), "--world-resource-gate", lite, S / "native-lite.json"]),
            ("native-hooks", ["dotnet", tool("TacticalLoadCheck"), "--compat-native", full, content, S / "native-hooks.json"]),
            ("ai-full", ["dotnet", tool("PackageCheck"), "--scmod", full, "--tactical-package", full, "--tactical-ai-only", "--vanilla-content", content, "--json", S / "ai-full.json"]),
            ("ai-lite", ["dotnet", tool("PackageCheck"), "--scmod", lite, "--tactical-package", agents, "--tactical-ai-only", "--vanilla-content", content, "--json", S / "ai-lite.json"]),
            ("tactical-full-suite", ["dotnet", tool("PackageCheck"), "--scmod", full, "--tactical-package", full, "--vanilla-content", content, "--json", S / "tactical-full-suite.json"]),
            ("family", ["dotnet", tool("CompatibilityCheck"), CAP / "1.0.0/src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll",
                        CAP / "1.2.0/src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll", S / "full/core/source/bin/Release/net10.0/ScCsgoKnives.dll", S / "family.json"]),
            ("inventory", ["dotnet", tool("InventoryCheck"), S / "full/core/source/bin/Release/net10.0/ScCsgoKnives.dll", GAME / "Mods", S / "inventory.json"])]
    for role, core, addon in [("full", full, full), ("lite", lite, agents)]:
        for variant in ["both", "reversed", "none"]:
            jobs.append((f"integration-{role}-{variant}", ["dotnet", tool("TacticalLoadCheck"), ROOT, content, addon, variant, S / f"integration-{role}-{variant}.json", core]))
    for variant in ["core", "agents"]:
        jobs.append((f"split-{variant}", ["dotnet", tool("SplitCheck"), lite, agents, content, variant, S / f"split-{variant}.json"]))
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        results = dict(zip([j[0] for j in jobs], pool.map(lambda j: run(*j), jobs)))
    dump(S / "checks.json", results)
    raise SystemExit(0 if all(v for k, v in results.items() if k != "tactical-full-suite") else 1)

def deliver():
    pk = json.loads((S / "packages.json").read_text("utf8")); report = {}
    for label, p in pk.items():
        src = S / "candidate" / p["file"]; dst = ROOT / "output" / p["file"]
        assert sha(src.read_bytes()) == p["sha256"]
        if dst.exists(): raise SystemExit(f"{dst.name} already exists in output; not overwriting")
        shutil.copyfile(src, dst); assert sha(dst.read_bytes()) == p["sha256"]; report[label] = dict(path=str(dst), bytes=dst.stat().st_size, sha256=p["sha256"])
    dump(ROOT / "output/release-1.4.0/manifest.json", dict(version=VERSION, revision=REVISION, packages=report, stage=str(S)))
    print(json.dumps(report, ensure_ascii=False, indent=1))

if __name__ == "__main__":
    step = argparse.ArgumentParser(); step.add_argument("step", choices=["prepare", "build", "refresh-tools", "package", "checks", "deliver"])
    {"prepare": prepare, "build": build, "refresh-tools": refresh_tools, "package": package, "checks": checks, "deliver": deliver}[step.parse_args().step]()
