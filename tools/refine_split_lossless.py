"""Try official ECT 0.9.5 ZIP recompression, then choose the smallest exact stream.

Run 'ect' alongside recompress_split_lossless.py, 'merge' after both finish, then
'validate', and finally 'publish'. All intermediate work stays in project .tmp.
"""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import zipfile

from recompress_split_lossless import ROOT, SOURCES, sha
from pack_single_scmods import raw_member, write_archive

ECT_SHA = '4ff4d81950f88c2cf734a5a845c00ca3c52950f68b6588a3e803a37b03ac54b2'
NATIVE = ROOT / '.tmp/split-lite-130-20260926/load-check/bin/Release/net10.0/TacticalLoadCheck.dll'


def read(path):
    return json.loads(path.read_bytes())


def save(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', 'utf8')


def inventory(path):
    with zipfile.ZipFile(path) as z:
        assert z.testzip() is None
        assert len(z.namelist()) == len(set(z.namelist()))
        return {i.filename: sha(z.read(i)) for i in z.infolist()}


def ect(stage):
    binary = stage / 'bin/ect.exe'
    assert sha(binary.read_bytes()) == ECT_SHA
    work = stage / 'ect'; work.mkdir(exist_ok=True)
    def run(owner):
        name, expected = SOURCES[owner]
        baseline = stage / 'baseline' / name
        assert sha(baseline.read_bytes()) == expected
        candidate = work / (owner+'.zip')
        shutil.copyfile(baseline, candidate)
        options = ['-9', '-zip', '--strict', '--disable-png', '--disable-jpg', '--mt-deflate=2']
        started = time.perf_counter()
        with (work / (owner+'.log')).open('w', encoding='utf8') as log:
            subprocess.run([str(binary), *options, str(candidate)],
                           stdout=log, stderr=subprocess.STDOUT, check=True)
        assert inventory(baseline) == inventory(candidate), owner
        row = dict(owner=owner, seconds=time.perf_counter()-started, options=options,
                   before=baseline.stat().st_size, after=candidate.stat().st_size,
                   sha256=sha(candidate.read_bytes()), payloadsIdentical=True)
        save(work / (owner+'.json'), row)
        print(json.dumps(row), flush=True)
        return row
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        rows = list(pool.map(run, SOURCES))
    save(stage/'ect-report.json', dict(version='0.9.5', executableSha256=ECT_SHA, packages=rows))


def merge(stage):
    zopfli = read(stage/'report.json')
    ect_report = read(stage/'ect-report.json')
    target_dir = stage/'final'; target_dir.mkdir(exist_ok=True)
    packages = {}
    for owner, (name, expected) in SOURCES.items():
        paths = [stage/'baseline'/name, stage/'candidate'/name, stage/'ect'/(owner+'.zip')]
        assert sha(paths[0].read_bytes()) == expected
        assert sha(paths[1].read_bytes()) == zopfli['packages'][owner]['sha256']
        assert sha(paths[2].read_bytes()) == next(r['sha256'] for r in ect_report['packages'] if r['owner']==owner)
        archives = [zipfile.ZipFile(p) for p in paths]
        try:
            member_names = set(archives[0].namelist())
            assert all(set(z.namelist()) == member_names for z in archives)
            entries = {}; members = {}
            for name_in_zip in sorted(member_names):
                data = archives[0].read(name_in_zip)
                assert all(z.read(name_in_zip) == data for z in archives[1:])
                best = min(range(3), key=lambda k: archives[k].getinfo(name_in_zip).compress_size)
                z = archives[best]; info = z.getinfo(name_in_zip)
                entries[name_in_zip] = (info.compress_type, info.CRC, info.file_size, raw_member(z, info))
                members[name_in_zip] = dict(sha256=sha(data), bytes=len(data),
                    before=archives[0].getinfo(name_in_zip).compress_size, after=info.compress_size,
                    selected=['original', 'zopfli', 'ect'][best])
            final = target_dir/name
            write_archive(final, entries)
        finally:
            for z in archives: z.close()
        assert inventory(final) == {n:r['sha256'] for n,r in members.items()}
        before = paths[0].stat().st_size; after = final.stat().st_size
        assert after <= min(p.stat().st_size for p in paths)
        packages[owner] = dict(file=name, before=before, after=after, saved=before-after,
            percentSaved=100*(before-after)/before, sha256=sha(final.read_bytes()),
            baselineSha256=expected, entries=members)
        print(json.dumps({k:v for k,v in packages[owner].items() if k!='entries'},ensure_ascii=False),flush=True)
    save(stage/'final-report.json', dict(packages=packages, payloadsIdentical=True,
        nativeVerified=False, published=False, zopfliReportSha256=sha((stage/'report.json').read_bytes()),
        ectReportSha256=sha((stage/'ect-report.json').read_bytes())))


def validate(stage):
    report = read(stage/'final-report.json')
    checks = {}
    for owner, row in report['packages'].items():
        baseline = stage/'baseline'/row['file']; final = stage/'final'/row['file']
        assert sha(baseline.read_bytes()) == row['baselineSha256']
        assert sha(final.read_bytes()) == row['sha256']
        result = stage/(owner+'-native.json')
        subprocess.run(['dotnet', str(NATIVE), '--archive-identical', str(baseline), str(final), str(result)],check=True)
        check = read(result)
        assert check['failed'] == 0 and check['count'] == len(row['entries'])
        assert check['nativeReader'] == 'Game.ZipArchive'
        checks[owner] = dict(**check, sha256=sha(result.read_bytes()),
                            baselineSha256=row['baselineSha256'], candidateSha256=row['sha256'])
    report.update(nativeVerified=True, checks=checks, nativeRunnerSha256=sha(NATIVE.read_bytes()))
    save(stage/'final-report.json', report)


def publish(stage):
    report = read(stage/'final-report.json')
    assert report['nativeVerified'] and report['payloadsIdentical']
    assert report['packages']['core']['after'] < 40_000_000
    # Validate both replacements before performing either mutation. Retain exact
    # previous deliverables under baseline; no Mods or world path is involved.
    for owner, row in report['packages'].items():
        check = report['checks'][owner]
        assert check['failed']==0 and check['candidateSha256']==row['sha256']
        assert check['sha256']==sha((stage/(owner+'-native.json')).read_bytes())
        assert sha((stage/'baseline'/row['file']).read_bytes()) == row['baselineSha256']
        candidate = stage/'final'/row['file']; output = ROOT/'output'/row['file']
        assert candidate.stat().st_size == row['after'] and sha(candidate.read_bytes()) == row['sha256']
        assert inventory(candidate) == {n:r['sha256'] for n,r in row['entries'].items()}
        assert sha(output.read_bytes()) in (row['baselineSha256'], row['sha256'])
    untouched = {p.name: sha(p.read_bytes()) for p in (ROOT/'output').glob('*.scmod')
                 if p.name not in {r['file'] for r in report['packages'].values()}}
    for row in report['packages'].values():
        output = ROOT/'output'/row['file']; temporary = output.with_suffix('.scmod.partial')
        shutil.copyfile(stage/'final'/row['file'], temporary)
        assert sha(temporary.read_bytes()) == row['sha256']
        os.replace(temporary, output)
        assert sha(output.read_bytes()) == row['sha256']
    assert all(sha((ROOT/'output'/n).read_bytes()) == h for n,h in untouched.items())
    report.update(published=True, otherPackagesUnchanged=untouched)
    save(stage/'final-report.json', report)
    print('Published the native-verified, byte-identical-payload split pair.',flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=['ect','merge','validate','publish'])
    parser.add_argument('--stage', default='.tmp/split-lossless-20260927')
    args = parser.parse_args()
    stage = (ROOT/args.stage).resolve()
    assert stage.is_relative_to(ROOT/'.tmp')
    {'ect':ect,'merge':merge,'validate':validate,'publish':publish}[args.phase](stage)
