"""Current family metadata, shared by normal packaging and metadata-only repair.

Never rewrite historical resource provenance, product assemblies or resource bytes.
"""
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
CORE='zh667.ScCsgoKnives'
def encode(value):return (json.dumps(value,ensure_ascii=False,indent=2)+'\n').encode('utf8')
def sources(root=ROOT):
    result={key:json.loads((root/'src'/project/'modinfo.json').read_text('utf8')) for key,project in
            [('core','ScCsgoKnives'),('agents','ScCsgoTactical'),('deathmatch','ScCsgoDeathmatch')]
            if (root/'src'/project/'modinfo.json').exists()}
    version=result['core']['Version']
    for key in ['agents','deathmatch']:
        if key not in result:continue
        assert result[key]['Version']==version,(key,'source version differs from core')
        assert result[key]['Dependencies']=={CORE:version},(key,'source dependency differs from core')
    assert all(m['Author']=='zh667' for m in result.values())
    return result

def metadata_members(archive,label,root=ROOT):
    src=sources(root);version=src['core']['Version'];names=set(archive.namelist());changes={}
    def emit(name,data):
        if name in names and archive.read(name)!=data:changes[name]=data
    for name in sorted(names):
        if name!='modinfo.json' and not name.endswith('.modinfo.json'):continue
        original=json.loads(archive.read(name).decode('utf-8-sig'))
        package=original.get('PackageName')
        if package==CORE:
            meta=dict(src['core']);full=label=='全量'
            meta['Name']='CS武器 · '+('全量版' if full else '轻量版')
            meta['Description']+=('包含探员、战术同伴、敌对小队、空投补给、人物外观与中英探员语音，无需另装探员包。' if full else '轻量资源版本，可搭配同一批发布的探员包使用。')
        elif package=='zh667.ScCsgoTactical':meta=dict(src['agents'])
        elif package=='zh667.ScCsgoDeathmatch':meta=dict(src['deathmatch'])
        else:continue
        assert meta['PackageName']==package and meta['Author']==original['Author']
        emit(name,encode(meta))
    if 'INSTALL.txt' in names:
        intro=(f'CS武器 {version} 全量包：已包含探员和中英探员语音，无需另装探员包。' if label=='全量' else
               f'CS武器 {version} 分体版：轻量包可单独使用；探员包需搭配同一批发布的轻量包。')
        text=intro+'\n'+f'全量与轻量只启用一种，勿同时启用旧版或旧独立战术包。死亡竞赛为可选附属包，需配套 {version} 核心。\n'
        text+='探员内容包含CT/T同伴、敌对小队、空投补给、盾牌、拆弹、手套、小鸡及探员语音。\n'
        text+='玩家CT/T外观切换另需NekoMeko Model 1.1和Neorxna 1.4；不装也能使用同伴和第一人称手套。\n'
        text+='退出世界后更换包，直接导入scmod，无需解压。世界备份由玩家自行管理，本模组不自动备份。\n'
        text+='存档兼容不代表可以跨版本联机；联机双方须使用同一批核心，并保持探员和启用的竞技附属包一致。\n'
        text+='暂时移除探员包时，探员相关实体和物品状态保留，装回匹配探员包后恢复。\n'
        emit('INSTALL.txt',text.encode('utf8'))
    bundle='Integrations/ScCsgoBundle.json'
    if bundle in names:
        value=json.loads(archive.read(bundle));value.update(version=version,core=version,tactical=version)
        value['coreSha256']=hashlib.sha256(archive.read('ScCsgoKnives.dll')).hexdigest()
        value['sourcePackagesNote']='Historical resource provenance only; current versions are above. coreSha256 identifies the core DLL in this archive.'
        emit(bundle,encode(value))
    return changes
