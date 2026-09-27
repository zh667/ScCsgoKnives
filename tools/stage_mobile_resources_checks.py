"""Extend existing edition gates with real Neo hooks and original-pixel texture checks."""
import json,shutil
from prepare_mobile_resources import ROOT,S,OLD
from prepare_codec_release import write
import stage_mobile_common_checks as previous
import stage_codec_checks as generic

def main():
    previous.S=S;previous.OLD=OLD;previous.main()
    for prefix in ["","full-check/"]:
        root=S/prefix
        refs={n:root/"assemblies"/(n+".dll") for n in ["ScCsgoKnives","ScCsgoResources","ScCsgoTactical"]}
        if not prefix:refs["ScCsgoResourceCodec"]=S/"assemblies/ScCsgoResourceCodec.dll"
        # Exact candidate's adapter, not the previous delivery.
        if not prefix:
            source=S/"appearance/source/bin/Release/net10.0/ScCsgoAppearance.dll"
            shutil.copyfile(source,root/"assemblies/ScCsgoAppearance.dll")
        appearance_refs=dict(refs,ScCsgoAppearance=root/"assemblies/ScCsgoAppearance.dll",
            neorxna=ROOT/".tmp/creature-audit-20260917/10/neorxna.dll",
            **{"sc-nekomekomodel":OLD/"runtime/AppearanceCheck/sc-nekomekomodel.dll"})
        folder=root/"checks/appearance"
        for p in (ROOT/"tools/AppearanceCheck").glob("*.cs"):write(folder/p.name,p.read_bytes())
        write(folder/"Check.csproj",generic.project("AppearanceCheck",appearance_refs).encode())
        folder=root/"checks/textures"
        write(folder/"Program.cs",(ROOT/"tools/TexturePreparationCheck/Program.cs").read_bytes())
        write(folder/"Check.csproj",generic.project("TexturePreparationCheck",refs).encode())
    # No need to rewrite resources. Staging retains final-payload comparison gates.
    print("Staged actual third-party hook reproduction and per-edition pixel comparisons.")

if __name__=="__main__":main()
