"""Experimental further reduction of Lite assets; no package publishing.
Requires the Lite resource DLL extraction in .tmp/lite-smooth-20260926/embedded.
Run via tools/dev.ps1; see docs/minimal-extra-reduction-2026-09-26.md.
"""
import pathlib,json,zipfile,sys,zlib,collections,io
ROOT=pathlib.Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'tools'))
import optimize_weapon_resources as opt
import numpy as np
from PIL import Image
S=ROOT/'.tmp/minimal-extra-reduction-20260926';S.mkdir(exist_ok=True)
opt.STAGE=S/'derived'
def simplify(vertices,indices,lock=None,ratio=.35):
 indices=np.asarray(indices,dtype=np.uint32)
 if not len(indices):return indices
 out=np.empty_like(indices);error=np.zeros(1,dtype=np.float32)
 if len(indices)>=384:
  count=opt.mo.simplify_with_attributes(out,indices,np.ascontiguousarray(vertices[:,:3]),np.ascontiguousarray(vertices[:,3:8]),np.array([.25,.25,.25,1,1],dtype=np.float32),vertex_lock=lock,target_index_count=max(126,int(len(indices)*ratio)//3*3),target_error=.01,options=opt.mo.SIMPLIFY_LOCK_BORDER,result_error=error)
  indices=out[:count].copy()
 assert len(indices) and np.isfinite(error[0]) and error[0]<=.01001
 out=np.empty_like(indices);opt.mo.optimize_vertex_cache(out,indices,vertex_count=len(vertices));return out
opt.simplify=simplify
models=[]
def record(name,b,a,row):
 models.append(dict(name=name,rawBefore=len(b),rawAfter=len(a),deflateBefore=len(zlib.compress(b,9)),deflateAfter=len(zlib.compress(a,9)),trianglesBefore=row['trianglesBefore'],trianglesAfter=row['trianglesAfter']))
for p in sorted((ROOT/'.tmp/lite-smooth-20260926/embedded').iterdir()):
 if p.suffix in ('.parts','.skin'):
  opt.binary(p);a=(opt.STAGE/'AnimationData'/p.name).read_bytes();record(p.name,p.read_bytes(),a,opt.rows[-1])
print('binary done',len(models),flush=True)
textures=[]
with zipfile.ZipFile(ROOT/'output/[API1.9]CS武器1.3.0-轻量包.scmod') as z:
 for item in z.infolist():
  name=item.filename
  if name.endswith('.obj'):
   b=z.read(name);a=opt.obj(name,b);record(name,b,a,opt.rows[-1]);dest=opt.STAGE/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(a)
 print('models done',len(models),flush=True)
 # Optional separate texture tier. Preserve data atlases/counter digits and alpha semantics.
 for item in z.infolist():
  name=item.filename
  if not name.startswith('Assets/Textures/') or not name.endswith('.webp'):continue
  b=z.read(name);im=Image.open(io.BytesIO(b));im.load()
  if pathlib.Path(name).stem in ['weapon_c4_digits','env_specular_rgbm','muzzle_fire','muzzle_smoke'] or max(im.size)<=256:continue
  old=im.size;im=im.resize(tuple(max(1,round(x*256/max(old))) for x in old),Image.Resampling.LANCZOS)
  if name.endswith('_normal.webp'):
   pixels=np.array(im.convert('RGBA'));v=pixels[:,:,:3].astype(np.float32)/127.5-1;n=np.linalg.norm(v,axis=2,keepdims=True);v/=np.maximum(n,1e-6);v[n[:,:,0]<1e-6]=(0,0,1);pixels[:,:,:3]=np.clip(np.rint((v+1)*127.5),0,255).astype(np.uint8);im=Image.fromarray(pixels)
  buf=io.BytesIO();im.save(buf,format='WEBP',quality=85,method=4,exact=True);a=buf.getvalue()
  textures.append(dict(name=name,oldSize=old,newSize=im.size,compressedBefore=item.compress_size,compressedAfter=len(zlib.compress(a,9))))
  dest=opt.STAGE/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(a)
summary={}
for label,rows in [('binary',[r for r in models if not r['name'].endswith('.obj')]),('obj',[r for r in models if r['name'].endswith('.obj')])]:
 summary[label]={k:sum(r[k] for r in rows) for k in ['trianglesBefore','trianglesAfter','deflateBefore','deflateAfter']};summary[label]['count']=len(rows)
summary['textures256']=dict(count=len(textures),bytesBefore=sum(r['compressedBefore'] for r in textures),bytesAfter=sum(r['compressedAfter'] for r in textures))
report=dict(summary=summary,models=models,textures=textures,scope='Experimental derived Lite assets only; 35% target triangles, 1% simplifier error bound, locked borders/skin/extrema. No visual/runtime acceptance or release. Model bytes compare zlib9 before/after, not stronger shipping ZIP deflate. All installed/released files unchanged.')
(S/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(summary,indent=2),flush=True)
