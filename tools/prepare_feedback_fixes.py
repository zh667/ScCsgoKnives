from prepare_settings_rollback import ROOT
import prepare_settings_rollback as previous
S=ROOT/".tmp/feedback-fixes-130-20260927"
OLD=ROOT/".tmp/settings-rollback-130-20260927"
BASELINES={"core":("轻量","5cedae8c37a83f4c0f57731fc15925a48f516dda2ea893a19f3d51277a3dd05c"),"agents":("探员","878bb5940db080215ca7789a27f9d02e52d151fa93ff7761fd24c0bc29ae38f4")}
FULL_SHA="38472749cafc2ce6da31946f4263d603cba7cbfaedcf3247f8628d70365256e6"
def main():
    previous.S=S;previous.OLD=OLD;previous.BASELINES=BASELINES;previous.FULL_SHA=FULL_SHA;previous.main()
if __name__=="__main__":main()
