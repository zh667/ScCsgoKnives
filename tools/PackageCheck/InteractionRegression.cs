using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;

static class InteractionRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Func<bool> test) {try{results.Add(new("interaction/"+name,test(),name));}catch(Exception e){results.Add(new("interaction/"+name,false,e.ToString()));}}
        var clock=mod.GetType("Game.KnifeClock");var ctrl=mod.GetType("Game.KnifeAnimationController");
        var rig=mod.GetType("Game.CsmcKnifeRig");var cs2=mod.GetType("Game.Cs2Rig");var timeline=mod.GetType("Game.ScGrenadePreparation");
        var savedTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var savedNames=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        bool wasVirtual=(bool)clock.GetField("Virtual").GetValue(null);double oldNow=(double)clock.GetField("VirtualNow").GetValue(null);
        T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        try {
            clock.GetField("Virtual").SetValue(null,true);clock.GetField("VirtualNow").SetValue(null,0d);
            string[] names=["ScKnifeBlock","ScGunBlock","ScGrenadeBlock"];
            for(int i=0;i<3;i++){Type type=mod.GetType("Game."+names[i]);BlocksManager.BlockTypeToIndex[type]=700+i;BlocksManager.BlockNameToIndex[names[i]]=700+i;}
            var gui=Blank<ComponentGui>();gui.m_modalPanelContainerWidget=new CanvasWidget();gui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());
            var gameWidget=Blank<GameWidget>();gameWidget.GuiWidget=new CanvasWidget();var data=Blank<PlayerData>();data.m_gameWidget=gameWidget;
            var player=Blank<ComponentPlayer>();player.ComponentGui=gui;player.PlayerData=data;
            for(int variant=0;variant<63;variant++) {
                int v=variant;
                Test("menu-keeps-real-pose/"+v,()=>{
                    var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;
                    object state=ctrl.GetMethod("StateFor",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,[model]);
                    object pose=rig.GetMethod("Sample").Invoke(null,[v,"idle",0f,true]);
                    state.GetType().GetField("Variant").SetValue(state,v);state.GetType().GetField("Pose").SetValue(state,pose);
                    int value=v<22?Terrain.MakeBlockValue(700,0,v):v<57?Terrain.MakeBlockValue(701,0,65536+(v-22)):Terrain.MakeBlockValue(702,0,v-57);
                    object actual=ctrl.GetMethod("Update").Invoke(null,[model,value]);
                    return actual is not null && ReferenceEquals(pose,actual);
                });
            }
            gui.m_modalPanelContainerWidget.Children.Clear();
            for(int kind=0;kind<6;kind++) foreach(bool low in new[]{false,true}) {
                string asset=(string)rig.GetMethod("GetAssetName").Invoke(null,[57+kind]),hold=low?"holdLow":"holdHigh",throwAlias=low?"throwLow":"throwHigh";
                float Duration(string alias)=>(float)cs2.GetMethod("Duration").Invoke(null,[asset,alias]);
                int variant=57+kind;
                Test($"hold-never-idles/{asset}/{low}",()=>{
                    var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;
                    object state=ctrl.GetMethod("StateFor",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,[model]);var type=state.GetType();
                    type.GetField("Variant").SetValue(state,variant);type.GetField("ClipAlias").SetValue(state,hold);
                    type.GetField("Action").SetValue(state,Enum.Parse(type.GetField("Action").FieldType,"Grenade"));
                    foreach(double t in new[]{0d,1d,60d}) {
                        clock.GetField("VirtualNow").SetValue(null,t);
                        object pose=ctrl.GetMethod("Update").Invoke(null,[model,Terrain.MakeBlockValue(702,0,variant-57)]);
                        if(pose is null || (string)pose.GetType().GetProperty("ClipAlias").GetValue(pose)!=hold)return false;
                    }
                    return true;
                });
                Test($"release-gates-throw/{asset}/{low}",()=>{
                    float pull=Duration("pullpin"),release=(float)cs2.GetMethod("GrenadeReleaseTime").Invoke(null,[asset,throwAlias]);
                    object state=Activator.CreateInstance(timeline,[0d,pull,release,Duration(throwAlias)]);
                    void Step(double now,bool pressed)=>timeline.GetMethod("Step").Invoke(state,[now,pressed]);
                    double Get(string property)=>(double)timeline.GetProperty(property).GetValue(state);
                    foreach(double now in new[]{0d,.2d,2d,30d,600d}) {
                        Step(now,true);
                        if(!double.IsPositiveInfinity(Get("ReleaseAt")) || !double.IsPositiveInfinity(Get("EndAt")))return false;
                    }
                    Step(601,false);
                    return Get("ThrowStartedAt")==601 && Math.Abs(Get("ReleaseAt")-601-release)<.0001 && Get("EndAt")>=Get("ReleaseAt");
                });
                Test($"early-release-waits-for-pin/{asset}/{low}",()=>{
                    float pull=Duration("pullpin");object state=Activator.CreateInstance(timeline,[0d,pull,.1f,.5f]);
                    timeline.GetMethod("Step").Invoke(state,[.1d,false]);
                    if(!(bool)timeline.GetProperty("ReleaseRequested").GetValue(state) || (bool)timeline.GetProperty("Throwing").GetValue(state))return false;
                    timeline.GetMethod("Step").Invoke(state,[(double)pull+.001,true]);
                    return (bool)timeline.GetProperty("Throwing").GetValue(state);
                });
            }
            Test("grenade-icon-fits-slot",()=>{
                float scale=(float)(mod.GetType("Game.ScGrenadeBlock").GetField("IconDrawSize")?.GetRawConstantValue() ?? 1.45f);
                float fraction=4*.85f*scale/3.6f;return fraction>.5f && fraction<.8f;
            });
        } finally {
            clock.GetField("Virtual").SetValue(null,wasVirtual);clock.GetField("VirtualNow").SetValue(null,oldNow);
            BlocksManager.BlockTypeToIndex.Clear();foreach(var p in savedTypes)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;
            BlocksManager.BlockNameToIndex.Clear();foreach(var p in savedNames)BlocksManager.BlockNameToIndex[p.Key]=p.Value;
        }
        return results;
    }
}
