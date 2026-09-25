"""Verify final package/test identities and summarize 1.2.0 migration follow-up evidence."""
from pathlib import Path
import hashlib,json,zipfile
ROOT=Path(__file__).resolve().parents[1]
def sha(data):return hashlib.sha256(data).hexdigest()
def read(p):return json.loads((ROOT/p).read_text('utf-8-sig'))
def main():
    balance=read('.tmp/balance-check-153.json');inventory=read('.tmp/inventory-check-153.json')
    assert balance['failed']==inventory['failed']==0
    assert balance['coreSha256']==inventory['sha256'].lower()
    reports={'balance':{k:balance[k] for k in ['count','passed','failed','coreSha256']},
             'sushi_inventory':{k:inventory[k] for k in ['count','passed','failed']},
             'protected_load':[c for c in balance['checks'] if c['Name'].startswith('protected-load/')]}
    packages={}
    for edition in ('Full','Lite'):
        package=read(f'output/release-single-1.5.3/{edition}.json')
        assert sha((ROOT/'output'/package['path']).read_bytes())==package['sha256']
        assert package['gameplayDlls']['ScCsgoKnives.dll']==balance['coreSha256']
        packages[edition]={k:package[k] for k in ['path','bytes','sha256','gameplayDlls']}
        for kind in ('core','tactical','gate'):
            path=f'output/release-single-1.5.3/{kind}-{edition.lower()}'+('-check.json' if kind!='gate' else '.json')
            report=read(path);assert report['failed']==0,(path,report['failed'])
            if kind=='core':assert report['packageSha256']==package['sha256']
            if kind=='tactical':assert report['coreSha256']==report['dlcSha256']==package['sha256']
            reports[kind+'-'+edition]=dict(count=len(report['checks']),failed=0,report_sha256=sha((ROOT/path).read_bytes()))
    for dll in ['ScCsgoKnives.dll','ScCsgoTactical.dll','ScCsgoBundle.dll','Integrations/ScCsgoAppearance.bin']:
        assert packages['Full']['gameplayDlls'][dll]==packages['Lite']['gameplayDlls'][dll]
    baseline=Path(r'D:\下载\[API1.9]CS武器1.2.0-全量版.scmod')
    assert sha(baseline.read_bytes())=='b370ad7ff6c7cae0ec4abe584d8389bea813eb790b29dc3c2a4772adca184c95'
    with zipfile.ZipFile(baseline) as z:
        legacy=z.read('ScCsgoKnives.dll')
        assert legacy==(ROOT/'.tmp/balance-implementation-20260924/legacy/0/ScCsgoKnives.dll').read_bytes()
    provenance=read('tools/fixtures/migration-120-20260925/provenance.json')
    assert sha((ROOT/'tools/fixtures/migration-120-20260925/world7-guns.xml').read_bytes())==provenance['fixture_sha256']
    evidence=dict(date='2026-09-25',base_commit='9426484',layout=5,schema=6,rules=7,packages=packages,reports=reports,
                  source_fixture=provenance,original_120_dll_sha256=sha(legacy),recovered_missing_states=False,
                  compatibility='Verified-backup-first local-damage preservation. Healthy gun states unchanged; two preexisting model conflicts kept unusable. Allocation watermark reserves old orphan IDs.',
                  limits=['No installed-mod or original player-world writes','Original M249/M4A1-S state still requires a trustworthy pre-loss backup','Offline actual old/new DLL and native XML load/save paths; no full rendered game or Android acceptance'])
    (ROOT/'docs/release-single-1.5.3-evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps({k:v for k,v in reports.items() if k!='protected_load'},ensure_ascii=False,indent=2))
if __name__=='__main__':main()
