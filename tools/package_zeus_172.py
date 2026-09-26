"""Patch accepted Full/Lite bundles in place by member, preserving their compressed resource streams."""
import argparse
import hashlib
import json
import zipfile
import zlib
from pathlib import Path
from pack_single_scmods import raw_member, write_archive
from stage_compatibility_sources import SOURCES

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output'
STAGE = ROOT / '.tmp/zeus-172'
VERSION = json.loads((ROOT / 'src/ScCsgoKnives/modinfo.json').read_text('utf-8'))['Version']
REPORT = OUT / ('release-zeus-' + VERSION)

def sha(data):
    return hashlib.sha256(data).hexdigest()

def read(path):
    return json.loads(path.read_text('utf-8'))

def locate(evidence):
    path = OUT / evidence['path']
    if not path.is_file():
        matches = [p for p in OUT.glob('*.scmod') if p.stat().st_size == evidence['bytes']
                   and sha(p.read_bytes()) == evidence['sha256']]
        assert len(matches) == 1, 'Expected one hash-verified source archive'
        path = matches[0]
    assert sha(path.read_bytes()) == evidence['sha256']
    return path

def extract(source, digest, label):
    assert sha(source.read_bytes()) == digest
    folder = STAGE / label
    folder.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source) as z:
        for name in ('ScCsgoKnives.dll', 'ScCsgoResources.dll'):
            (folder / name).write_bytes(z.read(name))
        if 'ScCsgoTactical.dll' in z.namelist():
            (folder / 'ScCsgoTactical.dll').write_bytes(z.read('ScCsgoTactical.dll'))
    return dict(label=label, source=str(source), sha256=digest)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--edition', choices=['Full', 'Lite'], default='Full')
    parser.add_argument('--prepare', action='store_true')
    args = parser.parse_args()
    evidence = read(OUT / 'release-feedback-1.7.1' / (args.edition + '.json'))
    source = locate(evidence)
    if args.prepare:
        sources = [extract(source, evidence['sha256'], 'previous')]
        for version, label in [('1.0.0', 'compat100'), ('1.2.0', 'compat120')]:
            item = read(OUT / 'release-compatibility-1' / (version + '-Full.json'))
            sources.append(extract(locate(item), item['sha256'], label))
        _, original, digest = SOURCES['1.2.0']
        sources.append(extract(original, digest, 'original120'))
        (STAGE / 'sources.json').write_text(json.dumps(sources, ensure_ascii=False, indent=2), 'utf-8')
        print(json.dumps(sources, ensure_ascii=False))
        return
    dll = (ROOT / 'src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll').read_bytes()
    dll_sha = sha(dll)
    balance = read(ROOT / '.tmp/zeus-balance-check2.json')
    assert balance['failed'] == 0 and balance['coreSha256'] == dll_sha
    for filename in ('isolation.json', 'family.json', 'previous-family.json'):
        check = read(STAGE / filename)
        assert check['failed'] == 0 and check['modules'][-1]['sha256'] == dll_sha, filename
    entries, hashes = {}, {}
    with zipfile.ZipFile(source) as z:
        for info in z.infolist():
            entries[info.filename] = (info.compress_type, info.CRC, info.file_size, raw_member(z, info))
            hashes[info.filename] = sha(z.read(info))
        original_hashes = dict(hashes)
        def add(name, data):
            c = zlib.compressobj(9, zlib.DEFLATED, -15)
            entries[name] = (8, zlib.crc32(data), len(data), c.compress(data) + c.flush())
            hashes[name] = sha(data)
        def metadata(name, **changes):
            item = json.loads(z.read(name)); item.update(changes)
            add(name, (json.dumps(item, ensure_ascii=False, indent=2) + chr(10)).encode('utf-8'))
        meta = json.loads(z.read('modinfo.json'))
        assert meta['Version'] == '1.7.1'
        metadata('modinfo.json', Version=VERSION, Description=meta['Description'] + ' 电击枪恢复1.2.0数值：初始10秒、满级1秒充能，其他枪械不变。')
        metadata('Integrations/ScCsgoKnives.modinfo.json', Version=VERSION)
        metadata('Integrations/ScCsgoBundle.json', version=VERSION, core=VERSION, coreSha256=dll_sha)
        metadata('Integrations/CompatibilityFamily.json', version=VERSION, core_sha256=dll_sha, build_revision='2026-09-26-zeus-only')
        add('ScCsgoKnives.dll', dll)
        install = z.read('INSTALL.txt').decode('utf-8-sig').replace('1.7.1', VERSION)
        install += chr(10) + '电击枪专项恢复1.2.0平衡：Lv0/10/20/30/40/50充能10/5/3/2/1.5/1秒；其他34把枪数值不变。旧存档正在充能的一轮保留剩余秒数，下一轮使用新周期。' + chr(10)
        add('INSTALL.txt', install.encode('utf-8-sig'))
    changed = sorted(n for n in hashes if hashes[n] != original_hashes[n])
    assert set(changed) == {'modinfo.json', 'Integrations/ScCsgoKnives.modinfo.json', 'Integrations/ScCsgoBundle.json', 'Integrations/CompatibilityFamily.json', 'ScCsgoKnives.dll', 'INSTALL.txt'}
    label = '全量包' if args.edition == 'Full' else '轻量包'
    target = OUT / ('[API1.9]CS武器' + VERSION + '-' + label + '.scmod')
    pending = target.with_suffix('.pending')
    write_archive(pending, entries)
    with zipfile.ZipFile(pending) as z:
        assert z.testzip() is None and set(z.namelist()) == set(entries)
        for name, digest in hashes.items():
            assert sha(z.read(name)) == digest, name
        with zipfile.ZipFile(source) as old:
            for name in set(entries) - set(changed):
                assert raw_member(z, z.getinfo(name)) == raw_member(old, old.getinfo(name)), name
    pending.replace(target)
    REPORT.mkdir(exist_ok=True)
    report = dict(path=target.name, version=VERSION, edition=args.edition, bytes=target.stat().st_size, sha256=sha(target.read_bytes()), coreSha256=dll_sha, sourcePath=source.name, sourceSha256=evidence['sha256'], entries=len(entries), changedEntries=changed, preservedEntries=len(entries)-len(changed))
    (REPORT / (args.edition + '.json')).write_text(json.dumps(report, ensure_ascii=False, indent=2) + chr(10), 'utf-8')
    print(json.dumps(report, ensure_ascii=False))

if __name__ == '__main__':
    main()
