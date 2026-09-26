"""Recompress immutable split-package payloads using best-of existing/Zopfli Deflate.

Install zopfli==0.4.1 in STAGE/deps. Run through tools/dev.ps1. This tool
creates candidates only; publishing requires separate native Game.ZipArchive checks.
No decoded asset, metadata, assembly, package member or runtime format is changed.
"""
from pathlib import Path
import argparse
import concurrent.futures
import hashlib
import json
import os
import shutil
import sys
import time
import zipfile
import zlib

from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {
    'core': ('[API1.9]CS武器1.3.0-轻量包.scmod',
             'b84a80ca9b38bba36be6c036365ff94d1ad61a89bbcc374b807cd619bd7ecd2c'),
    'agents': ('[API1.9]CS武器1.3.0-探员包.scmod',
               '89a2f12312d1026c790c21f8c24a36e46c058f5728508eb09765515f05f46760'),
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encode(job):
    stage, owner, archive, name, iterations, splitmax = job
    sys.path.insert(0, str(Path(stage) / 'deps'))
    import zopfli
    from zopfli.zlib import compress
    assert zopfli.__version__ == '0.4.1', zopfli.__version__
    with zipfile.ZipFile(archive) as z:
        info = z.getinfo(name)
        data = z.read(info)
    digest = sha(data)
    key = f'{digest}-zopfli041-i{iterations}-b{splitmax}'
    cache = Path(stage) / 'cache' / (key + '.deflate')
    metadata = cache.with_suffix('.json')
    started = time.perf_counter()
    cached = cache.exists() and metadata.exists()
    if cached:
        encoded = cache.read_bytes()
        elapsed = json.loads(metadata.read_bytes())['seconds']
    else:
        wrapped = compress(data, numiterations=iterations,
                           blocksplitting=1, blocksplittingmax=splitmax)
        # Zlib is exactly a two-byte header, raw Deflate, and Adler-32 trailer
        # here: no dictionary flag. Validate both containers before reuse.
        assert len(wrapped) >= 6 and not (wrapped[1] & 0x20)
        assert zlib.decompress(wrapped) == data
        encoded = wrapped[2:-4]
        elapsed = time.perf_counter() - started
    assert zlib.decompress(encoded, -15) == data, name
    result = dict(owner=owner, name=name, payloadSha256=digest,
                  bytes=info.file_size, originalCompressedBytes=info.compress_size,
                  zopfliBytes=len(encoded), saved=max(0, info.compress_size-len(encoded)),
                  iterations=iterations, blockSplittingMax=splitmax, seconds=elapsed,
                  compressedSha256=sha(encoded), cache=str(cache.relative_to(Path(stage))),
                  fromCache=cached)
    if not cached:
        cache.write_bytes(encoded)
        metadata.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', 'utf8')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', default='.tmp/split-lossless-20260927')
    parser.add_argument('--workers', type=int, default=4)
    parser.add_argument('--large-iterations', type=int, default=5)
    parser.add_argument('--small-iterations', type=int, default=15)
    parser.add_argument('--splitmax', type=int, default=15)
    args = parser.parse_args()
    stage = (ROOT / args.stage).resolve()
    assert stage.is_relative_to(ROOT / '.tmp')
    for folder in ['baseline', 'cache', 'candidate']:
        (stage / folder).mkdir(parents=True, exist_ok=True)
    jobs = []
    baselines = {}
    for owner, (name, expected) in SOURCES.items():
        baseline = stage / 'baseline' / name
        if not baseline.exists():
            source = ROOT / 'output' / name
            assert sha(source.read_bytes()) == expected, source
            shutil.copyfile(source, baseline)
        assert sha(baseline.read_bytes()) == expected, baseline
        baselines[owner] = baseline
        with zipfile.ZipFile(baseline) as z:
            assert len(z.namelist()) == len(set(z.namelist()))
            for i in z.infolist():
                assert i.compress_type in (0, 8) and not i.flag_bits & 1
                if i.file_size >= 256 and i.compress_size < i.file_size * .98:
                    iterations = args.large_iterations if i.file_size >= 2_000_000 else args.small_iterations
                    jobs.append((str(stage), owner, str(baseline), i.filename, iterations, args.splitmax, i.file_size))
    jobs.sort(key=lambda job: job[-1], reverse=True)
    print(json.dumps(dict(tasks=len(jobs), rawBytes=sum(j[-1] for j in jobs), workers=args.workers)), flush=True)
    results = []
    started = time.perf_counter()
    with (stage / 'progress.jsonl').open('w', encoding='utf8') as log:
        with concurrent.futures.ProcessPoolExecutor(max_workers=args.workers) as pool:
            pending = [pool.submit(encode, job[:-1]) for job in jobs]
            for future in concurrent.futures.as_completed(pending):
                row = future.result()
                results.append(row)
                log.write(json.dumps(row, ensure_ascii=False)+'\n'); log.flush()
                if len(results) % 20 == 0 or row['bytes'] >= 5_000_000:
                    print(json.dumps(dict(done=len(results), total=len(jobs),
                        saved=sum(r['saved'] for r in results), elapsed=round(time.perf_counter()-started,1),
                        last=row['name'], lastSaved=row['saved'])), flush=True)
    indexed = {(r['owner'], r['name']): r for r in results}
    packages = {}
    for owner, baseline in baselines.items():
        entries = {}; members = {}
        with zipfile.ZipFile(baseline) as old:
            for info in old.infolist():
                data = old.read(info)
                row = indexed.get((owner, info.filename))
                selected = bool(row and row['saved'] > 0)
                compressed = (stage / row['cache']).read_bytes() if selected else raw_member(old, info)
                if selected:
                    assert zlib.decompress(compressed, -15) == data
                entries[info.filename] = (8 if selected else info.compress_type, info.CRC, info.file_size, compressed)
                members[info.filename] = dict(sha256=sha(data), bytes=len(data),
                    before=info.compress_size, after=len(compressed),
                    codec='zopfli' if selected else 'unchanged',
                    attempted=row is not None)
        candidate = stage / 'candidate' / baseline.name
        write_archive(candidate, entries)
        with zipfile.ZipFile(candidate) as new:
            assert new.testzip() is None
            assert set(new.namelist()) == set(members)
            for name, row in members.items():
                assert sha(new.read(name)) == row['sha256'], name
            assert json.loads(new.read('modinfo.json'))['Version'] == '1.3.0'
        before, after = baseline.stat().st_size, candidate.stat().st_size
        assert after <= before
        packages[owner] = dict(file=baseline.name, before=before, after=after, saved=before-after,
            percentSaved=100*(before-after)/before, baselineSha256=sha(baseline.read_bytes()),
            sha256=sha(candidate.read_bytes()), entries=members)
        print(json.dumps({k:v for k,v in packages[owner].items() if k!='entries'}, ensure_ascii=False), flush=True)
    record = dict(tool='zopfli', version='0.4.1', largeIterations=args.large_iterations,
        smallIterations=args.small_iterations, blockSplittingMax=args.splitmax,
        seconds=time.perf_counter()-started, packages=packages,
        experiments=sorted(results, key=lambda r:(r['owner'],r['name'])),
        payloadsIdentical=True, nativeVerified=False, published=False)
    target = stage / 'report.json'
    temporary = target.with_suffix('.partial')
    temporary.write_text(json.dumps(record, ensure_ascii=False, indent=2)+'\n', 'utf8')
    os.replace(temporary, target)


if __name__ == '__main__':
    main()
