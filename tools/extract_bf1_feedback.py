"""Extract only BF1's normal kill confirmation. Requires Frostbite-Scripts and vgmstream.

python -X utf8 tools/extract_bf1_feedback.py --game <Battlefield 1>
  --frostbite <Frostbite-Scripts> --decoder <vgmstream-cli.exe> --work <temporary-directory>
No game files are modified. The intermediate dump stays outside packaged assets.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser()
    for name in ('game', 'frostbite', 'decoder', 'work'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--kind', choices=('kill', 'ding'), default='kill')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    game, scripts, decoder, work = (getattr(args, n).resolve() for n in ('game', 'frostbite', 'decoder', 'work'))
    sys.path.insert(0, str(scripts / 'frostbite3'))
    os.chdir(scripts / 'frostbite3')  # Upstream loads thirdparty decoder DLLs relative to cwd.
    import dbo, cas, payload, ebx
    import soundfile as sf
    for part in ('Data', 'Patch'):
        for cat in (game / part).rglob('cas.cat'):
            cas.readCat3(str(cat))
    name = 'sound/ui/ui_killmessage_headshotadd_wave' if args.kind == 'ding' else 'sound/ui/ui_killmessage_wave'
    ebx_path = work / 'ebx' / (name + '.ebx')
    source = None
    wanted = set()
    for part in ('Patch', 'Data'):
        # Normal kill feedback is in frontend; fall back to other CAS bundles.
        for toc_path in sorted((game / part).rglob('*.toc'), key=lambda p: 'frontend' not in p.name):
            sb_path = toc_path.with_suffix('.sb')
            if not sb_path.exists():
                continue
            toc = dbo.readToc(str(toc_path))
            if not toc.get('cas'):
                continue
            with sb_path.open('rb') as stream:
                for entry in toc.get('bundles', []):
                    if entry.get('base'):
                        continue
                    stream.seek(entry.get('offset'))
                    bundle = dbo.DbObject(stream)
                    for e in bundle.get('ebx', []):
                        if e.get('name') == name:
                            if payload.casPatchedBundlePayload(e, str(ebx_path), False):
                                source = dict(toc=str(toc_path), ebxSha1=e.get('sha1').hex())
                                break
                    if source:
                        break
            if source:
                break
        if source:
            break
    if not source:
        raise RuntimeError('Normal kill-message asset was not found')
    obj = ebx.Dbx(str(ebx_path), str(work / 'ebx'))
    for c in obj.prim.get('Chunks'):
        wanted.add(c.value.get('ChunkId').format())
    chunk_ids = sorted(wanted)
    def extract(c):
        guid = c.get('id').format()
        if guid in wanted and payload.casChunkPayload(c, str(work / 'chunks' / (guid + '.chunk'))):
            wanted.remove(guid)
    for part in ('Patch', 'Data'):
        for toc_path in sorted((game / part).rglob('*.toc'), key=lambda p: ('globals' not in p.name, 'chunks' not in p.name)):
            if not wanted:
                break
            toc = dbo.readToc(str(toc_path))
            for c in toc.get('chunks', []):
                extract(c)
            sb_path = toc_path.with_suffix('.sb')
            if not wanted or not toc.get('cas') or not sb_path.exists():
                continue
            with sb_path.open('rb') as stream:
                for entry in toc.get('bundles', []):
                    if entry.get('base'):
                        continue
                    stream.seek(entry.get('offset'))
                    bundle = dbo.DbObject(stream)
                    for c in bundle.get('chunks', []):
                        extract(c)
                    if not wanted:
                        break
    if wanted:
        raise RuntimeError(f'Missing sound chunks: {wanted}')
    obj.extractAssets(str(work / 'chunks'), str(work / 'chunks'), str(work / 'res'), str(work / 'sps'))
    asset = 'Sound/UI/UI_KillMessage_HeadShotAdd_Wave' if args.kind == 'ding' else 'Sound/UI/UI_KillMessage_Wave'
    sps = work / ('sps/' + asset + '.sps')
    wav = work / 'kill.wav'
    subprocess.run([str(decoder), '-o', str(wav), str(sps)], check=True)
    samples, rate = sf.read(wav)
    target = root / ('src/ScCsgoKnives/Assets/Audio/ScCsgoKnives/' + ('bf1_kill_ding.wav' if args.kind == 'ding' else 'bf1_kill_confirm.ogg'))
    if args.kind == 'ding':
        from build_bf1_ding import build
        source['conversion'] = build(wav, target)
    else:
        sf.write(target, samples, rate, format='OGG', subtype='VORBIS')
    source.update(asset=asset, chunkIds=chunk_ids,
                  sampleRate=rate, samples=len(samples), sha256=hashlib.sha256(target.read_bytes()).hexdigest())
    (work / 'provenance.json').write_bytes((json.dumps(source, indent=2) + '\n').encode('utf-8'))
    print(target)


if __name__ == '__main__':
    main()
