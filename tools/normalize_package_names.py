"""Prepare six ordinary-named packages from verified capacity deliveries; never install them."""
import json,subprocess,zipfile,zlib
from prepare_capacity_fix import ROOT,sha
from pack_single_scmods import raw_member,write_archive
from package_display import presentation

STAGE=ROOT/'.tmp/package-names-20260927'
def main():
    original=json.loads((ROOT/'output/release-capacity-20260927/manifest.json').read_text('utf8'))
    STAGE.mkdir(parents=True,exist_ok=True)
    reports={}
    for key,info in original['packages'].items():
        source=ROOT/'output'/info['file']
        if not source.exists():source=ROOT/'output/history-capacity-names-20260927/revision-labels'/info['file']
        assert sha(source.read_bytes())==info['sha256'],source
        old=ROOT/'output'/info['baseline']
        if sha(old.read_bytes())!=info['baselineSha256']:
            old=ROOT/'output/history-capacity-names-20260927/previous'/info['baseline']
        assert sha(old.read_bytes())==info['baselineSha256'],old
        target=STAGE/info['baseline']
        changed={};hashes={}
        with zipfile.ZipFile(source) as z:
            assert len(z.namelist())==len(set(z.namelist()))
            entries={e.filename:(e.compress_type,e.CRC,e.file_size,raw_member(z,e)) for e in z.infolist()}
            for name in ['modinfo.json','Integrations/ScCsgoKnives.modinfo.json']:
                if name not in entries:continue
                meta=json.loads(z.read(name));edition='lite' if key.endswith('lite') else 'full'
                meta['Name'],meta['Description']=presentation(meta['Version'],edition)
                data=json.dumps(meta,ensure_ascii=False,indent=2).encode('utf8')
                compressor=zlib.compressobj(9,zlib.DEFLATED,-15)
                entries[name]=(8,zlib.crc32(data),len(data),compressor.compress(data)+compressor.flush())
                changed[name]=data
            write_archive(target,entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and set(out.namelist())==set(z.namelist())
                for name in z.namelist():
                    expected=changed.get(name,z.read(name));assert out.read(name)==expected,name
                    if name not in changed:assert raw_member(out,out.getinfo(name))==raw_member(z,z.getinfo(name)),name
                    hashes[name]=sha(expected)
                for name in changed:
                    before=json.loads(z.read(name));after=json.loads(out.read(name))
                    assert {k:v for k,v in before.items() if k not in ['Name','Description']}=={k:v for k,v in after.items() if k not in ['Name','Description']}
                    assert not any(s in after['Name']+after['Description'] for s in ['schema','布局','扩容','修订','CAPACITY','最新','双向兼容','安1(3)'])
                display=json.loads(out.read('modinfo.json'))
        assert hashes['ScCsgoKnives.dll']==info['coreSha256']
        native=STAGE/(key+'-native.json')
        with (STAGE/(key+'-native.log')).open('w',encoding='utf8') as log:
            subprocess.run(['dotnet',str(ROOT/'tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll'),
                            '--world-resource-gate',str(target),str(native)],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,check=True)
        assert json.loads(native.read_text('utf8'))['failed']==0
        reports[key]=dict(file=target.name,sha256=sha(target.read_bytes()),bytes=target.stat().st_size,
                          coreSha256=info['coreSha256'],name=display['Name'],description=display['Description'],
                          previousFile=source.name,previousSha256=info['sha256'],
                          replacedSha256=info['baselineSha256'],changedEntries=list(changed),nativePassed=True)
        print(key,display['Name'],'PASS',flush=True)
    (STAGE/'manifest.json').write_text(json.dumps({'revision':'capacity-20260927','presentation':'ordinary-names-20260927',
        'packages':reports,'payloadUnchanged':True,'previousValidation':'output/release-capacity-20260927/manifest.json',
        'androidAcceptance':False},ensure_ascii=False,indent=2)+'\n','utf8')

if __name__=='__main__':main()
