"""Read-only world/directory or scworld ZIP size report. Never extracts or changes the world."""
import argparse
import json
from pathlib import Path
import zipfile


def category(name):
    path = Path(name)
    if path.suffix.lower() == '.snapshot':
        return 'backups'
    if path.name.lower() in ('project.xml', 'project.bak'):
        return 'project'
    if any('region' in part.lower() or 'chunk' in part.lower() for part in path.parts):
        return 'terrain'
    return 'other'


def report(source):
    source = Path(source).resolve(strict=True)
    if source.is_dir():
        rows = [{'name': str(p.relative_to(source)), 'bytes': p.stat().st_size}
                for p in source.rglob('*') if p.is_file() and not p.is_symlink()]
        basis = 'directory file bytes (comparable to the game list)'
    else:
        with zipfile.ZipFile(source) as archive:
            rows = [{'name': e.filename, 'bytes': e.file_size, 'compressedBytes': e.compress_size}
                    for e in archive.infolist() if not e.is_dir()]
        basis = 'archive uncompressed entry bytes; exported archives may omit snapshots'
    totals = dict.fromkeys(('backups', 'project', 'terrain', 'other'), 0)
    for row in rows:
        row['category'] = category(row['name'])
        totals[row['category']] += row['bytes']
    return {'source': str(source), 'basis': basis, 'totalBytes': sum(totals.values()),
            'totals': totals, 'files': sorted(rows, key=lambda r: r['bytes'], reverse=True)}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('world', help='World directory or exported .scworld path')
    args = parser.parse_args()
    print(json.dumps(report(args.world), ensure_ascii=True, indent=2))
