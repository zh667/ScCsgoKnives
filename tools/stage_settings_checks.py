from prepare_settings_rollback import ROOT,S,OLD
from prepare_codec_release import write
import stage_mobile_resources_checks as previous
import stage_codec_checks as generic
import json,shutil,zipfile

def main():
    packages=json.loads((S/"packages.json").read_bytes())
    with zipfile.ZipFile(S/"candidate"/packages["agents"]["file"]) as z:
        write(S/"appearance/source/bin/Release/net10.0/ScCsgoAppearance.dll",z.read("Integrations/ScCsgoAppearance.bin"))
    previous.S=S;previous.OLD=OLD;previous.main()
    for root in [S,S/"full-check"]:
        refs={n:root/"assemblies"/(n+".dll") for n in ["ScCsgoKnives","ScCsgoResources","ScCsgoTactical"]}
        if root==S:refs["ScCsgoResourceCodec"]=S/"assemblies/ScCsgoResourceCodec.dll"
        write(root/"checks/features/Program.cs",(ROOT/"tools/SettingsRollbackCheck/Program.cs").read_bytes())
        write(root/"checks/features/Check.csproj",generic.project("GameplayFollowupCheck",refs).encode())
    for p in (ROOT/"tools/PackageCheck").glob("*"):
        if p.suffix in [".cs",".csproj"]:write(S/"checks/package"/p.name,p.read_bytes())
    print("Staged new gameplay/UI/audio fixtures.")

if __name__=="__main__":main()
