"""Stage the independent 1.3.0 Mini candidate. Does not install or publish."""
import hashlib
import json
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/minimal-130-20260926'
NAME='[API1.9]CS武器1.3.0-极简包.scmod'
def sha(data):return hashlib.sha256(data).hexdigest()
def dump(data):return (json.dumps(data,ensure_ascii=False,indent=2)+'\n').encode('utf-8')

derivation=json.loads((STAGE/'resource-derivation.json').read_text(encoding='utf-8'))
entries={n:(STAGE/'package'/n).read_bytes() for n in derivation['files']}
for n,b in entries.items():assert sha(b)==derivation['files'][n],n
for name in ['Assets/ScCsgoDerivedResources.json','Assets/ScCsgoTacticalDerivedResources.json','Integrations/ScCsgoBundle.json']:
    entries.pop(name,None)
for assembly,folder in [('ScCsgoKnives','core'),('ScCsgoResources','resources')]:
    entries[assembly+'.dll']=(STAGE/f'{folder}/bin/Release/net10.0/{assembly}.dll').read_bytes()
meta=json.loads(entries['modinfo.json']);meta['Version']='1.3.0';meta['Name']='CS武器 · 1.3.0极简版'
meta['Description']='独立极简版：保留35种枪械、成长、弹药、手雷/C4及基础刀具；主战武器各一种额外枪皮。无探员/同伴/敌对小队及人物外观，关闭检视，模型减面、256贴图和压缩音效。完整目录和旧物品数据保留，不支持的枪皮按原厂外观显示。与全量/轻量三选一，备份由玩家自行管理。'
entries['modinfo.json']=dump(meta);entries['Integrations/ScCsgoKnives.modinfo.json']=dump(meta)
edition=ET.fromstring(entries['Assets/ScCsgoKnivesEdition.xml']);edition.set('Name','Mini');entries['Assets/ScCsgoKnivesEdition.xml']=ET.tostring(edition,encoding='utf-8',xml_declaration=True)
manifest=ET.fromstring(entries['Assets/ScCompatibilityManifest.xml']);manifest.set('Legacy','true');entries['Assets/ScCompatibilityManifest.xml']=ET.tostring(manifest,encoding='utf-8',xml_declaration=True)
family=json.loads(entries['Integrations/CompatibilityFamily.json']);family.update(profile='minimal',edition='Mini',version='1.3.0',legacy=True,backup_policy='manual',build_revision='minimal-130-20260926',core_sha256=sha(entries['ScCsgoKnives.dll']))
entries['Integrations/CompatibilityFamily.json']=dump(family)
entries['Integrations/ScMinimal.json']=dump(dict(version='1.3.0',profile='Mini',sourceSha256=derivation['sourceSha256'],gunCount=35,skinKeys=derivation['selectedSkins'],newKnives=['default_ct','default_t'],legacyKnifeCompatibilityGeometry=True,agents=False,inspect=False))
entries['INSTALL.txt']='''CS武器1.3.0独立极简版。直接导入scmod，不要解压。
与全量、轻量三选一；切换前退出世界，并由玩家自行备份世界。
保留35种枪械、成长/计数器/弹药/手雷/C4。新制作与创造列表只提供CT/T基础刀具、13种主战枪皮。
旧刀具保留必要的兼容模型和动作；额外枪皮/刀皮保留原始ID，当前显示原厂外观，换回完整包恢复。
无探员、同伴、小队、拆弹挑战和人物手套外观；原探员实体及战术物品数据休眠保留。
关闭检视动画；开火、换弹、消音器和投掷动作及事件保留。模型和256贴图降低细节，音效压缩。
不要同时启用独立战术拓展。模组不自动备份，不操作用户既有备份。
'''.encode('utf-8-sig')
marker=ET.Element('Resources',Version='1.10.4',Format='1',Edition='Optimized512')
for name,data in sorted(entries.items()):
    if name.startswith(('Assets/Models/','Assets/Textures/','Assets/Audio/','Assets/Animations/')):ET.SubElement(marker,'File',Path=name,Sha256=sha(data))
entries['Assets/ScCsgoResources.xml']=ET.tostring(marker,encoding='utf-8',xml_declaration=True)
target=STAGE/'candidate'/NAME;target.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
    for name,data in sorted(entries.items()):archive.writestr(name,data)
with zipfile.ZipFile(target) as archive:
    assert archive.testzip() is None
    sizes=sorted([(i.compress_size,i.filename) for i in archive.infolist()],reverse=True)
report=dict(path=NAME,sha256=sha(target.read_bytes()),bytes=target.stat().st_size,under30MB=target.stat().st_size<30_000_000,
    entries={n:sha(b) for n,b in entries.items()},largest=sizes[:25],sourceSha256=derivation['sourceSha256'])
(STAGE/'package.json').write_bytes(dump(report))
print(json.dumps({k:v for k,v in report.items() if k!='entries'},ensure_ascii=False,indent=2))
