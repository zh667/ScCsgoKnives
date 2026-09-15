from pathlib import Path
import zipfile,json,hashlib
r=Path.cwd()
paths=[Path('D:/下载/[API1.9]CS武器1.0.0.scmod'),Path('D:/下载/CS枪械/[API1.9]CS武器1.0-作者ZH667.scmod'),r/'output/ScCsgoKnives-1.0.14-preview.scmod',r/'output/CS武器-1.0.14-全量版.zip']
for p in paths:
 if not p.exists(): print('MISSING',p);continue
 with zipfile.ZipFile(p) as z:
  d={'path':str(p),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
  if 'modinfo.json' in z.namelist():
   m=json.loads(z.read('modinfo.json'));d.update(version=m['Version'],deps=m.get('Dependencies'),dll=hashlib.sha256(z.read('ScCsgoKnives.dll')).hexdigest())
  else:
   d['inner']=[dict(name=n,sha256=hashlib.sha256(z.read(n)).hexdigest()) for n in z.namelist() if n.endswith('.scmod')]
  print(json.dumps(d,ensure_ascii=False))
p=r/'output/check-1.0.14-final.json'; d=json.loads(p.read_text('utf-8-sig'));print(json.dumps({k:v for k,v in d.items() if k!='checks'},ensure_ascii=False))
