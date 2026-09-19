"""Remove CS2 locomotion travel from the derived chicken GLB, never the source export."""
import json, struct
from pathlib import Path


def remove_root_travel(document):
    roots = {i for i, n in enumerate(document['nodes']) if n.get('name') == 'root_motion'}
    if len(roots) != 1:
        raise ValueError('Expected one CS2 root_motion bone')
    removed = []
    for animation in document['animations']:
        channels = animation['channels']
        motion = [c for c in channels if c['target']['node'] in roots and c['target']['path'] == 'translation']
        animation['channels'] = [c for c in channels if c not in motion]
        removed.append({'clip': animation['name'], 'translationChannelsRemoved': len(motion)})
    # The rest skeleton has its root_motion at the origin; root's height/bob and
    # every limb channel stay untouched. Survivalcraft owns world displacement.
    for i in roots:
        if any(abs(v) > 1e-6 for v in document['nodes'][i].get('translation', [0, 0, 0])):
            raise ValueError('Unexpected nonzero rest root_motion')
    return removed


def patch(path):
    data = path.read_bytes()
    size = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20+size])
    remainder = data[20+size:]
    removed = remove_root_travel(doc)
    js = json.dumps(doc, separators=(',', ':')).encode()
    js += b' ' * (-len(js) % 4)
    path.write_bytes(struct.pack('<4sII', b'glTF', 2, 20+len(js)+len(remainder))
                     + struct.pack('<II', len(js), 0x4e4f534a) + js + remainder)
    return removed


if __name__ == '__main__':
    import hashlib
    root = Path(__file__).resolve().parent.parent
    target = root/'src/ScCsgoKnives/Assets/Models/ScCsgoKnives/chicken.glb'
    before = hashlib.sha256(target.read_bytes()).hexdigest()
    changes = patch(target)
    report = dict(target=str(target.relative_to(root)), beforeSha256=before,
                  afterSha256=hashlib.sha256(target.read_bytes()).hexdigest(), changes=changes,
                  reason='CS2 run root_motion travels 2.65034m/loop; native locomotion already moves the body. Remove only root translation channels.')
    (root/'docs/chicken-in-place-20260919.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
