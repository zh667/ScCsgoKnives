"""Pack the verified Release build as Full and/or Lite without changing source assets."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/ScCsgoKnives'
BUILD = SOURCE / 'bin/Release/net10.0'
OUT = ROOT / 'output'


def newest_source_mtime():
    return max(p.stat().st_mtime for p in SOURCE.rglob('*')
               if p.is_file() and not any(v in ('bin', 'obj') for v in p.relative_to(SOURCE).parts)
               and p.suffix in ('.cs', '.csproj', '.json'))


def check_catalog():
    root = SOURCE / 'AnimationData'
    expected = {}
    for path in sorted(root.glob('*.cs2.animation.json')):
        data = json.loads(path.read_text(encoding='utf-8'))
        expected[path.name.removesuffix('.cs2.animation.json')] = {
            key: data.get(key) for key in ('Skinned', 'Parts', 'MeshParts')}
    path = root / 'cs2_catalog.json'
    if json.loads(path.read_text(encoding='utf-8')) != expected:
        raise SystemExit('cs2_catalog.json is stale; run tools/generate_cs2_catalog.py and rebuild.')


# Edition -> the size 1024 x 1024 textures are derived at. Lite is the phone
# edition; Mini goes one step further for the smallest download (weapon textures
# visibly softer up close). Everything else - DLL, audio, gameplay - is shared.
EDITION_TEXTURE_SIZE = {'Lite': 512, 'Mini': 256}
EDITION_META = {
    'Lite': ('ScCsgoKnives 轻量版 Lite', 'CS2 real hands and all weapons. 512px textures and reduced decorative particles. 与完整版二选一安装，玩法与存档兼容。'),
    'Mini': ('ScCsgoKnives 迷你版 Mini', 'CS2 real hands and all weapons. 256px textures and reduced decorative particles; the smallest download. 与其它版本二选一安装，玩法与存档兼容。'),
}


def lite_texture(data, name, size=512):
    from PIL import Image
    import numpy as np
    with Image.open(io.BytesIO(data)) as image:
        if image.size != (1024, 1024):
            return data, None
        image = image.resize((size, size), Image.Resampling.LANCZOS)
        normal = name.endswith('_normal.png')
        if normal:
            # Normals are vectors, not colors. Normalize after filtering; preserve alpha.
            pixels = np.array(image.convert('RGBA'))
            vectors = pixels[:, :, :3].astype(np.float32) / 127.5 - 1
            length = np.linalg.norm(vectors, axis=2, keepdims=True)
            bad = length[:, :, 0] < 1e-6
            vectors /= np.maximum(length, 1e-6)
            vectors[bad] = (0, 0, 1)
            pixels[:, :, :3] = np.clip(np.rint((vectors + 1) * 127.5), 0, 255).astype(np.uint8)
            image = Image.fromarray(pixels)
        stream = io.BytesIO()
        image.save(stream, format='PNG', optimize=True)
        return stream.getvalue(), {'path': name, 'from': [1024, 1024], 'to': [size, size],
            'normalRenormalized': normal, 'sourceSha256': hashlib.sha256(data).hexdigest(),
            'resultSha256': hashlib.sha256(stream.getvalue()).hexdigest()}


def pack(edition, files, info):
    suffix = '' if edition == 'Full' else f'-{edition}'
    target = OUT / f"ScCsgoKnives-{info['Version']}{suffix}.scmod"
    transformed = []
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, path in files:
            data = path.read_bytes()
            if edition in EDITION_TEXTURE_SIZE:
                if name == 'modinfo.json':
                    meta = dict(info)
                    meta['Name'], meta['Description'] = EDITION_META[edition]
                    data = (json.dumps(meta, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
                elif name == 'Assets/ScCsgoKnivesEdition.xml':
                    data = f'<Edition Name="{edition}" />\n'.encode('utf-8')
                elif name.lower().endswith('.png'):
                    data, record = lite_texture(data, name, EDITION_TEXTURE_SIZE[edition])
                    if record:
                        transformed.append(record)
            entry = zipfile.ZipInfo.from_file(path, name)
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, data)
    manifest = {'edition': edition, 'version': info['Version'], 'package': target.name,
        'bytes': target.stat().st_size, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
        'dllSha256': hashlib.sha256((BUILD / 'ScCsgoKnives.dll').read_bytes()).hexdigest(),
        'transformed': transformed}
    target.with_suffix('.resources.json').write_bytes(json.dumps(manifest, ensure_ascii=False, indent=2).encode('utf-8'))
    print(f'{target}: {len(files)} entries, {target.stat().st_size / 1e6:.1f} MB; {len(transformed)} textures resized', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--edition', choices=('full', 'lite', 'mini', 'both', 'all'), default='full',
                        help='both = Full + Lite; all = Full + Lite + Mini')
    args = parser.parse_args()
    dll = BUILD / 'ScCsgoKnives.dll'
    if not dll.exists() or dll.stat().st_mtime < newest_source_mtime():
        raise SystemExit('ScCsgoKnives.dll is older than sources; build successfully first.')
    check_catalog()
    info = json.loads((BUILD / 'modinfo.json').read_text(encoding='utf-8'))
    files = [(name, BUILD / name) for name in ('THIRD_PARTY_NOTICES.md', 'LICENSE', 'ASSET_SOURCES.md', 'ScCsgoKnives.dll', 'modinfo.json')]
    for source in sorted((SOURCE / 'Assets').rglob('*')):
        if not source.is_file():
            continue
        relative = source.relative_to(SOURCE)
        built = BUILD / relative
        if not built.is_file() or source.read_bytes() != built.read_bytes():
            raise SystemExit(f'missing or stale build asset: {relative}; rebuild first')
        files.append((relative.as_posix(), built))
    for name, path in files:
        if not path.is_file():
            raise SystemExit(f'missing build file: {name}')
    OUT.mkdir(exist_ok=True)
    editions = {'full': ('Full',), 'lite': ('Lite',), 'mini': ('Mini',),
                'both': ('Full', 'Lite'), 'all': ('Full', 'Lite', 'Mini')}[args.edition]
    for edition in editions:
        pack(edition, files, info)


if __name__ == '__main__':
    main()
