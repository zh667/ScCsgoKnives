"""Summarize exact built-DLL, source and packaged checks without shipping local logs."""
import hashlib,json,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def read(p):return json.loads((ROOT/p).read_text('utf-8-sig'))
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
    matrix=read('.tmp/compatibility-check.json');balance=read('.tmp/balance-check-160.json')
    assert matrix['failed']==balance['failed']==0
    reports={'matrix':{k:matrix[k] for k in ['count','passed','failed','modules']},'latest_balance':{k:balance[k] for k in ['count','passed','failed','coreSha256']}}
    packages=[]
    for profile,edition,index in [('1.0.0','Full',0),('1.2.0','Full',1),('latest','Full',2),('latest','Lite',2)]:
        report=read(f'output/release-compatibility-1/{profile}-{edition}.json');p=ROOT/'output'/report['path']
        assert sha(p.read_bytes())==report['sha256']
        with zipfile.ZipFile(p) as z:assert sha(z.read('ScCsgoKnives.dll'))==matrix['modules'][index]['sha256']==report['coreSha256']
        packages.append(report)
    for key in ['100','120','160']:
        r=read(f'output/release-compatibility-1/native-{key}.json');assert r['failed']==0
        reports['native_'+key]={'count':len(r['checks']),'failed':0}
    for edition in ['full','lite']:
        r=read(f'output/release-compatibility-1/core-{edition}-check.json');assert r['failed']==0
        assert r['dllSha256']==balance['coreSha256']==matrix['modules'][2]['sha256']
        reports['packaged_'+edition]={'count':len(r['checks']),'failed':0,'packageSha256':r['packageSha256']}
    result={'family':1,'protocol':1,'layout':5,'schema':6,'rules':7,'base_commit':'6688492',
            'packages':packages,'reports':reports,'limitations':['Offline/native diagnostics, not full-game or Android acceptance',
            'Historical revisions share canonical 50-level persistence and safety code; they are not byte-identical originals',
            'Unmodified old binaries remain outside bidirectional family','Unknown future types/fields require preserving converters and all-prior-version release tests',
            'Already missing original gun records cannot be reconstructed without trustworthy backups']}
    (ROOT/'docs/release-compatibility-family-1-evidence.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(reports,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
