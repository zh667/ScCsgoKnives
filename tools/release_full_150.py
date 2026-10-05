"""Build the requested 1.5.0 Full delivery, preserving the pinned 1.4.0 resources.

Run through tools/dev.ps1: python tools/release_full_150.py prepare|build|package|checks|deliver.
No installation, world access or changes to other editions. Evidence stays with the release.
"""
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from pack_single_scmods import raw_member, write_archive

STAGE = ROOT / ".tmp/dev-temp/release-full-150"
REPORT = ROOT / "output/release-1.5.0"
GAME = Path(r"D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1")
OLD = ROOT / "output/[API1.9]CS武器1.4.0-全量包.scmod"
OLD_LITE = ROOT / "output/[API1.9]CS武器1.4.0-轻量包.scmod"
NAME = "[API1.9]CS武器1.5.0-全量包.scmod"
PIN = "ec949ff420c509b58935e1203bd8582381f7790107f9a51aa0e35d884ff74b94"
LITE_PIN = "b9598e861ff7e3a39196086d10944b6158a9cb89a712b5cacbd984ca0e10d5fe"
PRIOR_PIN = "50f95500342a29a38c85e772d0486c861152deb2b7a3f7c442477b0901b95b93"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", "utf8")


def read(path):
    return json.loads(path.read_text("utf-8-sig"))


def run(label, command):
    logs = STAGE / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    with (logs / (label + ".log")).open("w", encoding="utf8") as log:
        result = subprocess.run([str(a) for a in command], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    receipt = dict(command=[str(a) for a in command], exitCode=result.returncode, seconds=round(time.monotonic()-start, 2))
    dump(logs / (label + ".json"), receipt)
    print(label, receipt["exitCode"], receipt["seconds"], flush=True)
    return result.returncode == 0


def project(name, references, compile_items="", embedded="", extra=""):
    refs = ''.join(f'<Reference Include="{p.stem}"><HintPath>{p.as_posix()}</HintPath></Reference>' for p in references)
    return ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            f'<AssemblyName>{name}</AssemblyName><RootNamespace>Game</RootNamespace><LangVersion>preview</LangVersion>'
            f'<Nullable>disable</Nullable><DebugType>none</DebugType>{extra}</PropertyGroup><ItemGroup>'
            f'<PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{refs}{compile_items}{embedded}'
            '</ItemGroup></Project>')


def prepare():
    assert not STAGE.exists(), "Stage already exists; inspect before rebuilding"
    assert sha(OLD.read_bytes()) == PIN and sha(OLD_LITE.read_bytes()) == LITE_PIN
    STAGE.mkdir(parents=True)
    inputs = {}
    for role, package in [("old-full", OLD), ("old-lite", OLD_LITE)]:
        with zipfile.ZipFile(package) as z:
            for name in z.namelist():
                if name.endswith(".dll"):
                    target = STAGE / role / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(z.read(name))
    for name, short in [("ScCsgoKnives", "core"), ("ScCsgoTactical", "tactical")]:
        source = ROOT / "src" / name
        dest = STAGE / short
        dest.mkdir()
        for p in source.rglob("*.cs"):
            rel = p.relative_to(source)
            if any(s in ("bin", "obj") for s in rel.parts):
                continue
            target = dest / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(p, target)
            inputs[p.relative_to(ROOT).as_posix()] = sha(p.read_bytes())
    assert run("build-resource-reader", ["dotnet", "build", ROOT / "tools/CompatibilityCheck", "-c", "Release", "--nologo", "-v:q"])
    embedded()
    for short in ["core", "tactical"]:
        for p in (STAGE / short).rglob("*"):
            if p.is_file(): inputs[p.relative_to(STAGE).as_posix()] = sha(p.read_bytes())
    identity = sha(json.dumps(inputs, sort_keys=True).encode())[:32]
    for short in ["core", "tactical"]:
        (STAGE / short / "ScGameplayIdentity.g.cs").write_text(
            f'[assembly: System.Reflection.AssemblyMetadata("ScGameplayIdentity", "{identity}")]\n', "utf8")
    resources = STAGE / "old-full/ScCsgoResources.dll"
    core = STAGE / "core/bin/Release/net10.0/ScCsgoKnives.dll"
    (STAGE / "core/ScCsgoKnives.csproj").write_text(project("ScCsgoKnives", [resources],
        embedded='<EmbeddedResource Include="AnimationData/*.json;Shaders/*.vsh;Shaders/*.psh"/>',
        extra='<GenerateAssemblyInfo>false</GenerateAssemblyInfo>'), "utf8")
    (STAGE / "tactical/ScCsgoTactical.csproj").write_text(project("ScCsgoTactical", [core],
        embedded='<EmbeddedResource Include="ArmData/*"/>',
        extra='<ImplicitUsings>enable</ImplicitUsings><Version>1.5.0</Version><AssemblyVersion>1.2.0.0</AssemblyVersion>'), "utf8")
    dump(STAGE / "inputs.json", dict(commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        identity=identity, files=inputs, baselineSha256=PIN, baselineLiteSha256=LITE_PIN))


