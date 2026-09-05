"""DEPRECATED for the arm's dynamics: this is a Python replica of the renderer and
it has drifted from the C# three times (bone changes, the inspect pullback). For
anything about how the fist moves, use tools/preview.py, which runs the shipped
C# itself headless. Kept for the idle photo-fit sheet only.

Renders every knife's composition offline -- idle and the hold of its inspect --
and prints per-knife checks, so the nineteen knives without CS:MC photos can be
reviewed against the three that have them.

Idle uses verify_cs.compose (exactly the shipped placement). The hold applies the
shipped roll rule for ArmRollMode 1: the idle face-on side carried rigidly by the
hand bone, squared behind the knife past SquareFromDegrees, and the box sat back
by the handle hull's depth under the face.
"""
import sys, os, math, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw
import rigprobe as R
import verify_cs as V
import gripsolve as G

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
W, H = 960, 540
HULLS = json.load(open(os.path.join(ROOT, 'tools/reference/knife_hulls.json')))

def unit(v): return v / np.linalg.norm(v)
def proj(v, n): return v - n * np.dot(v, n)
def sangle(a, b, ax): return math.atan2(np.dot(np.cross(a, b), ax), np.dot(a, b))
def turn(v, axis, ang): return unit(proj(v * math.cos(ang) + np.cross(axis, v) * math.sin(ang), axis))

def box_corners(arm, side, clearance):
    up = unit(np.cross(side, arm['axis']))
    face = V.C.get('FistGripFace', 0.0)
    seat = arm['grip'] - side * (face * 0.5 * arm['width'] + clearance) - arm['axis'] * arm['overshoot']
    L = arm['reach'] + arm['overshoot']
    return np.array([seat + arm['axis'] * a + up * (su * arm['width']) + side * (ss * arm['width'])
                     for a in (0.0, L) for su in (-0.5, 0.5) for ss in (-0.5, 0.5)])

def hull_edges():
    idx = lambda a, u, s: a * 4 + u * 2 + s
    e = []
    for a in (0, 1):
        for u in (0, 1): e.append((idx(a, u, 0), idx(a, u, 1)))
        for s in (0, 1): e.append((idx(a, 0, s), idx(a, 1, s)))
    for u in (0, 1):
        for s in (0, 1): e.append((idx(0, u, s), idx(1, u, s)))
    return e
EDGES = hull_edges()

def draw_box(d, corners, colour):
    pts = [V.screen(c) for c in corners]
    if any(p is None for p in pts): return
    pts = [(p[0] * W, p[1] * H) for p in pts]
    for a, b in EDGES: d.line([pts[a], pts[b]], fill=colour, width=2)

def knife_points(name, rig, a, place):
    pts = []
    for part in G.MANIFEST[name]:
        Vm = G.mesh_part(name, part)[::6]
        wb = R.binding_matrix(rig, a, part) @ place
        pts.append((np.c_[Vm, np.ones(len(Vm))] @ wb)[:, :3])
    return np.concatenate(pts)

def hold_side(name, comp, a, hr, arm, clip="inspect"):
    """The shipped mode-1 side at this frame, and the clearance.

    Single-frame: the square-to-eye is taken at full weight, i.e. the wrist is
    assumed at rest (stillness 1). The shipped C# gates the square by the wrist's
    stillness, so mid-motion frames differ from this; the inspect *hold* it samples
    is at rest and matches. Use tools/roll_sweep.py for the temporal behaviour; the hold angle comes from it.
    """
    rig = comp['rig']; place = comp['place']
    idle = rig.absolute('idle', 0.0)
    hr0 = R.binding_matrix(rig, idle, 'hand_r') @ place
    ref_local = unit((np.r_[comp['r']['side'], 0] @ np.linalg.inv(hr0))[:3])
    axis, face_on = arm['axis'], arm['side']
    carried = proj((np.r_[ref_local, 0] @ hr)[:3], axis); s = np.linalg.norm(carried)
    w1 = min(1, max(0, (s - 0.06) / 0.22))
    resolved = unit(proj(face_on * (1 - w1) + carried / max(s, 1e-6) * w1, axis))
    ang = sangle(face_on, resolved, axis); size = abs(ang)
    # Shipped rule: straight line from SquareFromDegrees to the clip's measured hold
    # (or SquareFullDegrees when set) -> straight-behind. See tools/roll_sweep.py.
    import roll_sweep
    frm = math.radians(V.C.get('SquareFromDegrees', 45))
    full_cfg = V.C.get('SquareFullDegrees', 0.0)
    hold = math.radians(full_cfg) if full_cfg > 0.5 else roll_sweep.hold_angle(name, comp, clip)
    weight = V.C.get('SquareAtHold', 1.0)
    squared = roll_sweep.square_lin(size, frm, hold, weight)
    sm = min(1, max(0, (size - frm) / (hold - frm))) * weight if hold > frm + 0.01 else 0.0
    side = turn(face_on, axis, math.copysign(squared, ang))
    clearance = 0.0
    parts = HULLS.get(name, {}).get('parts', [])
    hh = [p for p in parts if p['handle']]
    if hh and sm > 0:
        pts = np.concatenate([(np.c_[np.array(p['handle']), np.ones(len(p['handle']))] @ (R.binding_matrix(rig, a, p['binding']) @ place))[:, :3] - arm['grip'] for p in hh])
        clearance = sm * min(max(0.0, float((-(pts @ side)).max())), arm['width'])
    return side, clearance, math.degrees(ang)

