"""Verify Full/Lite parity and compare original-quality assets to a previous package."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import zipfile
from PIL import Image
import numpy as np


def digest(archive, name):
    with archive.open(name) as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--full', required=True)
    ap.add_argument('--lite', required=True)
    ap.add_argument('--baseline', required=True)
    ap.add_argument('--json', required=True)
    args = ap.parse_args()
    checks, changes = [], []
    def check(name, ok, detail=''):
        checks.append(dict(name=name, ok=bool(ok), detail=detail))
    with zipfile.ZipFile(args.full) as full, zipfile.ZipFile(args.lite) as lite, zipfile.ZipFile(args.baseline) as old:
        check('same-paths', set(full.namelist()) == set(lite.namelist()))
        fm = json.loads(full.read('modinfo.json')); lm = json.loads(lite.read('modinfo.json'))
        check('same-package-identity', fm['PackageName'] == lm['PackageName'] == 'zh667.ScCsgoKnives')
        check('same-version-and-gameplay-metadata', all(fm[k] == lm[k] for k in fm if k not in ('Name', 'Description')))
        check('edition-markers', b'Name="Full"' in full.read('Assets/ScCsgoKnivesEdition.xml') and b'Name="Lite"' in lite.read('Assets/ScCsgoKnivesEdition.xml'))
        for name in full.namelist():
            if name.endswith('.png'):
                a, b = full.read(name), lite.read(name)
                w, h = struct.unpack('>II', a[16:24]); lw, lh = struct.unpack('>II', b[16:24])
                check('full-preserves/' + name, a == old.read(name))
                if (w, h) == (1024, 1024):
                    check('lite-size/' + name, (lw, lh) == (512, 512))
                    record = {'path': name, 'sourceSha256': hashlib.sha256(a).hexdigest(), 'liteSha256': hashlib.sha256(b).hexdigest(), 'sourceSize': [w,h], 'liteSize': [lw,lh]}
                    if name.endswith('_normal.png'):
                        arr = np.asarray(Image.open(io.BytesIO(b)).convert('RGB'), dtype=np.float32) / 127.5 - 1
                        error = float(np.max(np.abs(np.linalg.norm(arr, axis=2) - 1)))
                        check('normal-unit-vectors/' + name, error < .012, str(error)); record['normalMaxLengthError'] = error
                    changes.append(record)
                else:
                    check('other-textures-identical/' + name, a == b)
            elif name not in ('modinfo.json', 'Assets/ScCsgoKnivesEdition.xml'):
                check('identical/' + name, digest(full, name) == digest(lite, name))
        # New code is allowed; all previously shipped external gameplay/visual resources remain intact in Full.
        for name in old.namelist():
            if name.startswith('Assets/'):
                check('baseline-asset/' + name, name in full.namelist() and digest(old, name) == digest(full, name))
        check('resized-exactly-177', len(changes) == 177)
        check('same-dll', digest(full, 'ScCsgoKnives.dll') == digest(lite, 'ScCsgoKnives.dll'))
    result = {'full': str(Path(args.full).resolve()), 'lite': str(Path(args.lite).resolve()),
              'checks': checks, 'failed': sum(not c['ok'] for c in checks), 'resized': changes}
    Path(args.json).write_bytes(json.dumps(result, ensure_ascii=False, indent=2).encode('utf-8'))
    print(json.dumps({'checks':len(checks), 'failed':result['failed'], 'resized':len(changes)}))
    raise SystemExit(1 if result['failed'] else 0)

if __name__ == '__main__':
    main()
