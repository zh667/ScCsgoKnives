"""Stage immutable compatibility runner and published baselines without modifying peer tools.

The historical runner assumed Tactical types always lived in older core DLLs.
For bundles, actually resolve and check these types in their companion DLL instead.
"""
from pathlib import Path
import subprocess, zipfile, hashlib, json, os

root=Path(__file__).resolve().parents[1]
stage=root/os.environ.get('SC_MINIMAL_STAGE','.tmp/minimal-130-20260926')
out=stage/'compat-runner';out.mkdir(parents=True,exist_ok=True)
ref='5cd70c1'
source=subprocess.check_output(['git','show',ref+':tools/CompatibilityCheck/Program.cs'],cwd=root).decode('utf-8')
source=source.replace('if(mod==latest&&name.StartsWith("ScTactical"))continue;', '// Check core or actual bundled Tactical assembly, including Mini inert types.')
source=source.replace('readonly Assembly assembly;', 'readonly Assembly assembly;readonly Context context;Assembly tactical;')
source=source.replace('assembly=new Context(Path).LoadFromAssemblyPath(Path);', 'context=new Context(Path);assembly=context.LoadFromAssemblyPath(Path);')
source=source.replace('public Type Type(string n)=>assembly.GetType("Game."+n,true);', '''public Type Type(string n){
        var type=assembly.GetType("Game."+n);if(type!=null)return type;
        string companion=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path),"ScCsgoTactical.dll");
        if(System.IO.File.Exists(companion))return (tactical??=context.LoadFromAssemblyPath(companion)).GetType("Game."+n,true);
        throw new TypeLoadException(n);
    }''')
assert 'if(mod==latest&&' not in source and 'tactical??=' in source
(out/'Program.cs').write_text(source,encoding='utf-8')
(out/'CompatibilityCheck.csproj').write_bytes(subprocess.check_output(['git','show',ref+':tools/CompatibilityCheck/CompatibilityCheck.csproj'],cwd=root))
provenance={'runnerBase':ref,'runnerSha256':hashlib.sha256(source.encode()).hexdigest(),'packages':{}}
for key,name in [('full','全量'),('lite','轻量'),('mini','极简')]:
    path=(stage/'candidate' if key=='mini' else root/'output')/f'[API1.9]CS武器1.3.0-{name}包.scmod'
    directory=stage/'compat-baselines'/key;directory.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(path) as z:
        for n in ['ScCsgoKnives.dll','ScCsgoResources.dll','ScCsgoTactical.dll']:
            if n in z.namelist():(directory/n).write_bytes(z.read(n))
    provenance['packages'][key]=hashlib.sha256(path.read_bytes()).hexdigest()
(stage/'compat-runner-provenance.json').write_text(json.dumps(provenance,indent=2)+'\n',encoding='utf-8')
