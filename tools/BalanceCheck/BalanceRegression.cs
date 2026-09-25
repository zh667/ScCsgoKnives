using Game;
using Engine;
using System.Reflection;
using System.Text.Json;
static class BalanceRegression {
    public static void Run(Action<string,bool,string> check) {
        void Test(string name,Func<bool> test) {try{check("balance/"+name,test(),name);}catch(Exception e){check("balance/"+name,false,e.ToString());}}
        using var plan=JsonDocument.Parse(File.ReadAllText("docs/gun-balance-evidence-2026-09-24.json"));
        bool Near(double x,double y)=>Math.Abs(x-y)<.002;
        // Current user correction supersedes the historical plan's RPM only. Keep that evidence immutable.
        double ExpectedRpm(string name, float cycle, int l) {
            int Tier(int i)=>Math.Clamp(l-i*10,0,10);
            if(name=="taser")return 2*(1+.65*(1/(1-.05*Tier(0)-.02*Tier(1)-.01*Tier(2)-.005*Tier(3)-.005*Tier(4))-1));
            double multiplier=name is "awp" or "ssg08" ? 1+.65*(.05*Tier(1)+.05*Tier(2)+.075*Tier(3)+.075*Tier(4))
                : 1+(name is "scar20" or "g3sg1" ? .004 : .0065)*l;
            return 60/cycle*multiplier;
        }
        var registry=ScGunRegistry.Current; bool enabled=ScGunplaySettings.Enabled;
        var resolver=typeof(ScComponentCrafting).GetField("ResolveOverride",BindingFlags.NonPublic|BindingFlags.Static);
        var owner=typeof(ScWeaponCrafting).GetField("RecoveryOwnerOverride",BindingFlags.NonPublic|BindingFlags.Static);
        object oldResolver=resolver.GetValue(null),oldOwner=owner.GetValue(null);
        var ids=new Dictionary<string,int>();int Ingredient(string name){if(!ids.ContainsKey(name))ids[name]=800+ids.Count;return ids[name];}
        resolver.SetValue(null,(Func<string,int>)Ingredient);owner.SetValue(null,(Func<IInventory,string>)(_=>"balance-check"));
        try {
            foreach(var row in plan.RootElement.GetProperty("guns").EnumerateArray()) {
                string name=row.GetProperty("name").GetString();var spec=GunSpec.ForAsset(name);int variant=Array.IndexOf(GunSpec.All,spec);
                var entry=ScWeaponCrafting.All.Single(e=>!e.Knife&&e.Name==name);
                Test("assembly/"+name,()=>row.GetProperty("proposed_assembly").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{entry.B,entry.M,entry.H,entry.O,entry.Diamond,entry.Germanium}));
                foreach(var level in row.GetProperty("levels").EnumerateArray()) foreach(bool handling in new[]{false,true}) {
                    int lv=level.GetProperty("level").GetInt32();
                    Test($"plan/{name}/{lv}/{handling}",()=>{
                        ScGunplaySettings.Enabled=handling;
                        var stats=EffectiveGunStats.ResolveLevel(spec,Terrain.MakeBlockValue(512,0,GunSpec.WithId(variant,GunSpec.FreshFull)),false,lv);
                        var repair=ScWeaponRepair.FullCostAtLevel(entry,lv);
                        return Near(stats.Power,level.GetProperty("power").GetDouble())&&Near(ScGunAttributes.EffectiveRpm(stats),ExpectedRpm(name, spec.CycleSeconds, lv))
                            &&stats.Capacity==level.GetProperty("capacity").GetInt32()&&Near(stats.PelletPower(spec,0)*spec.Pellets,stats.Power)
                            &&repair.Values.SequenceEqual(level.GetProperty("proposed_full_repair").EnumerateArray().Select(x=>x.GetInt32()));
                    });
                }
            }
            string[] reps=["glock18","ak47","scar20","m249","taser"];
            int qi=0;
            foreach(var row in plan.RootElement.GetProperty("repair_quotes").EnumerateArray()) {
                int group=qi++/24;var entry=ScWeaponCrafting.All.Single(e=>!e.Knife&&e.Name==reps[group]);int lv=row.GetProperty("level_min").GetInt32(),missing=row.GetProperty("missing_percent").GetInt32();
                Test($"repair-table/{entry.Name}/{lv}/{missing}",()=>{
                    var cost=ScWeaponRepair.CostAtLevel(entry,100-missing,100,lv);
                    return cost[0]==row.GetProperty("blank").GetInt32()&&cost[1]==row.GetProperty("mechanism").GetInt32();});
            }
            var pistol=ScWeaponCrafting.All.Single(e=>e.Name=="glock18");
            Test("repair-single-rounding",()=>ScWeaponRepair.CostAtLevel(pistol,60,100,10)[0]==1&&ScWeaponRepair.FullCostAtLevel(pistol,10)[0]==3);
            Test("repair-long-integers",()=>ScWeaponRepair.CostAtLevel(pistol,1,int.MaxValue,50)[0]==4&&ScWeaponRepair.CostAtLevel(pistol,1,int.MaxValue,50)[1]==2);
            foreach(string gun in new[]{"nova","xm1014","sawedoff","mag7"}) Test("shotgun-monotonic/"+gun,()=>Enumerable.Range(1,50).All(l=>ScSurvivalBalance.PowerAtLevel(gun,l)>=ScSurvivalBalance.PowerAtLevel(gun,l-1)-1e-4));
            foreach(var (gun,cone) in new[]{("nova",2.1f),("xm1014",2.05f),("sawedoff",3.2f),("mag7",2.1f)}) Test("cone/"+gun,()=>Near(ScGunHandling.ForMode(gun,false).BaseCone,cone)&&Near(ScGunHandling.ForMode(gun,true).BaseCone,cone));
            Test("component-recipes",()=>ScComponentCrafting.All.Select(e=>string.Join(",",e.Ingredients.Select(p=>$"{p.Id}:{p.Count}"))).SequenceEqual(new[]{"ironingot:8,coalchunk:3","sccsgomaterial:0:1,copperingot:6,germaniumchunk:2","leather:4,planks:2,copperingot:1","glass:4,copperingot:2,germaniumchunk:2","pigment:0:4,canvas:2,copperingot:2"}));
            ScWorkbenchExtension.RegisterBaseRecipes();var shell=ScWorkbenchExtension.All.Single(r=>r.Key=="ammo-shell");
            foreach(int quantity in new[]{1,2,3}) Test("shell-batch/"+quantity,()=>{
                ScGunRegistry.Current=new();var inv=new Inventory();var materials=shell.Materials();int slot=0;foreach(var p in materials)inv.AddSlotItems(slot++,p.Key,p.Value*quantity);
                return shell.ResultCount==12&&ScCraftBatch.TryCraftBatch(inv,999,materials,quantity,shell.ResultCount)&&inv.Counts.Where((n,i)=>inv.Values[i]==999).Sum()==quantity*12
                    &&materials.Keys.All(k=>inv.Counts.Where((n,i)=>inv.Values[i]==k).Sum()==0);
            });
            Test("repair-transaction-and-stale",()=>{
                var (inv,r)=Gun("ak47",20);r.PendingGrowthLevel=50;r.KillCount=5250;
                var entry=ScWeaponCrafting.All.Single(e=>e.Name=="ak47");var q=ScWeaponRepair.Prepare(new(0,inv.Values[0]),entry,false,k=>950+k);
                // Pending Lv50 is not the applied Lv20 price. 40% missing: ceil(3*1.4*.4)=2 and ceil(2*1.4*.4)=2.
                if(q.Level!=20||q.Cost[950]!=2||q.Cost[951]!=2)return false;
                string before=ScGunRegistry.Current.Save(0).GetValue<TemplatesDatabase.ValuesDictionary>("Records").GetValue<string>("1");
                if(ScWeaponRepair.TryRepair(inv,q,"test")!=ScGunResult.InsufficientMaterials)return false;
                inv.AddSlotItems(1,950,2);inv.AddSlotItems(2,951,2);inv.ThrowSlot=2;
                if(ScWeaponRepair.TryRepair(inv,q,"test")==ScGunResult.Success||inv.Counts[1]!=2||inv.Counts[2]!=2)return false;
                inv.ThrowSlot=-1;
                if(before!=ScGunRegistry.Current.Save(0).GetValue<TemplatesDatabase.ValuesDictionary>("Records").GetValue<string>("1"))return false;
                r.Revision++;if(ScWeaponRepair.TryRepair(inv,q,"test")!=ScGunResult.StateChanged)return false;
                q=ScWeaponRepair.Prepare(new(0,inv.Values[0]),entry,false,k=>950+k);
                return ScWeaponRepair.TryRepair(inv,q,"test")==ScGunResult.Success&&r.Durability==r.MaxDurability&&inv.Counts[1]==0&&inv.Counts[2]==0&&r.PendingGrowthLevel==50&&r.KillCount==5250;
            });
            Test("skin-first-swap-strip",()=>{
                var (inv,r)=Gun("ak47",20);var skins=ScGunSkinCatalog.For(0).Take(2).ToArray();
                inv.AddSlotItems(1,951,2);inv.AddSlotItems(2,954,7);inv.AddSlotItems(3,Ingredient("diamond"),1);
                int durability=r.Durability,rounds=r.Rounds;long kills=r.KillCount;
                foreach(var skin in new ScGunSkin[]{skins[0],skins[1],null}) {
                    var q=ScWeaponSkinning.Prepare(inv,0,skin,false,k=>950+k);
                    var display=ScGunSkinCatalog.CostForChange(0,r.SkinId,skin,k=>950+k);
                    if(!q.Cost.OrderBy(p=>p.Key).SequenceEqual(display.OrderBy(p=>p.Key))||ScWeaponSkinning.Apply(inv,q,"test")!=ScGunResult.Success)return false;
                }
                return inv.Counts[1]==0&&inv.Counts[2]==0&&inv.Counts[3]==0&&r.SkinId==0&&r.Durability==durability&&r.Rounds==rounds&&r.KillCount==kills&&r.AppliedGrowthLevel==20;
            });
            Test("he-chicken-separate",()=>Near(ScGrenadeState.HePower(0),96)&&Near(ScGrenadeState.HePower(3.9f),48)&&ScGrenadeState.HePower(7.8f)==0&&ScGrenadeState.ChickenPower(0)==48&&ScGrenadeState.ChickenPower(6)==0);
            foreach(int kind in new[]{3,4}) Test("fire-boundaries/"+kind,()=>{
                float radius=kind==3?3:3.6f;var a=new ScGrenadeState{Kind=kind,Effect=true,Remaining=kind==3?6:7};
                bool valid=ScFireArea.Contains(a,new(radius,0,0))&&!ScFireArea.Contains(a,new(radius+.001f,0,0))&&ScFireArea.Exposure([a],Vector3.Zero,1,_=>false).Power==0;
                float total=0;while(a.Remaining>0){float dt=Math.Min(.17f,a.Remaining);total+=ScFireArea.Exposure([a,a],Vector3.Zero,dt,_=>true).Power;a.Remaining-=dt;}
                return valid&&Near(total,kind==3?36:42)&&ScFireArea.Exposure([a],Vector3.Zero,1,_=>true).Power==0;
            });
        } finally {ScGunRegistry.Current=registry;ScGunplaySettings.Enabled=enabled;resolver.SetValue(null,oldResolver);owner.SetValue(null,oldOwner);}
    }
    static (Inventory,ScGunRecord) Gun(string name,int level) {
        int variant=Array.FindIndex(GunSpec.All,g=>g.Name==name),max=ScGunGrowth.MaxDurability(variant,level);
        var reg=ScGunRegistry.Current=new(){GrowthMode=ScGunGrowthMode.CountAndGrow};int id=reg.Allocate(variant,3,false,max*3/5,max);
        var records=(Dictionary<int,ScGunRecord>)typeof(ScGunRegistry).GetField("m_records",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reg);var r=records[id];
        r.CounterInstalled=true;r.AppliedGrowthLevel=level;r.GrowthRulesVersion=7;r.KillCount=ScGunGrowth.KillsFor(variant,level);
        var inv=new Inventory();inv.AddSlotItems(0,Terrain.MakeBlockValue(512,0,GunSpec.WithId(variant,id)),1);return(inv,r);
    }
    sealed class Inventory:IInventory {
        public GameEntitySystem.Project Project=>null;public int SlotsCount=>10;public int VisibleSlotsCount{get;set;}=10;public int ActiveSlotIndex{get;set;}
        public int[] Values=new int[10],Counts=new int[10];public int ThrowSlot=-1;
        public int GetSlotValue(int i)=>Values[i];public int GetSlotCount(int i)=>Counts[i];public int GetSlotCapacity(int i,int v)=>i==0&&v<800?1:100;
        public int GetSlotProcessCapacity(int i,int v)=>0;
        public void AddSlotItems(int i,int v,int n){if(n==0)return;if(Counts[i]>0&&Values[i]!=v)throw new InvalidOperationException("mixed");Values[i]=v;Counts[i]+=n;}
        public int RemoveSlotItems(int i,int n){if(i==ThrowSlot)throw new InvalidOperationException("injected material failure");n=Math.Min(n,Counts[i]);Counts[i]-=n;return n;}
        public void ProcessSlotItems(int i,int v,int count,int process,out int result,out int resultCount){result=v;resultCount=0;}
        public void DropAllItems(Vector3 position)=>Array.Clear(Counts);
    }
}
