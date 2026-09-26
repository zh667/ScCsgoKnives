"""Strong standard ZIP Deflate with exact payload and native-reader verification later."""
from pathlib import Path
import zipfile,subprocess,json,hashlib,tempfile
from pack_single_scmods import raw_member,write_archive
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926'
reports=json.loads((S/'packages.json').read_bytes())
for owner,row in reports.items():
    p=S/'candidate'/row['file'];before=p.stat().st_size
    with tempfile.TemporaryDirectory(prefix='deflate-',dir=S) as scratch:
        temp=Path(scratch);d=temp/'input';d.mkdir()
        entries={};changed=[];cachePath=S/'compression-cache'/p.name
        cached=zipfile.ZipFile(cachePath) if cachePath.exists() else None
        with zipfile.ZipFile(p) as z:
            for i in z.infolist():
                if cached and i.filename in cached.namelist() and cached.read(i.filename)==z.read(i):
                    previous=cached.getinfo(i.filename);selected=cached if previous.compress_size<i.compress_size else z;item=selected.getinfo(i.filename)
                    entries[i.filename]=(item.compress_type,item.CRC,item.file_size,raw_member(selected,item));continue
                target=d/i.filename;assert target.resolve().is_relative_to(d.resolve())
                target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(z.read(i));changed.append(i.filename)
            (temp/'files.txt').write_text('\n'.join(changed)+'\n',encoding='utf8')
        if cached:cached.close()
        if changed:
            with (S/(owner+'-7zip.log')).open('w',encoding='utf8') as log:
                subprocess.run(['C:/Program Files/7-Zip/7z.exe','a','-tzip','-mm=Deflate','-mx=9','-mfb=258','-mpass=15','-mmt=2','-scsUTF-8',str(temp/'strong.zip'),'@'+str(temp/'files.txt')],cwd=d,stdout=log,stderr=subprocess.STDOUT,check=True)
        if changed:
          with zipfile.ZipFile(p) as old,zipfile.ZipFile(temp/'strong.zip') as new:
            assert set(changed)==set(new.namelist()) and new.testzip() is None
            for name in changed:
                i=old.getinfo(name)
                j=new.getinfo(i.filename);assert old.read(i)==new.read(j)
                selected=new if j.compress_size<i.compress_size else old;item=selected.getinfo(i.filename)
                entries[i.filename]=(item.compress_type,item.CRC,item.file_size,raw_member(selected,item))
        result=temp/'compressed.scmod';write_archive(result,entries)
        with zipfile.ZipFile(result) as z:assert z.testzip() is None
        # Keep the byte-equivalent precompression archive for Game.ZipArchive checks.
        previous=S/(owner+'-before-compression.scmod')
        previous.write_bytes(p.read_bytes())
        result.replace(p)
    row.update(bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest(),losslessSaved=before-p.stat().st_size)
    print(owner,row['bytes'],'saved',row['losslessSaved'],flush=True)
(S/'packages.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
