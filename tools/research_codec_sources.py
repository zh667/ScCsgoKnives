"""Capture primary upstream documentation and versions for the codec study."""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import hashlib,json,urllib.request
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/resource-codecs-20260926'
REPOS=['zeux/meshoptimizer','nfrechette/acl','guillaumeblanc/ozz-animation',
       'oleg-st/ZstdSharp','google/brotli','Blosc/c-blosc2','facebook/zstd']
def fetch(url):
    req=urllib.request.Request(url,headers={'User-Agent':'ScCsgo-codec-research'})
    return urllib.request.urlopen(req,timeout=40).read()
def capture(repo):
    api='https://api.github.com/repos/'+repo
    meta=json.loads(fetch(api));branch=meta['default_branch']
    commit=json.loads(fetch(api+'/commits/'+branch))['sha']
    names=['README.md','README.rst','README']
    for name in names:
        url=f'https://raw.githubusercontent.com/{repo}/{commit}/{name}'
        try: data=fetch(url);break
        except urllib.error.HTTPError as e:
            if e.code!=404:raise
    else:raise ValueError('README missing: '+repo)
    target=STAGE/'sources'/(repo.replace('/','-')+'-'+name)
    target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
    row=dict(repo=repo,branch=branch,commit=commit,url=url,
             license=(meta.get('license') or {}).get('spdx_id'),
             readme=str(target.relative_to(STAGE)),sha256=hashlib.sha256(data).hexdigest())
    print(json.dumps(row),flush=True);return row

def capture_details(rows):
    commits={row['repo']:row['commit'] for row in rows}
    targets=[
        ('nfrechette/acl','docs/creating_a_raw_track_list.md'),
        ('nfrechette/acl','docs/compressing_raw_tracks.md'),
        ('nfrechette/acl','docs/decompressing_a_track_list.md'),
        ('nfrechette/acl','docs/algorithm_uniformly_sampled.md'),
        ('oleg-st/ZstdSharp','src/ZstdSharp/Decompressor.cs'),
    ]
    details=[]
    for repo,path in targets:
        url=f'https://raw.githubusercontent.com/{repo}/{commits[repo]}/{path}'
        data=fetch(url)
        local=STAGE/'sources'/(repo.replace('/','-')+'-'+path.replace('/','-'))
        local.write_bytes(data)
        details.append(dict(repo=repo,path=path,url=url,sha256=hashlib.sha256(data).hexdigest()))
    (STAGE/'source-details.json').write_text(json.dumps(details,indent=2),'utf8')

if __name__=='__main__':
    with ThreadPoolExecutor(max_workers=4) as pool:rows=list(pool.map(capture,REPOS))
    (STAGE/'sources.json').write_text(json.dumps(rows,indent=2),'utf8')
    capture_details(rows)
