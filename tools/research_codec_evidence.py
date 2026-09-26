"""Persist compact evidence for the offline codec study; never edit releases."""
import collections
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / ".tmp/resource-codecs-20260926"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(name):
    return json.loads((STAGE / name).read_bytes())


def timing_totals(rows, field):
    groups = {}
    for row in rows:
        key = row[field] + "/" + row["group"]
        group = groups.setdefault(key, dict(cases=0, sumMedianMilliseconds=0,
                                            sumMedianAllocatedBytes=0))
        group["cases"] += 1
        group["sumMedianMilliseconds"] += row["medianMilliseconds"]
        group["sumMedianAllocatedBytes"] += row["medianAllocatedBytes"]
    return groups


def main():
    inventory = read("inventory.json")
    lossless = read("lossless-report.json")
    managed = read("managed-decode.json")
    budgets = read("budget-report.json")
    verified = read("budget-validation.json")
    acl = read("acl-fit.json")
    assert managed["failed"] == verified["failed"] == 0
    assert all(row["exact"] for row in managed["rows"] + verified["timings"])
    baselines = []
    for owner, label in [("core", "轻量"), ("agents", "探员")]:
        path = ROOT / f"output/[API1.9]CS武器1.3.0-{label}包.scmod"
        expected = budgets[0]["packages"][owner]
        assert sha(path) == expected["baselineSha256"]
        assert path.stat().st_size == expected["baselineBytes"]
        baselines.append(dict(path=path.relative_to(ROOT).as_posix(),
                              bytes=path.stat().st_size, sha256=sha(path),
                              unchanged=True))
    for budget in budgets:
        budget.pop("selected")
    report_names = [
        "inventory.json", "lossless-report.json", "meshoptimizer-report.json",
        "managed-decode.json", "budget-report.json", "budget-validation.json",
        "acl-fit.json", "sources.json", "source-details.json",
    ]
    tool_paths = sorted(ROOT.glob("tools/research_*codec*.py"))
    tool_paths += [ROOT / "tools/research_mesh_quantization.py",
                   ROOT / "tools/research_acl_fit.py"]
    tool_paths += sorted((ROOT / "tools/ResourceCodecCheck").glob("*.cs"))
    tool_paths += sorted((ROOT / "tools/ResourceCodecCheck").glob("*.csproj"))
    evidence = dict(
        date="2026-09-26",
        scope="Research only. No production readers, delivered packages, Mods or worlds changed.",
        units="Decimal bytes/MB. Windows warm stream decode is not game/Android load time; allocated bytes are cumulative, not peak RAM.",
        baselines=baselines,
        inventoryCounts=dict(collections.Counter(row["group"] for row in inventory)),
        resources=[{k: v for k, v in row.items() if k != "packed"} for row in inventory],
        sources=read("sources.json"), sourceDetails=read("source-details.json"),
        versions=dict(**lossless["versions"], meshoptimizerPython="0.2.30a0",
                      zstdSharpPort="0.8.8",
                      framework=managed["framework"], os=managed["os"],
                      architecture=managed["architecture"]),
        reportHashes={name: sha(STAGE / name) for name in report_names},
        toolHashes={path.relative_to(ROOT).as_posix(): sha(path) for path in tool_paths},
        losslessTotals=lossless["totals"],
        originalJsonControls={
            codec: sum(row["controls"][codec]["outerZipBytes"]
                       for row in lossless["results"] if row["controls"])
            for codec in ["brotli9", "zstd19"]
        },
        losslessResources=[
            {k: v for k, v in row.items() if k not in ["variants", "seconds"]}
            for row in lossless["results"]
        ],
        meshoptimizer=read("meshoptimizer-report.json"),
        managedValidation={
            k: v for k, v in managed.items() if k != "rows"
        },
        managedTiming=timing_totals(managed["rows"], "variant"),
        budgets=budgets,
        budgetValidation={
            k: v for k, v in verified.items() if k not in ["checks", "timings"]
        },
        budgetTiming=timing_totals(verified["timings"], "codec"),
        aclFit=dict(
            aclExecuted=acl["aclExecuted"], scope=acl["scope"],
            weapon=acl["weapon"],
            actors=[dict(name=a["name"], clips=a["clips"],
                         maxUniformGridDeviation=max(c["maxUniformGridDeviation"]
                                                     for c in a["details"]))
                    for a in acl["actors"]],
        ),
    )
    destination = ROOT / "docs/internal-resource-codecs-2026-09-26-evidence.json"
    destination.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf8")
    print(json.dumps({k: evidence[k] for k in [
        "inventoryCounts", "versions", "originalJsonControls",
        "budgetValidation", "budgetTiming", "aclFit",
    ]}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
