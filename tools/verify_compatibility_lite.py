import argparse,hashlib,json,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output';REPORT=OUT/'release-compatibility-1'
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('profile',choices=['1.0.0','1.2.0']);a=p.parse_args()
 full=OUT/f'[API1.9]CS武器{a.profile}-双向兼容-全量包.scmod';lite=OUT/f'[API1.9]CS武器{a.profile}-双向兼容-轻量包.scmod';checks=[]
 def check(n,ok,detail=''):checks.append({'name':n,'ok':bool(ok),'detail':detail})
 with zipfile.ZipFile(full) as f,zipfile.ZipFile(lite) as l:
  check('zip-integrity',f.testzip() is None and l.testzip() is None)
  check('dll-preserved',f.read('ScCsgoKnives.dll')==l.read('ScCsgoKnives.dll'),sha(l.read('ScCsgoKnives.dll')))
  for edition,z in [('Full',f),('Lite',l)]:
   meta=json.loads(z.read('modinfo.json'));family=json.loads(z.read('Integrations/CompatibilityFamily.json'))
   check(edition+'/plain-version',meta['Version']==family['version']==a.profile)
   check(edition+'/family-preserved',family['family']==1 and family['backup_policy']=='manual' and family['core_sha256']==sha(z.read('ScCsgoKnives.dll')))
   resources=ET.fromstring(z.read('Assets/ScCsgoResources.xml'))
   check(edition+'/all-resource-hashes',all(sha(z.read(e.get('Path')))==e.get('Sha256') for e in resources.findall('File')))
  marker=ET.fromstring(l.read('Assets/ScCsgoResources.xml'));listed={e.get('Path'):e.get('Sha256') for e in marker.findall('File')};assets={e.filename for e in l.infolist() if e.filename.startswith('Assets/Textures/') or e.filename.startswith('Assets/Models/') or e.filename.startswith('Assets/Audio/')};check('marker-coverage',set(listed)==assets,f'{len(listed)} listed/{len(assets)} assets')
  check('edition-optimized',ET.fromstring(l.read('Assets/ScCsgoKnivesEdition.xml')).get('Name')=='Optimized512')
  images=[];bad=[]
  for n in assets:
   if not n.startswith('Assets/Textures/'):continue
   b=l.read(n);check('hash/'+n,sha(b)==listed[n])
   try:
    im=Image.open(__import__('io').BytesIO(b));im.load();images.append(im.size)
    special=n.rsplit('/',1)[-1] in {'weapon_c4_digits.png','weapon_c4_digits.webp','env_specular_rgbm.png','env_specular_rgbm.webp','muzzle_fire.png','muzzle_fire.webp','muzzle_smoke.png','muzzle_smoke.webp'}
    if max(im.size)>512 and not special:bad.append((n,im.size))
   except Exception as e:bad.append((n,str(e)))
  check('texture-decoding',not bad,str(bad[:3]));check('light-under-100mb',lite.stat().st_size<100_000_000,str(lite.stat().st_size));check('non-resource-dll-count',sum(n.endswith('.dll') for n in l.namelist())==sum(n.endswith('.dll') for n in f.namelist()))
 report={'failed':sum(not c['ok'] for c in checks),'count':len(checks),'passed':sum(c['ok'] for c in checks),'checks':checks,'fullSha256':sha(full.read_bytes()),'liteSha256':sha(lite.read_bytes()),'liteBytes':lite.stat().st_size}
 REPORT.mkdir(exist_ok=True);(REPORT/f'{a.profile}-Lite-verify.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8');print(json.dumps({k:v for k,v in report.items() if k!='checks'},ensure_ascii=False))
 if report['failed']:raise SystemExit(1)
if __name__=='__main__':main()
