"""Read-only inventory for the Windows/VPS handoff; JSON goes to stdout.

Pass the parent of ScCsgoKnives and CSMCReverse as --workspace. Source code
trees use content hashes; large CS2 export trees use path/size inventories.
Newly archived resources are checked against their per-file SHA-256 manifests.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def inventory(root, content=False):
    if not root.is_dir():
        return {'exists': False}
    rows = []
    for path in sorted(root.rglob('*')):
        rel = path.relative_to(root)
        if any(part in {'bin', 'obj', '__pycache__', '.git'} or
               part.startswith('.syncthing.') for part in rel.parts):
            continue
        if path.is_file():
            row = [rel.as_posix(), path.stat().st_size]
            if content:
                row.append(sha256(path))
            rows.append(row)
    rows.sort(key=lambda r: r[0])
    encoded = json.dumps(rows, ensure_ascii=True, separators=(',', ':')).encode()
    return {'exists': True, 'files': len(rows), 'bytes': sum(r[1] for r in rows),
            'comparison': 'path-size-content-sha256' if content else 'path-size-only',
            'indexSha256': hashlib.sha256(encoded).hexdigest()}


def verify_manifest(root, manifest_name):
    path = root / manifest_name
    if not path.is_file():
        return {'manifestExists': False}
    manifest = json.loads(path.read_text(encoding='utf-8'))
    failures = []
    for entry in manifest['files']:
        file = (root / entry['path']).resolve()
        if not file.is_relative_to(root.resolve()):
            failures.append({'path': entry['path'], 'error': 'outside resource root'})
        elif not file.is_file():
            failures.append({'path': entry['path'], 'error': 'missing'})
        elif file.stat().st_size != entry['bytes'] or sha256(file) != entry['sha256']:
            failures.append({'path': entry['path'], 'error': 'content mismatch'})
    return {'manifestExists': True, 'manifestSha256': sha256(path),
            'expectedFiles': len(manifest['files']), 'failureCount': len(failures),
            'failures': failures}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--workspace', type=Path, default=Path.home() / 'workspaces')
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    paths = {
        'ScCsgoKnives/src/ScCsgoKnives': True,
        'ScCsgoKnives/tools': True,
        'ScCsgoKnives/docs/feedback-2026-09-07': True,
        'ScCsgoBox/src/ScCsgoBox': True,
    }
    export = 'CSMCReverse/local_cs2_analysis/all_weapons'
    for folder in ['01_weapon_data', '02_models', '03_legacy_vmodels_materials',
                   '04_current_weapon_materials', '05_audio', '06_particles',
                   '07_scope', '08_first_person', '09_knives', '10_paints', '11_icons']:
        paths[export + '/' + folder] = False
    manifests = {
        export + '/12_grenades': 'source-relocation-manifest.json',
        'CSMCReverse/references/scapi-1.9.2.1-windows-20260907': 'manifest.json',
    }
    documents = ['AGENTS.md', 'ASSET_SOURCES.md', 'docs/survival-grenade-sources.json',
                 'docs/community-feedback-plan-2026-09-07.md',
                 'docs/vps-handoff-2026-09-07.md']
    result = {
        'workspace': str(workspace),
        'inventories': {p: inventory(workspace / p, content) for p, content in paths.items()},
        'archives': {p: verify_manifest(workspace / p, name) for p, name in manifests.items()},
        'documents': {p: sha256(workspace / 'ScCsgoKnives' / p)
                      if (workspace / 'ScCsgoKnives' / p).is_file() else None for p in documents},
        'pythonModules': {m: importlib.util.find_spec(m) is not None
                          for m in ['PIL', 'numpy', 'soundfile', 'scipy', 'moderngl']},
        'commandsOnPath': {c: shutil.which(c) for c in
                           ['dotnet', 'python3', 'ffmpeg', 'xvfb-run', 'ilspycmd', 'glslangValidator']},
        'explicitToolPaths': {p: (Path.home() / p).is_file() for p in
                              ['.dotnet/dotnet', '.dotnet/tools/ilspycmd',
                               'tools/ffmpeg', 'tools/glslang/bin/glslang']},
        'nugetCoreDlls': {str(p.relative_to(Path.home())): sha256(p)
                         for p in (Path.home() / '.nuget/packages').glob(
                             'survivalcraftapi.*/1.9.2.1/lib/net10.0/*.dll')
                         if p.name in {'Survivalcraft.dll', 'Engine.dll', 'EntitySystem.dll'}},
    }
    print(json.dumps(result, ensure_ascii=True, indent=2))


if __name__ == '__main__':
    main()
