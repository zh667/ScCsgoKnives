"""Focused single-player checks for post-mp-bugs-20260930 on a stage's own tools and its sp-<tag> test packages.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/sp_checks.py visual <tag>
  visual  the stage's PackageCheck --visual-checkset (casings, smoke coverage, smoke view) on sp-<tag>-lite/full;
          reports in .tmp/bugs-fix-20260930/<tag>/visual-<edition>.json and the smoke-view.json measurements beside them.
"""
import json, os, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
STAGES = ROOT / ".tmp/completion-140-20260929"; PKG = ROOT / ".tmp/mp-m0-20260929/pkg"; OUT = ROOT / ".tmp/bugs-fix-20260930"


def visual(tag):
    tool = STAGES / tag / "tree/tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll"
    out = OUT / tag; out.mkdir(parents=True, exist_ok=True); failed = 0
    for edition in ["lite", "full"]:
        pkg = PKG / f"sp-{tag}-{edition}.scmod"; report = out / edition; report.mkdir(exist_ok=True)
        r = subprocess.run(["dotnet", str(tool), "--scmod", str(pkg), "--visual-checkset", "--json", str(out / f"visual-{edition}.json")],
                           capture_output=True, text=True, env=dict(os.environ, SC_CSGO_VISUAL_REPORT=str(report)), timeout=3000)
        d = json.loads((out / f"visual-{edition}.json").read_text("utf-8"))
        print(f"== {edition}: exit {r.returncode}, package {d.get('packageSha256')}, dll {d.get('dllSha256')}, failed {d.get('failed')}", flush=True)
        if r.returncode not in (0, 1): print(r.stderr[-1500:])
        for c in d["checks"]:
            if c["name"].startswith("smoke") or not c["ok"]: print(("PASS " if c["ok"] else "FAIL ") + c["name"] + " :: " + c["detail"][:1200])
        failed += d.get("failed", 1)
    return 1 if failed else 0


def main(tag, edition="lite", *names):
    """The stage's whole PackageCheck suite on sp-<tag>-<edition>; prints every failure and the checks whose names start
    with any of <names> (e.g. projectile-context smoke-view)."""
    tool = STAGES / tag / "tree/tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll"
    out = OUT / tag; out.mkdir(parents=True, exist_ok=True)
    pkg = PKG / f"sp-{tag}-{edition}.scmod"; content = Path(r"D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Content.zip")
    r = subprocess.run(["dotnet", str(tool), "--scmod", str(pkg), "--vanilla-content", str(content), "--json", str(out / f"main-{edition}.json")],
                       capture_output=True, text=True, timeout=3600)
    d = json.loads((out / f"main-{edition}.json").read_text("utf-8"))
    print(f"== main {edition}: exit {r.returncode}, package {d.get('packageSha256')}, failed {d.get('failed')} of {len(d['checks'])}", flush=True)
    if r.returncode not in (0, 1): print(r.stderr[-1500:])
    for c in d["checks"]:
        if not c["ok"] or any(c["name"].startswith(n) for n in names): print(("PASS " if c["ok"] else "FAIL ") + c["name"] + " :: " + c["detail"][:900])
    return 1 if d.get("failed") else 0


if __name__ == "__main__":
    sys.exit({"visual": visual, "main": main}[sys.argv[1]](*sys.argv[2:]))
