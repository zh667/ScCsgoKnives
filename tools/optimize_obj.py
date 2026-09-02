"""Shrink converter OBJ output: share vertices and trim float precision.

The converter emits three unshared vertices per triangle, which makes a 20k
triangle knife a multi-megabyte text file. Knives are small on screen, so five
decimals of position is well past what the renderer can show.

Everything that is not a vertex or a face is passed through untouched and in
order. Survivalcraft's Game.ObjModelReader needs the `o` declaration before the
first face -- without it the parser dereferences a null mesh -- so this must
never be a "keep the geometry, drop the rest" rewrite.
"""
import sys, os

def _fmt(value, dp):
    """Fixed-point, never exponent notation: SC parses these with float.Parse."""
    text = f'{value:.{dp}f}'.rstrip('0').rstrip('.')
    return text if text not in ('', '-') else '0'

def optimize(src, dst, pos_dp=5, uv_dp=6, nrm_dp=4):
    v, vt, vn = [], [], []
    body = []                      # (kind, payload) in original order
    for line in open(src):
        line = line.rstrip('\n')
        parts = line.split()
        if not parts:
            continue
        tag = parts[0]
        if tag == 'v':
            v.append(tuple(float(x) for x in parts[1:4]))
        elif tag == 'vt':
            vt.append(tuple(float(x) for x in parts[1:3]))
        elif tag == 'vn':
            vn.append(tuple(float(x) for x in parts[1:4]))
        elif tag == 'f':
            corners = []
            for corner in parts[1:]:
                idx = corner.split('/')
                if len(idx) != 3 or not all(idx):
                    raise ValueError(f'{src}: face corner "{corner}" is not p/t/n')
                corners.append(tuple(int(i) for i in idx))
            body.append(('f', corners))
        else:
            body.append(('raw', line))

    def dedup(items, dp):
        table, order, remap = {}, [], []
        for item in items:
            key = tuple(round(x, dp) for x in item)
            if key not in table:
                table[key] = len(order)
                order.append(key)
            remap.append(table[key])
        return order, remap

    out_v, map_v = dedup(v, pos_dp)
    out_t, map_t = dedup(vt, uv_dp)
    out_n, map_n = dedup(vn, nrm_dp)
    lines = ['# vertices shared and precision trimmed by tools/optimize_obj.py']
    lines += ['v ' + ' '.join(_fmt(c, pos_dp) for c in x) for x in out_v]
    lines += ['vt ' + ' '.join(_fmt(c, uv_dp) for c in x) for x in out_t]
    lines += ['vn ' + ' '.join(_fmt(c, nrm_dp) for c in x) for x in out_n]
    for kind, payload in body:
        if kind == 'raw':
            lines.append(payload)
        else:
            lines.append('f ' + ' '.join(
                f'{map_v[p-1]+1}/{map_t[t-1]+1}/{map_n[n-1]+1}' for p, t, n in payload))
    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    open(dst, 'w').write('\n'.join(lines) + '\n')
    return len(v), len(out_v), os.path.getsize(src), os.path.getsize(dst)

if __name__ == '__main__':
    a, b, c, d = optimize(sys.argv[1], sys.argv[2])
    print(f"{sys.argv[1]}: {a} -> {b} verts, {c/1e6:.2f} -> {d/1e6:.2f} MB")
