"""Package historical gameplay backports plus a shared persistence protocol; originals stay untouched."""
import argparse,json,hashlib,zipfile,zlib
from pathlib import Path
from stage_compatibility_sources import SOURCES
from pack_single_scmods import raw_member,write_archive
from compatibility_manifest import build
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output';REPORT=OUT/'release-compatibility-1'
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
    p=argparse.ArgumentParser();p.add_argument('profile',choices=['1.0.0','1.2.0','latest']);p.add_argument('--edition',choices=['Full','Lite'],default='Full');a=p.parse_args()
    latest=a.profile=='latest';lite=a.edition=='Lite'
    if not latest and lite:raise ValueError('Historical inputs are Full; never call a full-resource build Lite')
    if latest:
        evidence=json.loads((OUT/'release-single-1.5.3'/f'{a.edition}.json').read_text('utf8'));source=OUT/evidence['path'];expected=evidence['sha256']
        if not source.is_file():
            # A superseded package may have been retired. Reuse only the hash-verified
            # current family archive; the pending-file writer safely replaces it after closing.
            evidence=json.loads((REPORT/f'latest-{a.edition}.json').read_text('utf8'))
            source=OUT/evidence['path'];expected=evidence['sha256']
        dll=ROOT/'src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll';version='1.6.0'
        name=f'[API1.9]CS武器1.6.0-作者ZH667-{"512轻量" if lite else "全量"}总包.scmod'
    else:
        _,source,expected=SOURCES[a.profile];version=a.profile+'-compat.1';dll=ROOT/f'.tmp/compatibility/{a.profile}/src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll'
        name=f'[API1.9]CS武器{a.profile}-双向兼容修订1-作者ZH667.scmod'
    assert sha(source.read_bytes())==expected
    matrix=json.loads((ROOT/'.tmp/compatibility-check.json').read_text('utf8'));assert matrix['failed']==0
    index={'1.0.0':0,'1.2.0':1,'latest':2}[a.profile]
    assert matrix['modules'][index]['sha256']==sha(dll.read_bytes()),'Untested build'
    entries={};hashes={}
    def add(n,b):
        c=zlib.compressobj(9,zlib.DEFLATED,-15);entries[n]=(8,zlib.crc32(b),len(b),c.compress(b)+c.flush());hashes[n]=sha(b)
    with zipfile.ZipFile(source) as z:
        for e in z.infolist():
            entries[e.filename]=(e.compress_type,e.CRC,e.file_size,raw_member(z,e));hashes[e.filename]=sha(z.read(e))
        metadata=json.loads(z.read('modinfo.json'));metadata['Version']=version;metadata['ApiVersion']='1.9.3.1'
        metadata['Name']='CS武器 · '+('最新双向兼容总包'+(' · 512轻量' if lite else '') if latest else a.profile+'双向兼容修订')
        metadata['Description']='双向兼容系列1：只能与标有双向兼容修订的包互换；保留枪械ID、弹药、耐久、成长和新内容数据。旧版修订统一50级持久成长/事务，新版独有内容在旧版暂不可用，返回新版恢复。不自动备份，玩家自行备份。不可与未修订原1.0混用。'
        add('modinfo.json',(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n').encode('utf8'))
        if latest:
            core=json.loads(z.read('Integrations/ScCsgoKnives.modinfo.json'));core['Version']=version;add('Integrations/ScCsgoKnives.modinfo.json',json.dumps(core,ensure_ascii=False).encode('utf8'))
            bundle=json.loads(z.read('Integrations/ScCsgoBundle.json'));bundle.update(version=version,core=version,coreSha256=sha(dll.read_bytes()));add('Integrations/ScCsgoBundle.json',json.dumps(bundle,ensure_ascii=False).encode('utf8'))
    add('ScCsgoKnives.dll',dll.read_bytes())
    add('Assets/ScCompatibility.xdb',(ROOT/'src/ScCsgoKnives/Assets/ScCompatibility.xdb').read_bytes())
    add('Assets/ScCompatibilityManifest.xml',build(not latest))
    add('Integrations/CompatibilityFamily.json',json.dumps(dict(family=1,profile=a.profile,version=version,source_sha256=expected,core_sha256=sha(dll.read_bytes()),legacy=not latest,backup_policy='manual',build_revision='2026-09-25-manual-backup'),indent=2).encode('utf8'))
    text='''双向兼容系列1。退出世界后在本系列内替换，一个世界只启用一个CS主包。
系列成员：1.0.0-compat.1、1.2.0-compat.1、1.6.0（全量/轻量）。未修订原1.0/1.2包不是双向系列成员。
原版1.0/1.2存档可以首次导入。模组不自动生成备份；更新、切换及迁移前请自行备份世界。
旧版修订保留历史玩法/资源源码，统一回移50级持久成长、库存事务、身份与补偿保护，避免永久状态反复折算。
旧版修订中小鸡、战术实体暂时休眠，保留编号、装备和状态。新版物品保留类型与data，显示暂不可用；返回最新版恢复。
旧版暂不支持的CS人物外观/手套选择保留。不要同时启用依赖新版接口的独立战术包。
缺失的历史枪械记录不能凭空恢复；局部异常原样保留。未知格式或无法安全处理的身份冲突继续拒绝。
本包离线及原生加载诊断不等于完整Android/全部第三方Mod组合验收。
'''
    add('INSTALL.txt',text.encode('utf-8-sig'))
    out=OUT/name;pending=out.with_suffix('.pending');write_archive(pending,entries)
    with zipfile.ZipFile(pending) as z:
        assert z.testzip() is None
        for n,h in hashes.items():assert sha(z.read(n))==h
    pending.replace(out);REPORT.mkdir(exist_ok=True)
    report=dict(path=name,version=version,profile=a.profile,edition=a.edition,bytes=out.stat().st_size,sha256=sha(out.read_bytes()),coreSha256=sha(dll.read_bytes()),sourceSha256=expected,entries=len(entries))
    (REPORT/f'{a.profile}-{a.edition}.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8');print(json.dumps(report,ensure_ascii=False))
if __name__=='__main__':main()
