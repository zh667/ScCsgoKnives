using System.Reflection;
using System.Linq.Expressions;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// Contract fixture, not the third-party DLL or a shipped game component.
namespace Logistics {
    public sealed class StorageVault {
        public readonly int[] Values=new int[12], Counts=new int[12];
    }
    public sealed class ComponentStorageUnit : IInventory {
        public StorageVault Vault;
        public Guid VaultGuid {get;set;}=Guid.NewGuid();
        public bool TryResolveVault(out StorageVault vault){vault=Vault;return vault is not null;}
        public Project Project=>null;
        public int SlotsCount=>12;
        public int VisibleSlotsCount{get;set;}=12;
        public int ActiveSlotIndex{get;set;}
        public int GetSlotValue(int i)=>Vault.Values[i];
        public int GetSlotCount(int i)=>Vault.Counts[i];
        public int GetSlotCapacity(int i,int v)=>40;
        public int GetSlotProcessCapacity(int i,int v)=>0;
        public void AddSlotItems(int i,int v,int n){if(Vault.Counts[i]>0&&Vault.Values[i]!=v)throw new InvalidOperationException();Vault.Values[i]=v;Vault.Counts[i]+=n;}
        public int RemoveSlotItems(int i,int n){n=Math.Min(n,Vault.Counts[i]);Vault.Counts[i]-=n;return n;}
        public void ProcessSlotItems(int i,int v,int count,int process,out int result,out int resultCount){result=v;resultCount=0;}
        public void DropAllItems(Vector3 p)=>Array.Clear(Vault.Counts);
    }
}

