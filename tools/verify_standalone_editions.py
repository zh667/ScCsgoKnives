"""Verify the exact delivered Full/Lite pair, including DLL checks and texture data."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import zipfile
import numpy as np
from PIL import Image
from pack_standalone_editions import OUT, filename


def sha(b):
    return hashlib.sha256(b).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--version', default='1.1.0')
    args = parser.parse_args()
    report = OUT/('release-'+args.version)
    full, lite = filename(args.version, 'Full'), filename(args.version, 'Optimized512')
    checks = []
    def check(name, ok):
        checks.append(dict(name=name, ok=bool(ok)))
        if not ok:
            raise AssertionError(name)
    with zipfile.ZipFile(full) as a, zipfile.ZipFile(lite) as b:
        full_hash, lite_hash = sha(full.read_bytes()), sha(lite.read_bytes())
        check('same-gameplay-dll', a.read('ScCsgoKnives.dll') == b.read('ScCsgoKnives.dll'))
        for z in (a, b):
            meta = json.loads(z.read('modinfo.json'))
            check('standalone-version-and-identity/'+z.filename,
                meta['Version'] == args.version and meta['PackageName'] == 'zh667.ScCsgoKnives' and not meta['Dependencies'])
            check('no-duplicate-entries/'+z.filename, len(z.namelist()) == len(set(z.namelist())))
            check('crc/'+z.filename, z.testzip() is None)
        changed = {r['path']: r for r in json.loads(b.read('Assets/ScCsgoDerivedResources.json'))}
        for name in a.namelist():
            if name in changed:
                row = changed[name]
                target = name[:-4]+'.webp' if name.endswith('.png') else name
                check('source-and-derived-hashes/'+name,
                    sha(a.read(name)) == row['sourceSha256'] and sha(b.read(target)) == row['sha256'])
                if row['kind'] == 'texture':
                    src = Image.open(io.BytesIO(a.read(name))).convert('RGBA')
                    dst = Image.open(io.BytesIO(b.read(target))).convert('RGBA')
                    check('texture-size/'+name, dst.size == tuple(row['toSize']))
                    if row.get('alphaIsMaterialData', False):
                        check('known-opaque-material/'+name, name == 'Assets/Textures/ScCsgoKnives/c4_cs2.png')
                        src = src.convert('RGB').convert('RGBA')
                    if src.size != dst.size:
                        src = src.resize(dst.size, Image.Resampling.LANCZOS)
                    check('texture-alpha/'+name, np.array_equal(np.array(src)[:,:,3], np.array(dst)[:,:,3]))
                    if row.get('alphaIsMaterialData', False):
                        error=np.abs(np.array(src,dtype=np.float32)[:,:,:3]-np.array(dst,dtype=np.float32)[:,:,:3]).mean()
                        check('opaque-body-rgb-preserved/'+name, error < 10)
                    if row['lossless']:
                        check('special-atlas-exact/'+name, np.array_equal(np.array(src), np.array(dst)))
            elif name not in {'modinfo.json', 'ScCsgoResources.dll', 'Assets/ScCsgoResources.xml', 'Assets/ScCsgoKnivesEdition.xml'}:
                check('unchanged/'+name, a.read(name) == b.read(name))
        for which, digest in [('full', full_hash), ('lite', lite_hash)]:
            result = json.loads((report/f'{which}-check.json').read_text('utf-8-sig'))
            check('dll-report-hash/'+which, result['packageSha256'] == digest
                and result['dllSha256'] == sha(a.read('ScCsgoKnives.dll')) and result['failed'] == 0)
    (report/'delivery-verification.json').write_text(json.dumps(dict(
        fullSha256=full_hash, liteSha256=lite_hash, checks=checks, failed=0), indent=2)+'\n','utf-8')
    print('Verified', len(checks), 'delivery checks; DLL reports match the delivered bytes.')


if __name__ == '__main__':
    main()
