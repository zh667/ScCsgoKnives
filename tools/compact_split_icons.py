"""80px inventory thumbnails; retain the 512px weapon colour maps and geometry."""
from pathlib import Path
import io,json,hashlib,zipfile
from PIL import Image
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926';record=json.loads((S/'asset-derivation.json').read_bytes());rows=[]
with zipfile.ZipFile(R/'output/[API1.9]CS武器1.3.0-全量包.scmod') as full:
    for n,row in record['entries'].items():
        stem=Path(n).stem
        if row['owner']!='core' or not n.endswith('.webp') or '_slot' not in stem:continue
        p=S/'core/assets'/row['target'];old=p.read_bytes();original=n[:-5]+'.png'
        im=Image.open(io.BytesIO(full.read(original)));im.load();cap=80
        if max(im.size)>cap:im=im.resize(tuple(max(1,round(x*cap/max(im.size))) for x in im.size),Image.Resampling.LANCZOS)
        buf=io.BytesIO();im.save(buf,format='WEBP',quality=80,method=6,exact=True);data=buf.getvalue()
        p.write_bytes(data);row['sha256']=hashlib.sha256(data).hexdigest();row['operation']='inventory icon80 from original'
        rows.append(dict(path=n,size=im.size,before=len(old),after=len(data)))
(S/'asset-derivation.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf8')
(S/'icon-compaction.json').write_text(json.dumps(rows,indent=2),encoding='utf8')
print('Thumbnail savings',sum(r['before']-r['after'] for r in rows),'across',len(rows),'icons')
