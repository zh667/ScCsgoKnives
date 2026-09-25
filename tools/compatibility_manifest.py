"""Manifest of owned saved types. A future release must not drop or rename an existing family identity."""
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
def build(legacy=False):
    doc=ET.Element('Compatibility',Protocol='1',Legacy=str(legacy).lower())
    members={}
    for path in [*sorted((ROOT/'src/ScCsgoKnives/Assets').glob('*.xdb')),*sorted((ROOT/'src/ScCsgoTactical/Assets').glob('*.xdb'))]:
        root=ET.parse(path).getroot()
        for e in root.iter('MemberSubsystemTemplate'):
            members['Subsystem',e.get('Name')]=dict(Name=e.get('Name'),Guid=e.get('Guid'))
        for e in root.iter('EntityTemplate'):
            if e.get('Name','').startswith(('Sc','Tactical')):
                members['Entity',e.get('Name')]=dict(Name=e.get('Name'),Guid=e.get('Guid'))
        for e in root.iter('MemberComponentTemplate'):
            if e.get('Name','').startswith(('Sc','Tactical')):members['Component',e.get('Name')]=dict(Name=e.get('Name'))
    members['Component','ScGunTravel']=dict(Name='ScGunTravel')
    for (tag,name),attrs in sorted(members.items()):ET.SubElement(doc,tag,attrs)
    ET.indent(doc)
    return ET.tostring(doc,encoding='utf-8',xml_declaration=True)
if __name__=='__main__':
    path=ROOT/'src/ScCsgoKnives/Assets/ScCompatibilityManifest.xml';path.write_bytes(build())
    print(path)
