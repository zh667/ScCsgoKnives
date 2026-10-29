"""Isolated issue-9 candidate using the maintained package/check pipeline and read-only baseline inputs.
Usage via tools/dev.ps1: python tools/issue9_candidate.py <existing checkout> <step[,step...]>
No Mods/world/output writes. Candidate outputs stay under .tmp/dev-temp/issue9-candidate.
"""
import json
import shutil
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import completion_140 as pipeline
import api_engine_checks as api

inputs = Path(sys.argv[1]).resolve()
assert inputs != ROOT and (inputs / "output/release-1.5.0/manifest.json").is_file()
base = pipeline.base
base.REL = inputs / ".tmp/release-140-20260928"
base.BASELINE_DIR = inputs / "output"
base.BASE = ROOT / ".tmp/dev-temp"
pipeline.MP_REFS = inputs / ".tmp/mp-m0-20260929/refs/mp"
api.ENGINES = inputs / ".tmp/api-engines"
stage = base.BASE / "issue9-candidate"

def refresh():
    """Refresh the existing isolated source stage after a scoped fix, preserving its immutable build inputs."""
    tree = stage / "tree"
    for part in ["ScCsgoKnives", "ScCsgoTactical", "ScCsgoVoice", "ScCsgoAppearance"]:
        for src in (ROOT / "src" / part).rglob("*.cs"):
            rel = src.relative_to(ROOT / "src" / part)
            if "bin" in rel.parts or "obj" in rel.parts:
                continue
            dest = tree / "src" / part / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dest)
            for edition in ["full", "lite"]:
                owner = {"ScCsgoKnives": "core", "ScCsgoTactical": "agents", "ScCsgoVoice": "voice", "ScCsgoAppearance": "appearance"}[part]
                if part == "ScCsgoTactical" and rel.name == "TacticalBlocks.cs" and edition == "lite":
                    owner = "core"
                target = stage / edition / owner / "source" / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(src, target)
    for src in (ROOT / "src/ScCsgoKnives/Assets/Lang").glob("*.json"):
        shutil.copyfile(src, tree / "src/ScCsgoKnives/Assets/Lang" / src.name)
    shutil.copyfile(ROOT / "src/ScCsgoKnives/Assets/ScCsgoKnivesBlocksData.csv", tree / "src/ScCsgoKnives/Assets/ScCsgoKnivesBlocksData.csv")
    for tool in ["PackageCheck", "WeaponPresentationCheck"]:
        for src in (ROOT / "tools" / tool).glob("*"):
            if src.is_file():
                dest = tree / "tools" / tool / src.name
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(src, dest)
    gameplay = sorted((p.relative_to(tree).as_posix(), base.sha(p.read_bytes()))
                      for part in ["src/ScCsgoKnives", "src/ScCsgoTactical", "src/ScCsgoVoice"]
                      for p in (tree / part).rglob("*") if p.is_file())
    identity = base.sha(json.dumps(gameplay).encode())[:32]
    for edition in ["full", "lite"]:
        for part in ["core", "agents"]:
            (stage / edition / part / "source/ScGameplayIdentity.g.cs").write_text(
                f'[assembly: System.Reflection.AssemblyMetadata("ScGameplayIdentity", "{identity}")]\n', "utf8")
    base.dump(stage / "identity.json", {"identity": identity, "files": len(gameplay)})
    base.dump(stage / "source-hashes.json", {p.relative_to(tree).as_posix(): base.sha(p.read_bytes())
              for p in tree.rglob("*") if p.is_file() and p.suffix in (".cs", ".xdb", ".xml", ".json", ".py", ".csproj") and "bin" not in p.parts and "obj" not in p.parts})

def appearance():
    # Only the retained third-party reference lookup depends on the original checkout.
    previous = base.ROOT
    try:
        base.ROOT = inputs
        return base.appearance(stage)
    finally:
        base.ROOT = previous

def retained_inputs(step):
    previous = pipeline.ROOT
    try:
        pipeline.ROOT = inputs
        return pipeline.STEPS[step](stage)
    finally:
        pipeline.ROOT = previous

def presentation():
    project = ROOT / "tools/WeaponPresentationCheck/WeaponPresentationCheck.csproj"
    if not base.run(stage, "presentation-build", ["dotnet", "build", project, "-c", "Release", "-p:SkipScmodPackaging=true", "--nologo", "-v:q"]):
        return False
    out = project.parent / "bin/Release/net10.0"
    packages = json.loads((stage / "packages.json").read_text("utf8"))
    candidate = stage / "candidate" / packages["全量"]["file"]
    # The test process must execute the shipped candidate's assemblies, not its project-reference rebuild.
    with zipfile.ZipFile(candidate) as archive:
        for name in archive.namelist():
            if "/" not in name and name.endswith(".dll"):
                (out / name).write_bytes(archive.read(name))
    return base.run(stage, "presentation", ["dotnet", out / "WeaponPresentationCheck.dll", ROOT,
                    base.GAME / "Content.zip", stage / "presentation", candidate])

