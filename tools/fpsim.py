"""Offline reproduction of the mod's first-person render maths.

Row-vector (XNA/Engine) convention throughout: v' = v * M, and A*B means
"apply A, then B".  Mirrors CsmcKnifeRig.cs + CsmcFirstPersonRenderer.cs so the
on-screen result can be inspected without launching Survivalcraft.
"""
import json, math, os, struct, zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ANIM = os.path.join(ROOT, "src/ScCsgoKnives/AnimationData")
MODELS = os.path.join(ROOT, "src/ScCsgoKnives/Assets/Models/ScCsgoKnives")

# ---------------------------------------------------------------- matrices
def ident():
    return [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]

def mul(a, b):
    o = [0.0]*16
    for i in range(4):
        for j in range(4):
            o[i*4+j] = (a[i*4+0]*b[0*4+j] + a[i*4+1]*b[1*4+j]
                        + a[i*4+2]*b[2*4+j] + a[i*4+3]*b[3*4+j])
    return o

def muls(*ms):
    r = ms[0]
    for m in ms[1:]:
        r = mul(r, m)
    return r

def scale(s):
    if isinstance(s, (int, float)):
        s = (s, s, s)
    return [s[0],0,0,0, 0,s[1],0,0, 0,0,s[2],0, 0,0,0,1]

def trans(t):
    return [1,0,0,0, 0,1,0,0, 0,0,1,0, t[0],t[1],t[2],1]

def rotx(a):
    c, s = math.cos(a), math.sin(a)
    return [1,0,0,0, 0,c,s,0, 0,-s,c,0, 0,0,0,1]

def roty(a):
    c, s = math.cos(a), math.sin(a)
    return [c,0,-s,0, 0,1,0,0, s,0,c,0, 0,0,0,1]

def rotz(a):
    c, s = math.cos(a), math.sin(a)
    return [c,s,0,0, -s,c,0,0, 0,0,1,0, 0,0,0,1]

def quat_matrix(q):
    x, y, z, w = q
    return [1-2*(y*y+z*z), 2*(x*y+z*w),   2*(x*z-y*w),   0,
            2*(x*y-z*w),   1-2*(x*x+z*z), 2*(y*z+x*w),   0,
            2*(x*z+y*w),   2*(y*z-x*w),   1-2*(x*x+y*y), 0,
            0,0,0,1]

def yawpitchroll(yaw, pitch, roll):
    return muls(rotz(roll), rotx(pitch), roty(yaw))

def xform(v, m):
    x, y, z = v
    return (x*m[0]+y*m[4]+z*m[8]+m[12],
            x*m[1]+y*m[5]+z*m[9]+m[13],
            x*m[2]+y*m[6]+z*m[10]+m[14])

def xform_dir(v, m):
    x, y, z = v
    return (x*m[0]+y*m[4]+z*m[8],
            x*m[1]+y*m[5]+z*m[9],
            x*m[2]+y*m[6]+z*m[10])

