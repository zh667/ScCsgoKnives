"""Verify a counter release pair: one DLL in both editions, and both packages pass the packaged check.

Reads only; writes the two per-edition reports and one summary under docs/. Offline evidence, not device
acceptance: it says the shipped bytes compute and persist the counter, growth and schema rules correctly.
"""
import argparse, hashlib, json, subprocess, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def dll_sha(package):
    with zipfile.ZipFile(package) as z:
        name = next(n for n in z.namelist() if n.endswith("ScCsgoKnives.dll"))
        return hashlib.sha256(z.read(name)).hexdigest(), len(z.namelist())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default=json.loads((ROOT / 'src/ScCsgoKnives/modinfo.json').read_text('utf-8'))['Version'])
    ap.add_argument("--vanilla-content", required=True, help="a vanilla Content.zip for the packaged regressions")
    ap.add_argument("--check", type=Path, default=ROOT / "tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll")
    ap.add_argument("--out", type=Path, default=None)
    ap.add_argument("--stattrak-out", type=Path, default=None, help="export the Full package renderer's actual UVs/frames")
    args = ap.parse_args()
    out = args.out or ROOT / f"docs/gun-counter-growth-{args.version.replace('.', '')}-verification.json"

    result = {"version": args.version, "editions": {}}
    for edition, suffix in [("Full", ""), ("Lite", "-Lite")]:
        package = ROOT / f"output/ScCsgoKnives-{args.version}{suffix}.scmod"
        if not package.is_file():
            sys.exit("missing package: " + str(package))
        digest = sha(package)
        dll, entries = dll_sha(package)
        report = ROOT / f"output/ScCsgoKnives-{args.version}{suffix}-check.json"
        command = [
            "dotnet", str(args.check), "--scmod", str(package), "--sha256", digest,
            "--vanilla-content", args.vanilla_content, "--json", str(report)]
        if edition == 'Full' and args.stattrak_out:
            command += ['--stattrak-out', str(args.stattrak_out)]
        proc = subprocess.run(command,
            capture_output=True, text=True, encoding='utf-8', errors='replace', cwd=ROOT, timeout=3600)
        if not report.is_file():
            sys.exit("PackageCheck produced no report for %s:\n%s" % (edition, proc.stderr[-2000:]))
        evidence = json.loads(report.read_text('utf-8-sig'))
        checks = evidence.get("checks", [])
        failed = [c["name"] for c in checks if not c["ok"]]
        # A failed process, empty check set or stale report is not successful verification.
        if proc.returncode != 0: failed.append('PackageCheck/exit-code')
        if not checks: failed.append('PackageCheck/empty-check-set')
        if evidence.get('packageSha256') != digest or evidence.get('dllSha256') != dll:
            failed.append('PackageCheck/report-hash-mismatch')
        if evidence.get('failed', 1) != 0: failed.append('PackageCheck/reported-failures')
        result["editions"][edition] = {
            "package": package.name, "bytes": package.stat().st_size, "entries": entries,
            "sha256": digest, "dllSha256": dll, "checks": len(checks), "failed": failed,
            "exitCode": proc.returncode, "report": report.name,
        }
        print(edition, package.name, len(checks), "checks,", len(failed), "failed, dll", dll[:16])

    full, lite = result["editions"]["Full"], result["editions"]["Lite"]
    result["sameDll"] = full["dllSha256"] == lite["dllSha256"]
    result["allPassed"] = not full["failed"] and not lite["failed"]
    print("same DLL:", result["sameDll"], " all passed:", result["allPassed"])
    out.write_text(json.dumps(result, indent=2) + "\n", encoding='utf-8')
    sys.exit(0 if result["sameDll"] and result["allPassed"] else 1)


if __name__ == "__main__":
    main()
