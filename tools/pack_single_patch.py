"""Release 1.5.2 with the verified, unchanged 1.5.1 resource streams and a tested core DLL."""
import argparse,hashlib,json,zipfile,zlib
from pathlib import Path
from pack_single_scmods import raw_member,write_archive
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output'
def sha(data):return hashlib.sha256(data).hexdigest()
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--edition',choices=['Full','Lite'],required=True);a=p.parse_args()
    old=json.loads((OUT/'release-single-1.5.1'/f'{a.edition}.json').read_text('utf8'))
    source=OUT/old['path'];assert sha(source.read_bytes())==old['sha256'],'Source release changed'
    check=json.loads((ROOT/'.tmp/balance-check-152.json').read_text('utf8'))
    dll=(ROOT/'src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll').read_bytes()
    assert check['failed']==0 and check['coreSha256']==sha(dll),'Core DLL was not validated'
    entries={};hashes={}
    def add(name,data):
        c=zlib.compressobj(9,zlib.DEFLATED,-15);entries[name]=(8,zlib.crc32(data),len(data),c.compress(data)+c.flush());hashes[name]=sha(data)
    with zipfile.ZipFile(source) as z:
        for i in z.infolist():
            data=z.read(i);entries[i.filename]=(i.compress_type,i.CRC,i.file_size,raw_member(z,i));hashes[i.filename]=sha(data)
        metadata=json.loads(z.read('modinfo.json'));metadata['Version']='1.5.2'
        metadata['Description']='CS武器1.5.2整合总包：全部枪械Lv0恢复CS2基准射速，降低升级射速增幅；旧世界升级前验证完整备份，缺枪械记录时拒绝加载。含完整战术同伴、敌对小队、拆弹与内置外观。全量与轻量二选一，停用旧独立CS主包、资源包和战术拓展。玩家CT/T外观另需NekoMeko Model1.1及Neorxna1.4。'
        add('modinfo.json',(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n').encode('utf8'))
        core=json.loads(z.read('Integrations/ScCsgoKnives.modinfo.json'));core['Version']='1.5.2'
        add('Integrations/ScCsgoKnives.modinfo.json',json.dumps(core,ensure_ascii=False,indent=2).encode('utf8'))
        membership=json.loads(z.read('Integrations/ScCsgoBundle.json'));membership.update(version='1.5.2',core='1.5.2',sourceBundleSha256=old['sha256'],coreSha256=sha(dll))
        membership['sourcePackagesNote']='Historical resource provenance; gameplay core replaced by the tested 1.5.2 DLL.'
        add('Integrations/ScCsgoBundle.json',json.dumps(membership,ensure_ascii=False,indent=2).encode('utf8'))
    add('ScCsgoKnives.dll',dll)
    add('INSTALL.txt',('''CS武器1.5.2单文件总包，直接导入scmod。全量与轻量二选一。
退出世界后停用旧独立CS武器、资源包、战术拓展及早期独立外观包，启用本总包；不要将包含多个scmod的ZIP直接改后缀导入。
全部枪械Lv0使用CS2基准射速；普通枪Lv50 x1.325，连狙x1.2，栓狙x2.625。Zeus Lv0充能30秒，Lv50约4.38秒。
保留1.5.0的材料、霰弹枪、投掷物、自制模型及战术1.4.0玩法。轻量保持既有512资源及无损Deflate压缩。
正常旧世界首次进入前创建并验证完整snapshot备份，同schema的1.2.0也覆盖；布局v5/schema6/rules7和原枪型ID不变。
正在充能的旧枪保留剩余秒数及已保存周期。缺失或不匹配的旧枪记录会阻止进入与自动保存，不能用新枪默认状态恢复。
World7当前缺失记录的问题需要该世界丢失前的完整备份；此包不能创造已丢失的记录。
CT/T玩家外观仍需另装NekoMeko Model1.1及Neorxna1.4。不要同时装旧战术拓展。
离线测试不等于完整实机/Android验收；还原旧版时同时恢复升级前完整世界备份。
''').encode('utf-8-sig'))
    target=OUT/f'[API1.9]CS武器1.5.2-作者ZH667-{"512轻量" if a.edition=="Lite" else "全量"}总包.scmod'
    pending=target.with_suffix('.pending');write_archive(pending,entries)
    with zipfile.ZipFile(pending) as z:
        assert z.testzip() is None
        for n,h in hashes.items():assert sha(z.read(n))==h,n
    pending.replace(target)
    result=dict(path=target.name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),entries=len(entries),membership=membership,
                gameplayDlls={n:h for n,h in hashes.items() if n.endswith(('.dll','.bin'))},memberHashes=hashes)
    reports=OUT/'release-single-1.5.2';reports.mkdir(exist_ok=True)
    (reports/f'{a.edition}.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps({k:v for k,v in result.items() if k not in ('memberHashes','membership')},ensure_ascii=False))
if __name__=='__main__':main()
