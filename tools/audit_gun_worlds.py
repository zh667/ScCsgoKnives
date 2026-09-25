"""Read-only local save inventory; copy the affected XML and log into an isolated audit directory."""
from pathlib import Path
import hashlib,json,shutil,zipfile
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
game=Path(r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1')
out=ROOT/'.tmp/migration-20260925';out.mkdir(parents=True,exist_ok=True)
def analyze(data,label):
    doc=ET.fromstring(data)
    gun=doc.find("./Subsystems/Values[@Name='ScGunBlockBehavior']")
    rows=doc.findall("./Subsystems/Values[@Name='ScGunBlockBehavior']/Values[@Name='GunRegistry']/Values[@Name='Records']/Value")
    maps=doc.findall("./Subsystems/Values[@Name='BlocksManager']/Value[@Value='ScGunBlock']")
    result=dict(source=label,sha256=hashlib.sha256(data).hexdigest(),records=len(rows),gun_subsystem=gun is not None)
    refs=[]
    if len(maps)==1:
        block=int(maps[0].get('Name'));ids={int(r.get('Name')) for r in rows}
        for holder in doc.iter('Values'):
            slots=holder.find("Values[@Name='Slots']")
            if slots is None:continue
            for slot in slots:
                field=slot.find("Value[@Name='Contents']")
                if field is None:continue
                value=int(field.get('Value'));data=value>>14;rid=(data>>6)&1023
                if value&1023==block and rid not in (0,1023):
                    refs.append(dict(holder=holder.get('Name'),slot=slot.get('Name'),record=rid,variant=data&63,missing=rid not in ids))
    result['references']=refs
    return result
reports=[]
for world in sorted((game/'doc/Worlds').glob('World*')):
    for filename in ('Project.xml','Project.bak'):
        p=world/filename
        if not p.is_file():continue
        data=p.read_bytes();reports.append(analyze(data,f'{world.name}/{filename}'))
        target=out/f'{world.name}-{filename}'
        if not target.exists():target.write_bytes(data)
for p in Path(r'D:\下载').glob('*.scworld'):
    with zipfile.ZipFile(p) as z:
        for name in z.namelist():
            if name.lower().endswith('project.xml'):
                reports.append(analyze(z.read(name),p.name+'/'+name))
log=out/'Game.log'
if not log.exists():shutil.copyfile(game/'Bugs/Game.log',log)
(out/'audit.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2),'utf-8')
for r in reports:print(r['source'], 'records=',r['records'],'missing=',[i['record'] for i in r['references'] if i['missing']])