def locales():
    """Refresh a translation after binary verification, proving every non-locale member remains identical.
    The gameplay binary identity stays frozen; the final package and locale hashes are recorded separately.
    """
    def members():
        packages = json.loads((stage / "packages.json").read_text("utf8"))
        result = {}
        for label, package in packages.items():
            with zipfile.ZipFile(stage / "candidate" / package["file"]) as archive:
                result[label] = {name: base.sha(archive.read(name)) for name in archive.namelist()}
        return result
    before = members()
    sources = json.loads((stage / "source-hashes.json").read_text("utf8"))
    for source in (ROOT / "src/ScCsgoKnives/Assets/Lang").glob("*.json"):
        dest = stage / "tree/src/ScCsgoKnives/Assets/Lang" / source.name
        shutil.copyfile(source, dest)
        sources[dest.relative_to(stage / "tree").as_posix()] = base.sha(source.read_bytes())
    if retained_inputs("package") is False:
        return False
    after = members()
    changed = {label: [name for name in after[label] if before[label].get(name) != after[label][name]] for label in after}
    assert all(name.startswith("Assets/Lang/") for names in changed.values() for name in names), changed
    assert all(set(before[label]) == set(after[label]) for label in before)
    base.dump(stage / "source-hashes.json", sources)
    base.dump(stage / "locale-only.json", {"changed": changed, "allOtherMembersIdentical": True,
              "binaryIdentity": json.loads((stage / "identity.json").read_text("utf8"))["identity"]})
    return True

def current_compat():
    cores = []
    for label, edition in [("全量", "full"), ("轻量", "lite")]:
        source = base.baseline(label)
        assert base.sha(source.read_bytes()) == base.BASELINES[label]
        dest = stage / "current150" / edition
        dest.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(source) as archive:
            for name in archive.namelist():
                if name.endswith(".dll"):
                    (dest / Path(name).name).write_bytes(archive.read(name))
        cores.append(dest / "ScCsgoKnives.dll")
    return base.run(stage, "switching-current150", ["dotnet", pipeline.tool(stage, "CompatibilityCheck"), *cores,
                    stage / "full/core/source/bin/Release/net10.0/ScCsgoKnives.dll", stage / "switching-current150.json"])

def export():
    required = ["build", "appearance", "package", "gates", "compat", "api-contract", "api-suites", "netloop", "appnet", "locales", "presentation", "current-compat"]
    receipts = {name: json.loads((stage / ("step-" + name + ".json")).read_text("utf8")) for name in required}
    assert all(row["passed"] for row in receipts.values())
    packages = json.loads((stage / "packages.json").read_text("utf8"))
    output = ROOT / "output"
    output.mkdir(exist_ok=True)
    for package in packages.values():
        source = stage / "candidate" / package["file"]
        assert base.sha(source.read_bytes()) == package["sha256"]
        dest = output / package["file"]
        assert not dest.exists() or base.sha(dest.read_bytes()) == package["sha256"], "Existing review candidate differs; preserve it"
        shutil.copyfile(source, dest)
        assert base.sha(dest.read_bytes()) == package["sha256"]
    reports = {}
    for name in ["main-full", "main-lite", "baseline-main-full", "baseline-main-lite", "tactical-full-suite", "ai-full", "ai-lite", "vf-full", "vf-lite", "c4-full", "c4-lite", "family", "switching-140", "switching-150", "switching-current150", "netloop-compat", "netloop-nocompat", "appnet-full", "appnet-lite", "presentation/checks"]:
        path = stage / (name + ".json")
        report = json.loads(path.read_text("utf-8-sig"))
        checks = report.get("checks", [])
        failed = [item.get("name", item.get("Name")) for item in checks if isinstance(item, dict) and not item.get("ok", item.get("Ok", False))]
        reports[name] = {"report": str(path), "sha256": base.sha(path.read_bytes()), "checks": len(checks), "failed": failed, "errors": report.get("errors", [])}
    focus = json.loads((stage / "presentation/checks.json").read_text("utf8"))
    assert focus["resourcePackageSha256"] == packages["全量"]["sha256"]
    manifest = {"status": "candidate-awaiting-gameplay-visual-acceptance", "branch": "fix/issue9-counter-20261029",
                "baseline": "4cd1560", "packages": packages, "steps": receipts, "reports": reports,
                "nativeEngine": focus["engineApiVersion"], "nativeRunnerSha256": focus["runnerSha256"],
                "localeOnly": json.loads((stage / "locale-only.json").read_text("utf8")),
                "engineSuites": json.loads((stage / "api-engines.json").read_text("utf8")),
                "notRun": ["full-game sessions", "Android gameplay", "user visual acceptance"]}
    base.dump(output / "issue9-counter-manifest.json", manifest)
    evidence = ROOT / "docs/tasks/issue9-counter-20261029"
    evidence.mkdir(exist_ok=True)
    base.dump(evidence / "verification.json", manifest)
    for asset in ["ak47", "m4a1s", "awp", "deagle"]:
        shutil.copyfile(stage / "presentation" / (asset + "-pbr.png"), evidence / (asset + "-pbr.png"))
    return True

steps = {
    "prepare": lambda: pipeline.prepare(stage),
    "refresh": refresh,
    "build": lambda: base.build(stage),
    "appearance": appearance,
    "package": lambda: retained_inputs("package"),
    "gates": lambda: retained_inputs("gates"),
    "baseline": lambda: retained_inputs("baseline"),
    "compat": lambda: retained_inputs("compat"),
    "netloop": lambda: retained_inputs("netloop"),
    "appnet": lambda: retained_inputs("appnet"),
    "presentation": presentation,
    "locales": locales,
    "current-compat": current_compat,
    "export": export,
    "api-contract": lambda: api.contract(stage),
    "api-suites": lambda: api.suites(stage),
}
for name in sys.argv[2].split(","):
    started = time.monotonic()
    ok = steps[name]() is not False
    receipt = {"step": name, "passed": ok, "seconds": round(time.monotonic() - started, 2)}
    base.dump(stage / ("step-" + name + ".json"), receipt)
    print(json.dumps(receipt), flush=True)
    if not ok:
        raise SystemExit(1)
