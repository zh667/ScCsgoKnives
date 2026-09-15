"""User-authorized in-place replacement of the 1.1.0 Lite C4 color texture only."""
from pathlib import Path
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile
from optimize_weapon_resources import texture, rows, sha

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output'
NAME='Assets/Textures/ScCsgoKnives/c4_cs2.webp'


def main():
    package=OUT/'[API1.9]CS武器1.1.0-512轻量版.scmod'
    report=OUT/'release-1.1.0'
    before=sha(package.read_bytes())
    backup=ROOT/'.tmp/c4-color-backups'/before
    backup.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(package,backup/package.name)
    with zipfile.ZipFile(package) as z:
        entries={n:z.read(n) for n in z.namelist()}
    data=texture(NAME.replace('.webp','.png'),(ROOT/'src/ScCsgoKnives'/NAME.replace('.webp','.png')).read_bytes())
    entries[NAME]=data
    original_rows=json.loads(entries['Assets/ScCsgoDerivedResources.json'])
    updated=[rows[-1] if row['path']==NAME.replace('.webp','.png') else row for row in original_rows]
    provenance=(json.dumps(updated,ensure_ascii=False,indent=2)).encode('utf-8')
    entries['Assets/ScCsgoDerivedResources.json']=provenance
    marker=ET.fromstring(entries['Assets/ScCsgoResources.xml'])
    next(f for f in marker if f.attrib['Path']==NAME).set('Sha256',sha(data))
    entries['Assets/ScCsgoResources.xml']=ET.tostring(marker,encoding='utf-8')
    pending=package.with_suffix('.pending')
    with zipfile.ZipFile(pending,'w',zipfile.ZIP_DEFLATED) as z:
        for n,b in sorted(entries.items()):
            info=zipfile.ZipInfo(n,(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,b)
    with zipfile.ZipFile(backup/package.name) as a,zipfile.ZipFile(pending) as b:
        changes=[n for n in a.namelist() if a.read(n)!=b.read(n)]
        assert set(changes).issubset({NAME,'Assets/ScCsgoDerivedResources.json','Assets/ScCsgoResources.xml'}),changes
        assert a.namelist()==b.namelist()
        assert a.read('ScCsgoKnives.dll')==b.read('ScCsgoKnives.dll')
        assert a.read('ScCsgoResources.dll')==b.read('ScCsgoResources.dll')
    pending.replace(package)
    (report/'assets.json').write_bytes(provenance)
    meta=json.loads((report/'Optimized512.json').read_text('utf-8'))
    meta.update(bytes=package.stat().st_size,sha256=sha(package.read_bytes()))
    (report/'Optimized512.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n','utf-8')
    audit=report/'c4-fix';audit.mkdir(exist_ok=True)
    (audit/'replacement.json').write_text(json.dumps(dict(beforeSha256=before,afterSha256=sha(package.read_bytes()),
        backup=str(backup),changedEntries=changes,gameplayAndResourceDllsUnchanged=True),ensure_ascii=False,indent=2)+'\n','utf-8')
    print(package,package.stat().st_size,sha(package.read_bytes()))


if __name__=='__main__':main()
