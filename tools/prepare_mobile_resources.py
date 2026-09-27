"""Shared own-code repair; immutable Neo/NMM references and resource baselines."""
from pathlib import Path
import json,shutil,zipfile
import prepare_mobile_common as common
import prepare_codec_release as prep

ROOT=prep.ROOT
S=ROOT/".tmp/mobile-resources-130-20260927"
OLD=ROOT/".tmp/mobile-common-130-20260927"
BASELINES={
    "core":("轻量","90ac125093a3acb0513b011afe9162dce22eb51bc9bd9c53a93bdf1143f33ab5"),
    "agents":("探员","aa58b153776c36d7d3dcde72782412f67a47e69f91ef37a1951b2683cb8bcd59"),
}
FULL_SHA="7a9b014a9a732ad48b2cba44ab4532e584c2ec1636e892ea3ab86e6abf56904d"

def main():
    common.S=S;common.OLD=OLD;common.BASELINES=BASELINES;common.FULL_SHA=FULL_SHA
    common.main()
    # Build the same optional adapter against each edition's type ownership.
    for prefix in ["","full/"]:
        directory=S/(prefix+"appearance/source")
        for p in (ROOT/"src/ScCsgoAppearance").glob("*.cs"):
            prep.write(directory/p.name,p.read_bytes())
        refs={"ScCsgoKnives":S/(prefix+"core/source/bin/Release/net10.0/ScCsgoKnives.dll"),
              "ScCsgoTactical":S/(prefix+"agents/source/bin/Release/net10.0/ScCsgoTactical.dll"),
              "neorxna":ROOT/".tmp/creature-audit-20260917/10/neorxna.dll",
              "sc-nekomekomodel":ROOT/".tmp/mobile-common-130-20260927/runtime/AppearanceCheck/sc-nekomekomodel.dll"}
        items="".join(f'<Reference Include="{n}"><HintPath>{p.as_posix()}</HintPath></Reference>' for n,p in refs.items())
        xml=f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoAppearance</AssemblyName><Version>1.2.1</Version><AssemblyVersion>1.1.0.0</AssemblyVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DebugType>none</DebugType></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{items}</ItemGroup></Project>'
        prep.write(directory/"ScCsgoAppearance.csproj",xml.encode())
    hashes=json.loads((S/"source-hashes.json").read_bytes())
    for p in (ROOT/"src/ScCsgoAppearance").glob("*.cs"):hashes["ScCsgoAppearance/"+p.name]=prep.sha(p.read_bytes())
    prep.dump(S/"source-hashes.json",hashes)
    prep.dump(S/"third-party.json",{str(p.relative_to(ROOT)):prep.sha(p.read_bytes()) for p in refs.values() if p.name in ["neorxna.dll","sc-nekomekomodel.dll"]})
    print("Staged own adapters; external binaries are read-only references.")

if __name__=="__main__":main()
