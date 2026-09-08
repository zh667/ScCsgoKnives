"""Summarize [GUN_DIAG] JSON from Game.log. Never estimate totals from sampled lines."""
import argparse,json
from collections import defaultdict
from pathlib import Path

def summarize(path):
    sessions={};bad=0
    for line in path.read_text('utf-8-sig',errors='replace').splitlines():
        if '[GUN_DIAG] ' not in line:continue
        try:r=json.loads(line.split('[GUN_DIAG] ',1)[1])
        except json.JSONDecodeError:bad+=1;continue
        sid=r.get('session')
        if not sid:
            if r.get('type')=='budget_exhausted' and sessions:next(reversed(sessions.values()))['budgetExhausted']=True
            continue
        s=sessions.setdefault(sid,{'mode':None,'sampledDetails':0,'summaryBatches':0,'budgetExhausted':False,'guns':{}})
        if r['type']=='session':s['mode']=r['mode']
        elif r['type']=='shot':s['sampledDetails']+=1
        elif r['type']=='summary':
            s['summaryBatches']+=1
            g=s['guns'].setdefault(r['gun'],{'shots':0,'pellets':0,'shotsWithGeometryHit':0,'head':0,'body':0,'terrain':0,'rangeEnd':0,'fallback':0,'coneViolations':0,'rangeViolations':0,'pelletMismatches':0,'traceMsTotal':0})
            for k in ('shots','pellets','shotsWithGeometryHit'):g[k]+=r[k]
            for k in ('head','body','terrain','rangeEnd','fallback'):g[k]+=r['outcomes'][k]
            for k in ('coneViolations','rangeViolations','pelletMismatches'):g[k]+=r['checks'][k]
            g['traceMsTotal']+=r['timing']['traceMsPerShot']*r['shots']
    return {'note':'Totals use completed summary batches only. An unfinished final batch or exhausted diagnostic budget is not included. Geometry hits are not accuracy against an intended target or guaranteed health damage.',
            'malformedRecords':bad,'sessions':sessions}

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('log',type=Path);args=ap.parse_args()
    print(json.dumps(summarize(args.log),ensure_ascii=False,indent=2))
