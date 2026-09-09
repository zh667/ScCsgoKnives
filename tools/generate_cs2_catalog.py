"""Refresh lightweight CS2 resource metadata after importing animations; rebuild afterwards."""
from pathlib import Path
import json
root = Path(__file__).resolve().parents[1] / 'src/ScCsgoKnives/AnimationData'
catalog = {}
for path in sorted(root.glob('*.cs2.animation.json')):
    data = json.loads(path.read_text(encoding='utf-8'))
    catalog[path.name.removesuffix('.cs2.animation.json')] = {k: data.get(k) for k in ('Skinned', 'Parts', 'MeshParts')}
    catalog[path.name.removesuffix('.cs2.animation.json')]['Clips'] = {
        name: {k: clip[k] for k in ('SourceName', 'Alias', 'Duration', 'Events', 'Additive', 'AdditiveBase', 'AdditiveOver') if k in clip}
        for name, clip in data['Clips'].items()}
(root / 'cs2_catalog.json').write_bytes(json.dumps(catalog, separators=(',', ':')).encode('utf-8'))
print(f'{len(catalog)} CS2 assets indexed; rebuild the mod before packing.')
