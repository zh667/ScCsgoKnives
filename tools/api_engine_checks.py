"""A candidate stage on every standalone engine after 1.9.3.1 (api-194-1951-compat-20261016, the user: "这个从1.9.3.1之后的都要
适配（这些版本都要可用cs枪械）"). Windows only, via tools/dev.ps1:

  python tools/api_engine_checks.py <tag> [contract,suites]   (both by default)

For the stage .tmp/completion-140-20260929/<tag> (its candidate packages and its tree's tools/PackageCheck):
  contract  tools/ApiContractCheck: every DLL of the candidates (and of the delivered deathmatch package) against the
            1.9.3.1 download and each engine's runtime DLLs - references, overrides, abstract/interface members, the
            engine fields read by reflection
  suites    the stage's PackageCheck sources built against each engine's NuGet packages: the default suite (Full, Lite),
            the tactical AI set (Full; Lite with the agents package) and the video-feedback set (Full), each engine's own
            Content.zip; failures and check names compared with the stage's own 1.9.3.1 results (main-*, ai-*, vf-full)
A failure the 1.9.3.1 run does not have, or a check that ran there and not here, fails the step. Engines live in
.tmp/api-engines/<version> (Windows runtime) and .tmp/api-engines/nupkgs/<version> (SurvivalcraftAPI.*.nupkg); their
other NuGet dependencies come from nuget.org and the SurvivalcraftAPI feed. Gameplay on each engine is
tools/MpM0/sp_engine_compat.py (M0_ENGINE=194/195/1951). Never writes output/, Mods or worlds.
"""
import hashlib, json, os, shutil, subprocess, sys, time
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import completion_140 as c   # stage layout, report(); sets SC_SLOWER_CHECK_PACKAGE for the tactical set
base = c.base

VERSIONS = ["1.9.4", "1.9.5", "1.9.5.1"]
ENGINES = ROOT / ".tmp/api-engines"
FEEDS = ["https://api.nuget.org/v3/index.json", "https://nuget.fury.io/survivalcraftapi"]


def packages(S):
    pk = json.loads((S / "packages.json").read_text("utf8"))
    return {label: S / "candidate" / pk[label]["file"] for label in ["全量", "轻量", "探员"]}


def contract(S):
    p = packages(S); dm = base.baseline("死亡竞赛")   # the delivered deathmatch package (not rebuilt by the stage)
    args = ["--engine", f"1.9.3.1={base.GAME}"] + [a for v in VERSIONS for a in ("--engine", f"{v}={ENGINES / v}")]
    args += ["--package", f"full={p['全量']}", "--package", f"lite={p['轻量']}", "--package", f"agents={p['探员']}"]
    if dm.exists(): args += ["--package", f"dm={dm}"]
    args += ["--work", str(S / "api/contract"), "--out", str(S / "api-contract.json")]
    return base.run(S, "api-contract", ["dotnet", "run", "--project", ROOT / "tools/ApiContractCheck", "-c", "Release", "--", *args])


def suites(S):
    p = packages(S); ok = True; summary = {}
    for version in VERSIONS:
        H = S / "api" / version; content = ENGINES / version / "Content.zip"; feed = ENGINES / "nupkgs" / version
        if H.exists(): shutil.rmtree(H / "PackageCheck", ignore_errors=True)
        shutil.copytree(S / "tree/tools/PackageCheck", H / "PackageCheck", ignore=shutil.ignore_patterns("bin", "obj"))
        proj = H / "PackageCheck/PackageCheck.csproj"; text = proj.read_text("utf8")
        assert text.count('Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"') == 1, "PackageCheck's engine reference changed"
        proj.write_text(text.replace('Version="1.9.3.1"', f'Version="[{version}]"'), "utf8")
        (H / "nuget.config").write_text('<?xml version="1.0" encoding="utf-8"?>\n<configuration><packageSources><clear />'
            + f'<add key="api-{version}" value="{feed}" />' + "".join(f'<add key="feed{i}" value="{u}" />' for i, u in enumerate(FEEDS))
            + "</packageSources></configuration>\n", "utf8")
        if not base.run(S, f"api-{version}-build", ["dotnet", "build", proj, "-c", "Release", "-o", H / "out", "-p:UseSharedCompilation=false", "--nologo", "-v:q"]):
            ok = False; continue
        # The engine packages restored must be the official ones in the feed folder, byte for byte.
        same = {}
        for n in ["Survivalcraft", "Engine", "EntitySystem"]:
            cache = Path(os.environ.get("NUGET_PACKAGES", r"E:/DevDeps/nuget/.nuget/packages")) / f"survivalcraftapi.{n.lower()}/{version}/survivalcraftapi.{n.lower()}.{version}.nupkg"
            local = feed / f"SurvivalcraftAPI.{n}.{version}.nupkg"
            same[n] = cache.exists() and local.exists() and hashlib.sha256(cache.read_bytes()).digest() == hashlib.sha256(local.read_bytes()).digest()
        ok &= all(same.values()); summary[version] = {"officialPackages": same}
        for key, args in [("main-full", ["--scmod", p["全量"]]), ("main-lite", ["--scmod", p["轻量"]]),
                          ("ai-full", ["--scmod", p["全量"], "--tactical-package", p["全量"], "--tactical-ai-only"]),
                          ("ai-lite", ["--scmod", p["轻量"], "--tactical-package", p["探员"], "--tactical-ai-only"]),
                          ("vf-full", ["--scmod", p["全量"], "--video-feedback-checkset"])]:
            out = S / f"api-{version}-{key}.json"
            base.run(S, f"api-{version}-{key}", ["dotnet", H / "out/PackageCheck.dll", *args, "--vanilla-content", content, "--json", out])
            here, there = S / f"api-{version}-{key}.json", S / f"{key}.json"
            if not here.exists() or not there.exists(): ok = False; summary[version][key] = "no report" if not here.exists() else f"no 1.9.3.1 result {there.name}"; continue
            def checks(path): return {x.get("name", x.get("Name")): bool(x.get("ok", x.get("Ok"))) for x in json.loads(path.read_text("utf-8-sig"))["checks"]}
            now, then = checks(here), checks(there)
            new = sorted(n for n, good in now.items() if not good and then.get(n, False)); unrun = sorted(set(then) - set(now))
            summary[version][key] = {"checks": len(now), "failed": sum(not g for g in now.values()), "newFailures": new, "notRunHere": unrun}
            print(version, key, summary[version][key], flush=True); ok &= not new and not unrun
    base.dump(S / "api-engines.json", summary)
    return ok


STEPS = {"contract": contract, "suites": suites}

if __name__ == "__main__":
    S = base.BASE / sys.argv[1]
    steps = sys.argv[2].split(",") if len(sys.argv) > 2 else list(STEPS)
    results = {}
    for name in steps:
        t0 = time.time(); results[name] = bool(STEPS[name](S)); print(f"step {name}: {'passed' if results[name] else 'FAILED'} in {time.time() - t0:.0f} s", flush=True)
    sys.exit(0 if all(results.values()) else 1)
