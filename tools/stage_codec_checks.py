"""Adapt full native split checks to decoded resources, preserving every assertion."""
from prepare_codec_release import ROOT, S, write, dump, sha
import io, json, shutil, subprocess, zipfile
from PIL import Image

OLD = ROOT / ".tmp/split-lite-130-20260926"

def project(name, refs):
    references = ''.join(f'<Reference Include="{n}"><HintPath>{p.as_posix()}</HintPath></Reference>' for n,p in refs.items())
    return f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><AssemblyName>{name}</AssemblyName></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{references}</ItemGroup></Project>'

def junction(path, target):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists(): assert path.resolve() == target.resolve(); return
    script=S/"junction.ps1"
    script.write_text('param([string]$Link,[string]$Target)\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n',encoding="utf8")
    subprocess.run(["pwsh","-NoProfile","-File",str(script),str(path),str(target)],check=True)

def main():
    entries={}
    packages=json.loads((S/"packages.json").read_bytes())
    for row in packages.values():
        with zipfile.ZipFile(S/"candidate"/row["file"]) as z:
            for n in z.namelist():
                data=z.read(n)
                if n in entries and entries[n]!=data: assert not n.startswith("Assets/"),n
                entries[n]=data
    fixture=S/"fixture"
    for n,data in entries.items():
        if not n.startswith("Assets/"):continue
        target=fixture/"src/ScCsgoKnives"/n;write(target,data)
        if target.suffix==".webp":
            image=Image.open(io.BytesIO(data));image.load();image.save(target.with_suffix(".png"),format="PNG")
    for name in ["ScCsgoTactical","ScCsgoAppearance"]: junction(fixture/f"src/{name}/Assets",fixture/"src/ScCsgoKnives/Assets")
    junction(fixture/".tmp/nmm-player-appearance-audit-20260920",ROOT/".tmp/nmm-player-appearance-audit-20260920")
    junction(fixture/"tools",ROOT/"tools")
    with zipfile.ZipFile(S/"fixture-union.scmod","w",zipfile.ZIP_STORED) as z:
        for n,b in entries.items():z.writestr(n,b)
    refs={n[:-4]:S/"assemblies"/n for n in ["ScCsgoKnives.dll","ScCsgoResources.dll","ScCsgoTactical.dll","ScCsgoResourceCodec.dll"]}
    for n in refs: write(refs[n],entries[n+".dll"])
    p=(OLD/"resource-check/Program.cs").read_text("utf8")
    p=p.replace("typeof(ScCsgoResources.ResourceMarker).Assembly.GetManifestResourceStream(", "Resource(")
    p=p.replace("long limit=40_000_000;", 'Stream Resource(string name)=>ScResourceCompression.Open(typeof(ScCsgoResources.ResourceMarker).Assembly.GetManifestResourceStream(name),name);\nlong limit=40_000_000;')
    write(S/"checks/resources/Program.cs",p.encode())
    write(S/"checks/resources/SwitchAnimationRegression.cs",(ROOT/"tools/PackageCheck/SwitchAnimationRegression.cs").read_bytes())
    write(S/"checks/resources/Check.csproj",project("SplitResourceCheck",refs).encode())
    p=(ROOT/"tools/NpcWeaponCheck/Program.cs").read_text("utf8")
    p=p.replace('input.CopyTo(bytes);Check(asset+" packaged native geometry"', 'using var decoded=ScResourceCompression.Open(input,asset,leaveOpen:true);decoded.CopyTo(bytes);Check(asset+" packaged native geometry"')
    p=p.replace('info.SetContentStream(new MemoryStream(data.ToArray()));',
        'MemoryStream packagedStream;using(var input=package.GetEntry("Assets/"+ScNpcWeaponGeometry.PathFor(asset,legacy)).Open()){packagedStream=new MemoryStream();input.CopyTo(packagedStream);packagedStream.Position=0;info.SetContentStream(packagedStream);}')
    p=p.replace('foreach(int size in new[]{0,4,100,(int)data.Length-1})', '''var cached=ScNpcWeaponGeometry.For(asset,legacy);long consumed=packagedStream.Position;
    foreach(int squad in new[]{3,5})for(int member=0;member<squad;member++)Check(asset+" repeated squad cached "+squad+"/"+member,ReferenceEquals(cached,ScNpcWeaponGeometry.For(asset,legacy))&&packagedStream.CanRead&&packagedStream.Position==consumed);
    ScNpcWeaponGeometry.Clear();byte[] broken=packagedStream.ToArray();broken[broken.AsSpan().StartsWith("SCZSTD01"u8)?16:0]^=255;
    var damagedInfo=new ContentInfo(ScNpcWeaponGeometry.PathFor(asset,legacy));damagedInfo.SetContentStream(new MemoryStream(broken));ContentManager.Add(damagedInfo);bool noFallback=false;
    try{ScNpcWeaponGeometry.For(asset,legacy);}catch(Exception e)when(e is ScResourceCodecException or InvalidDataException){noFallback=true;}
    Check(asset+" corrupt cache refuses expensive fallback",noFallback);
    var exhaustedInfo=new ContentInfo(ScNpcWeaponGeometry.PathFor(asset,legacy));exhaustedInfo.SetContentStream(new ExhaustedStream());ContentManager.Add(exhaustedInfo);bool noMemoryFallback=false;
    try{ScNpcWeaponGeometry.For(asset,legacy);}catch(OutOfMemoryException){noMemoryFallback=true;}
    Check(asset+" memory exhaustion refuses expensive fallback",noMemoryFallback);
    packagedStream.Position=0;ContentManager.Add(info);Check(asset+" repaired cache retries",ScNpcWeaponGeometry.For(asset,legacy).Groups.Length==copy.Groups.Length);
    foreach(int size in new[]{0,4,100,(int)data.Length-1})''')
    write(S/"checks/npc/Program.cs",p.encode())
    with (S/"checks/npc/Program.cs").open("a",encoding="utf8") as f:
        f.write('\nsealed class ExhaustedStream:MemoryStream { public ExhaustedStream():base(new byte[100]){} public override int Read(byte[] b,int o,int n)=>throw new OutOfMemoryException("simulated"); public override int Read(Span<byte> b)=>throw new OutOfMemoryException("simulated"); }\n')
    write(S/"checks/npc/Check.csproj",project("NpcWeaponCheck",refs).encode())
    # Bind the actor check to the actual packaged tactical implementation, not a source copy.
    write(S/"checks/actor/Program.cs",(ROOT/"tools/ActorLoadCheck/Program.cs").read_bytes())
    write(S/"checks/actor/Check.csproj",project("ActorLoadCheck",refs).encode())
    for name in ["load-check","compat-runner"]:
        for p in (OLD/name).glob("*"):
            if p.suffix in [".cs",".csproj"]:write(S/"checks"/name/p.name,p.read_bytes())
    p=(ROOT/"tools/SplitCheck/Program.cs").read_text("utf8")
    p=p.replace('Check("wrong core gets explicit package requirement",refused);', '''Check("wrong core gets explicit package requirement",refused);
        var oldCore=Assembly.Load(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(args[0]),"../compat-baselines/old-split/ScCsgoKnives.dll")));
        ModsManager.Dlls[dll.FullName]=oldCore;refused=false;
        try{tactical.GetType("Game.ScSplitAgentMarker").GetMethod("ValidateCore").Invoke(null,null);}catch(TargetInvocationException e){refused=e.InnerException is InvalidOperationException;}finally{ModsManager.Dlls[dll.FullName]=dll;}
        Check("old same-version split core refuses new codec addon",refused);''')
    write(S/"checks/split/Program.cs",p.encode())
    write(S/"checks/split/Check.csproj",project("SplitCheck",{}).encode())
    # Existing appearance runner already performs native lifecycle tests; only replace
    # packaged DLL inputs and declare the new isolated dependency for host resolution.
    for tool in ["AppearanceCheck"]:
        dest=S/"runtime"/tool;shutil.copytree(OLD/"runtime"/tool,dest,dirs_exist_ok=True)
        for n in refs:write(dest/(n+".dll"),entries[n+".dll"])
        write(dest/"ScCsgoAppearance.dll",entries["Integrations/ScCsgoAppearance.bin"])
        deps=json.loads((dest/(tool+".deps.json")).read_bytes())
        framework=next(iter(deps["targets"]))
        deps["targets"][framework]["ScCsgoResourceCodec/0.8.8"]={"runtime":{"ScCsgoResourceCodec.dll":{"assemblyVersion":"0.8.8.0","fileVersion":"0.8.8.0"}}}
        deps["libraries"]["ScCsgoResourceCodec/0.8.8"]={"type":"reference","serviceable":False,"sha512":""}
        dump(dest/(tool+".deps.json"),deps)
    for key in ["full","lite","mini"]:
        shutil.copytree(OLD/"compat-baselines"/key,S/"compat-baselines"/key,dirs_exist_ok=True)
    for n in refs:write(S/"compat-baselines/new"/(n+".dll"),entries[n+".dll"])
    with zipfile.ZipFile(S/"baseline"/packages["core"]["file"]) as z:
        for n in ["ScCsgoKnives.dll","ScCsgoResources.dll"]:write(S/"compat-baselines/old-split"/n,z.read(n))
    dump(S/"fixture-hashes.json",{n:sha(b) for n,b in entries.items() if n.startswith("Assets/") or n.endswith((".dll",".bin"))})
    print("Prepared exact packaged fixture, native resources/render/load/compatibility checks.")

if __name__=="__main__":main()
