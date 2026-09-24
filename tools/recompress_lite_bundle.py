"""Recompress existing Lite scmods with standard Deflate, retaining every payload byte.

Requires 7-Zip. Originals, gameplay and source assets remain untouched.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

ROOT=Path(__file__).resolve().parents[1]


def sha(data):return hashlib.sha256(data).hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seven-zip',type=Path,default=Path('C:/Program Files/7-Zip/7z.exe'))
    args=parser.parse_args()
    out=ROOT/'output/release-bundle-1.5.0-lossless'
    stage=ROOT/'.tmp/bundle150-lossless'
    out.mkdir(parents=True,exist_ok=True)
    reports=[]
    names=['[API1.9]CS武器1.5.0-作者ZH667-512轻量版.scmod','[API1.9]CS战术同伴拓展1.4.0-作者ZH667-512轻量版.scmod']
    for index,name in enumerate(names):
        source_scmod=ROOT/'output'/name
        target=out/name
        if target.exists():
            with zipfile.ZipFile(source_scmod) as before,zipfile.ZipFile(target) as after:
                assert set(before.namelist())==set(after.namelist()) and after.testzip() is None
                for n in before.namelist():assert before.read(n)==after.read(n)
                count=len(before.namelist())
            reports.append(dict(path=name,bytesBefore=source_scmod.stat().st_size,bytesAfter=target.stat().st_size,
                sha256=sha(target.read_bytes()),identicalPayloadFiles=count))
            continue
        directory=stage/str(index);directory.mkdir(parents=True,exist_ok=True)
        # Validate archive paths before extraction; only extract into this task's stage.
        with zipfile.ZipFile(source_scmod) as z:
            assert z.testzip() is None
            for item in z.infolist():
                assert (directory/item.filename).resolve().is_relative_to(directory.resolve())
            z.extractall(directory)
            member_names=z.namelist()
        listing=stage/f'{index}-files.txt'
        listing.write_text('\n'.join(member_names)+'\n','utf8')
        pending=out/f'{index}.pending.zip'
        # Do not update a previous archive: its stale members could silently survive.
        if pending.exists():raise SystemExit('Pending archive exists; inspect it before retry: '+str(pending))
        print('Compressing '+name,flush=True)
        with (out/f'7zip-{index}.log').open('w',encoding='utf8') as log:
            subprocess.run([str(args.seven_zip),'a','-tzip','-mm=Deflate','-mx=9','-mfb=258','-mpass=15','-mmt=2','-scsUTF-8',str(pending),'@'+str(listing)],cwd=directory,stdout=log,stderr=subprocess.STDOUT,check=True)
        with zipfile.ZipFile(source_scmod) as before,zipfile.ZipFile(pending) as after:
            assert set(before.namelist())==set(after.namelist())
            assert after.testzip() is None
            for item in after.infolist():
                assert item.compress_type in (zipfile.ZIP_STORED,zipfile.ZIP_DEFLATED)
                assert before.read(item.filename)==after.read(item.filename),item.filename
        if pending.stat().st_size>=source_scmod.stat().st_size:
            raise SystemExit('Recompression did not save space: '+name)
        pending.replace(target)
        reports.append(dict(path=name,bytesBefore=source_scmod.stat().st_size,bytesAfter=target.stat().st_size,
            sha256=sha(target.read_bytes()),identicalPayloadFiles=len(member_names)))
        print(json.dumps(reports[-1],ensure_ascii=False),flush=True)
    report=dict(bytesBefore=sum(r['bytesBefore'] for r in reports),bytesAfter=sum(r['bytesAfter'] for r in reports),packages=reports,
        algorithm='standard ZIP Deflate; 7-Zip mx9/fb258/pass15; all uncompressed member bytes identical')
    (out/'recompression.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(report,ensure_ascii=False,indent=2),flush=True)


if __name__=='__main__':main()
