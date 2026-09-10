from pathlib import Path
import sys,subprocess
import audit_requested_cs2_finishes as a
root=Path(__file__).resolve().parents[1]/'.tmp/finish33-inputs'
index=a.vpk.read_vpk_index()
paths=[p for p in index if any(x in p for x in ('customization/pist_glock18/','customization/shot_mag7/')) and p.endswith(('.vmat_c','.vtex_c'))]
for p in paths:a.vpk.extract_entry(p,index[p],root/'raw'/p)
print('input paths',len(paths),flush=True)
subprocess.run(['E:/CSMCReverse-Tools/ValveResourceFormat/CLI/bin/Release/Source2Viewer-CLI.exe','-i',str(root/'raw'),'-o',str(root/'decoded'),'--recursive','-d'],check=True)
