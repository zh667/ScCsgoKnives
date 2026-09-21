"""Summarize installed-template facts without redistributing third-party assets.

Run extract_creature_templates.py first. Arguments: extracted root, output JSON.
Values are explicit XDB overrides, not fully inherited/runtime health estimates.
"""
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root, output = map(Path, sys.argv[1:])
manifest = json.loads((root / 'manifest.json').read_text('utf-8'))
rows = []
for mod in manifest:
    folder = Path(mod['folder'])
    entities = []
    for path in folder.rglob('*.xdb'):
        for entity in ET.parse(path).iter('EntityTemplate'):
            stats = {p.get('Name'): p.get('Value') for p in entity.iter('Parameter')
                     if p.get('Name') in {'AttackResilience', 'AttackPower', 'ExplosionResilience', 'WalkSpeed'}}
            if stats:
                entities.append(dict(name=entity.get('Name'), explicitOverrides=stats))
    rows.append(dict(file=mod['file'], sha256=mod['sha256'], entities=entities))
report = dict(scope='Static installed-package metadata and explicit entity parameters; no runtime combat guarantee', mods=rows)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(mods=len(rows), entities=sum(len(m['entities']) for m in rows), output=str(output)), ensure_ascii=False))