static class CommunityRepairRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Check(string name,bool ok,string detail="")=>results.Add(new("community-repair/"+name,ok,detail));
        var registryType=mod.GetType("Game.ScGunRegistry");var current=registryType.GetField("Current");object saved=current.GetValue(null);
        var mutation=mod.GetType("Game.ScGunMutation");var locator=mutation.GetField("HolderLocator");object oldLocator=locator.GetValue(null);
        try {
            var holders=mod.GetType("Game.ScGunHolders");var key=holders.GetMethod("Key");
            string Key(object obj,int slot)=>(string)key.Invoke(null,[obj,slot]);
            var vault=new Logistics.StorageVault();Guid guid=Guid.NewGuid();
            var aliases=Enumerable.Range(0,7).Select(_=>new Logistics.ComponentStorageUnit{Vault=vault,VaultGuid=guid}).ToArray();
            var independent=new Logistics.ComponentStorageUnit{Vault=new(),VaultGuid=guid};
            Check("seven-aliases-one-key",aliases.Select(a=>Key(a,0)).Distinct().Count()==1);
            Check("equal-guid-is-not-proof",Key(aliases[0],0)!=Key(independent,0));
            Check("slots-stay-independent",Key(aliases[0],0)!=Key(aliases[1],1));
            var epochs=mod.GetType("Game.ScInventoryTransaction");
            epochs.GetMethod("Changed").Invoke(null,[aliases[0]]);
            Check("shared-revision",aliases.All(a=>(long)epochs.GetMethod("Revision").Invoke(null,[a])==1));
            var registry=Activator.CreateInstance(registryType);current.SetValue(null,registry);
            int id=(int)registryType.GetMethod("Allocate").Invoke(registry,[0,19,false,700,2250,0]);
            var record=registryType.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(registry,[id]);var rt=record.GetType();
            rt.GetField("CounterInstalled").SetValue(record,true);rt.GetField("KillCount").SetValue(record,1234L);rt.GetField("AppliedGrowthLevel").SetValue(record,10);rt.GetField("GrowthRulesVersion").SetValue(record,(int)mod.GetType("Game.ScGunGrowth").GetField("RulesVersion").GetRawConstantValue());
            int value=Terrain.MakeBlockValue(512,0,(int)mod.GetType("Game.GunSpec").GetMethod("WithId").Invoke(null,[0,id]));aliases[0].AddSlotItems(0,value,1);
            Func<int,string,IEnumerable<string>> find=(rid,except)=>aliases.Where(a=>a.GetSlotCount(0)>0).Select(a=>Key(a,0)).Distinct().Where(k=>k!=except);
            locator.SetValue(null,find);
            var parameter=Expression.Parameter(rt);var action=Expression.Lambda(typeof(Action<>).MakeGenericType(rt),Expression.Empty(),parameter).Compile();
            bool all=true;
            for(int i=0;i<210;i++) {
                var a=aliases[i%7];object[] args=[a,0,Key(a,0),null];object tx=mutation.GetMethod("Prepare").Invoke(null,args);
                all &= tx is not null && mutation.GetMethod("Commit").Invoke(tx,[action,0,0,null]).ToString()=="Success";
            }
            Check("210-shared-transactions-no-id-growth",all&&(int)registryType.GetProperty("Next").GetValue(registry)==2);
            Check("kills-life-ammo-retained",(long)rt.GetField("KillCount").GetValue(record)==1234&&(int)rt.GetField("AppliedGrowthLevel").GetValue(record)==10&&(int)rt.GetField("Durability").GetValue(record)==700&&(int)rt.GetField("Rounds").GetValue(record)==19);
            independent.AddSlotItems(0,value,1);
            locator.SetValue(null,(Func<int,string,IEnumerable<string>>)((rid,except)=>aliases.Append(independent)
                .Where(a=>a.GetSlotCount(0)>0&&(int)mod.GetType("Game.GunSpec").GetMethod("GetId").Invoke(null,[Terrain.ExtractData(a.GetSlotValue(0))])==rid)
                .Select(a=>Key(a,0)).Distinct().Where(k=>k!=except).ToArray()));
            object[] copyArgs=[independent,0,Key(independent,0),null];var copyTx=mutation.GetMethod("Prepare").Invoke(null,copyArgs);
            Check("real-independent-copy-separated",mutation.GetMethod("Commit").Invoke(copyTx,[action,0,0,null]).ToString()=="Success"&&independent.GetSlotValue(0)!=value&&aliases.All(a=>a.GetSlotValue(0)==value));
            var copyRecord=registryType.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(registry,[2]);
            Check("only-real-copy-loses-growth",(long)rt.GetField("KillCount").GetValue(copyRecord)==0&&(int)rt.GetField("AppliedGrowthLevel").GetValue(copyRecord)==0&&(long)rt.GetField("KillCount").GetValue(record)==1234);
            var holderType=holders.GetNestedType("Holder");
            object Holder(IInventory inv)=>Activator.CreateInstance(holderType,[id,Key(inv,0),inv,0]);
            var witnessMethod=holders.GetMethod("StillDuplicates",BindingFlags.Static|BindingFlags.NonPublic);
            independent.Vault.Values[0]=value;
            object acting=Holder(independent), witness=Holder(aliases[0]);
            bool Witness()=>(bool)witnessMethod.Invoke(null,[acting,witness,512]);
            object Tx(IInventory inv) {object[] a=[inv,0,Key(inv,0),null];return mutation.GetMethod("Prepare").Invoke(null,a);}
            var direct=Tx(independent); mutation.GetField("DuplicateWitness",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(direct,(Func<bool>)Witness);
            int scans=0;locator.SetValue(null,(Func<int,string,IEnumerable<string>>)((rid,except)=>{scans++;return [Key(aliases[0],0)];}));
            Check("live-witness-splits-without-world-rescan",mutation.GetMethod("Commit").Invoke(direct,[action,0,0,null]).ToString()=="Success"&&scans==0);
            independent.Vault.Values[0]=value;acting=Holder(independent);witness=Holder(aliases[0]);
            var movedWitness=Tx(independent);mutation.GetField("DuplicateWitness",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(movedWitness,(Func<bool>)Witness);
            aliases[0].Vault.Counts[0]=0;int nextBefore=(int)registryType.GetProperty("Next").GetValue(registry);
            Check("vanished-witness-refused-without-allocation",mutation.GetMethod("Commit").Invoke(movedWitness,[action,0,0,null]).ToString()=="StateChanged"&&(int)registryType.GetProperty("Next").GetValue(registry)==nextBefore&&scans==0);
            aliases[0].Vault.Counts[0]=1;
            var originalVault=independent.Vault;independent.Vault=vault;
            Check("witness-merge-is-not-a-copy",!Witness());independent.Vault=originalVault;
            // Save/reload must retain the high-water mark consumed by this extra test copy.
            int afterWitnessNext=(int)registryType.GetProperty("Next").GetValue(registry);
            // With runtime canonical checks enabled, remapping a proxy between Prepare and Commit is refused.
            registryType.GetField("RecoveryOwner").SetValue(registry,(Func<IInventory,string>)(_=>"test/vault"));
            object[] prepared=[aliases[0],0,Key(aliases[0],0),null];var stale=mutation.GetMethod("Prepare").Invoke(null,prepared);
            aliases[0].Vault=new();aliases[0].AddSlotItems(0,value,1);
            Check("proxy-remap-refused",mutation.GetMethod("Commit").Invoke(stale,[action,0,0,null]).ToString()=="StateChanged");aliases[0].Vault=vault;
            registryType.GetField("RecoveryOwner").SetValue(registry,null);
            for(int round=0;round<2;round++) {
                var data=(ValuesDictionary)registryType.GetMethod("Save").Invoke(registry,[0d]);var xml=new XElement("Values");data.Save(xml);
                var read=new ValuesDictionary();read.ApplyOverrides(XElement.Parse(xml.ToString()));registry=registryType.GetMethod("Load").Invoke(null,[read,0d]);current.SetValue(null,registry);
                record=registryType.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(registry,[id]);
                Check("real-xml-roundtrip/"+round,(long)rt.GetField("KillCount").GetValue(record)==1234&&(int)rt.GetField("AppliedGrowthLevel").GetValue(record)==10&&(int)registryType.GetProperty("Next").GetValue(registry)==afterWitnessNext);
            }
            var gate=Activator.CreateInstance(mod.GetType("Game.ScTriggerReleaseGate"));var observe=gate.GetType().GetMethod("Observe");object inv=new();
            bool Gate(int slot,int v,bool down,bool available=true)=>(bool)observe.Invoke(gate,[inv,slot,v,down,available]);
            Check("hold-switch-release-press",Gate(0,1,false)&&Gate(0,1,true)&&!Gate(1,2,true)&&!Gate(1,2,true)&&Gate(1,2,false)&&Gate(1,2,true));
            Check("pause-resume-held-blocked",!Gate(1,2,true,false)&&!Gate(1,2,true)&&Gate(1,2,false)&&Gate(1,2,true));
            var shape=Activator.CreateInstance(mod.GetType("Game.ScCrosshairShape"),[float.NaN,999f,-1f,0f,float.PositiveInfinity]);
            shape=shape.GetType().GetMethod("Normalize").Invoke(shape,null);
            Check("crosshair-nonfinite-clamp",(float)shape.GetType().GetProperty("Width").GetValue(shape)==2&&(float)shape.GetType().GetProperty("Length").GetValue(shape)==32);
            var scale=mod.GetType("Game.ScProjectileDefense").GetMethod("Scale");
            Check("defense-interval-and-reduction",(float)scale.Invoke(null,["GiantTurtle",100f,true])==50&&(float)scale.Invoke(null,["Kraken",100f,true])==80&&(float)scale.Invoke(null,["Kraken",5f,true])==1&&(float)scale.Invoke(null,["BlueWhale",100f,false])==0);
            Check("gamepad-all-ten-actions",((string[])mod.GetType("Game.ScGamepadBindings").GetMethod("Options").Invoke(null,null)).Length==19);
            var components=(Array)mod.GetType("Game.ScComponentCrafting").GetField("All").GetValue(null);
            var expected=new[]{new[]{("ironingot",12),("coalchunk",4)},new[]{("sccsgomaterial:0",2),("copperingot",8),("germaniumchunk",4)},
                new[]{("leather",8),("planks",4),("copperingot",2)},new[]{("glass",8),("copperingot",4),("germaniumchunk",4),("diamond",1)},new[]{("pigment:0",8),("canvas",4),("copperingot",4)}};
            for(int i=0;i<5;i++) {var entry=components.GetValue(i);Check("component-recipe/"+i,((ValueTuple<string,int>[])entry.GetType().GetProperty("Ingredients").GetValue(entry)).SequenceEqual(expected[i]));}
            var recipes=((Array)mod.GetType("Game.ScWeaponCrafting").GetField("All").GetValue(null)).Cast<object>().ToArray();
            var ak=recipes.Single(e=>(string)e.GetType().GetProperty("Name").GetValue(e)=="ak47");
            int Part(string name)=>(int)ak.GetType().GetProperty(name).GetValue(ak);
            Check("ak-exact-raw-economy",Part("B")==6&&Part("M")==5&&Part("H")==2&&(Part("B")+Part("M")*2)*12==192&&Part("M")*8+Part("H")*2==44);
        }catch(Exception e){Check("exception",false,e.ToString());}
        finally{current.SetValue(null,saved);locator.SetValue(null,oldLocator);}
        return results;
    }
}
