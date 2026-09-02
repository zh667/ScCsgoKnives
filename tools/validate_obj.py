"""Check an OBJ against Survivalcraft's Game.ObjModelReader, not a lenient one.

The reader is strict in ways most OBJ loaders are not, and every one of these
rules has a matching crash: no `o` before a face is a null dereference, a quad
throws "模型必须为三角面", a missing index throws "面参数错误", and it re-expands
three vertices per face into a ushort index buffer.
"""
import sys, glob

USHORT_MAX = 65535

def validate(path):
    v = t = n = 0
    mesh = None
    faces = {}
    errors, warnings = [], []
    for lineno, line in enumerate(open(path), 1):
        parts = line.split()
        if not parts or parts[0].startswith('#'):
            continue
        tag = parts[0]
        if tag == 'v':
            v += 1
            for c in parts[1:4]:
                if 'e' in c.lower():
                    # float.Parse does accept exponents, so this is precautionary
                    warnings.append(f'{lineno}: exponent notation "{c}"')
        elif tag == 'vt': t += 1
        elif tag == 'vn': n += 1
        elif tag == 'o':
            mesh = parts[1] if len(parts) > 1 else ''
            faces.setdefault(mesh, 0)
        elif tag == 'mtllib':
            errors.append(f'{lineno}: mtllib requires a shipped .mtl asset')
        elif tag == 'f':
            if mesh is None:
                errors.append(f'{lineno}: face before any "o" declaration (null mesh in SC)')
                mesh = ''
                faces.setdefault(mesh, 0)
            if len(parts) - 1 != 3:
                errors.append(f'{lineno}: {len(parts)-1} corners, SC requires triangles')
            for corner in parts[1:]:
                idx = corner.split('/')
                if len(idx) != 3 or not all(idx):
                    errors.append(f'{lineno}: corner "{corner}" is not p/t/n')
                    continue
                p, tc, nc = (int(i) for i in idx)
                if not (1 <= p <= v): errors.append(f'{lineno}: position index {p} out of range')
                if not (1 <= tc <= t): errors.append(f'{lineno}: uv index {tc} out of range')
                if not (1 <= nc <= n): errors.append(f'{lineno}: normal index {nc} out of range')
            faces[mesh] += 1
    for name, count in faces.items():
        if count * 3 > USHORT_MAX:
            errors.append(f'object "{name}": {count} faces -> {count*3} vertices exceeds the ushort index buffer')
    if not faces:
        errors.append('no faces')
    return errors, warnings

if __name__ == '__main__':
    targets = sys.argv[1:] or sorted(glob.glob('src/ScCsgoKnives/Assets/Models/ScCsgoKnives/*.obj'))
    bad = 0
    for path in targets:
        errs, warns = validate(path)
        if errs:
            bad += 1
            print(f"FAIL {path}")
            for e in errs[:4]:
                print(f"     {e}")
        elif warns:
            print(f"warn {path}: {warns[0]} (+{len(warns)-1} more)")
    print(f"{len(targets)} files checked, {bad} bad")
    sys.exit(1 if bad else 0)
