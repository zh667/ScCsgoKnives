"""Read-only size attribution and snapshot timeline. Never removes or rewrites game files."""
import argparse,hashlib,json,zipfile
from pathlib import Path
from datetime import datetime
import xml.etree.ElementTree as ET

def field(parent,name):
    e=parent.find(f"Value[@Name='{name}']") if parent is not None else None
    return e.get('Value') if e is not None else None
def project(data):
    doc=ET.fromstring(data);subs=doc.find('Subsystems');gun=subs.find("Values[@Name='ScGunBlockBehavior']")
    mods=subs.find("Values[@Name='UsedMods']/Values[@Name='Mods']")
    versions={field(m,'PackageName'):field(m,'Version') for m in mods} if mods is not None else {}
    compat=subs.find("Values[@Name='ScCompatibility']")
    capsule=field(compat,'Capsule')
    return dict(bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),
        name=field(subs.find("Values[@Name='GameInfo']"),'WorldName'),
        cs_version=versions.get('zh667.ScCsgoKnives'),compat_build=field(compat,'Build'),
        records=len(gun.findall("Values[@Name='GunRegistry']/Values[@Name='Records']/Value")) if gun is not None else 0,
        entities=len(doc.findall('./Entities/Entity')),capsule_utf8_bytes=len(capsule.encode('utf8')) if capsule else 0,
        gun_subtree_xml_bytes=len(ET.tostring(gun,encoding='utf8')) if gun is not None else 0)
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worlds',type=Path,required=True);parser.add_argument('--detail',default='World6');parser.add_argument('--out',type=Path,required=True)
    a=parser.parse_args();all_worlds=[];timeline=[]
    for directory in sorted(a.worlds.iterdir()):
        if not directory.is_dir() or not (directory/'Project.xml').is_file():continue
        files=list(p for p in directory.rglob('*') if p.is_file())
        totals=dict(total=0,snapshots=0,regions=0,project=0,bak=0,other=0);snapshots=[]
        for p in files:
            size=p.stat().st_size;totals['total']+=size
            category='snapshots' if p.suffix.lower()=='.snapshot' else 'regions' if 'Regions' in p.relative_to(directory).parts else 'project' if p.name=='Project.xml' else 'bak' if p.name=='Project.bak' else 'other'
            totals[category]+=size
            if category=='snapshots':snapshots.append(dict(path=str(p.relative_to(directory)),bytes=size,modified=datetime.fromtimestamp(p.stat().st_mtime).isoformat()))
        totals['without_snapshots']=totals['total']-totals['snapshots']
        current=project((directory/'Project.xml').read_bytes())
        all_worlds.append(dict(directory=directory.name,project=current,bytes=totals,snapshots=snapshots))
        if directory.name!=a.detail:continue
        live={str(p.relative_to(directory)).replace('\\','/'):(p.stat().st_size,hashlib.sha256(p.read_bytes()).hexdigest()) for p in files if p.suffix.lower()!='.snapshot'}
        for s in sorted(snapshots,key=lambda s:s['modified']):
            with zipfile.ZipFile(directory/s['path']) as z:
                entries=z.infolist();nested=[i.filename for i in entries if i.filename.lower().endswith('.snapshot')]
                changed=[];identical=0
                for i in entries:
                    data=z.read(i)
                    if i.filename in live and live[i.filename]==(len(data),hashlib.sha256(data).hexdigest()):identical+=1
                    else:changed.append(dict(path=i.filename,backup_bytes=len(data),current_bytes=live.get(i.filename,(None,None))[0]))
                timeline.append(dict(**s,uncompressed_bytes=sum(i.file_size for i in entries),entries=len(entries),nested_snapshots=nested,
                    project=project(z.read('Project.xml')),region_bytes=sum(i.file_size for i in entries if 'Regions/' in i.filename),
                    identical_to_current_files=identical,changed_from_current=changed))
    result=dict(date=datetime.now().isoformat(),source=str(a.worlds),read_only=True,worlds=all_worlds,detail_world=a.detail,timeline=timeline)
    a.out.parent.mkdir(parents=True,exist_ok=True);a.out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf8')
    for w in all_worlds:print(w['directory'],w['project']['name'],json.dumps(w['bytes']), 'snapshots=',len(w['snapshots']))
    for t in timeline:print(t['path'],json.dumps({k:t[k] for k in ['bytes','uncompressed_bytes','region_bytes','nested_snapshots','project']},ensure_ascii=False))
if __name__=='__main__':main()