def build():
    # Refresh a single working stage after a scoped fix, without accumulating full package copies.
    inputs = read(STAGE / "inputs.json")
    for name, short in [("ScCsgoKnives", "core"), ("ScCsgoTactical", "tactical")]:
        source = ROOT / "src" / name
        for p in source.rglob("*.cs"):
            rel = p.relative_to(source)
            if any(s in ("bin", "obj") for s in rel.parts): continue
            target = STAGE / short / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(p, target)
            inputs["files"][p.relative_to(ROOT).as_posix()] = sha(p.read_bytes())
            inputs["files"][short + "/" + rel.as_posix()] = sha(p.read_bytes())
    for folder in ["core/AnimationData", "core/Shaders", "tactical/ArmData"]:
        for p in (STAGE / folder).glob("*"):
            if p.is_file(): inputs["files"][p.relative_to(STAGE).as_posix()] = sha(p.read_bytes())
    inputs["identity"] = sha(json.dumps(inputs["files"], sort_keys=True).encode())[:32]
    for short in ["core", "tactical"]:
        (STAGE / short / "ScGameplayIdentity.g.cs").write_text(
            f'[assembly: System.Reflection.AssemblyMetadata("ScGameplayIdentity", "{inputs["identity"]}")]\n', "utf8")
    dump(STAGE / "inputs.json", inputs)
    for short, name in [("core", "ScCsgoKnives"), ("tactical", "ScCsgoTactical")]:
        assert run("build-" + short, ["dotnet", "build", STAGE / short / (name + ".csproj"), "-c", "Release", "--nologo", "-v:q"])


def package():
    assert sha(OLD.read_bytes()) == PIN
    changes = {name + ".dll": (STAGE / short / "bin/Release/net10.0" / (name + ".dll")).read_bytes()
               for short, name in [("core", "ScCsgoKnives"), ("tactical", "ScCsgoTactical")]}
    with zipfile.ZipFile(OLD) as z:
        entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
        hashes = {n: sha(z.read(n)) for n in z.namelist()}
        for name in ["modinfo.json", "Integrations/ScCsgoKnives.modinfo.json", "Integrations/ScCsgoTactical.modinfo.json"]:
            if name in entries:
                info = json.loads(z.read(name)); info["Version"] = "1.5.0"
                changes[name] = (json.dumps(info, ensure_ascii=False, indent=2) + "\n").encode()
        for name in ["INSTALL.txt", "Integrations/ScCsgoBundle.json", "Assets/ScCompatibilityManifest.xml"]:
            if name in entries:
                changes[name] = z.read(name).decode("utf-8-sig").replace("1.4.0", "1.5.0").encode("utf8")
    for name, data in changes.items():
        c = zlib.compressobj(9, zlib.DEFLATED, -15)
        entries[name] = (8, zlib.crc32(data), len(data), c.compress(data) + c.flush())
        hashes[name] = sha(data)
    candidate = STAGE / NAME
    write_archive(candidate, entries)
    with zipfile.ZipFile(candidate) as z:
        assert z.testzip() is None and set(z.namelist()) == set(entries)
        assert all(sha(z.read(n)) == h for n, h in hashes.items())
        assert json.loads(z.read("modinfo.json"))["Version"] == "1.5.0"
    if (STAGE / "package.json").exists():
        dump(STAGE / "attempts" / str(time.time_ns()) / "package.json", read(STAGE / "package.json"))
    dump(STAGE / "package.json", dict(file=NAME, bytes=candidate.stat().st_size, sha256=sha(candidate.read_bytes()),
        baselineSha256=PIN, changedMembers=sorted(changes), unchangedMembers=len(hashes)-len(changes), memberHashes=hashes))
    print(NAME, candidate.stat().st_size, flush=True)


