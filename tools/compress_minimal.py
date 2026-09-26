"""Stronger standard Deflate, verifying unchanged payloads. No installation/publishing."""
from pathlib import Path
import zipfile,subprocess,json,sys,hashlib,tempfile
from pack_single_scmods import raw_member,write_archive

r=Path(__file__).resolve().parents[1];s=r/'.tmp/minimal-130-20260926'
p=s/'candidate/[API1.9]CS武器1.3.0-极简包.scmod'
before=p.stat().st_size
# Fresh scratch avoids 7-Zip update mode retaining stale files from a prior build.
with tempfile.TemporaryDirectory(prefix='deflate-',dir=s) as scratch:
    temp=Path(scratch);d=temp/'input';d.mkdir()
    with zipfile.ZipFile(p) as z:
        assert len(z.namelist())==len(set(z.namelist())) and z.testzip() is None
        for i in z.infolist():
            target=d/i.filename;assert target.resolve().is_relative_to(d.resolve())
            target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(z.read(i))
        (temp/'files.txt').write_text('\n'.join(z.namelist())+'\n',encoding='utf8')
    with (s/'7zip.log').open('w',encoding='utf8') as log:
        subprocess.run(['C:/Program Files/7-Zip/7z.exe','a','-tzip','-mm=Deflate','-mx=9','-mfb=258','-mpass=15','-mmt=2','-scsUTF-8',str(temp/'strong.zip'),'@'+str(temp/'files.txt')],cwd=d,stdout=log,stderr=subprocess.STDOUT,check=True)
    entries={}
    with zipfile.ZipFile(p) as old,zipfile.ZipFile(temp/'strong.zip') as new:
        assert set(old.namelist())==set(new.namelist()) and new.testzip() is None
        for i in old.infolist():
            j=new.getinfo(i.filename);assert old.read(i)==new.read(j)
            selected=new if j.compress_size<i.compress_size else old;item=selected.getinfo(i.filename)
            entries[i.filename]=(item.compress_type,item.CRC,item.file_size,raw_member(selected,item))
    result=temp/'compressed.scmod';write_archive(result,entries)
    with zipfile.ZipFile(result) as z:
        assert z.testzip() is None and all(i.compress_type in [0,8] for i in z.infolist())
    result.replace(p)
report=json.loads((s/'package.json').read_text(encoding='utf8'))
report.update(sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size,under30MB=p.stat().st_size<30_000_000,losslessSaved=before-p.stat().st_size)
with zipfile.ZipFile(p) as z:report['largest']=sorted([(i.compress_size,i.filename) for i in z.infolist()],reverse=True)[:25]
(s/'package.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print({k:v for k,v in report.items() if k not in ['entries','largest']},flush=True)
