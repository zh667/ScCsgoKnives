"""Verify edition parity and create two ZIP bundles retaining both mod identities.

Run after current Full/Lite core and tactical packages have passed PackageCheck.
No installation or source-resource mutation. ZIP members are complete scmods.
"""
from pathlib import Path
import hashlib
import io
import json
import struct
import zipfile
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output'
REPORT=OUT/'release-bundle-1.5.0'


def sha(b):return hashlib.sha256(b).hexdigest()


def entries(path):
    with zipfile.ZipFile(path) as z:
        assert len(z.namelist())==len(set(n.casefold() for n in z.namelist()))
        assert z.testzip() is None
        return {n:z.read(n) for n in z.namelist()}


def parse_glb(b):
    length=struct.unpack_from('<I',b,12)[0]
    return json.loads(b[20:20+length]),b[28+length:]


def compare_glb(full,lite):
    a,ab=parse_glb(full);b,bb=parse_glb(lite)
    for key in set(a)-{'bufferViews','buffers','images'}:assert a[key]==b[key],key
    assert a.get('images')==b.get('images')
    image_views={i['bufferView'] for i in a.get('images',[]) if 'bufferView' in i}
    assert len(a['bufferViews'])==len(b['bufferViews'])
    for i,(av,bv) in enumerate(zip(a['bufferViews'],b['bufferViews'])):
        assert {k:v for k,v in av.items() if k not in ('byteOffset','byteLength')}=={k:v for k,v in bv.items() if k not in ('byteOffset','byteLength')}
        x=ab[av.get('byteOffset',0):av.get('byteOffset',0)+av['byteLength']]
        y=bb[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']]
        if i not in image_views:assert x==y,('mesh/animation changed',i)
        else:
            image=Image.open(io.BytesIO(y));image.load();assert max(image.size)<=512
    return len(a['bufferViews'])-len(image_views)


def verify_pair(full_path,lite_path,tactical=False):
    full=entries(full_path);lite=entries(lite_path)
    fm=json.loads(full['modinfo.json']);lm=json.loads(lite['modinfo.json'])
    for field in ('PackageName','Version','Dependencies','NonPersistentMod'):assert fm.get(field)==lm.get(field)
    changed=0;views=0
    manifest=json.loads(lite['Assets/ScCsgoTacticalDerivedResources.json' if tactical else 'Assets/ScCsgoDerivedResources.json'])
    rows={r['path']:r for r in manifest}
    exempt={'modinfo.json','Assets/ScCsgoResources.xml','Assets/ScCsgoKnivesEdition.xml'}
    expected=set()
    for name,data in full.items():
        target=name[:-4]+'.webp' if name.endswith('.png') else name
        expected.add(target);assert target in lite,target
        if name.endswith('.png') or name in rows:
            row=rows[name];assert row['sourceSha256']==sha(data) and row['sha256']==sha(lite[target]),name
            if tactical and name.endswith('.glb'):views+=compare_glb(data,lite[target])
            if target.endswith('.webp'):
                image=Image.open(io.BytesIO(lite[target]));image.load()
                assert list(image.size)==row['toSize']
            changed+=data!=lite[target]
        elif name=='ScCsgoResources.dll' and not tactical:pass # separately built, verified by package self-tests
        elif name not in exempt:assert data==lite[target],name
    extra='Assets/ScCsgoTacticalDerivedResources.json' if tactical else 'Assets/ScCsgoDerivedResources.json'
    assert set(lite)==expected|{extra}
    dlls={name:sha(data) for name,data in full.items() if name.endswith(('.dll','.bin')) and name!='ScCsgoResources.dll'}
    for name,digest in dlls.items():assert sha(lite[name])==digest
    return dict(full=full_path.name,lite=lite_path.name,gameplayDlls=dlls,changedResources=changed,unchangedGlbBufferViews=views)


README='''CS武器 1.5.0 + 战术同伴拓展 1.4.0 总包（API 1.9.3.1）

本 ZIP 是分发总包，不是可以直接导入游戏的 scmod。先解压，再导入里面的两个 scmod。
主包：枪械、刀具、投掷物、枪械台、完整武器资源。
战术拓展：防爆盾、同伴、敌对小队、拆弹、手套及内置人物外观适配。
两个 scmod 保留原 PackageName，不能同时安装同一模组的旧版或另一画质版。

升级：先退出世界，完整备份世界目录；保留旧包以便配合备份恢复。
在模组管理中停用/移出旧 CS武器、旧 CS战术同伴拓展（以及早期独立 CS资源包/CS人物外观包），
然后导入本总包两个文件。全量与轻量只选一套；不需要两个画质同时装。
不要在只更新了主包、却遗漏战术拓展时进入含同伴的旧世界。
本次未改武器编号、布局 v5、记录 schema6/rules7；保留两个用户指定的1.0/1.2迁移路径。
1.0旧格式转换仍需完整世界备份成功才激活；过量弹药按恢复机制保留。
若需退回旧版，请恢复升级前世界备份，不要直接用旧 DLL 打开已升级世界。

本次包含：±10级预览；攻速/连狙/低等级霰弹调整；制作、维修、涂装材料调整；
手榴弹与燃烧范围伤害调整；自制物资和无线电模型；Sushi库存兼容修复。
不包含生物着火，也未实施可选第二轮成长曲线重做。

第三方前置不打包：同伴和手套玩法可独立使用；玩家切换 CT/T 外观需要自行安装
NekoMeko Model 1.1及Neorxna 1.4。原有合法资源来源说明保留在各 scmod 内。

全量保留原始资源。轻量：独立贴图最大512px WebP Q85，关键数据/特效图集保留无损；
枪械使用既有受约束减面方案；战术人物内嵌贴图最大512px PNG，网格、骨骼及动画不变。
两版玩法 DLL 字节相同。轻量会降低近距离贴图细节；安装体积不等于运行内存。

验证为包内 DLL 回归与原生加载诊断，不代表实际游戏、Android或全部Mod组合已验收。
本次只生成文件，未自动安装、删除旧包或改写任何玩家世界。
'''


def main():
    core=[OUT/f'[API1.9]CS武器1.5.0-作者ZH667-{label}.scmod' for label in ('全量版','512轻量版')]
    tactical=[OUT/'[API1.9]CS战术同伴拓展1.4.0-作者ZH667.scmod',OUT/'[API1.9]CS战术同伴拓展1.4.0-作者ZH667-512轻量版.scmod']
    for index,label in enumerate(('full','lite')):
        check=json.loads((REPORT/f'core-{label}-check.json').read_text('utf8'))
        assert check['failed']==0 and check['packageSha256']==sha(core[index].read_bytes()), 'Core acceptance is missing or stale'
        check=json.loads((REPORT/f'tactical-{label}-check.json').read_text('utf8'))
        assert check['failed']==0 and check['coreSha256']==sha(core[index].read_bytes()) and check['dlcSha256']==sha(tactical[index].read_bytes()), 'Tactical acceptance is missing or stale'
    comparisons=[verify_pair(*core),verify_pair(*tactical,tactical=True)]
    # The user-specified tactical release must retain every source asset in Full.
    baseline=Path(r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods\[API1.9]CS战术同伴拓展1.3.2-作者ZH667.scmod')
    assert sha(baseline.read_bytes())=='25db07ab4d68be479ef53e2911e3680232f865050bb229cc146370f24183162c'
    old=entries(baseline);current=entries(tactical[0]);preserved=0
    for name,data in old.items():
        if name.startswith('Assets/'):
            assert current[name]==data,name;preserved+=1
    bundles=[]
    for index,label in enumerate(('全量','512轻量')):
        files={p.name:p.read_bytes() for p in (core[index],tactical[index])}
        members={n:dict(bytes=len(b),sha256=sha(b)) for n,b in files.items()}
        files['安装与升级说明.txt']=README.encode('utf-8-sig')
        files['SHA256SUMS.txt']=(''.join(f'{sha(b)}  {n}\n' for n,b in files.items())).encode('utf-8-sig')
        target=OUT/f'[API1.9]CS武器1.5.0-含战术同伴1.4.0-{label}总包.zip'
        pending=target.with_suffix('.pending')
        with zipfile.ZipFile(pending,'w',zipfile.ZIP_STORED) as z:
            for name,data in files.items():z.writestr(zipfile.ZipInfo(name,(2026,1,1,0,0,0)),data)
        pending.replace(target)
        assert entries(target)==files
        bundles.append(dict(path=target.name,bytes=target.stat().st_size,MB=round(target.stat().st_size/1e6,3),MiB=round(target.stat().st_size/1048576,3),sha256=sha(target.read_bytes()),members=members))
    report=dict(bundles=bundles,liteUnder100MB=bundles[1]['bytes']<100_000_000,comparisons=comparisons,preservedTactical132Assets=preserved)
    (REPORT/'bundles.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(report,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
