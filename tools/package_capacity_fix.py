"""Write distinctly named capacity revision candidates; published archives are immutable inputs."""
import json,zipfile,zlib,xml.etree.ElementTree as ET
from prepare_capacity_fix import ROOT,STAGE,sha
from run_capacity_checks import DLLS
from pack_single_scmods import raw_member,write_archive
from package_display import presentation
NOTICE='''枪械扩容修订 capacity-20260927，布局6／schema7。保留全部原枪编号与属性，最多66558条实例记录，不回收旧编号。1023仍是空弹模板。
升级、换包之前请自行导出完整世界备份。启用本修订后，只能在本次扩容修订的1.0.0／1.2.0／1.3.0之间互换。
已传播的未更新兼容包不支持新格式，应安全拒绝；它们不会因版本号相同而自动获得支持。不要手动降布局或schema。
原始1.0/1.2未修订包、极简包及其它未验证版本不属于本次扩容互换范围。一个世界只启用一个CS核心。
1.3.0轻量核心继续使用原探员包（可选），本次不改资源。必须使用已修复玲兰共享库存适配的扩容核心，避免历史版本继续误分配。
扩容不会凭空补回已经删除的枪械记录。安1(3)需要配套的原记录恢复副本。
Windows原生离线验证不等于Android设备、完整游戏操作或任意第三方组合验收。没有自动备份。
'''
def main():
    baseline=json.loads((STAGE/'baselines.json').read_text('utf8'));packages={}
    matrix=json.loads((STAGE/'extended-matrix.json').read_text('utf8'));assert matrix['failed']==0
    assert json.loads((STAGE/'family.json').read_text('utf8'))['failed']==0
    for key,dll in DLLS.items():
        report=json.loads((STAGE/(key+'-check.json')).read_text('utf8'));assert report['failed']==0 and report['coreSha256'].lower()==sha(dll.read_bytes())
    for key,version,label in [('lite','1.3.0','轻量'),('full','1.3.0','全量'),('1.0.0-full','1.0.0','全量'),('1.0.0-lite','1.0.0','轻量'),('1.2.0-full','1.2.0','全量'),('1.2.0-lite','1.2.0','轻量')]:
        current=version=='1.3.0';sourceName=f'[API1.9]CS武器{version}-'+('' if current else '双向兼容-')+label+'包.scmod'
        source=STAGE/'baseline'/sourceName;assert sha(source.read_bytes())==baseline[sourceName]['sha256']
        dll=DLLS[key if current else version];core=dll.read_bytes();changes={'ScCsgoKnives.dll':core,'扩容修订说明.txt':NOTICE.encode('utf-8-sig')}
        with zipfile.ZipFile(source) as z:
            entries={e.filename:(e.compress_type,e.CRC,e.file_size,raw_member(z,e)) for e in z.infolist()};hashes={n:sha(z.read(n)) for n in z.namelist()}
            # Earlier actor-cache releases retained a stale resource manifest. Repair metadata
            # from the actual preserved archive bytes, including animation/mesh cache assets.
            marker=ET.fromstring(z.read('Assets/ScCsgoResources.xml'))
            for child in list(marker):
                if child.tag=='File':marker.remove(child)
            for name in sorted(z.namelist()):
                if name.startswith(('Assets/Textures/','Assets/Models/','Assets/Audio/')):
                    ET.SubElement(marker,'File',Path=name,Sha256=hashes[name])
            changes['Assets/ScCsgoResources.xml']=ET.tostring(marker,encoding='utf-8',xml_declaration=True)
            for name in ['modinfo.json','Integrations/ScCsgoKnives.modinfo.json','Integrations/ScCsgoBundle.json','Integrations/CompatibilityFamily.json','Integrations/ScSplit.json']:
                if name not in z.namelist():continue
                meta=json.loads(z.read(name))
                if name.endswith('modinfo.json'):
                    meta['Name'],meta['Description']=presentation(version,'lite' if label=='轻量' else 'full')
                elif name.endswith('ScCsgoBundle.json'):meta['coreSha256']=sha(core)
                else:meta.update(build_revision='capacity-20260927',gun_layout=6,gun_schema=7,capacity=66558,requires_capacity_revision=True)
                if name.endswith('CompatibilityFamily.json'):meta['core_sha256']=sha(core)
                changes[name]=json.dumps(meta,ensure_ascii=False,indent=2).encode('utf8')
            # Intermediate revision filenames are normalized at delivery by normalize_package_names.py.
            target=STAGE/'candidate'/sourceName.replace('.scmod','-扩容修订.scmod');target.parent.mkdir(exist_ok=True)
            for name,data in changes.items():
                compressor=zlib.compressobj(9,zlib.DEFLATED,-15);entries[name]=(8,zlib.crc32(data),len(data),compressor.compress(data)+compressor.flush());hashes[name]=sha(data)
            write_archive(target,entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and {n:sha(out.read(n)) for n in out.namelist()}==hashes
                for name in z.namelist():
                    if name not in changes:assert raw_member(out,out.getinfo(name))==raw_member(z,z.getinfo(name))
        packages[key]=dict(file=target.name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),coreSha256=sha(core),baseline=sourceName,baselineSha256=baseline[sourceName]['sha256'],changed=sorted(changes),entries=hashes)
        print(key,target.stat().st_size,flush=True)
    (STAGE/'packages.json').write_text(json.dumps(packages,ensure_ascii=False,indent=2),'utf8')
if __name__=='__main__':main()
