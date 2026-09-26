"""Correct the two current public package labels without rebuilding gameplay or assets."""
import hashlib
import json
import shutil
import zipfile
import zlib
from pathlib import Path
from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output'
REPORT = OUT / 'release-public-1.3.0'
VERSION = '1.3.0'
SOURCES = {
    'Full': ('全量包', '3b7dcc6ffbdc9b3cb8142d2e571dbbc88a0b52155c28a831922c2a29ae057ec8'),
    'Lite': ('轻量包', '3402409c9a7a0a3c7b9893c1fb5bf3bc0dd1bdf42dfb08a1c934e3b984298cc4'),
}
FIELDS = {
    'modinfo.json': ('Version',),
    'Integrations/ScCsgoKnives.modinfo.json': ('Version',),
    'Integrations/ScCsgoBundle.json': ('version', 'core'),
    'Integrations/CompatibilityFamily.json': ('version',),
}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def validate_versions(archive):
    for name, fields in FIELDS.items():
        metadata = json.loads(archive.read(name))
        assert all(metadata[field] == VERSION for field in fields), name
    assert json.loads(archive.read('modinfo.json'))['PackageName'] == 'zh667.ScCsgoKnives'
    family = json.loads(archive.read('Integrations/CompatibilityFamily.json'))
    assert family['family'] == 1 and family['backup_policy'] == 'manual'
    core_sha = sha(archive.read('ScCsgoKnives.dll'))
    assert family['core_sha256'] == core_sha
    assert json.loads(archive.read('Integrations/ScCsgoBundle.json'))['coreSha256'] == core_sha
    assert archive.read('INSTALL.txt').decode('utf-8-sig').startswith('CS武器1.3.0总包')

def main():
    REPORT.mkdir(parents=True, exist_ok=True)
    for edition, (label, expected) in SOURCES.items():
        target = OUT / f'[API1.9]CS武器{VERSION}-{label}.scmod'
        before_sha = sha(target.read_bytes())
        entries = {}
        allowed = set(FIELDS) | {'INSTALL.txt'}
        with zipfile.ZipFile(target) as old:
            if json.loads(old.read('modinfo.json'))['Version'] == VERSION:
                validate_versions(old)
                assert old.testzip() is None
                report = json.loads((REPORT / f'{edition}.json').read_text('utf-8'))
                assert report['sha256'] == before_sha
                print(json.dumps({'edition': edition, 'unchanged': True, 'sha256': before_sha}))
                continue
            assert before_sha == expected, 'Unexpected source package'
            before_dlls = {n: sha(old.read(n)) for n in old.namelist() if n.endswith('.dll')}
            def add(name, data):
                compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
                entries[name] = (8, zlib.crc32(data), len(data), compressor.compress(data) + compressor.flush())
            for info in old.infolist():
                entries[info.filename] = (info.compress_type, info.CRC, info.file_size, raw_member(old, info))
            for name, fields in FIELDS.items():
                metadata = json.loads(old.read(name))
                for field in fields:
                    assert metadata[field] == '1.7.2', (name, field)
                    metadata[field] = VERSION
                add(name, (json.dumps(metadata, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
            install = old.read('INSTALL.txt').decode('utf-8-sig')
            add('INSTALL.txt', install.replace('CS武器1.7.2总包', 'CS武器1.3.0总包').encode('utf-8-sig'))
            pending = target.with_suffix('.pending')
            write_archive(pending, entries)
            with zipfile.ZipFile(pending) as new:
                assert new.testzip() is None
                assert set(new.namelist()) == set(old.namelist())
                validate_versions(new)
                changed = sorted(n for n in entries if new.read(n) != old.read(n))
                assert set(changed) == allowed
                for name in set(entries) - allowed:
                    assert new.read(name) == old.read(name), name
                    assert raw_member(new, new.getinfo(name)) == raw_member(old, old.getinfo(name)), name
                after_dlls = {n: sha(new.read(n)) for n in before_dlls}
                assert after_dlls == before_dlls
        # Developer artifact provenance only; no game/world snapshot is created.
        backup = ROOT / '.tmp/public-version-130' / before_sha / target.name
        backup.parent.mkdir(parents=True, exist_ok=True)
        if not backup.exists():
            shutil.copy2(target, backup)
        assert sha(backup.read_bytes()) == before_sha
        pending.replace(target)
        result = dict(path=target.name, version=VERSION, edition=edition,
                      bytes=target.stat().st_size, sha256=sha(target.read_bytes()),
                      sourceSha256=before_sha, coreSha256=after_dlls['ScCsgoKnives.dll'],
                      dllHashes=after_dlls, entries=len(entries), changedEntries=changed,
                      preservedCompressedEntries=len(entries)-len(changed), failed=0)
        (REPORT / f'{edition}.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', 'utf-8')
        print(json.dumps(result, ensure_ascii=True))

if __name__ == '__main__':
    main()