def render(name, clip, frac, canvas):
    comp = V.compose(name); rig = comp['rig']; place = comp['place']
    clips = json.load(open(os.path.join(ROOT, f'src/ScCsgoKnives/AnimationData/{name}.csmc.animation.json')))['Clips']
    if clip not in clips: clip = 'idle'
    dur = clips[clip]['Duration']
    a = rig.absolute(clip, dur * frac)
    hr = R.binding_matrix(rig, a, 'hand_r') @ place
    grip = R.xform(V.GRIPS[name], hr)
    arm = V.solve_arm(name, grip, comp['anchor'], False)
    if clip == 'idle' or frac == 0.0:
        side, clearance, roll = arm['side'], 0.0, 0.0
    else:
        side, clearance, roll = hold_side(name, comp, a, hr, arm, clip)
    d = ImageDraw.Draw(canvas)
    draw_box(d, box_corners(arm, side, clearance), (255, 255, 0))
    if comp['left_usable']:
        draw_box(d, V.box_corners(comp['l']), (255, 160, 0))
    pts = knife_points(name, rig, a, place)
    info = dict(roll=roll, clearance=clearance / (arm['width'] / 2) if arm['width'] else 0.0)
    xs = []
    for p in pts:
        s = V.screen(p)
        if s is None: continue
        xs.append(s)
        x, y = s[0] * W, s[1] * H
        if 0 <= x < W and 0 <= y < H: d.point((x, y), fill=(0, 255, 0))
    g = V.screen(grip)
    if g is not None: d.ellipse([g[0] * W - 4, g[1] * H - 4, g[0] * W + 4, g[1] * H + 4], outline=(255, 0, 255), width=2)
    if xs:
        S = np.array(xs); info['extent'] = float(np.linalg.norm(S.max(0) - S.min(0)))
        info['tip'] = tuple(S[np.argmax([np.hypot(s[0] - g[0], (s[1] - g[1]) * 0.5625) for s in S])]) if g is not None else None
    return info

def checks(name):
    """Idle sanity numbers."""
    comp = V.compose(name); rig = comp['rig']; place = comp['place']
    a = rig.absolute('idle', 0.0)
    kn = G.Knife(name)
    P = (np.c_[kn.P, np.ones(len(kn.P))] @ place)[:, :3]
    grip = R.xform(V.GRIPS[name], R.binding_matrix(rig, a, 'hand_r') @ place)
    dist = float(np.linalg.norm(P - grip, axis=1).min())
    arm = comp['r']
    rel = P - grip
    inside = (np.abs(rel @ arm['side'] + V.C.get('FistGripFace', 0.0) * 0.5 * arm['width']) <= arm['width'] * 0.5) & (np.abs(rel @ arm['up']) <= arm['width'] * 0.5) \
             & (rel @ arm['axis'] >= -arm['overshoot'])
    tf = (kn.t - kn.lo) / (kn.hi - kn.lo)
    handle = tf < 0.35
    return dict(grip_to_mesh=dist, handle_hidden=float(inside[handle].mean()) if handle.any() else 0.0, scale=comp['scale'])

if __name__ == '__main__':
    names = R.NAMES
    cols, tw, th = 6, 480, 270
    rows = (len(names) * 2 + cols - 1) // cols
    sheet = Image.new('RGB', (cols * tw, rows * th), (120, 160, 240))
    dr = ImageDraw.Draw(sheet)
    print(f"{'knife':<11}{'scale':>7}{'grip->mesh':>11}{'handle hidden':>15}{'idle extent':>12}{'hold roll':>10}{'hold clr':>9}")
    for i, name in enumerate(names):
        rowinfo = {}
        for j, (clip, frac) in enumerate((('idle', 0.0), ('inspect', 0.8))):
            canvas = Image.new('RGB', (W, H), (120, 160, 240))
            try:
                info = render(name, clip, frac, canvas)
            except Exception as e:
                info = dict(error=str(e)); ImageDraw.Draw(canvas).text((10, 10), str(e)[:80], fill=(255, 0, 0))
            rowinfo[clip] = info
            k = i * 2 + j
            tile = canvas.resize((tw, th)); sheet.paste(tile, ((k % cols) * tw, (k // cols) * th))
            dr.text(((k % cols) * tw + 6, (k // cols) * th + 4), f"{name} {clip}", fill=(255, 255, 0))
        c = checks(name)
        hold = rowinfo.get('inspect', {})
        print(f"{name:<11}{c['scale']:7.3f}{c['grip_to_mesh']:11.4f}{c['handle_hidden']:15.2f}{rowinfo['idle'].get('extent', 0):12.3f}{hold.get('roll', 0):10.1f}{hold.get('clearance', 0):9.2f}")
    out = os.path.join(ROOT, '.tmp-fist/fleet_qa.png')
    sheet.save(out); print('wrote', out)
