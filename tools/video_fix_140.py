"""Isolated candidates for video-feedback-20260929 (S0, R1-R4) on top of the delivered revision of 1.4.0.

Usage (Windows, via tools/dev.ps1): python tools/video_fix_140.py <tag> <step>[,<step>...]|all
Same stage layout and steps as tools/followup_140.py, under .tmp/video-fix-140-20260929/<tag>. The baselines are the
three packages currently in output/: rounds s0-01 to r2-06 started from f5-01 (b60804f8, dd365605, 9052dc95); on
2026-09-29 the user had the r2-06 candidate moved into output/, so later rounds start from it.
Never writes output/, Mods or worlds; third-party packages are only read.
"""
import os, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import followup_140 as base

base.BASE = ROOT / ".tmp/video-fix-140-20260929"
base.BASELINES = {
    "全量": "9219228aa6e8203cf51c850dde2e9ad354497949bdb5170fd4e47191e0be6c66",
    "轻量": "6d9ed6ea31e03a9b43411dd54b5fe53c9f93ad967ab455a4aa7746a41a00f319",
    "探员": "e92811fa9d9d728df949793669ece2d331fdac9847136f2c43e417522bc4fa0f",
}
# Read-only third-party input of the natural-spawn budget test (S0); the test reports when it is missing.
SLOWER = base.GAME / "Mods/[1SlowerCreatureSpawnsMod.scmod"
os.environ["SC_SLOWER_CHECK_PACKAGE"] = str(SLOWER)

def native(tool, label, extra=()):
    """A tools/<tool> check built against this stage's Full DLLs; output in <stage>/<label>."""
    def step(S):
        assets, dll = base.native_tool(S, tool)
        os.environ["ALSOFT_DRIVERS"] = "null"
        return base.run(S, label, ["dotnet", dll, assets, base.GAME / "Content.zip", S / label, *extra])
    return step

def vf(S):
    """Core-side regressions of this task (R8 input, menu cancellation) on the Full and the Lite core."""
    import json
    pk = json.loads((S / "packages.json").read_text("utf8")); c = lambda l: S / "candidate" / pk[l]["file"]
    tool = S / "tree/tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll"; ok = True
    for key, core in [("vf-full", c("全量")), ("vf-lite", c("轻量"))]:
        ok &= base.run(S, key, ["dotnet", tool, "--scmod", core, "--video-feedback-checkset", "--vanilla-content", base.GAME / "Content.zip", "--json", S / f"{key}.json"])
        if (S / f"{key}.json").exists():
            d = json.loads((S / f"{key}.json").read_text("utf-8-sig"))
            print(key, "failed", d["failed"], "of", len(d["checks"]), [x["name"] for x in d["checks"] if not x["ok"]][:12])
    return ok

# Failures of the complete tactical suite that the delivered 1.4.0 already has (release-1.4.0 record); reported, not gating.
KNOWN_TACTICAL = {"optional-package-identity-and-no-bundled-engine", "native-gltf/ct", "native-gltf/t", "npc-full-registry-reload-refuses-without-ammo-loss"}

def tactical(S):
    """The complete tactical suite on the Full candidate (native model loading, the engine animation controller)."""
    import json
    pk = json.loads((S / "packages.json").read_text("utf8")); full = S / "candidate" / pk["全量"]["file"]
    tool = S / "tree/tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll"
    base.run(S, "tactical-full-suite", ["dotnet", tool, "--scmod", full, "--tactical-package", full, "--vanilla-content", base.GAME / "Content.zip", "--json", S / "tactical-full-suite.json"])
    if not (S / "tactical-full-suite.json").exists(): return False
    d = json.loads((S / "tactical-full-suite.json").read_text("utf-8-sig")); bad = [x["Name"] for x in d["checks"] if not x["Ok"]]
    new = [n for n in bad if n not in KNOWN_TACTICAL]
    print("tactical-full-suite failed", len(bad), "of", len(d["checks"]), "known", sorted(set(bad) & KNOWN_TACTICAL), "other", new)
    return not new

def checks(S):
    a = base.checks(S); b = vf(S); c = tactical(S)
    return a is not False and b and c

def throw(S):
    """R2 evidence: held throwables and the throw on the CS actors and the vanilla humans (poses, contacts, renders)."""
    import json
    pk = json.loads((S / "packages.json").read_text("utf8")); full = S / "candidate" / pk["全量"]["file"]
    ok = native("ThrowPoseCheck", "throw", [full])(S)
    if (S / "throw/throw.json").exists():
        d = json.loads((S / "throw/throw.json").read_text("utf-8-sig")); print("throw failures", len(d["failures"]), json.dumps(d["failures"][:12], ensure_ascii=False))
    return ok

def retest(S):
    """Test-only iteration: refresh tools/PackageCheck sources in the stage, rebuild it and rerun the AI suites
    against the stage's candidate packages. Product sources are not refreshed (that needs a new stage)."""
    import json, shutil
    src = ROOT / "tools/PackageCheck"; dst = S / "tree/tools/PackageCheck"
    for p in src.glob("*.cs"): shutil.copyfile(p, dst / p.name)
    if not base.run(S, "build-tool-PackageCheck", ["dotnet", "build", dst / "PackageCheck.csproj", "-c", "Release", "--nologo", "-v:q"]): return False
    pk = json.loads((S / "packages.json").read_text("utf8")); c = lambda l: S / "candidate" / pk[l]["file"]
    tool = dst / "bin/Release/net10.0/PackageCheck.dll"; content = base.GAME / "Content.zip"; ok = True
    for key, core, agents in [("ai-full", c("全量"), c("全量")), ("ai-lite", c("轻量"), c("探员"))]:
        ok &= base.run(S, key, ["dotnet", tool, "--scmod", core, "--tactical-package", agents, "--tactical-ai-only", "--vanilla-content", content, "--json", S / f"{key}.json"])
        d = json.loads((S / f"{key}.json").read_text("utf-8-sig"))
        print(key, "failed", d["failed"], "of", len(d["checks"]), [x["Name"] for x in d["checks"] if not x["Ok"]])
    b = vf(S); c = tactical(S)
    return ok and b and c

