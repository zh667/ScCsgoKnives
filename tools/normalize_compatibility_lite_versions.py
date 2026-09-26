"""Normalize Lite metadata and synchronize its checked Full DLL without re-encoding assets."""
import hashlib
import json
import shutil
import zipfile
import zlib
from pathlib import Path
from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output'
REPORT = OUT / 'release-compatibility-1'

def sha(data):
    return hashlib.sha256(data).hexdigest()

def main():
    for version in ('1.0.0', '1.2.0'):
        report_path = REPORT / f'{version}-Lite.json'
        report = json.loads(report_path.read_text('utf-8'))
        target = OUT / f'[API1.9]CS武器{version}-双向兼容-轻量包.scmod'
        assert sha(target.read_bytes()) == report['sha256'], 'Unrecognized Lite source'
        entries = {}
        allowed = {'modinfo.json', 'Integrations/CompatibilityFamily.json', 'INSTALL.txt', 'ScCsgoKnives.dll'}
        full = json.loads((REPORT / f'{version}-Full.json').read_text('utf-8'))
        full_path = OUT / full['path']
        assert sha(full_path.read_bytes()) == full['sha256']
        with zipfile.ZipFile(full_path) as full_zip:
            core_bytes = full_zip.read('ScCsgoKnives.dll')
        assert sha(core_bytes) == full['coreSha256']
        with zipfile.ZipFile(target) as old:
            def add(name, data):
                compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
                entries[name] = (8, zlib.crc32(data), len(data), compressor.compress(data) + compressor.flush())
            for info in old.infolist():
                entries[info.filename] = (info.compress_type, info.CRC, info.file_size, raw_member(old, info))
            for name, field in [('modinfo.json', 'Version'), ('Integrations/CompatibilityFamily.json', 'version')]:
                metadata = json.loads(old.read(name))
                assert metadata[field] in (version, version + '-compat.1')
                metadata[field] = version
                if name == 'Integrations/CompatibilityFamily.json':
                    metadata['core_sha256'] = full['coreSha256']
                add(name, (json.dumps(metadata, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
            add('ScCsgoKnives.dll', core_bytes)
            install = old.read('INSTALL.txt').decode('utf-8-sig')
            install = install.replace('1.0.0-compat.1', '1.0.0').replace('1.2.0-compat.1', '1.2.0')
            add('INSTALL.txt', install.encode('utf-8-sig'))
            pending = target.with_suffix('.pending')
            write_archive(pending, entries)
            with zipfile.ZipFile(pending) as new:
                assert new.testzip() is None
                assert set(new.namelist()) == set(old.namelist())
                for name in set(entries) - allowed:
                    assert new.read(name) == old.read(name), name
                    assert raw_member(new, new.getinfo(name)) == raw_member(old, old.getinfo(name)), name
                core_sha = sha(new.read('ScCsgoKnives.dll'))
                assert core_sha == full['coreSha256']
                changed = sorted(name for name in allowed if new.read(name) != old.read(name))
        if not changed:
            pending.unlink()
            print(json.dumps(dict(path=target.name, unchanged=True, sha256=report['sha256']), ensure_ascii=False))
            continue
        backup = ROOT / '.tmp/compat-version-20260926' / report['sha256']
        backup.mkdir(parents=True, exist_ok=True)
        for source in (target, report_path):
            digest = sha(source.read_bytes())
            name = source.name if source == target else f'{source.stem}-{digest}{source.suffix}'
            saved = backup / name
            if not saved.exists():
                shutil.copy2(source, saved)
            assert sha(saved.read_bytes()) == digest
        pending.replace(target)
        report.update(path=target.name, version=version, bytes=target.stat().st_size,
                      sha256=sha(target.read_bytes()), coreSha256=core_sha,
                      previousPackageSha256=report['sha256'], changedEntries=changed,
                      preservedCompressedEntries=len(entries) - len(allowed))
        report.pop('metadataOnlyChanges', None)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', 'utf-8')
        print(json.dumps(report, ensure_ascii=False))

if __name__ == '__main__':
    main()
