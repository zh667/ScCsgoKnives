"""Pack bin/Release/net10.0 into output/ScCsgoKnives-<version>.scmod.

A .scmod is a plain zip of the build output (dll, modinfo.json, Assets/, notices).
The VPS has no `zip`, hence Python. The version comes from modinfo.json so the
file name can never disagree with what the game reports.
"""
import json, os, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = os.path.join(ROOT, 'src/ScCsgoKnives/bin/Release/net10.0')
SOURCE = os.path.join(ROOT, 'src/ScCsgoKnives')
OUT = os.path.join(ROOT, 'output')

def newest_source_mtime():
    newest = 0.0
    for dirpath, dirs, files in os.walk(os.path.join(ROOT, 'src/ScCsgoKnives')):
        dirs[:] = [d for d in dirs if d not in ('bin', 'obj')]
        for f in files:
            if f.endswith(('.cs', '.csproj', '.json')):
                newest = max(newest, os.path.getmtime(os.path.join(dirpath, f)))
    return newest

def main():
    # Refuse to ship a stale build: a failed compile leaves the previous dll in
    # place, and `dotnet build | grep` hides the failure from a && chain.
    dll = os.path.join(BUILD, 'ScCsgoKnives.dll')
    if not os.path.exists(dll) or os.path.getmtime(dll) < newest_source_mtime():
        raise SystemExit('ScCsgoKnives.dll is older than the sources; build first (and check it succeeded).')
    version = json.load(open(os.path.join(BUILD, 'modinfo.json'), encoding='utf-8'))['Version']
    os.makedirs(OUT, exist_ok=True)
    target = os.path.join(OUT, f'ScCsgoKnives-{version}.scmod')
    count = 0
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as z:
        for top in ('THIRD_PARTY_NOTICES.md', 'LICENSE', 'ASSET_SOURCES.md', 'ScCsgoKnives.dll', 'modinfo.json'):
            path = os.path.join(BUILD, top)
            if not os.path.exists(path):
                raise SystemExit(f'missing {top} in {BUILD}')
            z.write(path, top); count += 1
        # Enumerate the source manifest, never stale copied files left by an
        # incremental build after assets were removed (the CS2-only migration).
        for dirpath, _, files in os.walk(os.path.join(SOURCE, 'Assets')):
            for f in sorted(files):
                source = os.path.join(dirpath, f)
                relative = os.path.relpath(source, SOURCE)
                full = os.path.join(BUILD, relative)
                if not os.path.isfile(full) or open(source, 'rb').read() != open(full, 'rb').read():
                    raise SystemExit(f'missing or stale build asset: {relative}; rebuild first')
                z.write(full, relative.replace(os.sep, '/')); count += 1
    print(f'{target}: {count} entries, {os.path.getsize(target)/1e6:.1f} MB')

if __name__ == '__main__':
    main()
