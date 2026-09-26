"""Build matching 1.3.0 core and optional agents candidates (no installation)."""
from pathlib import Path
import hashlib,json,zipfile,xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1];S=ROOT/'.tmp/split-lite-130-20260926'
def sha(b):return hashlib.sha256(b).hexdigest()
def dump(o):return (json.dumps(o,ensure_ascii=False,indent=2)+'\n').encode()
derivation=json.loads((S/'asset-derivation.json').read_bytes());reports={}
original=S/'original-lite.scmod'
if not original.exists():original=ROOT/'output/[API1.9]CS武器1.3.0-轻量包.scmod'
assert sha(original.read_bytes())==derivation['sourceSha256']
with zipfile.ZipFile(original) as source:
    for owner,label in [('core','轻量'),('agents','探员')]:
        entries={}
        for n,row in derivation['entries'].items():
            if row['owner']!=owner:continue
            data=(S/owner/'assets'/row['target']).read_bytes();assert sha(data)==row['sha256'];entries[row['target']]=data
        if owner=='core':
            entries['ScCsgoKnives.dll']=(S/'core/source/bin/Release/net10.0/ScCsgoKnives.dll').read_bytes()
            entries['ScCsgoResources.dll']=(S/'resources/bin/Release/net10.0/ScCsgoResources.dll').read_bytes()
            meta=json.loads(source.read('modinfo.json'));meta.update(Name='CS武器 · 1.3.0轻量版',Version='1.3.0',Description='独立武器轻量包：35种枪械、44种额外枪皮、22种刀具及刀皮、检视、成长、工作台、手雷/C4全部保留。512颜色贴图，压缩音效和动画数据。探员/战术/小鸡/语音由可选的1.3.0探员包提供。与旧轻量/全量/极简三选一。')
            manifest=ET.fromstring(entries['Assets/ScCompatibilityManifest.xml']);manifest.set('Legacy','false');manifest.set('OptionalAgents','true')
            ET.SubElement(manifest,'Subsystem',Name='ScAgentVoice',Guid='13917e94-9243-4430-9e6d-b85542136ad4')
            entries['Assets/ScCompatibilityManifest.xml']=ET.tostring(manifest,encoding='utf-8',xml_declaration=True)
            family=json.loads(source.read('Integrations/CompatibilityFamily.json'));family.update(profile='split-lite',edition='Lite',version='1.3.0',legacy=False,backup_policy='manual',build_revision='split-lite-130-20260926',core_sha256=sha(entries['ScCsgoKnives.dll']))
            entries['Integrations/CompatibilityFamily.json']=dump(family)
            entries['Integrations/ScCsgoKnives.modinfo.json']=dump(meta)
            marker=ET.Element('Resources',Version='1.10.4',Format='1',Edition='Optimized512')
            for n,b in sorted(entries.items()):
                if n.startswith(('Assets/Models/','Assets/Textures/','Assets/Audio/','Assets/Animations/')):ET.SubElement(marker,'File',Path=n,Sha256=sha(b))
            entries['Assets/ScCsgoResources.xml']=ET.tostring(marker,encoding='utf-8',xml_declaration=True)
        else:
            entries['ScCsgoTactical.dll']=(S/'agents/source/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes()
            entries['ScCsgoVoice.dll']=source.read('ScCsgoVoice.dll')
            entries['Integrations/ScCsgoAppearance.bin']=source.read('Integrations/ScCsgoAppearance.bin')
            meta=json.loads(source.read('Integrations/ScCsgoTactical.modinfo.json'))
            meta.update(Name='CS武器 · 1.3.0探员包',Version='1.3.0',Dependencies={'zh667.ScCsgoKnives':'1.3.0'},Description='配套新1.3.0轻量包：CT/T探员、同伴、敌队挑战、盾牌、拆弹、人物手套、CS小鸡和中英探员语音。保留加载与NPC预烘焙缓存。切换玩家外观另需NekoMeko Model 1.1和Neorxna 1.4。请勿搭配旧全量/轻量/极简或旧独立战术包。')
        entries['modinfo.json']=dump(meta)
        entries['Integrations/ScSplit.json']=dump(dict(version='1.3.0',protocol=1,role=owner,sourceSha256=derivation['sourceSha256'],gunCount=35,gunSkins=44,knifeCount=22,inspection=True))
        entries['INSTALL.txt']='''CS武器 1.3.0 分体版
轻量包可单独使用，保留35种枪械、44种额外枪皮、22种刀具及刀皮、检视、成长、工作台、手雷/C4。
探员包必须搭配本次新的1.3.0轻量包，包含同伴/敌队/盾牌/拆弹、CT/T外观和手套、小鸡、中英探员语音。
玩家CT/T外观切换另需NekoMeko Model 1.1和Neorxna 1.4；不装这两个前置仍可玩同伴等功能。
退出世界后更换包。轻量与旧全量、旧轻量、极简三选一，勿混装旧独立战术包。
暂时移除探员包时，旧探员/小鸡实体休眠，战术物品暂不可用，原状态保留；装回匹配探员包恢复。
世界备份由玩家自行管理，本模组不自动备份。直接导入scmod，不要解压。
'''.encode('utf-8-sig')
        for n in ['LICENSE','THIRD_PARTY_NOTICES.md','ASSET_SOURCES.md']:
            if n in source.namelist():entries[n]=source.read(n)
        p=S/'candidate'/f'[API1.9]CS武器1.3.0-{label}包.scmod';p.parent.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(p,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
            for n,b in sorted(entries.items()):z.writestr(n,b)
        reports[owner]=dict(file=p.name,bytes=p.stat().st_size,sha256=sha(p.read_bytes()),entries={n:sha(b) for n,b in entries.items()})
        print(owner,p.stat().st_size,flush=True)
(S/'packages.json').write_bytes(dump(reports))