def tool(name):
    return ROOT / "tools" / name / "bin/Release/net10.0" / (name + ".dll")


def embedded():
    for name, short in [("ScCsgoKnives", "core"), ("ScCsgoTactical", "tactical")]:
        assert run("extract-" + short, ["dotnet", tool("CompatibilityCheck"), "--extract-embedded", STAGE / "old-full" / (name + ".dll"), STAGE / short, STAGE / ("original-embedded-" + short + ".json")])


def checks(selected=None):
    import completion_140
    assert completion_140.ancient_package()
    os.environ["SC_NMM_CHECK_PACKAGE"] = str(GAME / "Mods/[API1.9]NekoMeko Model-v1.1.scmod")
    needed = {"main": "PackageCheck", "ai": "PackageCheck", "native": "TacticalLoadCheck", "hooks": "TacticalLoadCheck", "switching": "CompatibilityCheck", "switching150": "CompatibilityCheck", "embedded-core": "CompatibilityCheck", "embedded-tactical": "CompatibilityCheck", "inventory": "InventoryCheck"}
    build_tools = set(needed.values()) if selected is None else {needed[k] for k in selected if k in needed}
    for name in sorted(build_tools):
        assert run("build-" + name, ["dotnet", "build", ROOT / "tools" / name / (name + ".csproj"), "-c", "Release", "--nologo", "-v:q"])
    # Run focused gameplay checks against the very DLLs shipped, not a second source build.
    feedback = STAGE / "feedback"
    feedback.mkdir(exist_ok=True)
    includes = [ROOT / "tools/GameplayFeedbackCheck/Program.cs", ROOT / "tools/PackageCheck/TacticalEnemyRegression.cs", ROOT / "tools/PackageCheck/StarterEquipmentRegression.cs"]
    core = STAGE / "core/bin/Release/net10.0/ScCsgoKnives.dll"
    tactical = STAGE / "tactical/bin/Release/net10.0/ScCsgoTactical.dll"
    if selected is None or "switching150" in selected:
        prior = ROOT / "output" / NAME
        if not prior.exists() or sha(prior.read_bytes()) != PRIOR_PIN:
            prior = ROOT / "output/history-1.5.0" / PRIOR_PIN / NAME
        assert sha(prior.read_bytes()) == PRIOR_PIN
        refs = STAGE / "old150"; refs.mkdir(exist_ok=True)
        with zipfile.ZipFile(prior) as z:
            for name in z.namelist():
                if name.endswith(".dll"): (refs / name).write_bytes(z.read(name))
        dump(STAGE / "previous-150.json", dict(path=str(prior), sha256=PRIOR_PIN))
    ui = STAGE / "ui-tool"
    if selected is None or "ui" in selected:
        ui.mkdir(exist_ok=True)
        (ui / "FollowupCheck.csproj").write_text(project("FollowupCheck", [core, tactical, STAGE / "old-full/ScCsgoResources.dll"],
            compile_items=f'<Compile Include="{(ROOT / "tools/FollowupCheck/Program.cs").as_posix()}"/>',
            extra='<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings>'), "utf8")
        assert run("build-ui", ["dotnet", "build", ui / "FollowupCheck.csproj", "-c", "Release", "--nologo", "-v:q"])
        os.environ["ALSOFT_DRIVERS"] = "null"
    (feedback / "GameplayFeedbackCheck.csproj").write_text(project("GameplayFeedbackCheck", [core, tactical, STAGE / "old-full/ScCsgoResources.dll"],
        compile_items=''.join(f'<Compile Include="{p.as_posix()}"/>' for p in includes),
        extra='<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings>'), "utf8")
    if selected is None or "feedback" in selected:
        assert run("build-feedback", ["dotnet", "build", feedback / "GameplayFeedbackCheck.csproj", "-c", "Release", "--nologo", "-v:q"])
    candidate = STAGE / NAME
    jobs = {
        "main": ["dotnet", tool("PackageCheck"), "--scmod", candidate, "--vanilla-content", GAME / "Content.zip", "--json", STAGE / "main.json"],
        "ai": ["dotnet", tool("PackageCheck"), "--scmod", candidate, "--tactical-package", candidate, "--tactical-ai-only", "--vanilla-content", GAME / "Content.zip", "--json", STAGE / "ai.json"],
        "native": ["dotnet", tool("TacticalLoadCheck"), "--world-resource-gate", candidate, STAGE / "native.json"],
        "hooks": ["dotnet", tool("TacticalLoadCheck"), "--compat-native", candidate, GAME / "Content.zip", STAGE / "hooks.json"],
        "switching": ["dotnet", tool("CompatibilityCheck"), STAGE / "old-full/ScCsgoKnives.dll", STAGE / "old-lite/ScCsgoKnives.dll", core, STAGE / "switching.json"],
        "switching150": ["dotnet", tool("CompatibilityCheck"), STAGE / "old-full/ScCsgoKnives.dll", STAGE / "old150/ScCsgoKnives.dll", core, STAGE / "switching150.json"],
        "ui": ["dotnet", ui / "bin/Release/net10.0/FollowupCheck.dll", GAME / "Content.zip", STAGE / "ui"],
        "inventory": ["dotnet", tool("InventoryCheck"), core, GAME / "Mods", STAGE / "inventory.json"],
        "feedback": ["dotnet", feedback / "bin/Release/net10.0/GameplayFeedbackCheck.dll", STAGE / "feedback.json"],
        "embedded-core": ["dotnet", tool("CompatibilityCheck"), "--embedded", STAGE / "old-full/ScCsgoKnives.dll", core, STAGE / "embedded-core.json"],
        "embedded-tactical": ["dotnet", tool("CompatibilityCheck"), "--embedded", STAGE / "old-full/ScCsgoTactical.dll", tactical, STAGE / "embedded-tactical.json"],
    }
    for variant in ["both", "reversed", "none"]:
        key = "integration-" + variant
        jobs[key] = ["dotnet", tool("TacticalLoadCheck"), ROOT, GAME / "Content.zip", candidate, variant, STAGE / (key + ".json"), candidate]
    if selected is not None:
        assert set(selected) <= set(jobs)
        jobs = {k: v for k, v in jobs.items() if k in selected}
    # Keep failed attempts before replacing their reports; matching unchanged checks may be reused.
    previous = read(STAGE / "checks.json") if (STAGE / "checks.json").exists() else {}
    tested_packages = read(STAGE / "check-packages.json") if (STAGE / "check-packages.json").exists() else {}
    candidate_sha = read(STAGE / "package.json")["sha256"]
    previous = {k: ok for k, ok in previous.items() if tested_packages.get(k) == candidate_sha}
    for key in jobs:
        if (STAGE / (key + ".json")).exists():
            attempt = STAGE / "attempts" / str(time.time_ns())
            attempt.mkdir(parents=True)
            for p in [STAGE / (key + ".json"), STAGE / "logs" / (key + ".log"), STAGE / "logs" / (key + ".json")]:
                if p.exists(): shutil.copyfile(p, attempt / (p.parent.name + "-" + p.name))
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        result = previous | dict(zip(jobs, pool.map(lambda key: run(key, jobs[key]), jobs)))
    dump(STAGE / "checks.json", result)
    dump(STAGE / "check-packages.json", tested_packages | {key: candidate_sha for key in jobs})
    dump(STAGE / "check-inputs.json", dict(packageSha256=read(STAGE / "package.json")["sha256"],
        tools={p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for name in ["PackageCheck", "TacticalLoadCheck", "CompatibilityCheck", "InventoryCheck", "GameplayFeedbackCheck", "FollowupCheck"]
               for p in (ROOT / "tools" / name).glob("*.cs")}))
    assert all(result.values()), result


