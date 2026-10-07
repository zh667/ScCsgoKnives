"""Managed-heap composition after the memory scenario (2026-10-01 memory round, "where else can we save").

Same scenario as sp_memory.py (menu → world → the fixed 19 weapons twice → CT and T in view), on the isolated 1.9.3.1 copy;
then a measurement-only full GC and dotnet-gcdump of the game process (tool in .tmp/dev-temp/tools), its type report
(types by total size), plus the CS-specific static holders measured in-process. Desktop CoreCLR: the Android runtime
(Mono) holds the same objects, but its sizes and overheads differ.
Usage: sp_gcdump.py <label> <package> [<package> ...]
"""
import json, subprocess, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import KEEP_ACTIVE
from sp_memory import ITEMS, AGENTS, HAS_AGENTS, hold, MEASURE, CACHE_BYTES, FULL_GC

TOOLS = m0.ROOT / ".tmp/dev-temp/tools"


def main(label, *packages):
    def resolve(p):
        if p.startswith(("stage-lite:", "stage-agents:", "stage-full:")):
            kind, tag = p.split(":", 1); suffix = {"stage-lite": "轻量包.scmod", "stage-agents": "探员包.scmod", "stage-full": "全量包.scmod"}[kind]
            return sorted((m0.ROOT / ".tmp/completion-140-20260929" / tag / "candidate").glob("*" + suffix))[0]
        return {"output-lite": m0.LITE, "output-full": m0.FULL, "output-agents": m0.AGENTS}.get(p) or Path(p)
    pkgs = [resolve(p) for p in packages]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"gcdump-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"gcdump-{label}", "packages": {p.name: sha(p) for p in pkgs}}
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); time.sleep(5)
        m0.enter_world(g); g.func(KEEP_ACTIVE); time.sleep(5)
        items = [tuple(x.rsplit(":", 1)) for x in g.func(ITEMS).split(",")]
        for _ in range(2):
            for name, value in items: g.func(hold(value)); time.sleep(1.2)
        if g.func(HAS_AGENTS) == "True": print("agents:", g.func(AGENTS), flush=True); time.sleep(4)
        R["measure"] = json.loads(g.func(FULL_GC + MEASURE, timeout=120)); R["caches"] = json.loads(g.func(CACHE_BYTES, timeout=120))
        pid = g.proc.pid; dump = case_dir / "heap.gcdump"
        r = subprocess.run([str(TOOLS / "dotnet-gcdump.exe"), "collect", "-p", str(pid), "-o", str(dump)], capture_output=True, text=True, timeout=600)
        print("collect:", r.returncode, r.stdout[-300:], r.stderr[-300:], flush=True)
        r = subprocess.run([str(TOOLS / "dotnet-gcdump.exe"), "report", str(dump)], capture_output=True, text=True, timeout=600)
        (case_dir / "report.txt").write_text(r.stdout, "utf-8"); R["report"] = r.stdout.splitlines()[:80]
        print("\n".join(r.stdout.splitlines()[:70]), flush=True)
        R["gameErrors"] = g.errors()
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"gcdump-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
