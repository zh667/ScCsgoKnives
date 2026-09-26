"""Re-encode 512 colour maps from Full originals, preserving resolution and alpha."""
from pathlib import Path
import io,json,hashlib,zipfile
from PIL import Image
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926'
record=json.loads((S/'asset-derivation.json').read_bytes());rows=[]
with zipfile.ZipFile(R/'output/[API1.9]CS武器1.3.0-全量包.scmod') as full:
    for n,row in record['entries'].items():
        stem=Path(n).stem
        if row['owner']!='core' or not n.endswith('.webp') or not n.startswith('Assets/Textures/'):continue
        if not (stem.endswith('_hd') or '_hd__' in stem or '_finish__' in stem or stem in ['cs2_arm','cs2_glove']):continue
        if stem.endswith(('_normal','_orm')):continue
        p=S/'core/assets'/row['target'];old=p.read_bytes();shape=Image.open(io.BytesIO(old)).size
        original=n[:-5]+'.png';assert original in full.namelist(),n
        im=Image.open(io.BytesIO(full.read(original)));im.load()
        if im.size!=shape:im=im.resize(shape,Image.Resampling.LANCZOS)
        buf=io.BytesIO();im.save(buf,format='WEBP',quality=72,method=6,exact=True);data=buf.getvalue()
        assert Image.open(io.BytesIO(data)).size==shape
        if len(data)>=len(old):continue
        p.write_bytes(data);row['sha256']=hashlib.sha256(data).hexdigest();row['operation']='512 colour WebP72 from original'
        rows.append(dict(path=n,shape=shape,before=len(old),after=len(data)))
(S/'asset-derivation.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf8')
(S/'colour-compaction.json').write_text(json.dumps(rows,indent=2),encoding='utf8')
print('512 colour savings',sum(r['before']-r['after'] for r in rows),'across',len(rows),'textures')
