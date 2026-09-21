"""Verify final packages, preservation, asset provenance, and compact release evidence."""
import hashlib,json,zipfile
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[1]
REPORT=ROOT/'output/release-firstperson-1.4.8'
PAIRS=[('[API1.9]CS武器1.4.8-作者ZH667-全量版.scmod','[API1.9]CS武器1.4.7-作者ZH667-全量版.scmod'),
       ('[API1.9]CS战术同伴拓展1.3.0-作者ZH667.scmod','[API1.9]CS战术同伴拓展1.2.3-作者ZH667.scmod')]
def read(p):return json.loads(p.read_text('utf-8-sig'))
def sha(b):return hashlib.sha256(b).hexdigest()
evidence={'packages':{},'checks':{},'boundaries':'Packaged DLL checks and native GPU diagnostic; not full-game, Android or network acceptance.'}
for kind in ['core','tactical']:
    d=read(REPORT/f'{kind}-check.json');assert d['failed']==0
    assert all(c.get('ok',c.get('Ok',False)) for c in d['checks'])
    evidence['checks'][kind]={'count':len(d['checks']),'failed':0}
    assert sha((ROOT/'output'/PAIRS[0][0]).read_bytes())==d['packageSha256' if kind=='core' else 'coreSha256']
    if kind=='tactical':assert sha((ROOT/'output'/PAIRS[1][0]).read_bytes())==d['dlcSha256']
for current,old in PAIRS:
    path=ROOT/'output'/current;baseline=ROOT/'output'/old
    if not baseline.exists():
        manifest=read(ROOT/'docs/firstperson-1.4.8-installation.json')
        baseline=next(Path(m['Archived']) for m in manifest['Archived'] if Path(m['Original'])==baseline)
    with zipfile.ZipFile(path) as z,zipfile.ZipFile(baseline) as prev:
        assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
        resources=[n for n in prev.namelist() if n.startswith('Assets/') or n=='ScCsgoResources.dll']
        for n in resources:assert z.read(n)==prev.read(n),'Changed previous resource '+n
        evidence['packages'][current]={'sha256':sha(path.read_bytes()),'bytes':path.stat().st_size,'preservedResources':len(resources),
            'dlls':{n:sha(z.read(n)) for n in z.namelist() if n.endswith(('.dll','.bin'))}}
assets=read(ROOT/'docs/firstperson-gloves-assets.json');assert all(v==.06 for v in assets['minimumWear'].values())
with zipfile.ZipFile(ROOT/'output'/PAIRS[1][0]) as z:
    for name,digest in assets['textures'].items():assert sha(z.read('Assets/Textures/ScCsgoKnives/'+name))==digest
d=read(ROOT/'output/arms-native-148/checks.json');assert d['passed'];evidence['checks']['armsNative']={k:v for k,v in d.items() if k!='results'}
d=read(ROOT/'output/appearance-native-148/checks.json');assert d['failed']==0;evidence['checks']['appearanceNative']={'count':len(d['checks']),'failed':0}
d=read(ROOT/'output/tactical-1.3.0/loading-checks.json');assert d['failed']==0 and d['tacticalSha256']==sha((ROOT/'output'/PAIRS[1][0]).read_bytes())
evidence['checks']['optionalLoading']={'runs':len(d['runs']),'failed':0}
for name in ['ScCsgoKnives','ScCsgoTactical','ScCsgoAppearance']:
    package=PAIRS[0][0] if name=='ScCsgoKnives' else PAIRS[1][0]
    entry='Integrations/ScCsgoAppearance.bin' if name=='ScCsgoAppearance' else name+'.dll'
    with zipfile.ZipFile(ROOT/'output'/package) as z:assert z.read(entry)==(ROOT/f'src/{name}/bin/Release/net10.0/{name}.dll').read_bytes()
(ROOT/'docs/release-firstperson-1.4.8-evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf8')
# A labeled hand-only contact sheet; these are diagnostic renders, never game screenshots.
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
sheet=Image.new('RGB',(1440,640),(26,31,40));draw=ImageDraw.Draw(sheet)
names=[('original','CT 原装袖子／手套'),('sporty_green','树篱迷宫 0.06'),('sporty_purple','潘多拉之盒 0.06'),
       ('specialist_kimono_diamonds_red','深红和服 0.06'),('sporty_blue_pink','迈阿密风云 0.06'),('slick_red','深红织物 0.06')]
for i,(key,label) in enumerate(names):
    im=Image.open(ROOT/f'output/arms-native-148/ct-{key}-ak47-reload.png').resize((480,270),Image.Resampling.LANCZOS)
    x=(i%3)*480;y=(i//3)*320;sheet.paste(im,(x,y));draw.text((x+12,y+277),label,font=font,fill='white')
sheet.save(REPORT/'gloves-preview.png')
print(json.dumps(evidence,ensure_ascii=False,indent=2))
