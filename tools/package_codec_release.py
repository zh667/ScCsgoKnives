"""Package exact baseline assets with verified envelopes and new readers; candidates only."""
from prepare_codec_release import ROOT, S, COMMIT, BASELINES, sha, dump
from pack_single_scmods import raw_member, write_archive
import json, subprocess, zipfile, zlib

def member(data):
    c = zlib.compressobj(9, zlib.DEFLATED, -15)
    packed = c.compress(data) + c.flush()
    return (8, zlib.crc32(data), len(data), packed) if len(packed) < len(data) else (0, zlib.crc32(data), len(data), data)

def main():
    report = {}
    encoded = json.loads((S / "encoded.json").read_bytes())
    for owner, (label, expected) in BASELINES.items():
        name = f"[API1.9]CS武器1.3.0-{label}包.scmod"
        path = S / "baseline" / name
        assert sha(path.read_bytes()) == expected
        with zipfile.ZipFile(path) as z:
            entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
            hashes = {n: sha(z.read(n)) for n in z.namelist()}
            changes = {}
            def add(n, data):
                entries[n] = member(data); hashes[n] = sha(data); changes[n] = hashes[n]
            if owner == "core":
                add("ScCsgoKnives.dll", (S / "core/source/bin/Release/net10.0/ScCsgoKnives.dll").read_bytes())
                add("ScCsgoResources.dll", (S / "resources/bin/Release/net10.0/ScCsgoResources.dll").read_bytes())
                add("ScCsgoResourceCodec.dll", (ROOT / "src/ScCsgoResourceCodec/bin/Release/net10.0/ScCsgoResourceCodec.dll").read_bytes())
                family = json.loads(z.read("Integrations/CompatibilityFamily.json"))
                family.update(build_revision="split-zstd-130-20260927", core_sha256=hashes["ScCsgoKnives.dll"])
                add("Integrations/CompatibilityFamily.json", json.dumps(family, ensure_ascii=False, indent=2).encode())
                license_path = S / "upstream" / ("ZstdSharp-" + COMMIT) / "LICENSE"
                add("Licenses/ZstdSharp-MIT.txt", license_path.read_bytes())
                notices = z.read("THIRD_PARTY_NOTICES.md") + (
                    "\n\nZstdSharp.Port 0.8.8 (MIT), source https://github.com/oleg-st/ZstdSharp/tree/" + COMMIT +
                    "\nRebuilt as ScCsgoResourceCodec.dll with isolated assembly identity; original codec sources and InlineMethod.Fody inlining retained. "
                    "License: Licenses/ZstdSharp-MIT.txt. This is a managed C# codec, not an Android native binary.\n").encode()
                add("THIRD_PARTY_NOTICES.md", notices)
            else:
                add("ScCsgoTactical.dll", (S / "agents/source/bin/Release/net10.0/ScCsgoTactical.dll").read_bytes())
                for row in encoded:
                    if row["owner"] != "agents": continue
                    data = (S / row["path"]).read_bytes()
                    assert sha(data) == row["sha256"]
                    if len(member(data)[3]) < z.getinfo(row["name"]).compress_size: add(row["name"], data)
            split = json.loads(z.read("Integrations/ScSplit.json"))
            split.update(resourceCodec=1, build_revision="split-zstd-130-20260927")
            add("Integrations/ScSplit.json", json.dumps(split, ensure_ascii=False, indent=2).encode())
            add("INSTALL.txt", z.read("INSTALL.txt") + "\n本次为1.3.0无损资源压缩版。已有探员包的玩家请同时更新本次轻量包与探员包；公开版本相同不代表旧轻量包具备新资源解码器。模型、512颜色贴图和全部动作不变。\n".encode("utf8"))
            # Optimize only changed entries; preserve stronger original ZIP streams elsewhere.
            changed_zip = S / (owner + "-changed.zip")
            files = S / (owner + "-changed")
            files.mkdir(exist_ok=True)
            for n in changes:
                data = entries[n][3] if entries[n][0] == 0 else zlib.decompress(entries[n][3], -15)
                p = files / n; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(data)
            if changed_zip.exists(): changed_zip.unlink()
            with (S / (owner + "-7zip.log")).open("w", encoding="utf8") as log:
                subprocess.run(["C:/Program Files/7-Zip/7z.exe", "a", "-tzip", "-mm=Deflate", "-mx=9", "-mfb=258", "-mpass=15", "-mmt=2", str(changed_zip), "."], cwd=files, stdout=log, stderr=subprocess.STDOUT, check=True)
            with zipfile.ZipFile(changed_zip) as stronger:
                for n in changes:
                    i = stronger.getinfo(n)
                    assert sha(stronger.read(n)) == hashes[n]
                    if i.compress_size < len(entries[n][3]): entries[n] = (i.compress_type, i.CRC, i.file_size, raw_member(stronger, i))
            candidate = S / "candidate" / name; candidate.parent.mkdir(exist_ok=True)
            write_archive(candidate, entries)
            with zipfile.ZipFile(candidate) as check:
                assert check.testzip() is None
                assert {n: sha(check.read(n)) for n in check.namelist()} == hashes
            assert candidate.stat().st_size < 40_000_000
            report[owner] = dict(file=name, bytes=candidate.stat().st_size, sha256=sha(candidate.read_bytes()),
                                 baselineSha256=expected, entries=hashes, changes=changes)
            print(owner, candidate.stat().st_size, flush=True)
    dump(S / "packages.json", report)

if __name__ == "__main__": main()
