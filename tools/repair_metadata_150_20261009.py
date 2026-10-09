"""Prepare verified metadata-only candidates from the just-delivered 1.5.0 family."""
import hashlib,json,zipfile,zlib
from pathlib import Path
from pack_single_scmods import raw_member,write_archive
from release_metadata import metadata_members

SOURCE=Path(__file__).resolve().parents[1]
ROOT=Path('E:/projects/ScCsgoKnives')
STAGE=ROOT/'.tmp/dev-temp/metadata-150-20261009'
PINS={'全量':'26878682e03a4bd78d428094baa050267db62303a92c6c3cca1b6d41b1df2a30',
      '轻量':'f7d2a75c1ae4b6eb8bf62b033cb98dfe35091862082b1a911475e77c88334429',
      '探员':'8b5aa8acd8a2f95802a81c7b6c6554444f0a8baa1a7bc1f50b7d5c1a6fb99dfe',
      '死亡竞赛':'0f52593464f96bd33a492bbe0a860dcc72f42a0ca8da8e822ee7ab0e31f83a8f'}
def sha(b):return hashlib.sha256(b).hexdigest()
def member(b):
    c=zlib.compressobj(9,zlib.DEFLATED,-15);return (8,zlib.crc32(b),len(b),c.compress(b)+c.flush())
def main():
    STAGE.mkdir(parents=True,exist_ok=True);records={}
    manifest=json.loads((ROOT/'output/release-1.5.0/manifest.json').read_text('utf8'))
    for label,expected in PINS.items():
        name=manifest['packages'][label]['file'];old=ROOT/'output'/name;dest=STAGE/name
        assert sha(old.read_bytes())==expected and not dest.exists()
        with zipfile.ZipFile(old) as z:
            changes=metadata_members(z,label,SOURCE)
            assert changes and all(n=='INSTALL.txt' or n=='modinfo.json' or n.endswith('.modinfo.json') or n=='Integrations/ScCsgoBundle.json' for n in changes)
            entries={i.filename:(i.compress_type,i.CRC,i.file_size,raw_member(z,i)) for i in z.infolist()}
            entries.update({n:member(b) for n,b in changes.items()});write_archive(dest,entries)
            unchanged=0;assemblies={}
            with zipfile.ZipFile(dest) as new:
                assert set(z.namelist())==set(new.namelist()) and new.testzip() is None
                assert not metadata_members(new,label,SOURCE),'Metadata normalization must be idempotent'
                for n in z.namelist():
                    if n not in changes:
                        assert z.read(n)==new.read(n) and raw_member(z,z.getinfo(n))==raw_member(new,new.getinfo(n));unchanged+=1
                    else:assert new.read(n)==changes[n]
                    if n.endswith(('.dll','.bin')):assemblies[n]=sha(new.read(n))
                if 'Integrations/ScCsgoBundle.json' in new.namelist():
                    bundle=json.loads(new.read('Integrations/ScCsgoBundle.json'))
                    assert bundle['coreSha256']==sha(new.read('ScCsgoKnives.dll'))
                    original=json.loads(z.read('Integrations/ScCsgoBundle.json'))
                    assert bundle['sourcePackages']==original['sourcePackages'] and bundle['sourceBundleSha256']==original['sourceBundleSha256']
        records[label]=dict(file=name,sha256=sha(dest.read_bytes()),bytes=dest.stat().st_size,baselineSha256=expected,
            changed={n:sha(b) for n,b in changes.items()},unchangedMembers=unchanged,unchangedAssemblies=assemblies)
        print(label,records[label]['sha256'],list(changes),flush=True)
    (STAGE/'metadata-packages.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n','utf8')
    (STAGE/'input-sources.json').write_text(json.dumps({str(p.relative_to(SOURCE)):sha(p.read_bytes()) for p in
        [SOURCE/'tools/release_metadata.py',SOURCE/'tools/repair_metadata_150_20261009.py',*[SOURCE/'src'/s/'modinfo.json' for s in ['ScCsgoKnives','ScCsgoTactical','ScCsgoDeathmatch']]]},indent=2)+'\n','utf8')
if __name__=='__main__':main()