def clips(S):
    """R2 resources: the F3 air clips and the world pull-pin / throw clips appended to the dense actors, native caches
    baked with the edition's own loader; the actor geometry must come out byte-identical to the delivered package."""
    import hashlib, json, shutil, struct, zipfile
    sys.path[:0] = [str(ROOT / ".tmp/resource-codecs-20260926/deps"), str(ROOT / ".tmp/optimization-deps")]
    import zstandard as zstd
    def envelope(raw):
        packed = zstd.ZstdCompressor(level=19).compress(raw); assert zstd.ZstdDecompressor().decompress(packed) == raw
        return b"SCZSTD01" + struct.pack("<II", len(raw), len(packed)) + hashlib.sha256(raw).digest() + packed
    work = S / "clips"; work.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(S / "tree/tools"))
    from actor_air_clips import append_to_dense
    from actor_throw_clips import append_throw
    if not base.run(S, "build-tool-ActorLoadCheck", ["dotnet", "build", S / "tree/tools/ActorLoadCheck/ActorLoadCheck.csproj", "-c", "Release", "--nologo", "-v:q"]): raise SystemExit(1)
    tool = next((S / "tree/tools/ActorLoadCheck/bin/Release").rglob("ActorLoadCheck.dll")); report = {}
    with zipfile.ZipFile(ROOT / "output" / base.name("全量")) as full, zipfile.ZipFile(ROOT / "output" / base.name("探员")) as agents:
        for role in ["ct", "t"]:
            air = work / f"{role}-air.glb"; dense = work / f"{role}-dense.glb"
            report[role] = {"air": append_to_dense(base.DENSE / f"{role}.glb", base.EXPORTS[role], air)}
            report[role]["throw"] = append_throw(air, base.EXPORTS[role], dense); air.unlink()
            scanim = work / f"{role}.scanim"
            if not base.run(S, f"bake-{role}", ["dotnet", tool, "bake", dense, scanim, work / f"{role}-bake.json"]): raise SystemExit(1)
            stripped = work / f"{role}.glb"
            if not base.run(S, f"strip-{role}", [sys.executable, S / "tree/tools/prepare_actor_geometry.py", dense, stripped, role, work / f"{role}-strip.json"], cwd=S / "tree/tools"): raise SystemExit(1)
            glb = f"Assets/Models/ScCsgoTactical/{role}.glb"; cache = f"Assets/Animations/ScCsgoTactical/{role}.scanim"
            assert stripped.read_bytes() == full.read(glb), f"{role} geometry changed; clips must not alter the actor GLB"
            old_lite = agents.read(cache); assert old_lite[:8] == b"SCZSTD01" and envelope(full.read(cache)) == old_lite, "Lite cache is not the Full cache envelope"
            raw = scanim.read_bytes()
            for edition, payload in [("full", raw), ("lite", envelope(raw))]:
                t = S / "resources" / edition / cache; t.parent.mkdir(parents=True, exist_ok=True); t.write_bytes(payload)
            report[role]["bake"] = json.loads((work / f"{role}-bake.json").read_text("utf-8-sig"))
            report[role]["cacheFull"] = base.sha(raw); report[role]["cacheLite"] = base.sha(envelope(raw))
    config(S)
    base.dump(S / "clips.json", report)
    for role, r in report.items():
        print(role, "clips", r["throw"]["clipsBefore"], "->", r["throw"]["clipsAfter"], "cache", r["cacheFull"][:16], "bake failed", r["bake"].get("failed"),
              [(a["alias"], round(a["duration"], 3), a["actorChannels"], a["propBones"], a["fastestRightHandAt"]) for a in r["throw"]["added"]], flush=True)

def config(S):
    """Stage the CT/T and hostage animation configs without re-baking clips (the clip set is unchanged)."""
    import shutil
    for edition in ["full", "lite"]:
        for cfg in ["ScTactical.json", "ScTacticalHostage.json"]:
            t = S / "resources" / edition / "Assets/Animations" / cfg; t.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(S / "tree/src/ScCsgoTactical/Assets/Animations" / cfg, t)

STEPS = {"prepare": base.prepare, "build": base.build, "air": base.air, "appearance": base.appearance, "package": base.package,
         "checks": checks, "vf": vf, "tactical": tactical, "motion": base.motion, "hotspots": base.hotspots, "ui": base.ui, "retest": retest, "config": config, "clips": clips, "throw": throw}
ORDER = ["prepare", "build", "clips", "appearance", "package", "checks", "motion", "throw", "hotspots", "ui"]

if __name__ == "__main__":
    tag, wanted = sys.argv[1], sys.argv[2]; S = base.BASE / tag
    chosen = ORDER if wanted == "all" else wanted.split(",")
    unknown = [s for s in chosen if s not in STEPS]; assert not unknown, unknown
    failed = [s for s in chosen if STEPS[s](S) is False]
    print("failed steps:", failed); raise SystemExit(1 if failed else 0)
