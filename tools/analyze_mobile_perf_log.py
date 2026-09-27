"""Summarize existing CS_PERF logs without treating nested scopes as additive."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def values(text):
    result = {}
    for key, value in re.findall(r"([\w/]+)=([^,\s]+)", text):
        try:
            value = float(value) if "." in value else int(value)
        except ValueError:
            if value in ["True", "False"]:
                value = value == "True"
        result[key] = value
    return result


def analyze(path):
    raw = path.read_bytes()
    lines = raw.decode("utf-8-sig").splitlines()
    windows, spawns, warmups = [], [], []
    for number, line in enumerate(lines, 1):
        base = dict(line=number, time=line.split()[0])
        if "[CS_PERF] summary " in line:
            parts = line.split(" | ")
            row = dict(**base, **values(parts[0]))
            row["stages"] = {part.split(":", 1)[0]: values(part.split(":", 1)[1])
                             for part in parts[1:]}
            row["approxFpsFromMeanFrame"] = 1000 / row["engineAvgMs"]
            row["overThresholdCounts"] = list(map(int, row["over50/100/250"].split("/")))
            row["gcCounts"] = list(map(int, row["gc"].split("/")))
            row["perFrame"] = {
                name: dict(milliseconds=s["totalMs"] / row["frames"],
                           allocatedKiB=s["allocKB"] / row["frames"],
                           calls=s["n"] / row["frames"])
                for name, s in row["stages"].items()
            }
            # Source-confirmed separate scopes in the steady-state NPC route.
            # Sample/update are nested in Animate; weapon prepare/draw in Extras.
            names = ["EnemyAI", "CompanionAI", "Animate", "Bones", "Extras",
                     "WeaponSubmit", "Director"]
            covered = sum(row["stages"].get(n, {}).get("totalMs", 0) for n in names)
            row["selectedNonNestedMsPerFrame"] = covered / row["frames"]
            row["cpuFrameMinusSelectedScopesMs"] = row["engineCpuAvgMs"] - covered / row["frames"]
            windows.append(row)
        elif "[CS_PERF] spawn end " in line:
            spawns.append(dict(**base, **values(line)))
        elif "[CS_PERF] warmup " in line:
            warmups.append(dict(**base, **values(line)))
    frames = sum(w["frames"] for w in windows)
    report = dict(
        input=dict(file=path.name, bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest()),
        scope="Observed Android session only, no matched before/after. Approximate FPS=1000/mean logged frame; nested scopes must not be added.",
        windows=windows, spawns=spawns, warmups=warmups,
        totals=dict(windows=len(windows), frames=frames,
                    frameWeightedMeanMs=sum(w["frames"] * w["engineAvgMs"] for w in windows) / frames,
                    overThresholdCounts=[sum(w["overThresholdCounts"][i] for w in windows) for i in range(3)],
                    gcCounts=[sum(w["gcCounts"][i] for w in windows) for i in range(3)],
                    spawnRequests=sum(w["spawnRequests"] for w in windows),
                    spawnFailed=sum(w["spawnFailed"] for w in windows),
                    managedMiBRange=[min(w["managedMB"] for w in windows), max(w["managedMB"] for w in windows)],
                    warmupMilliseconds=sum(w["ms"] for w in warmups),
                    warnings=[dict(line=i, text=l) for i, l in enumerate(lines, 1) if "WARNING:" in l],
                    errors=[dict(line=i, text=l) for i, l in enumerate(lines, 1) if "ERROR:" in l],
                    worldLoadLines=[dict(line=i, text=l) for i, l in enumerate(lines, 1) if "世界加载成功" in l],
                    buildLines=[dict(line=i, text=l) for i, l in enumerate(lines, 1) if "[CS_PERF] build=" in l]),
    )
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("log", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = analyze(args.log)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print(json.dumps(report["totals"], ensure_ascii=False, indent=2))
    for row in report["windows"]:
        print(row["time"], row["enemies"], round(row["approxFpsFromMeanFrame"], 1),
              round(row["selectedNonNestedMsPerFrame"], 2),
              round(row["cpuFrameMinusSelectedScopesMs"], 2))
