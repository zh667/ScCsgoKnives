from prepare_feedback_fixes import ROOT,S,OLD
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
        write(root/"checks/features/Program.cs",(ROOT/"tools/FeedbackFixesCheck/Program.cs").read_bytes())
        write(root/"checks/features/Check.csproj",generic.project("GameplayFollowupCheck",refs).encode())
    for p in (ROOT/"tools/PackageCheck").glob("*"):
        if p.suffix in [".cs",".csproj"]:write(S/"checks/package"/p.name,p.read_bytes())
    stage_sushi()
    print("Staged new gameplay/UI/audio fixtures.")

def stage_sushi():
    refs={n:S/"assemblies"/(n+".dll") for n in ["ScCsgoKnives","ScCsgoResources","ScCsgoResourceCodec"]}
    write(S/"checks/sushi/Check.csproj",generic.project("SushiFeedbackCheck",refs).encode())
    write(S/"checks/sushi/Program.cs",(ROOT/"tools/SushiFeedbackCheck/Program.cs").read_bytes())
    for name in ["SushiInventoryRegression.cs","SushiSyncInventoryRegression.cs"]:write(S/"checks/sushi"/name,(ROOT/"tools/PackageCheck"/name).read_bytes())

if __name__=="__main__":main()
