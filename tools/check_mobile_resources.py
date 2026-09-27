import sys,shutil,json
from prepare_mobile_resources import ROOT,S
import check_mobile_common as common
import check_codec_release as base
common.S=S;common.F=S/"full-check";base.S=S

def main():
    mode=sys.argv[1]
    if mode=="build":
        common.main()
        for root in [S,S/"full-check"]:
            shutil.copytree(root/"checks/appearance/bin/Release/net10.0",root/"runtime/AppearanceCheck",dirs_exist_ok=True)
    elif mode=="textures":
        packages=json.loads((S/"packages.json").read_bytes())
        for role,root in [("core",S),("full",S/"full-check")]:
            base.run(role+"-textures",common.dll(root,"textures","TexturePreparationCheck"),S/"candidate"/packages[role]["file"],root/"textures",
                S/"candidate"/packages["agents" if role=="core" else "full"]["file"])
    else:common.main()

if __name__=="__main__":main()