def deliver():
    results = read(STAGE / "checks.json")
    required = {"main", "ai", "native", "hooks", "switching", "switching150", "ui", "inventory", "feedback", "embedded-core", "embedded-tactical", "integration-both", "integration-reversed", "integration-none"}
    assert set(results) == required and all(results.values())
    package_info = read(STAGE / "package.json")
    assert read(STAGE / "check-inputs.json")["packageSha256"] == package_info["sha256"]
    assert all(read(STAGE / "check-packages.json").get(k) == package_info["sha256"] for k in results)
    source = STAGE / NAME
    assert sha(source.read_bytes()) == package_info["sha256"]
    target = ROOT / "output" / NAME
    # Standing authorization: replace the matching validated output, retaining the exact older release as a fixture.
    if target.exists():
        assert sha(target.read_bytes()) == PRIOR_PIN, "Unexpected current delivery; inspect before replacement"
        history = ROOT / "output/history-1.5.0" / PRIOR_PIN
        history.mkdir(parents=True, exist_ok=True)
        assert not (history / NAME).exists() and not (history / "evidence").exists()
        target.rename(history / NAME)
        if REPORT.exists(): REPORT.rename(history / "evidence")
    shutil.copyfile(source, target)
    assert sha(target.read_bytes()) == package_info["sha256"]
    with zipfile.ZipFile(target) as z:
        assert z.testzip() is None
        assert all(sha(z.read(n)) == h for n, h in package_info["memberHashes"].items())
    REPORT.mkdir(exist_ok=True)
    for p in STAGE.glob("*.json"):
        shutil.copyfile(p, REPORT / p.name)
    shutil.copytree(STAGE / "logs", REPORT / "logs", dirs_exist_ok=True)
    if (STAGE / "ui").exists(): shutil.copytree(STAGE / "ui", REPORT / "ui", dirs_exist_ok=True)
    if (STAGE / "attempts").exists(): shutil.copytree(STAGE / "attempts", REPORT / "attempts", dirs_exist_ok=True)
    dump(REPORT / "ui-inputs.json", dict(sourceSha256=sha((ROOT / "tools/FollowupCheck/Program.cs").read_bytes()),
        toolSha256=sha((STAGE / "ui-tool/bin/Release/net10.0/FollowupCheck.dll").read_bytes()),
        coreSha256=package_info["memberHashes"]["ScCsgoKnives.dll"], tacticalSha256=package_info["memberHashes"]["ScCsgoTactical.dll"]))
    dump(REPORT / "manifest.json", dict(version="1.5.0", edition="Full", path=str(target), **package_info,
        checks=results, inputs=read(STAGE / "inputs.json"), limitations=["Android device and multiplayer runtime acceptance pending", "Movement/cover/flash appearance pending user acceptance"]))
    print("Delivered", target, package_info["sha256"], flush=True)


if __name__ == "__main__":
    for step in sys.argv[1].split(","):
        if step.startswith("checks:"):
            checks(step.split(":", 1)[1].split("+"))
            continue
        {"prepare": prepare, "embedded": embedded, "build": build, "package": package, "checks": checks, "deliver": deliver}[step]()
