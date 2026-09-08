"""Compare native-UV release packages, including the exact replacement/removal manifest."""
import hashlib
import argparse
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify():
    ap=argparse.ArgumentParser()
    ap.add_argument("--version",default="0.38.4",choices=["0.38.4","0.38.5"])
    args=ap.parse_args()
    version=args.version
    previous_version="0.38.4" if version=="0.38.5" else "0.38.3"
    old_path = ROOT/f"output/ScCsgoKnives-{previous_version}.scmod"
    full_path = ROOT/f"output/ScCsgoKnives-{version}.scmod"
    lite_path = ROOT/f"output/ScCsgoKnives-{version}-Lite.scmod"
    catalog = json.loads((ROOT/"tools/gun_skins_catalog.json").read_text("utf-8"))
    expected = {f"Assets/Textures/ScCsgoKnives/{s['gun']}_hd__{s['key']}{suffix}.png"
                for s in catalog["skins"] if (s["gun"]=="ak47" if version=="0.38.5" else s["legacyModel"]) for suffix in ("", "_orm", "_normal")}
    expected |= {"ScCsgoKnives.dll", "modinfo.json", "ASSET_SOURCES.md"}
    with zipfile.ZipFile(old_path) as old, zipfile.ZipFile(full_path) as full, zipfile.ZipFile(lite_path) as lite:
        previous, current = set(old.namelist()), set(full.namelist())
        removed = sorted(previous-current)
        assert not removed, removed
        changed = {}
        for name in sorted(previous & current):
            a, b = digest(old.read(name)), digest(full.read(name))
            if a != b:
                changed[name] = {"before": a, "after": b}
        assert set(changed) <= expected, set(changed)-expected
        assert len([n for n in changed if n.endswith(".png")]) == (12 if version=="0.38.5" else 30)
        assert current == set(lite.namelist())
        assert full.read("ScCsgoKnives.dll") == lite.read("ScCsgoKnives.dll")
        for name in current:
            if name.endswith(".png") or name in ("modinfo.json", "Assets/ScCsgoKnivesEdition.xml"):
                continue
            assert full.read(name) == lite.read(name), name
        checks = {}
        for edition, path in (("Full", full_path), ("Lite", lite_path)):
            report = json.loads(path.with_name(path.stem+"-check.json").read_text("utf-8-sig"))
            assert report["failed"] == 0
            assert report["packageSha256"] == digest(path.read_bytes())
            checks[edition] = {k: report[k] for k in ("packageSha256", "packageBytes", "dllSha256", "entries", "failed")}
            checks[edition]["passed"] = len(report["checks"])
        result = {"version": version, "previousVersion": previous_version, "checks": checks, "removedFiles": removed,
                  "replacedFiles": changed, "addedFiles": sorted(current-previous),
                  "preserved": "Every other prior package entry is byte-identical: default meshes/maps, all icons, audio and unrelated effects.",
                  "gameVisualAcceptance": "pending; offline native-UV renders are not in-game screenshots"}
        target = ROOT/f"docs/gun-skins-native-{version.replace('.','')}-verification.json"
        target.write_text(json.dumps(result, indent=2)+"\n", "utf-8")
        print(json.dumps({"checks": checks, "added": len(result["addedFiles"]), "replaced": len(changed), "removed": len(removed)}, indent=2))


if __name__ == "__main__":
    verify()
