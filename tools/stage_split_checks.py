"""Adapt the existing native resource/dependency runners to the full-catalogue split.

Changes concern package composition/availability assertions, never skipped assets.
"""
from pathlib import Path
import shutil
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926'
def write(p,t):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(t,encoding='utf8')
p=(R/'tools/MinimalCheck/Program.cs').read_text(encoding='utf8')
p=p.replace('long limit=args.Length==5?40_000_000:30_000_000;', 'long limit=40_000_000;')
p=p.replace('ScMinimalEdition.Enabled&&package.GetEntry', '!ScMinimalEdition.Enabled&&ScOptionalAgents.Split&&package.GetEntry')
p=p.replace('ScGunSkinCatalog.Available.Count()==13','ScGunSkinCatalog.Available.Count()==44')
p=p.replace('Integrations/ScMinimal.json','Integrations/ScSplit.json').replace('GetProperty("inspect")','GetProperty("inspection")')
p=p.replace('afterCounter.GetCreativeValues().Count()==48','afterCounter.GetCreativeValues().SequenceEqual(beforeCounter.GetCreativeValues())')
p=p.replace('new ScKnifeBlock().GetCreativeValues().Select(ScKnifeBlock.GetVariant).SequenceEqual(new[]{8,9})','new ScKnifeBlock().GetCreativeValues().Select(ScKnifeBlock.GetVariant).Distinct().Count()==22')
p=p.replace('ScWeaponCrafting.All.Count(e=>e.Knife)==2','ScWeaponCrafting.All.Count(e=>e.Knife)==22')
p=p.replace('var oldSpecs=(Array)', '''foreach(string name in oldResources.GetManifestResourceNames().Where(n=>n.EndsWith(".skin")||n.EndsWith(".parts"))){
    using var a=oldResources.GetManifestResourceStream(name);using var b=typeof(ScCsgoResources.ResourceMarker).Assembly.GetManifestResourceStream(name);
    using var am=new MemoryStream();using var bm=new MemoryStream();a.CopyTo(am);b.CopyTo(bm);Check(name+" unchanged Lite geometry",am.ToArray().SequenceEqual(bm.ToArray()));
}
string ObjNumbers(byte[] raw){return string.Join("\\n",System.Text.Encoding.UTF8.GetString(raw).Split('\\n').Select(l=>{
    var fields=l.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(fields.Length==0)return "";
    return fields[0] is "v" or "vn" or "vt"?fields[0]+" "+string.Join(" ",fields.Skip(1).Select(n=>BitConverter.SingleToInt32Bits(float.Parse(n,System.Globalization.CultureInfo.InvariantCulture)))):l.Trim();
}));}
foreach(var entry in package.Entries.Where(e=>e.FullName.EndsWith(".obj")))Check(entry.FullName+" exact decoded OBJ numbers and faces",ObjNumbers(Bytes(package,entry.FullName))==ObjNumbers(Bytes(baseline,entry.FullName)));
var oldSpecs=(Array)''')
write(S/'resource-check/Program.cs',p)
shutil.copyfile(R/'tools/PackageCheck/SwitchAnimationRegression.cs',S/'resource-check/SwitchAnimationRegression.cs')
proj='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/><Reference Include="ScCsgoKnives"><HintPath>../core/source/bin/Release/net10.0/ScCsgoKnives.dll</HintPath></Reference><Reference Include="ScCsgoResources"><HintPath>../resources/bin/Release/net10.0/ScCsgoResources.dll</HintPath></Reference></ItemGroup></Project>'''
write(S/'resource-check/SplitResourceCheck.csproj',proj)
p=(R/'tools/TacticalLoadCheck/Program.cs').read_text(encoding='utf8')
p=p.replace('assemblies[tactical].Length==1','assemblies[tactical].Select(a=>a.GetName().Name).Order().SequenceEqual(new[]{"ScCsgoTactical","ScCsgoVoice"})')
p=p.replace('else tactical.HandleAssembly(tacticalAssembly);','else {foreach(var a in assemblies[core])core.HandleAssembly(a);foreach(var a in assemblies[tactical])tactical.HandleAssembly(a);}')
p=p.replace('tactical.BlockTypes.Count>=4','core.BlockTypes.Count(t=>t.Name.StartsWith("ScTactical"))==4&&tactical.BlockTypes.Count==0')
write(S/'load-check/Program.cs',p)
shutil.copyfile(R/'tools/TacticalLoadCheck/TacticalLoadCheck.csproj',S/'load-check/TacticalLoadCheck.csproj')
# Reuse the already fixed historical runner without touching the peer's dirty sources.
(S/'compat-runner').mkdir(exist_ok=True)
for n in ['Program.cs','CompatibilityCheck.csproj']:
    shutil.copyfile(R/'.tmp/minimal-inspect40-130-20260926/compat-runner'/n,S/'compat-runner'/n)
print('Staged resource, load and historical compatibility runners')
