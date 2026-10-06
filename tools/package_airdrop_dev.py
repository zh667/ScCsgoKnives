"""Build a local full-edition review candidate; never replaces a release or installs it."""
import hashlib, json, zipfile
from pathlib import Path
from pack_single_scmods import raw_member, write_archive
from followup_140 import member

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/dev-temp/subworld-airdrops-20261006'
BASE=ROOT/'output/[API1.9]CS武器1.5.0-全量包.scmod'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    changes={'ScCsgoTactical.dll':ROOT/'src/ScCsgoTactical/bin/Release/net10.0/ScCsgoTactical.dll'}
    for name in ['ScTactical.xdb','Models/ScCsgoTactical/airdrop.glb','Textures/ScCsgoTactical/airdrop.png','Textures/ScCsgoTactical/airdrop_smoke.png']:
        changes['Assets/'+name]=ROOT/'src/ScCsgoTactical/Assets'/name
    dest=STAGE/'airdrop-dev-full.scmod'
    with zipfile.ZipFile(BASE) as z:
        entries={info.filename:(info.compress_type,info.CRC,info.file_size,raw_member(z,info)) for info in z.infolist() if info.filename not in changes}
    for n,p in changes.items(): entries[n]=member(p.read_bytes())
    write_archive(dest,entries)
    with zipfile.ZipFile(dest) as z:
        for n,p in changes.items(): assert z.read(n)==p.read_bytes(),n
    result=dict(candidate=str(dest),sha256=sha(dest),baselineSha256=sha(BASE),members={n:sha(p) for n,p in changes.items()},
        status='development only; no release gates or multiplayer identity stamp')
    (STAGE/'candidate.json').write_text(json.dumps(result,indent=2)+'\n','utf8');print(json.dumps(result))
if __name__=='__main__': main()
