"""Read local mod XDB templates for PackageCheck; never modifies source scmods.

Supports the API archive wrappers, Assets.pkg and the installed Subnautica pack.
Requires cryptography. Extracts only XDB and modinfo, not textures/audio/models.
"""
import argparse
import hashlib
import hmac
import io
import json
from pathlib import Path
import struct
import zipfile
import zlib
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
manifest = []
for index, path in enumerate(sorted(args.source.glob('*.scmod'))):
    raw = path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    header1 = '有头有脸天才少年,耍猴表演敢为人先'.encode()
    header2 = '修改他人mod请获得原作者授权，否则小心出名！'.encode()
    if raw.startswith(header1):
        raw = raw[len(header1):][::-1]
    elif raw.startswith(header2):
        body = raw[len(header2):]
        half = (len(body) + 1) // 2
        raw = bytearray(len(body))
        raw[::2], raw[1::2] = body[:half], body[half:]
    folder = args.output / f'{index:02d}'
    selected = []

    def save(name, data):
        target = (folder / name).resolve()
        if not target.is_relative_to(folder.resolve()):
            raise ValueError('Unsafe archive path: ' + name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        selected.append(name)

    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        metadata = json.loads(archive.read('modinfo.json').decode('utf-8-sig'))
        package = metadata['PackageName']
        if 'Assets.pkg' in archive.namelist():
            key = hashlib.pbkdf2_hmac('sha256', package.encode(), b'SurvivalcraftApi_SCAE', 10000, 32)
            data = archive.read('Assets.pkg')
            assert data[:8] == b'SCENC1.0'
            version = struct.unpack_from('<I', data, 8)[0]
            assert version >= 3
            plain = AESGCM(key).decrypt(data[12:24], data[40:] + data[24:40], None)
            if version < 4:
                plain = zlib.decompress(plain, -15)
            with zipfile.ZipFile(io.BytesIO(plain)) as assets:
                for name in assets.namelist():
                    if name.lower().endswith('.xdb'):
                        save('data/' + name, assets.read(name))
        for index_path in (n for n in archive.namelist() if n.endswith('/assets.index')):
            if 'Subnautica' not in package:
                raise ValueError('Unrecognized custom asset pack: ' + package)
            key = hashlib.sha256(b'Tp.Subnautica.AssetPack.2026').digest()
            mac = hashlib.sha256(key + b'AMPD-HMAC').digest()
            data = archive.read(index_path)
            assert data[:4] == b'AMPK'
            plain = AESGCM(key).decrypt(data[8:20], data[40:] + data[20:36], None)
            count = struct.unpack_from('<I', plain, 0)[0]
            offset, chunks = 4, {}
            for _ in range(count):
                size = struct.unpack_from('<H', plain, offset)[0]
                offset += 2
                name = plain[offset:offset + size].decode()
                offset += size
                chunk, start, length = struct.unpack_from('<IQQ', plain, offset)
                offset += 20
                checksum = plain[offset:offset + 32]
                offset += 32
                if not name.lower().endswith('.xdb'):
                    continue
                if chunk not in chunks:
                    chunk_path = index_path.rsplit('/', 1)[0] + f'/assets.chunk.{chunk:03d}'
                    blob = archive.read(chunk_path)
                    assert hmac.compare_digest(hmac.digest(mac, blob[:-32], 'sha256'), blob[-32:])
                    chunks[chunk] = blob[:-32]
                blob = chunks[chunk]
                payload = AESGCM(key).decrypt(blob[start:start + 12], blob[start + 28:start + 28 + length] + blob[start + 12:start + 28], None)
                assert hashlib.sha256(payload).digest() == checksum
                save('data/' + name, payload)
        for name in archive.namelist():
            if name.lower().endswith('.xdb') or name == 'modinfo.json':
                save(name, archive.read(name))
    row = dict(file=path.name, sha256=digest, folder=str(folder), files=selected)
    manifest.append(row)
    print(json.dumps(row))
(args.output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
