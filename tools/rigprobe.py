"""Replicates the shipped rig maths so the arm bones can be measured offline.

Everything here mirrors CsmcKnifeRig.Sample and CsmcFirstPersonRenderer.BuildPlacement
exactly, including Engine's row-vector convention (v' = v @ M), so a number printed
here is the number the mod puts on screen.
"""
import json, math, os, re, numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'src/ScCsgoKnives/AnimationData')

def scale(s):
    m = np.eye(4); m[0,0], m[1,1], m[2,2] = s; return m

def translation(t):
    m = np.eye(4); m[3,:3] = t; return m

def from_quat(q):
    x, y, z, w = q
    return np.array([
        [1-2*(y*y+z*z), 2*(x*y+w*z),   2*(x*z-w*y),   0],
        [2*(x*y-w*z),   1-2*(x*x+z*z), 2*(y*z+w*x),   0],
        [2*(x*z+w*y),   2*(y*z-w*x),   1-2*(x*x+y*y), 0],
        [0, 0, 0, 1]], float)

def rot_x(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    return np.array([[1,0,0,0],[0,c,s,0],[0,-s,c,0],[0,0,0,1]], float)

def rot_y(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    return np.array([[c,0,-s,0],[0,1,0,0],[s,0,c,0],[0,0,0,1]], float)

def rot_z(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    return np.array([[c,s,0,0],[-s,c,0,0],[0,0,1,0],[0,0,0,1]], float)

def source_matrix(v):
    # A JOML float[16] is column-major; sequential assignment into row-major
    # fields is the transpose the row-vector renderer wants.
    return np.array(v, float).reshape(4, 4) if v and len(v) >= 16 else np.eye(4)

def nlerp_quat(a, b, f):
    a, b = np.array(a, float), np.array(b, float)
    if np.dot(a, b) < 0: b = -b
    q = a + (b - a) * f
    n = np.linalg.norm(q)
    return q / n if n > 1e-6 else a

def find_keys(times, t):
    if len(times) == 1 or t <= times[0]: return 0, 0, 0.0
    last = len(times) - 1
    if t >= times[last]: return last, last, 0.0
    hi = int(np.searchsorted(times, t))
    lo = hi - 1
    return lo, hi, (t - times[lo]) / max(1e-6, times[hi] - times[lo])

def sample_curve(curve, t, fallback, quat=False):
    if not curve: return fallback
    times, values = np.array(curve['Times'], float), curve['Values']
    if len(times) == 0: return fallback
    lo, hi, f = find_keys(times, t)
    if quat:
        return values[lo] if lo == hi else nlerp_quat(values[lo], values[hi], f)
    a, b = np.array(values[lo], float)[:3], np.array(values[hi], float)[:3]
    return a if lo == hi else a + (b - a) * f

_RIG_CACHE = {}

def rig(name):
    """Cached Rig. Each animation file is ~1 MB of curves and the fitters build
    the same twenty-two dozens of times."""
    if name not in _RIG_CACHE: _RIG_CACHE[name] = Rig(name)
    return _RIG_CACHE[name]


class Rig:
    def __init__(self, name):
        self.name = name
        with open(os.path.join(DATA, f'{name}.csmc.animation.json')) as fh:
            self.f = json.load(fh)
        centre = np.array(self.f['MeshCenter'], float)
        self.N = translation(-centre) @ scale([self.f['MeshNormalizationScale']] * 3)
        self.invN = np.linalg.inv(self.N)
        self.bindings = {b['Name']: b for b in self.f['Bindings']}
        self.ref_scale = self.f['SourceReferenceScale']
        self._abs_cache = {}

    def absolute(self, clip_name, t):
        key = (clip_name, round(t, 6))
        if key in self._abs_cache: return self._abs_cache[key]
        value = self._absolute(clip_name, t)
        self._abs_cache[key] = value
        return value

    def _absolute(self, clip_name, t):
        clip = self.f['Clips'][clip_name]
        bones, curves = self.f['Skeleton'], clip['Bones']
        local = []
        for b in bones:
            c = curves.get(b['Name'])
            if c is None:
                local.append(source_matrix(b.get('Matrix')) if b.get('Matrix') else
                             scale(b.get('Scale') or [1,1,1]) @ from_quat(b.get('Rotation') or [0,0,0,1])
                             @ translation(b.get('Translation') or [0,0,0]))
                continue
            s = sample_curve(c.get('Scale'), t, np.array(b.get('Scale') or [1,1,1], float))
            r = sample_curve(c.get('Rotation'), t, b.get('Rotation') or [0,0,0,1], quat=True)
            tr = sample_curve(c.get('Translation'), t, np.array(b.get('Translation') or [0,0,0], float))
            local.append(scale(s) @ from_quat(r) @ translation(tr))
        out = [None] * len(bones)
        def calc(i):
            if out[i] is not None: return out[i]
            p = bones[i]['Parent']
            out[i] = local[i] @ calc(p) if 0 <= p < len(bones) else local[i]
            return out[i]
        for i in range(len(bones)): calc(i)
        return {b['Name']: out[b['Index']] for b in bones}

    def bone(self, absolute, name):
        """Normalised bone frame -- the attachment frame for external geometry."""
        return self.invN @ absolute[name] @ self.N

    def binding(self, absolute, name):
        b = self.bindings[name]
        return self.invN @ (source_matrix(b['RightMatrix']) @ absolute[b['Name'] if False else name.split('#')[0]]
                            if False else np.eye(4)) @ self.N

def binding_matrix(rig, absolute, name):
    b = rig.bindings[name]
    bone_name = next(s['Name'] for s in rig.f['Skeleton'] if s['Index'] == b['BoneIndex'])
    src = source_matrix(b['RightMatrix']) @ absolute[bone_name] @ source_matrix(b['LeftMatrix'])
    return rig.invN @ src @ rig.N

def xform(p, m):
    v = np.array([p[0], p[1], p[2], 1.0]) @ m
    return v[:3]

MANIFEST = json.load(open(os.path.join(DATA, 'knives.json')))
NAMES = [e['Name'] for e in MANIFEST]
REFERENCE_SOURCE_SCALE = 13.618

def placement(rig, knife_scale, anchor, grip_offset=(0,0,0)):
    s = knife_scale * rig.ref_scale / REFERENCE_SOURCE_SCALE
    orientation = scale([s]*3) @ rot_z(270) @ rot_y(180) @ rot_x(90)
    idle = rig.absolute('idle', 0.0)
    hand = binding_matrix(rig, idle, 'hand_r')
    idle_grip = xform(grip_offset, hand @ orientation)
    return orientation @ translation(np.array(anchor) - idle_grip)

def to_screen(p, fx, fy):
    d = -p[2]
    if d <= 1e-4: return None
    return (p[0] * fx / d * 0.5 + 0.5, 0.5 - p[1] * fy / d * 0.5)

def to_view(sx, sy, depth, fx, fy):
    return np.array([(sx - 0.5) * 2 * depth / fx, (0.5 - sy) * 2 * depth / fy, -depth])
