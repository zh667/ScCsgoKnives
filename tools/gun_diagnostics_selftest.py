import json,tempfile,unittest
from pathlib import Path
from summarize_gun_diagnostics import summarize

class DiagnosticsTests(unittest.TestCase):
    def test_summary_not_sample_counts(self):
        session={'type':'session','session':'test1','mode':'sampled'}
        summary={'type':'summary','session':'test1','gun':'ak47','shots':10,'pellets':10,'shotsWithGeometryHit':3,
                 'outcomes':{'head':1,'body':2,'terrain':4,'rangeEnd':3,'fallback':0},
                 'checks':{'coneViolations':0,'rangeViolations':0,'pelletMismatches':0},'timing':{'traceMsPerShot':.2}}
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/'Game.log'
            records=[session,{'type':'shot','session':'test1'},summary,{'type':'budget_exhausted'}]
            p.write_text('\n'.join('01:02 INFO: [GUN_DIAG] '+json.dumps(r) for r in records)+'\n[GUN_DIAG] {truncated','utf-8')
            result=summarize(p);s=result['sessions']['test1'];g=s['guns']['ak47']
            self.assertEqual(s['sampledDetails'],1);self.assertEqual(g['shots'],10)
            self.assertEqual(sum(g[k] for k in ('head','body','terrain','rangeEnd')),10)
            self.assertTrue(s['budgetExhausted']);self.assertEqual(result['malformedRecords'],1)
    def test_old_log_has_no_fabricated_telemetry(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/'Game.log';p.write_text('shot awp: target part=Body at 10 m','utf-8')
            self.assertEqual(summarize(p)['sessions'],{})

if __name__=='__main__':unittest.main()
