"""Freeze only the gun subsystem and gun slots from the locally retained 1.2.0 save.
Never include terrain, player names, locations, unrelated mods, or overwrite an existing fixture.
"""
from pathlib import Path
import hashlib,json
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
source=root/'.tmp/migration-120-followup/World7-Project.xml'
data=source.read_bytes()
assert hashlib.sha256(data).hexdigest()=='018ae333415562cfe28184b6a54ec5ac2b9f2c2584cf7bf051ee18b828117d7e'
doc=ET.fromstring(data);sub=doc.find('Subsystems')
def group(p,n):return p.find(f"Values[@Name='{n}']")
def val(p,n):return p.find(f"Value[@Name='{n}']").get('Value')
core=next(m for m in group(group(sub,'UsedMods'),'Mods') if val(m,'PackageName')=='zh667.ScCsgoKnives')
assert val(core,'Version')=='1.2.0'
result=ET.Element('Project');subs=ET.SubElement(result,'Subsystems')
mapping=ET.SubElement(subs,'Values',Name='BlocksManager')
block=next(e for e in group(sub,'BlocksManager') if e.get('Value')=='ScGunBlock');mapping.append(block)
subs.append(group(sub,'ScGunBlockBehavior'))
mods=ET.SubElement(ET.SubElement(subs,'Values',Name='UsedMods'),'Values',Name='Mods')
mod=ET.SubElement(mods,'Values',Name='0')
for name in ['Version','PackageName']:ET.SubElement(mod,'Value',Name=name,Type='string',Value=val(core,name))
entities=ET.SubElement(result,'Entities')
for player in doc.find('Entities'):
    inv=group(player,'CreativeInventory')
    if inv is None:continue
    target=ET.SubElement(entities,'Entity',Name='MalePlayer')
    slots=ET.SubElement(ET.SubElement(target,'Values',Name='CreativeInventory'),'Values',Name='Slots')
    for slot in group(inv,'Slots'):
        if int(val(slot,'Contents'))&1023==int(block.get('Name')):slots.append(slot)
    packet=group(player,'ScGunTravel')
    if packet is not None:target.append(packet)
ET.indent(result)
folder=root/'tools/fixtures/migration-120-20260925';folder.mkdir(parents=True,exist_ok=True)
path=folder/'world7-guns.xml';payload=ET.tostring(result,encoding='utf-8',xml_declaration=True)
if path.exists():assert path.read_bytes()==payload,'Immutable fixture differs'
else:path.write_bytes(payload)
meta=dict(source_project_sha256=hashlib.sha256(data).hexdigest(),fixture_sha256=hashlib.sha256(payload).hexdigest(),
          source_version='1.2.0',source_package_sha256='b370ad7ff6c7cae0ec4abe584d8389bea813eb790b29dc3c2a4772adca184c95',
          extraction='Only saved gun block mapping, complete gun subsystem, CS version, gun creative slots, and gun travel packet. No item/record values changed.',
          expected_valid_slots=['Slot0','Slot1'],expected_conflicting_slots=['Slot10','Slot11'])
mp=folder/'provenance.json'
if not mp.exists():mp.write_text(json.dumps(meta,indent=2)+'\n','utf8')
print(json.dumps(meta,indent=2))