def invert(m):
    # general 4x4 inverse via Gauss-Jordan
    a = [m[i*4:i*4+4] + [1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]
    for col in range(4):
        piv = max(range(col, 4), key=lambda r: abs(a[r][col]))
        if abs(a[piv][col]) < 1e-12:
            raise ValueError("singular")
        a[col], a[piv] = a[piv], a[col]
        d = a[col][col]
        a[col] = [v/d for v in a[col]]
        for r in range(4):
            if r == col:
                continue
            f = a[r][col]
            if f:
                a[r] = [vr - f*vc for vr, vc in zip(a[r], a[col])]
    out = []
    for r in range(4):
        out.extend(a[r][4:])
    return out

def vsub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def vadd(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def vmul(a, s): return (a[0]*s, a[1]*s, a[2]*s)
def dot(a, b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def length(a): return math.sqrt(dot(a, a))
def norm(a):
    l = length(a)
    return (a[0]/l, a[1]/l, a[2]/l) if l > 1e-12 else (0.0, 0.0, 0.0)

# ------------------------------------------------------------------- rig
class Rig:
    def __init__(self, name):
        self.name = name
        with open(os.path.join(ANIM, f"{name}.csmc.animation.json")) as f:
            self.f = json.load(f)
        c = self.f["MeshCenter"]
        self.normalization = mul(trans((-c[0], -c[1], -c[2])),
                                 scale(self.f["MeshNormalizationScale"]))
        self.inv_normalization = invert(self.normalization)
        self.bones = self.f["Skeleton"]
        self.bindings = {b["Name"]: b for b in self.f["Bindings"]}

    @staticmethod
    def read_source(v):
        return list(v) if v and len(v) >= 16 else ident()

    def sample_local(self, bone, clip, t):
        curves = (clip.get("Bones") or {}).get(bone["Name"])
        rest_t = bone.get("Translation") or [0, 0, 0]
        rest_r = bone.get("Rotation") or [0, 0, 0, 1]
        rest_s = bone.get("Scale") or [1, 1, 1]
        if curves is None:
            mat = bone.get("Matrix")
            if mat and len(mat) >= 16:
                return self.read_source(mat)
            return muls(scale(rest_s), quat_matrix(rest_r), trans(rest_t))
        tr = sample_vec(curves.get("Translation"), t, rest_t)
        ro = sample_quat(curves.get("Rotation"), t, rest_r)
        sc = sample_vec(curves.get("Scale"), t, rest_s)
        return muls(scale(sc), quat_matrix(ro), trans(tr))

    def absolute(self, clip_alias, t):
        clip = self.f["Clips"].get(clip_alias) or self.f["Clips"]["idle"]
        local = [self.sample_local(b, clip, t) for b in self.bones]
        absol = [None]*len(local)
        def calc(i):
            if absol[i] is not None:
                return absol[i]
            p = self.bones[i]["Parent"]
            absol[i] = mul(local[i], calc(p)) if 0 <= p < len(local) else local[i]
            return absol[i]
        for i in range(len(local)):
            calc(i)
        return absol, clip

    def pose(self, clip_alias, t):
        absol, clip = self.absolute(clip_alias, t)
        bones, attach, binds = {}, {}, {}
        for b in self.bones:
            i = b["Index"]
            if not (0 <= i < len(absol)):
                continue
            bones[b["Name"]] = muls(self.inv_normalization, absol[i], self.normalization)
            st = (absol[i][12], absol[i][13], absol[i][14])
            mesh_point = (st[2]/0.0254, st[0]/0.0254, st[1]/0.0254)
            attach[b["Name"]] = xform(mesh_point, self.normalization)
        for name, b in self.bindings.items():
            i = b["BoneIndex"]
            if not (0 <= i < len(absol)):
                continue
            src = muls(self.read_source(b.get("RightMatrix")), absol[i],
                       self.read_source(b.get("LeftMatrix")))
            binds[name] = muls(self.inv_normalization, src, self.normalization)
        return {"bindings": binds, "bones": bones, "attachments": attach,
                "duration": clip["Duration"], "clip": clip["SourceName"]}

def find_keys(times, t):
    if len(times) == 1 or t <= times[0]:
        return 0, 0, 0.0
    last = len(times)-1
    if t >= times[last]:
        return last, last, 0.0
    hi = 0
    while times[hi] < t:
        hi += 1
    lo = hi-1
    f = (t-times[lo])/max(1e-6, times[hi]-times[lo])
    return lo, hi, f

def sample_vec(curve, t, fallback):
    if not curve or not curve.get("Times") or not curve.get("Values"):
        return fallback
    lo, hi, f = find_keys(curve["Times"], t)
    a, b = curve["Values"][lo], curve["Values"][hi]
    if lo == hi:
        return a[:3]
    return [a[i]+(b[i]-a[i])*f for i in range(3)]

def sample_quat(curve, t, fallback):
    if not curve or not curve.get("Times") or not curve.get("Values"):
        return fallback
    lo, hi, f = find_keys(curve["Times"], t)
    a, b = curve["Values"][lo], curve["Values"][hi]
    if lo == hi:
        return a[:4]
    if sum(x*y for x, y in zip(a[:4], b[:4])) < 0:
        b = [-x for x in b[:4]]
    q = [a[i]+(b[i]-a[i])*f for i in range(4)]
    n = math.sqrt(sum(x*x for x in q)) or 1.0
    return [x/n for x in q]

# ------------------------------------------------------------------ obj
def load_obj(path):
    verts, faces = [], []
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                p = line.split()
                verts.append((float(p[1]), float(p[2]), float(p[3])))
            elif line.startswith("f "):
                idx = [int(p.split("/")[0]) for p in line.split()[1:]]
                idx = [i-1 if i > 0 else len(verts)+i for i in idx]
                for k in range(1, len(idx)-1):
                    faces.append((idx[0], idx[k], idx[k+1]))
    return verts, faces

# ------------------------------------------------------------- rasterizer
class Frame:
    def __init__(self, w, h, fov_deg=80.0):
        self.w, self.h = w, h
        self.color = [(30, 34, 40)]*(w*h)
        self.depth = [1e30]*(w*h)
        aspect = w/h
        self.fy = 1.0/math.tan(math.radians(fov_deg)/2.0)
        self.fx = self.fy/aspect

    def project(self, v):
        x, y, z = v
        if z >= -1e-4:          # behind the eye (view space looks down -Z)
            return None
        sx = (x*self.fx/-z*0.5+0.5)*self.w
        sy = (0.5-y*self.fy/-z*0.5)*self.h
        return sx, sy, -z

    def tri(self, p0, p1, p2, col):
        pts = [self.project(p) for p in (p0, p1, p2)]
        if any(p is None for p in pts):
            return
        (x0, y0, z0), (x1, y1, z1), (x2, y2, z2) = pts
        minx, maxx = max(0, int(min(x0, x1, x2))), min(self.w-1, int(max(x0, x1, x2))+1)
        miny, maxy = max(0, int(min(y0, y1, y2))), min(self.h-1, int(max(y0, y1, y2))+1)
        if minx > maxx or miny > maxy:
            return
        area = (x1-x0)*(y2-y0)-(x2-x0)*(y1-y0)
        if abs(area) < 1e-9:
            return
        for py in range(miny, maxy+1):
            for px in range(minx, maxx+1):
                cx, cy = px+0.5, py+0.5
                w0 = ((x1-x0)*(cy-y0)-(cx-x0)*(y1-y0))/area
                w1 = ((cx-x0)*(y2-y0)-(x2-x0)*(cy-y0))/area
                if w0 < 0 or w1 < 0 or w0+w1 > 1:
                    continue
                z = z0+(z2-z0)*w0+(z1-z0)*w1
                i = py*self.w+px
                if z < self.depth[i]:
                    self.depth[i] = z
                    self.color[i] = col

    def mesh(self, verts, faces, m, base):
        tv = [xform(v, m) for v in verts]
        for a, b, c in faces:
            p0, p1, p2 = tv[a], tv[b], tv[c]
            n = norm(cross(vsub(p1, p0), vsub(p2, p0)))
            sh = 0.45+0.55*abs(dot(n, (0.35, 0.5, 0.79)))
            self.tri(p0, p1, p2, tuple(min(255, int(c0*sh)) for c0 in base))

    def crosshair(self):
        cx, cy = self.w//2, self.h//2
        for d in range(-9, 10):
            for i in (cy*self.w+cx+d, (cy+d)*self.w+cx):
                if 0 <= i < len(self.color):
                    self.color[i] = (255, 255, 255)

    def save(self, path):
        raw = b"".join(b"\x00"+b"".join(bytes(self.color[y*self.w+x]) for x in range(self.w))
                       for y in range(self.h))
        def chunk(t, d):
            c = t+d
            return struct.pack(">I", len(d))+c+struct.pack(">I", zlib.crc32(c) & 0xffffffff)
        png = (b"\x89PNG\r\n\x1a\n"
               + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0))
               + chunk(b"IDAT", zlib.compress(raw, 6))
               + chunk(b"IEND", b""))
        with open(path, "wb") as f:
            f.write(png)
