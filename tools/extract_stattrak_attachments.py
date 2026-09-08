"""Read CS2's own StatTrak attachment transforms out of the 35 firearm model dumps.

This is the placement half of the counter: Valve's `stattrak` and `stattrak_legacy` attachments give the bone,
rotation and offset the counter module sits at on each weapon. Nothing here is eyeballed, and nothing here is a
mesh: the module model and its digit atlas still have to be exported from the game files, which this machine does
not have. See docs/gun-stattrak-attachments-2026-09-09.md for the exact missing paths.

Reads only; never writes into the CS2 extraction.
"""
import argparse, hashlib, json, re
from pathlib import Path
import cs2_weapons as source

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs/gun-stattrak-attachments-2026-09-09.json"
ATTACHMENTS = ("stattrak", "stattrak_legacy")

VEC = re.compile(r"\[\s*([-\d.eE]+),\s*([-\d.eE]+),\s*([-\d.eE]+)(?:,\s*([-\d.eE]+))?\s*\]")


def blocks(text, name):
    """Every attachment block with this exact m_name, as raw text."""
    out = []
    needle = 'm_name = "%s"' % name
    start = 0
    while True:
        at = text.find(needle, start)
        if at < 0:
            return out
        end = text.find("m_nInfluences", at)
        if end < 0:
            return out
        out.append(text[at:end])
        start = end


def parse(block):
    names = re.search(r"m_influenceNames\s*=\s*\[(.*?)\]", block, re.S)
    bone = re.findall(r'"([^"]*)"', names.group(1))[0] if names else ""
    rotations = re.search(r"m_vInfluenceRotations\s*=\s*\[(.*?)\]\s*m_vInfluenceOffsets", block, re.S)
    offsets = re.search(r"m_vInfluenceOffsets\s*=\s*\[(.*?)\]\s*m_influenceWeights", block, re.S)
    if not rotations or not offsets:
        return None
    r = VEC.search(rotations.group(1))
    o = VEC.search(offsets.group(1))
    if not r or not o:
        return None
    return {
        "bone": bone,
        "rotation": [float(r.group(i)) for i in (1, 2, 3, 4)],
        "offset": [float(o.group(i)) for i in (1, 2, 3)],
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--export-root", type=Path,
                    default=Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons")
    ap.add_argument("--out", type=Path, default=OUT)
    args = ap.parse_args()
    events = args.export_root / "02_models/events_full"
    guns, missing, disagreements = {}, [], []
    for mod, stem in sorted(source.GUNS.items()):
        path = events / (stem + ".analysis.txt")
        if not path.is_file():
            missing.append(mod)
            continue
        text = path.read_text("utf-8", "replace")
        entry = {"cs2": stem, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        for attachment in ATTACHMENTS:
            found = [parse(b) for b in blocks(text, attachment)]
            found = [f for f in found if f]
            if not found:
                entry[attachment] = None
                continue
            first = found[0]
            # Valve repeats the attachment on every render mesh of the model; they must agree, or the
            # placement is ambiguous and must not be guessed at.
            if any(f != first for f in found[1:]):
                disagreements.append("%s/%s" % (mod, attachment))
                entry[attachment] = None
                continue
            entry[attachment] = first
        guns[mod] = entry
    result = {
        "version": 1,
        "note": ("CS2's own stattrak / stattrak_legacy attachment transforms, read from the local model dumps. "
                 "Units are CS2 inches in the weapon's own space; rotation is a quaternion [x,y,z,w]. "
                 "The counter module mesh and digit atlas are NOT here and are not on this machine."),
        "attachments": list(ATTACHMENTS),
        "missingModels": missing,
        "ambiguous": disagreements,
        "guns": guns,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", "utf-8")
    # The runtime copy the mod embeds: placement only, no audit fields, so the game never loads the design file.
    runtime = {"Version": 1, "Guns": {mod: {a: entry.get(a) for a in ATTACHMENTS} for mod, entry in guns.items()}}
    data = ROOT / "src/ScCsgoKnives/AnimationData/gun_stattrak.json"
    data.write_text(json.dumps(runtime, indent=1, ensure_ascii=False) + "\n", "utf-8")
    print("runtime placement ->", data)
    have = sum(1 for g in guns.values() if g.get("stattrak"))
    legacy = sum(1 for g in guns.values() if g.get("stattrak_legacy"))
    print("%d guns, %d with stattrak, %d with stattrak_legacy -> %s" % (len(guns), have, legacy, args.out))
    if missing:
        print("missing model dumps:", ", ".join(missing))
    if disagreements:
        print("ambiguous (meshes disagree):", ", ".join(disagreements))


if __name__ == "__main__":
    main()
