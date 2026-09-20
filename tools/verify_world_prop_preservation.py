"""Check retained FPP resources/eye textures against the superseded release packages."""
import hashlib,json,struct,zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
def sha(data):return hashlib.sha256(data).hexdigest()
def package(name):return zipfile.ZipFile(ROOT/'output'/name)
def images(blob):
    length=struct.unpack_from('<I',blob,12)[0];doc=json.loads(blob[20:20+length]);start=28+length
    result=[]
    for image in doc['images']:
        view=doc['bufferViews'][image['bufferView']];offset=start+view.get('byteOffset',0)
        result.append(sha(blob[offset:offset+view['byteLength']]))
    return result
checks=[]
def check(name,ok):
    checks.append({'name':name,'ok':ok})
    assert ok,name
with package('[API1.9]CS武器1.4.5-作者ZH667-全量版.scmod') as old, package('[API1.9]CS武器1.4.6-作者ZH667-全量版.scmod') as new:
    check('core entry manifest unchanged',set(old.namelist())==set(new.namelist()))
    for name in old.namelist():
        if name not in ('ScCsgoKnives.dll','modinfo.json'):
            check('unchanged core resource '+name,old.read(name)==new.read(name))
with package('[API1.9]CS战术同伴拓展1.2.1-作者ZH667.scmod') as old, package('[API1.9]CS战术同伴拓展1.2.2-作者ZH667.scmod') as new:
    for name in ('ct','t'):
        route=f'Assets/Models/ScCsgoTactical/{name}.glb'
        check('all embedded textures preserved including eyes '+name,images(old.read(route))==images(new.read(route)))
    for route in ('Assets/Models/ScCsgoTactical/hostage.glb','Assets/Models/ScCsgoTactical/shield.glb','Assets/Animations/ScTactical.json'):
        check('unchanged tactical resource '+route,old.read(route)==new.read(route))
report={'failed':0,'checks':checks}
(ROOT/'output/tactical-1.2.2/preservation-checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8')
print(f'PASS {len(checks)} preservation checks')
