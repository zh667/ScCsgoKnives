"""Stage/publish the NMM assembly-identity fix while preserving the current 1.3.0 packages."""
import argparse
import hashlib
import json
import shutil
import zipfile
import zlib
from pathlib import Path
from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output'
STAGE = ROOT / '.tmp/nmm-official-audit-20260926'
CANDIDATES = STAGE / 'candidates'
REPORT = OUT / 'release-nmm-fix-1.3.0'
PAYLOAD = 'Integrations/ScCsgoAppearance.bin'
TACTICAL = 'ScCsgoTactical.dll'
SOURCES = {
    'Full': ('全量包', '25f9641a15352a0d5a6fd940e738b6f189b34b8173e4cf3c94d80c6e1de9e622'),
    'Lite': ('轻量包', '0a7ba7c1cd1d24dafc0764c0ce517cc1999cd657ecc485d9f67a4b1b13610308'),
}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def read(path):
    return json.loads(path.read_text('utf-8-sig'))

def validate():
    packages = {}
    reports = []
    for edition in SOURCES:
        item = read(STAGE / f'candidate-{edition}.json')
        candidate = CANDIDATES / item['path']
        assert sha(candidate.read_bytes()) == item['sha256']
        packages[edition] = item['sha256']
        names = [f'{provider}-{edition}-{mode}'
                 for provider in ('official', 'source-built')
                 for mode in ('both', 'reversed', 'none', 'nmm-only', 'neo-only', 'disabled', 'outdated')]
        names += [f'{kind}-{edition}' for kind in ('gate', 'native')]
        for name in names:
            path = STAGE / (name + '.json')
            result = read(path)
            assert path.stat().st_mtime_ns >= candidate.stat().st_mtime_ns, f'Stale report: {name}'
            assert result['failed'] == 0 and all(c.get('ok') is True for c in result['checks']), name
            reports.append(dict(name=name, checks=len(result['checks']), failed=0, sha256=sha(path.read_bytes())))
    compatibility = read(STAGE / 'compatibility.json')
    assert compatibility['failed'] == 0 and compatibility['passed'] == compatibility['count']
    assert all(c.get('ok') is True for c in compatibility['checks'])
    assert [m['sha256'] for m in compatibility['modules']] == [
        '0028d99a9f15bb7e5ef8971efb14caab5b0c47b30f63e2386a465744c22c86d4',
        '166073263c689563385130a3ea4527f7c5ea1ee8867a4ca90d28b7f487d64e24',
        read(STAGE / 'candidate-Full.json')['dllHashes']['ScCsgoKnives.dll']]
    assert read(STAGE / 'candidate-Lite.json')['dllHashes']['ScCsgoKnives.dll'] == compatibility['modules'][2]['sha256']
    negative = read(STAGE / 'negative-control.json')
    assert negative['failed'] == 1 and 'sc-nekomekomodel, Version=1.0.0.0' in json.dumps(negative)
    result = dict(date='2026-09-26', failed=0, packages=packages,
                  checks=sum(r['checks'] for r in reports)+compatibility['count'], reports=reports,
                  compatibility=dict(checks=compatibility['count'], failed=0, modules=compatibility['modules']),
                  negativeControl=dict(expectedFailure=True, missingAssembly='sc-nekomekomodel, Version=1.0.0.0'),
                  scope='Native headless assembly/database/resource/save checks; not graphical or Android acceptance')
    (STAGE / 'validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
    return result

def stage():
    CANDIDATES.mkdir(parents=True, exist_ok=True)
    replacement = (ROOT / 'src/ScCsgoAppearance/bin/Release/net10.0/ScCsgoAppearance.dll').read_bytes()
    replacements = {PAYLOAD: replacement, TACTICAL: (ROOT / 'src/ScCsgoTactical/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes()}
    for edition, (label, expected) in SOURCES.items():
        source = OUT / f'[API1.9]CS武器1.3.0-{label}.scmod'
        assert sha(source.read_bytes()) == expected, 'Source changed; re-audit before patching'
        target = CANDIDATES / source.name
        entries = {}
        with zipfile.ZipFile(source) as old:
            assert json.loads(old.read('modinfo.json'))['Version'] == '1.3.0'
            old_payload_sha = sha(old.read(PAYLOAD))
            assert old_payload_sha != sha(replacement)
            for info in old.infolist():
                entries[info.filename] = (info.compress_type, info.CRC, info.file_size, raw_member(old, info))
            for name,data in replacements.items():
                compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
                entries[name] = (8, zlib.crc32(data), len(data), compressor.compress(data) + compressor.flush())
            pending = target.with_suffix('.pending')
            write_archive(pending, entries)
            with zipfile.ZipFile(pending) as new:
                assert new.testzip() is None
                assert set(new.namelist()) == set(old.namelist())
                for name in entries:
                    assert new.read(name) == (replacements[name] if name in replacements else old.read(name)), name
                    if name not in replacements:
                        assert raw_member(new, new.getinfo(name)) == raw_member(old, old.getinfo(name)), name
                dll_hashes = {n: sha(new.read(n)) for n in new.namelist() if n.endswith('.dll')}
        pending.replace(target)
        result = dict(path=source.name,version='1.3.0',edition=edition,bytes=target.stat().st_size,
                      sha256=sha(target.read_bytes()),sourceSha256=expected,
                      appearanceSha256=sha(replacement),previousAppearanceSha256=old_payload_sha,
                      dllHashes=dll_hashes,entries=len(entries),changedEntries=sorted(replacements),
                      preservedCompressedEntries=len(entries)-len(replacements))
        (STAGE / f'candidate-{edition}.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
        print(json.dumps(result,ensure_ascii=True))

def publish():
    validation = validate()
    assert validation['failed'] == 0
    items = []
    for edition in SOURCES:
        report = read(STAGE / f'candidate-{edition}.json')
        candidate = CANDIDATES / report['path']
        target = OUT / report['path']
        assert sha(candidate.read_bytes()) == report['sha256'] == validation['packages'][edition]
        assert sha(target.read_bytes()) == report['sourceSha256'], 'Published source changed during testing'
        for provider in ('official','source-built'):
            for mode in ('both','reversed','none','nmm-only','neo-only','disabled','outdated'):
                check = read(STAGE / f'{provider}-{edition}-{mode}.json')
                assert check['failed'] == 0, (provider,edition,mode)
        for kind in ('gate','native'):
            assert read(STAGE / f'{kind}-{edition}.json')['failed'] == 0
        items.append((report,candidate,target))
    # Preserve exact developer delivery inputs; do not touch installed mods or worlds.
    for report,candidate,target in items:
        saved = STAGE / 'previous-deliveries' / report['sourceSha256'] / target.name
        saved.parent.mkdir(parents=True,exist_ok=True)
        if not saved.exists():shutil.copy2(target,saved)
        assert sha(saved.read_bytes()) == report['sourceSha256']
        shutil.copy2(candidate,target.with_suffix('.pending'))
    REPORT.mkdir(parents=True,exist_ok=True)
    for report,candidate,target in items:
        target.with_suffix('.pending').replace(target)
        assert sha(target.read_bytes()) == report['sha256']
        (REPORT / (report['edition']+'.json')).write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
        print(json.dumps(dict(edition=report['edition'],version='1.3.0',sha256=report['sha256'],published=True)))

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    mode=parser.add_mutually_exclusive_group()
    mode.add_argument('--publish',action='store_true')
    mode.add_argument('--validate',action='store_true')
    args=parser.parse_args()
    if args.publish: publish()
    elif args.validate: print(json.dumps(validate(),ensure_ascii=True))
    else: stage()
