"""Derive mobile assets; never edit original source assets. Requires meshoptimizer 0.2.30a0.
Run via tools/dev.ps1 python -X utf8 tools/optimize_weapon_resources.py.
Each collapse retains an original vertex (including UV/normal/skin data). Mixed-bone
vertices and vertices touching a different bone signature are locked; borders stay locked.
"""
from pathlib import Path
import sys, struct, json, io, hashlib, zipfile, argparse
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.tmp/optimization-deps'))
import numpy as np
import meshoptimizer as mo
from PIL import Image
import ctypes as ct
# The 0.2.30a0 Python loader omits this exported function's ctypes declaration.
from meshoptimizer._loader import lib
lib.meshopt_simplifyWithAttributes.argtypes = [ct.POINTER(ct.c_uint),ct.POINTER(ct.c_uint),ct.c_size_t,
    ct.POINTER(ct.c_float),ct.c_size_t,ct.c_size_t,ct.POINTER(ct.c_float),ct.c_size_t,
    ct.POINTER(ct.c_float),ct.c_size_t,ct.POINTER(ct.c_ubyte),ct.c_size_t,ct.c_float,ct.c_uint,ct.POINTER(ct.c_float)]
lib.meshopt_simplifyWithAttributes.restype = ct.c_size_t

STAGE = ROOT / '.tmp/optimized-resources'
REPORT = ROOT / 'output/optimization-106'
rows = []

def sha(data): return hashlib.sha256(data).hexdigest()

