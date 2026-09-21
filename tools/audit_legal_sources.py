"""Read official policy pages and record dated excerpts, without contacting rights holders."""
from pathlib import Path
from html.parser import HTMLParser
from datetime import datetime,timezone
import hashlib,json,re,urllib.request,zipfile
ROOT=Path(__file__).resolve().parents[1]
class Text(HTMLParser):
    def __init__(self):super().__init__();self.hide=0;self.parts=[]
    def handle_starttag(self,tag,attrs):
        if tag in ('script','style'):self.hide+=1
    def handle_endtag(self,tag):
        if tag in ('script','style'):self.hide=max(0,self.hide-1)
    def handle_data(self,data):
        if not self.hide:self.parts.append(data)
sources=[
 ('steam-ssa','https://store.steampowered.com/subscriber_agreement/',[
  ('personal-use','Valve hereby grants',800),('developer-tools','C. License to Use Valve Developer Tools',2000),('fan-art','D. License to Use Valve Game Content in Fan Art.',1700),('ownership','F. Ownership of Content and Services',850),('restrictions','G. Restrictions on Use of Content and Services',2300)]),
 ('source-mods','https://partner.steamgames.com/doc/sdk/uploading/distributing_source_engine',[
  ('guidelines','Our expectation for mods',2300),('valve-ip','Can I use Valve IP in my Source Mod?',520),('other-games','What about mods for games on Steam that were not developed by Valve?',420)]),
 ('video-policy','https://store.steampowered.com/video_policy',[
  ('asset-distribution',"We’re not fine",500),('asset-distribution-ascii',"We're not fine",500)]),
 ('ea-content','https://help.ea.com/en/help/faq/how-to-request-permission-for-ea-games-content/',[
  ('noncommercial',"Don’t sell your content",750),('data-mining','data mining',480),('mixing',"Don’t combine our game content",1050),('third-party','We do not provide you with any permission',450)])]
report={'checkedAt':datetime.now(timezone.utc).isoformat(),'scenario':'User confirmed free public release, no paid content.','sources':[]}
for key,url,targets in sources:
    with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'Mozilla/5.0'}),timeout=35) as r:raw=r.read();final=r.url
    parser=Text();parser.feed(raw.decode('utf8'));text=re.sub(r'\s+',' ',' '.join(parser.parts)).strip()
    excerpts={}
    for label,term,count in targets:
        at=text.find(term)
        if at>=0:excerpts[label]=text[max(0,at-60):at+count]
    assert excerpts,key
    report['sources'].append({'id':key,'url':url,'resolvedUrl':final,'htmlSha256':hashlib.sha256(raw).hexdigest(),'excerpts':excerpts})
    print(key,list(excerpts))
baseline=ROOT/'output/[API1.9]CS武器1.4.8-作者ZH667-全量版.scmod'
if not baseline.exists():
    manifest=json.loads((ROOT/'docs/gloves-1.4.9-installation.json').read_text('utf-8-sig'))
    baseline=next(Path(m['Archived']) for m in manifest['Archived'] if Path(m['Original'])==baseline)
with zipfile.ZipFile(baseline) as z:
    report['currentPackageBfAudio']=[n for n in z.namelist() if 'bf1_' in n]
report['repository']={'url':'https://github.com/zh667/ScCsgoKnives','visibility':'PUBLIC','verifiedWith':'gh repo view --json visibility,isPrivate,url'}
(ROOT/'docs/legal-sources-2026-09-21.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
