"""Merge validated core/tactical editions into native single scmods.

Copy existing standard Deflate streams, retaining stronger lossless compression.
Only metadata, resource index and the small identity adapter are added/changed.
"""
import argparse,hashlib,json,struct,zlib,zipfile
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output'
REPORT=OUT/'release-single-1.5.1'


def sha(b):return hashlib.sha256(b).hexdigest()


def raw_member(z,info):
    z.fp.seek(info.header_offset)
    header=z.fp.read(30)
    assert header[:4]==b'PK\x03\x04'
    n,e=struct.unpack_from('<HH',header,26)
    z.fp.seek(n+e,1)
    return z.fp.read(info.compress_size)


def write_archive(path,entries):
    central=[]
    with path.open('wb') as f:
        for name,(method,crc,size,compressed) in sorted(entries.items()):
            encoded=name.encode('utf8');offset=f.tell();length=len(compressed)
            assert method in (0,8) and max(size,length,offset)<0xffffffff
            # All filenames explicitly UTF-8; no encryption, ZIP64 or data descriptors.
            flags=0x800;date=(2026-1980)<<9|1<<5|1
            f.write(struct.pack('<I5H3I2H',0x04034b50,20,flags,method,0,date,crc,length,size,len(encoded),0)+encoded+compressed)
            central.append(struct.pack('<I6H3I5H2I',0x02014b50,20,20,flags,method,0,date,crc,length,size,len(encoded),0,0,0,0,0,offset)+encoded)
        offset=f.tell()
        for row in central:f.write(row)
        length=f.tell()-offset
        f.write(struct.pack('<I4H2IH',0x06054b50,0,0,len(central),len(central),length,offset,0))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--edition',choices=('Full','Lite'),required=True)
    args=parser.parse_args();lite=args.edition=='Lite'
    core=OUT/('[API1.9]CS武器1.5.0-作者ZH667-'+('512轻量版' if lite else '全量版')+'.scmod')
    tactical=OUT/('[API1.9]CS战术同伴拓展1.4.0-作者ZH667'+('-512轻量版' if lite else '')+'.scmod')
    originals=[core,tactical]
    previous=OUT/'release-bundle-1.5.0'
    label='lite' if lite else 'full'
    core_check=json.loads((previous/f'core-{label}-check.json').read_text('utf8'))
    tactical_check=json.loads((previous/f'tactical-{label}-check.json').read_text('utf8'))
    assert core_check['failed']==0 and core_check['packageSha256']==sha(core.read_bytes()),'Unverified core source'
    assert tactical_check['failed']==0 and tactical_check['dlcSha256']==sha(tactical.read_bytes()) and tactical_check['coreSha256']==core_check['packageSha256'],'Unverified tactical source'
    if lite:
        enhanced=OUT/'release-bundle-1.5.0-lossless'
        core=enhanced/core.name if (enhanced/core.name).is_file() else core
        tactical=enhanced/tactical.name if (enhanced/tactical.name).is_file() else tactical
    entries={};hashes={}
    def add(name,data):
        co=zlib.compressobj(9,zlib.DEFLATED,-15)
        entries[name]=(8,zlib.crc32(data),len(data),co.compress(data)+co.flush())
        hashes[name]=sha(data)
    for index,source in enumerate((core,tactical)):
        with zipfile.ZipFile(source) as z,zipfile.ZipFile(originals[index]) as baseline:
            assert set(z.namelist())==set(baseline.namelist())
            for info in z.infolist():
                name=info.filename;data=z.read(name)
                assert data==baseline.read(name),name
                if name=='modinfo.json':
                    metadata=json.loads(data)
                    add('Integrations/'+('ScCsgoTactical' if index else 'ScCsgoKnives')+'.modinfo.json',data)
                    if index==0:core_metadata=metadata
                    else:tactical_metadata=metadata
                    continue
                if index and name=='ASSET_SOURCES.md':name='Integrations/ScCsgoTactical.ASSET_SOURCES.md'
                if name in entries:
                    assert hashes[name]==sha(data),'Conflicting member: '+name
                    continue
                entries[name]=(info.compress_type,info.CRC,info.file_size,raw_member(z,info));hashes[name]=sha(data)
    core_metadata['Version']='1.5.1'
    core_metadata['Name']='CS武器 · '+('512轻量总包' if lite else '全量总包')
    core_metadata['Description']='已整合CS枪械、完整武器资源、战术同伴、敌对小队、拆弹及手套，仅需安装本scmod。含1.5.0的平衡、材料和自制模型更新。玩家CT/T外观仍需另装NekoMeko Model1.1和Neorxna1.4。全量与轻量二选一；停用旧独立CS主包、资源包、战术拓展后再启用总包。'+('512轻量资源，与94.26 MB版相同画质；加强无损Deflate压缩。' if lite else '原始全量资源。')
    assert not core_metadata['Dependencies']
    add('modinfo.json',(json.dumps(core_metadata,ensure_ascii=False,indent=2)+'\n').encode('utf8'))
    adapter=ROOT/'src/ScCsgoBundle/bin/Release/net10.0/ScCsgoBundle.dll'
    assert adapter.stat().st_mtime>=max(p.stat().st_mtime for p in (ROOT/'src/ScCsgoBundle').glob('*.cs'))
    add('ScCsgoBundle.dll',adapter.read_bytes())
    membership=dict(version='1.5.1',core='1.5.0',tactical=tactical_metadata['Version'],edition=args.edition,
        sourcePackages=[dict(path=p.name,sha256=sha(p.read_bytes())) for p in originals])
    add('Integrations/ScCsgoBundle.json',json.dumps(membership,ensure_ascii=False,indent=2).encode('utf8'))
    text='''CS武器1.5.1单文件总包：直接导入本scmod，无需解压安装子包。
先退出世界并备份整个世界目录。全量和512轻量只启用一个；停用旧独立CS武器、CS资源、战术同伴及早期独立CS外观包。
包含主包1.5.0与战术1.4.0的全部玩法、资源及内置外观适配。布局v5/schema6/rules7和原ID不变。
总包加载完成后保留战术包的原身份，防止旧世界误报缺少战术拓展；不重复注册玩法。
CT/T玩家角色仍需另装NekoMeko Model1.1及Neorxna1.4，第三方Mod不包含在总包里。
轻量沿用512贴图及既有减面；加强压缩不会在94.26 MB版基础上进一步损失画质。
旧版1.0/1.2迁移保护保留。退回旧版时请恢复升级前世界备份，不直接用旧DLL打开升级后的存档。
发布验证是离线回归及原生加载诊断，不等同完整游戏、Android或全部Mod组合验收。
'''
    add('INSTALL.txt',text.encode('utf-8-sig'))
    marker=ET.Element('Resources',Version='1.10.4',Format='1',Edition='Optimized512' if lite else 'Full')
    for name,digest in sorted(hashes.items()):
        if name.startswith(('Assets/Textures/','Assets/Models/','Assets/Audio/')):ET.SubElement(marker,'File',Path=name,Sha256=digest)
    # Survivalcraft's native XmlReader accepts the standard IANA name "utf-8";
    # Python's shorthand "utf8" is rejected by the game when it loads a world.
    add('Assets/ScCsgoResources.xml',ET.tostring(marker,encoding='utf-8',xml_declaration=True))
    target=OUT/f'[API1.9]CS武器1.5.1-作者ZH667-{"512轻量" if lite else "全量"}总包.scmod'
    pending=target.with_suffix('.pending');write_archive(pending,entries)
    with zipfile.ZipFile(pending) as z:
        assert z.testzip() is None and set(z.namelist())==set(entries)
        for name,digest in hashes.items():assert sha(z.read(name))==digest,name
    pending.replace(target)
    result=dict(path=target.name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),entries=len(entries),
        membership=membership,compressionSources=[p.name for p in (core,tactical)],
        gameplayDlls={n:h for n,h in hashes.items() if n.endswith(('.dll','.bin'))},memberHashes=hashes)
    REPORT.mkdir(parents=True,exist_ok=True)
    (REPORT/(args.edition+'.json')).write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps({k:v for k,v in result.items() if k!='memberHashes'},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