def simplify(vertices, indices, lock=None, ratio=.5):
    indices = np.asarray(indices, dtype=np.uint32)
    assert len(indices) % 3 == 0 and (not len(indices) or int(indices.max()) < len(vertices))
    if not len(indices): return indices
    out = np.empty_like(indices)
    error = np.zeros(1, dtype=np.float32)
    if len(indices) >= 384:
        count = mo.simplify_with_attributes(out, indices, np.ascontiguousarray(vertices[:, :3]),
            np.ascontiguousarray(vertices[:, 3:8]), np.array([.25,.25,.25,1,1], dtype=np.float32),
            vertex_lock=lock, target_index_count=max(126, int(len(indices)*ratio)//3*3),
            target_error=.002, options=mo.SIMPLIFY_LOCK_BORDER, result_error=error)
        indices = out[:count].copy()
    assert len(indices) and np.isfinite(error[0]) and error[0] <= .00201
    out = np.empty_like(indices)
    mo.optimize_vertex_cache(out, indices, vertex_count=len(vertices))
    return out

class Reader:
    def __init__(self, b): self.b=b; self.o=0
    def read(self, n): v=self.b[self.o:self.o+n]; self.o+=n; assert len(v)==n; return v
    def num(self, fmt): return struct.unpack('<'+fmt,self.read(struct.calcsize('<'+fmt)))[0]
    def string(self): return self.read(self.num('H')).decode()
def number(fmt, value): return struct.pack('<'+fmt, value)
def string(s): b=s.encode(); return number('H',len(b))+b

def compact(records, groups):
    # Keep original bytes of every surviving vertex, then order by first use.
    used = list(dict.fromkeys(int(i) for g in groups for i in g['indices']))
    remap = np.zeros(len(records), dtype=np.uint32)
    remap[used] = np.arange(len(used), dtype=np.uint32)
    for g in groups: g['indices'] = remap[g['indices']]
    return records[used].copy()

def optimize_section(records, groups, skinned):
    verts = np.frombuffer(records[:, :32].copy().tobytes(), dtype='<f4').reshape(-1,8)
    assert np.isfinite(verts).all()
    lock = np.zeros(len(verts), dtype=np.uint8)
    # Protect extrema so weapon scale/attachment alignment does not drift.
    if len(verts):
        lock[np.argmin(verts[:,:3],axis=0)] = 1
        lock[np.argmax(verts[:,:3],axis=0)] = 1
    if skinned and len(verts):
        weights = np.frombuffer(records[:,36:52].copy().tobytes(), dtype='<f4').reshape(-1,4)
        assert np.isfinite(weights).all()
        bones = records[:,32:36]
        lock[(weights > 1e-5).sum(axis=1) > 1] = 1
        signature = bones[np.arange(len(bones)), weights.argmax(axis=1)]
        for g in groups:
            tris=g['indices'].reshape(-1,3)
            different=np.any(signature[tris] != signature[tris[:,0]][:,None],axis=1)
            lock[tris[different].reshape(-1)] = 1
    before=sum(len(g['indices']) for g in groups)//3
    for g in groups: g['indices'] = simplify(verts, g['indices'], lock)
    result=compact(records,groups)
    # Bone weights, normals, UVs and positions are original bytes, not reconstructed.
    return result, before, sum(len(g['indices']) for g in groups)//3

def binary(path):
    source=path.read_bytes(); r=Reader(source); magic=r.read(8); version=r.num('I')
    assert (magic,version) in [(b'SCK2PART',1),(b'SCK2SKIN',2)]
    skin=magic==b'SCK2SKIN'; joints=r.num('H')
    for _ in range(joints): r.string();r.read(64)
    header=source[:r.o]
    count=r.num('i'); stride=52 if skin else 32
    records=np.frombuffer(r.read(count*stride),dtype=np.uint8).reshape(-1,stride).copy()
    groups=[]
    for _ in range(r.num('H')):
        joint=None if skin else r.num('H'); material=r.string();n=r.num('i')
        groups.append(dict(joint=joint,material=material,indices=np.frombuffer(r.read(n*4),dtype='<u4').copy()))
    records,before,after=optimize_section(records,groups,skin)
    result=header+number('i',len(records))+records.tobytes()+number('H',len(groups))
    for g in groups:
        if not skin: result+=number('H',g['joint'])
        result+=string(g['material'])+number('i',len(g['indices']))+g['indices'].astype('<u4').tobytes()
    blend_before=blend_after=0
    if not skin:
        n=r.num('i'); blended=np.frombuffer(r.read(n*52),dtype=np.uint8).reshape(-1,52).copy()
        bg=[]
        for _ in range(r.num('H')):
            material=r.string();n=r.num('i');bg.append(dict(material=material,indices=np.frombuffer(r.read(n*4),dtype='<u4').copy()))
        if len(blended): blended,blend_before,blend_after=optimize_section(blended,bg,True)
        result+=number('i',len(blended))+blended.tobytes()+number('H',len(bg))
        for g in bg:result+=string(g['material'])+number('i',len(g['indices']))+g['indices'].astype('<u4').tobytes()
    assert r.o==len(source)
    target=STAGE/'AnimationData'/path.name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(result)
    rows.append(dict(path='AnimationData/'+path.name,kind='skin' if skin else 'parts',
        trianglesBefore=before+blend_before,trianglesAfter=after+blend_after,verticesBefore=count,verticesAfter=len(records),
        sourceSha256=sha(source),sha256=sha(result),bytesBefore=len(source),bytesAfter=len(result)))

def obj(name,source):
    positions=[];uvs=[];normals=[];records=[];lookup={};groups=[];current=None;prefix=[]
    state={'o':None,'g':None,'usemtl':None,'s':None}
    def index(raw, length):
        n=int(raw);return n-1 if n>0 else length+n
    for line in source.decode('utf-8-sig').splitlines():
        fields=line.split()
        if not fields or fields[0]=='#':continue
        cmd=fields[0]
        if cmd=='v': positions.append([float(x) for x in fields[1:4]])
        elif cmd=='vt':uvs.append([float(x) for x in fields[1:3]])
        elif cmd=='vn':normals.append([float(x) for x in fields[1:4]])
        elif cmd in state:
            state[cmd]=' '.join(fields[1:]);current=None
        elif cmd=='mtllib':prefix.append(line)
        elif cmd=='f':
            if current is None:current=dict(state=state.copy(),indices=[]);groups.append(current)
            face=[]
            for field in fields[1:]:
                v=field.split('/');key=(index(v[0],len(positions)),index(v[1],len(uvs)) if len(v)>1 and v[1] else None,index(v[2],len(normals)) if len(v)>2 and v[2] else None)
                if key not in lookup:
                    lookup[key]=len(records);records.append(positions[key[0]]+(normals[key[2]] if key[2] is not None else [0,0,1])+(uvs[key[1]] if key[1] is not None else [0,0]))
                face.append(lookup[key])
            for i in range(1,len(face)-1):current['indices']+= [face[0],face[i],face[i+1]]
        else:raise ValueError(f'Unhandled OBJ directive {cmd}: {name}')
    vertices=np.array(records,dtype=np.float32);original=len(vertices);before=0
    # Separate groups may be independently transformed by engine models. Never collapse their borders.
    lock=np.zeros(len(vertices),dtype=np.uint8)
    lock[np.argmin(vertices[:,:3],axis=0)]=1;lock[np.argmax(vertices[:,:3],axis=0)]=1
    for g in groups:
        before+=len(g['indices'])//3;g['indices']=simplify(vertices,np.array(g['indices'],dtype=np.uint32),lock)
    vertices=compact(vertices,groups)
    output=['# Derived mobile mesh; original assets preserved.']+prefix
    for v in vertices:output.append('v '+' '.join(format(float(x),'.9g') for x in v[:3]))
    for v in vertices:output.append('vt '+' '.join(format(float(x),'.9g') for x in v[6:8]))
    for v in vertices:output.append('vn '+' '.join(format(float(x),'.9g') for x in v[3:6]))
    for g in groups:
        for k,v in g['state'].items():
            if v is not None:output.append(k+' '+v)
        for f in g['indices'].reshape(-1,3):output.append('f '+' '.join(f'{i+1}/{i+1}/{i+1}' for i in f))
    result=('\n'.join(output)+'\n').encode()
    rows.append(dict(path=name,kind='obj',trianglesBefore=before,trianglesAfter=sum(len(g['indices'])//3 for g in groups),
        verticesBefore=original,verticesAfter=len(vertices),sourceSha256=sha(source),sha256=sha(result),bytesBefore=len(source),bytesAfter=len(result)))
    return result

def texture(name, data):
    image=Image.open(io.BytesIO(data)); image.load(); old=image.size
    # C4's exported alpha is material data, not opacity: all body render paths
    # deliberately draw RGB opaque. RGBA resizing premultiplies by that alpha,
    # and lossy WebP discards RGB in transparent texels. Strip it BEFORE either
    # operation so the body's brown/green paint is not replaced with black/white.
    material_alpha=Path(name).name=='c4_cs2.png'
    if material_alpha:image=image.convert('RGB')
    special=Path(name).name in ('weapon_c4_digits.png','env_specular_rgbm.png','muzzle_fire.png','muzzle_smoke.png')
    if max(old)>512 and not special:
        image=image.resize((round(old[0]*512/max(old)),round(old[1]*512/max(old))),Image.Resampling.LANCZOS)
        if name.endswith('_normal.png'):
            pixels=np.array(image.convert('RGBA'));v=pixels[:,:,:3].astype(np.float32)/127.5-1
            n=np.linalg.norm(v,axis=2,keepdims=True);v/=np.maximum(n,1e-6);v[n[:,:,0]<1e-6]=(0,0,1)
            pixels[:,:,:3]=np.clip(np.rint((v+1)*127.5),0,255).astype(np.uint8);image=Image.fromarray(pixels)
    # RGBM and effect atlases are data/layout-sensitive. Keep their pixels exact.
    buf=io.BytesIO();image.save(buf,format='WEBP',quality=100 if special else 85,lossless=special,method=4,exact=True)
    result=buf.getvalue(); decoded=Image.open(io.BytesIO(result));decoded.load()
    assert decoded.size==image.size
    if 'A' in image.getbands():assert np.array_equal(np.array(decoded.convert('RGBA'))[:,:,3],np.array(image.convert('RGBA'))[:,:,3])
    rows.append(dict(path=name,kind='texture',fromSize=old,toSize=image.size,lossless=special,quality=100 if special else 85,
        alphaIsMaterialData=material_alpha,
        sourceSha256=sha(data),sha256=sha(result),bytesBefore=len(data),bytesAfter=len(result)))
    return result

def main():
    global STAGE, REPORT
    parser=argparse.ArgumentParser()
    parser.add_argument('--models-only',action='store_true')
    parser.add_argument('--source-package',type=Path,default=ROOT/'output/ScCsgoResources-1.8.0.scmod')
    parser.add_argument('--stage',type=Path,default=STAGE)
    parser.add_argument('--report',type=Path,default=REPORT)
    args=parser.parse_args();STAGE=args.stage.resolve();REPORT=args.report.resolve()
    (STAGE/'AnimationData').mkdir(parents=True,exist_ok=True);REPORT.mkdir(parents=True,exist_ok=True)
    for p in sorted((ROOT/'src/ScCsgoKnives/AnimationData').iterdir()):
        if p.suffix in ('.skin','.parts'): binary(p)
        elif p.name.endswith('.cs2.animation.json'):
            (STAGE/'AnimationData'/p.name).write_bytes(p.read_bytes())
    print('Binary models done',len(rows),flush=True)
    with zipfile.ZipFile(args.source_package) as archive:
        for entry in archive.infolist():
            name=entry.filename
            if name.startswith('Assets/Models/') and name.endswith('.obj'): data=obj(name,archive.read(name))
            elif name.endswith('.png') and not args.models_only:
                data=texture(name,archive.read(name));name=name[:-4]+'.webp'
            else:continue
            p=STAGE/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
            if len(rows)%50==0:print('Processed',len(rows),flush=True)
    (REPORT/'assets.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
    models=[r for r in rows if 'trianglesBefore' in r]
    summary=dict(models=len(models),trianglesBefore=sum(r['trianglesBefore'] for r in models),trianglesAfter=sum(r['trianglesAfter'] for r in models),
        reducedModels=sum(r['trianglesAfter']<r['trianglesBefore'] for r in models),textures=sum(r['kind']=='texture' for r in rows))
    (REPORT/'summary.json').write_text(json.dumps(summary,indent=2));print(summary,flush=True)

if __name__=='__main__':main()
