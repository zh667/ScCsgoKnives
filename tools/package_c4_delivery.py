"""Release only packages whose exact core/resource hashes passed PackageCheck."""
from pathlib import Path
import hashlib,json,zipfile,shutil
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output';REPORT=OUT/'c4-hud-111'
sha=lambda b:hashlib.sha256(b).hexdigest()
manifest=json.loads((REPORT/'delivery.json').read_text(encoding='utf-8'))
notes='''CS 武器 1.0.11 安装与复现

完全退出游戏，替换旧 CS 武器主包；只安装本 ZIP 中的一组主包和资源。
全量版配资源 1.4.0，512 优化版配资源 1.5.0，二选一。
已经安装的全量资源 1.4.0 可以保留，1.0.9/1.0.10 主包请移出 Mods。

本次修正：地面 C4 只剩白条、图标细条、屏幕与下包动画、滴声轮换变调；
第一人称屏蔽本地角色额外手持枪。弹药和耐久保留快捷栏上方中央，
耐久常显，手机字号适当减小，去掉日常操作长提示。

创造背包原版“武器”找 C4 定时炸弹。站稳并等切出结束，按住 E/放置 C4
3.2 秒：蹲下、输入密码、安装、收手；提前松手/移动/切物品取消且不扣物品。
安装后 20 秒爆炸，中心伤害 5000、半径 32 格，向边缘衰减。
创造模式在开阔地退到 35～40 格观察冲击波，爆心视角看不全扩散范围。
用同一把枪站立/蹲下、抬头低头查看小枪重影，再切第三人称查看手持枪。

枪械编号和存档格式保持不变，未自动安装、未改写玩家世界。
Full/512 使用同一 DLL；全套包内 DLL 检查通过，不等于手机/全部模组实机验收。
完整说明：docs/c4-hud-111-2026-09-13.md（开发项目目录）。
'''
bundles=[]
for label,report,core,resource in [
    ('全量版','full-tests.json','ScCsgoKnives-1.0.11-preview.scmod','ScCsgoResources-1.4.0.scmod'),
    ('512优化版','optimized-tests.json','ScCsgoKnives-1.0.11-Optimized512-preview.scmod','ScCsgoResources-1.5.0-Optimized512.scmod')]:
    tests=json.loads((REPORT/report).read_text(encoding='utf-8'))
    assert not tests['failed'] and all(c['ok'] for c in tests['checks'])
    cb=(OUT/core).read_bytes();rb=(OUT/resource).read_bytes()
    assert sha(cb)==tests['packageSha256'] and sha(rb)==tests['resourcePackageSha256']
    with zipfile.ZipFile(OUT/core) as z:
        assert sha(z.read('ScCsgoKnives.dll'))==manifest['coreDllSha256']==tests['dllSha256']
    target=OUT/f'CS武器-1.0.11-{label}.zip';pending=target.with_suffix('.pending')
    with zipfile.ZipFile(pending,'w',zipfile.ZIP_STORED) as z:
        for name,data in [(core,cb),(resource,rb),('安装与复现.txt',notes.encode('utf-8-sig'))]:
            info=zipfile.ZipInfo(name,(2026,9,13,0,0,0));z.writestr(info,data)
    with zipfile.ZipFile(pending) as z:assert z.testzip() is None
    if target.exists():
        assert sha(target.read_bytes())==sha(pending.read_bytes()),'Delivery name already exists with different bytes'
        pending.unlink()
    else:pending.replace(target)
    bundles.append(dict(path=target.name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),checks=len(tests['checks']),failed=0))
manifest['bundles']=bundles
(REPORT/'delivery.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
shutil.copyfile(OUT/'optimization-106/model-integrity.json',REPORT/'model-integrity.json')
shutil.copyfile(OUT/'c4-hud-110/new-assets-optimization.json',REPORT/'new-assets-optimization.json')
source=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/13_c4'
visuals=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/c4_visuals.json').read_text())
evidence=dict(version='1.0.11',source=str(source),previousImportReport='c4-source-109.json',
    vertices=14368,triangles=17945,optimizedTriangles=8977,materials=['weapon_c4','weapon_c4_digits'],
    digits=visuals['Digits'],worldPoses=list(visuals['WorldPoses']),
    soundAdaptation='Stable PlantSound pitch; matching short version below 10s; cadence is adapted, not a CS2 timer-code reproduction.',files=[])
for relative in ['glb/weapon_c4.glb','decompiled/weapons/models/c4/materials/weapon_c4_digits.vmat','decompiled/animation/anims/viewmodel/equipment/c4/plant_c4.vnmclip','decompiled/soundevents/game_sounds_weapons.vsndevts','decompiled-extra/arm_bomb.wav','decompiled-extra/nvg_on.wav']:
    p=source/relative;evidence['files'].append(dict(path=relative,bytes=p.stat().st_size,sha256=sha(p.read_bytes())))
(ROOT/'docs/c4-source-111.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(bundles,ensure_ascii=False,indent=2))
