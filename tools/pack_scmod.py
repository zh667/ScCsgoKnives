"""Pack bin/Release/net10.0 into output/ScCsgoKnives-<version>.scmod.

A .scmod is a plain zip of the build output (dll, modinfo.json, Assets/, notices).
The VPS has no `zip`, hence Python. The version comes from modinfo.json so the
file name can never disagree with what the game reports.
"""
import json, os, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = os.path.join(ROOT, 'src/ScCsgoKnives/bin/Release/net10.0')
OUT = os.path.join(ROOT, 'output')

def main():
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
        for dirpath, _, files in os.walk(os.path.join(BUILD, 'Assets')):
            for f in sorted(files):
                full = os.path.join(dirpath, f)
                z.write(full, os.path.relpath(full, BUILD).replace(os.sep, '/')); count += 1
    print(f'{target}: {count} entries, {os.path.getsize(target)/1e6:.1f} MB')

if __name__ == '__main__':
    main()
