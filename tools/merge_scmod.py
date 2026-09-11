"""Build one self-contained CS武器 package from the verified core/resource archives."""
from pathlib import Path
import hashlib, json, zipfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output"
CORE = OUT / "[API1.9]CS武器1.0.0.scmod"
RES = OUT / "[API1.9]CS武器资源包1.0.0.scmod"
TARGET = OUT / "[API1.9]CS武器1.0.0-整合版.scmod"

def main():
    if not CORE.exists() or not RES.exists():
        raise SystemExit("先生成两个1.0.0正式包")
    files = {}
    with zipfile.ZipFile(CORE) as z:
        for e in z.infolist():
            files[e.filename] = z.read(e)
        meta = json.loads(files["modinfo.json"])
    with zipfile.ZipFile(RES) as z:
        for e in z.infolist():
            # The monolithic package has one mod identity and one copy of notices.
            if e.filename in {"modinfo.json", "LICENSE", "ASSET_SOURCES.md", "THIRD_PARTY_NOTICES.md"}:
                continue
            if e.filename in files:
                raise SystemExit(f"duplicate package path: {e.filename}")
            files[e.filename] = z.read(e)
    meta["Dependencies"] = {}
    meta["Description"] = meta["Description"].replace("需同时安装正式版CS武器资源包1.0.0", "已包含完整资源")
    files["modinfo.json"] = (json.dumps(meta, ensure_ascii=False, indent=2) + "\n").encode()
    tmp = TARGET.with_suffix(TARGET.suffix + ".pending")
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
        for name in sorted(files):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, files[name])
    tmp.replace(TARGET)
    print(f"{TARGET}: {TARGET.stat().st_size/1e6:.2f} MB; {len(files)} entries")
    print("sha256", hashlib.sha256(TARGET.read_bytes()).hexdigest())

if __name__ == "__main__": main()
