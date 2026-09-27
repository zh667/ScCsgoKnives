"""Stage common gameplay/settings/audio fixes with untouched third-party binaries."""
import json
from pathlib import Path
import prepare_mobile_resources as previous
from prepare_codec_release import write,dump,sha
from PIL import Image

ROOT=previous.ROOT;S=ROOT/".tmp/gameplay-audio-130-20260927";OLD=ROOT/".tmp/mobile-resources-130-20260927"
BASELINES={"core":("轻量","2a7085f443c3ecc4045c7dda79d7bd792a1e54faabc67a6ed157cf5ae5fc1cb6"),
           "agents":("探员","e82a9afca9802a3f9febbbef8d5b5c081e5d862cea8439681f0425f6b9db68c6")}
FULL_SHA="f40e4690b4c570ee041c4f16c06dd00e3c1578a4856f79d70debb02cb0f7a2ab"

def main():
    previous.S=S;previous.OLD=OLD;previous.BASELINES=BASELINES;previous.FULL_SHA=FULL_SHA;previous.main()
    hashes=json.loads((S/"source-hashes.json").read_bytes())
    for prefix in ["","full/"]:
        dest=S/(prefix+"voice/source")
        for p in (ROOT/"src/ScCsgoVoice").glob("*.cs"):
            write(dest/p.name,p.read_bytes());hashes["ScCsgoVoice/"+p.name]=sha(p.read_bytes())
        core=(S/(prefix+"core/source/bin/Release/net10.0/ScCsgoKnives.dll")).as_posix()
        xml=f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoVoice</AssemblyName><Version>1.0.0</Version><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DebugType>none</DebugType></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/><Reference Include="ScCsgoKnives"><HintPath>{core}</HintPath></Reference></ItemGroup></Project>'
        write(dest/"ScCsgoVoice.csproj",xml.encode())
    dump(S/"source-hashes.json",hashes)
    source=ROOT.parent/"CSMCReverse/local_cs2_analysis/all_weapons/06_particles/textures/materials/particle/flames"
    frames=sorted(source.glob("flame_omni_seq0_*.png"),key=lambda p:int(p.stem.rsplit("_",1)[1]))
    assert len(frames)>=16
    chosen=[frames[round(i*(len(frames)-1)/15)] for i in range(16)]
    atlas=Image.new("RGBA",(1024,1024))
    for i,p in enumerate(chosen):
        image=Image.open(p).convert("RGBA")
        scale=min(248/image.width,248/image.height);image=image.resize((round(image.width*scale),round(image.height*scale)),Image.Resampling.LANCZOS)
        atlas.paste(image,((i%4)*256+(256-image.width)//2,(i//4)*256+(256-image.height)//2))
    folder=S/"new-assets";folder.mkdir(exist_ok=True)
    atlas.save(folder/"grenade_fireburst_atlas.png",optimize=True)
    write(ROOT/"src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/grenade_fireburst_atlas.png",(folder/"grenade_fireburst_atlas.png").read_bytes())
    atlas.resize((512,512),Image.Resampling.LANCZOS).save(folder/"grenade_fireburst_atlas.webp",quality=85,method=6,exact=True)
    dump(S/"fireburst-source.json",dict(sourceFrames=[dict(path=str(p),sha256=sha(p.read_bytes())) for p in chosen],
        conversion="CS2 flame_omni sequence: 16 padded tiles; Full 1024 PNG, Lite 512 WebP quality85. Visual only, not Source2 particle simulation.",
        assets={p.name:dict(bytes=p.stat().st_size,sha256=sha(p.read_bytes())) for p in folder.glob("grenade_fireburst_atlas.*")}))
    print("Staged voice and CS2 fireburst assets.")

if __name__=="__main__":main()
