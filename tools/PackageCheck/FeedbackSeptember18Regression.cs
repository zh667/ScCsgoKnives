using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class FeedbackSeptember18Regression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Inventory : IInventory {
        public int[] Values=new int[18],Counts=new int[18];
        public int Fail=-1; public bool After;
        public Project Project=>null;public int SlotsCount=>18;public int VisibleSlotsCount{get;set;}public int ActiveSlotIndex{get;set;}
        public int GetSlotValue(int s)=>Values[s];public int GetSlotCount(int s)=>Counts[s];public int GetSlotCapacity(int s,int v)=>100;
        public int GetSlotProcessCapacity(int s,int v)=>0;
        public void AddSlotItems(int s,int v,int n){bool fail=s==Fail;Fail=-1;if(fail&&!After)throw new Exception("before add");Values[s]=v;Counts[s]+=n;if(fail)throw new Exception("after add");}
        public int RemoveSlotItems(int s,int n){n=Math.Min(n,Counts[s]);Counts[s]-=n;return n;}
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn){rv=rn=0;}
        public void DropAllItems(Vector3 p){}
        public string Snapshot()=>string.Join(";",Values.Select((v,s)=>Counts[s]>0?$"{v}:{Counts[s]}":"0"));
    }
    internal static List<Result> Run(Assembly mod) {
        List<Result> result=[];
        void Test(string n,Func<bool> f){try{result.Add(new("feedback-0918/"+n,f(),""));}catch(Exception e){result.Add(new("feedback-0918/"+n,false,e.ToString()));}}
        Type T(string n)=>mod.GetType("Game."+n);
        object Call(string t,string m,params object[] a)=>T(t).GetMethod(m).Invoke(null,a);
        object F(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);
        void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(o,v);
        U Blank<U>()=>(U)RuntimeHelpers.GetUninitializedObject(typeof(U));
        var types=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var names=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var current=T("ScGunRegistry").GetField("Current");var old=current.GetValue(null);
        var resolve=T("ScComponentCrafting").GetField("ResolveOverride",BindingFlags.Static|BindingFlags.NonPublic);var oldResolve=resolve.GetValue(null);
        var clock=T("KnifeClock");bool virtualOld=(bool)clock.GetField("Virtual").GetValue(null);double timeOld=(double)clock.GetField("VirtualNow").GetValue(null);
        float volume=SettingsManager.SoundsVolume;
        try {
            foreach(var pair in new[]{("ScKnifeBlock",700),("ScGunBlock",701),("ScWeaponMaterialBlock",702)}) {BlocksManager.BlockTypeToIndex[T(pair.Item1)]=pair.Item2;BlocksManager.BlockNameToIndex[pair.Item1]=pair.Item2;}
            var registry=Activator.CreateInstance(T("ScGunRegistry"));current.SetValue(null,registry);Set(registry,"RecoveryOwner",(Func<IInventory,string>)(_=>"player/987"));
            resolve.SetValue(null,(Func<string,int>)(_=>703));
            var specs=(Array)T("GunSpec").GetField("All").GetValue(null);
            int Index(string n)=>Array.FindIndex(specs.Cast<object>().ToArray(),s=>(string)F(s,"Name")==n);
            var rate=T("ScGunGrowth").GetMethod("FireRateMultiplier",[typeof(int),typeof(int)]);
            foreach(string n in new[]{"scar20","g3sg1","awp","ssg08","ak47"})Test("rate/"+n,()=>{
                int v=Index(n);for(int l=0;l<=50;l++) {float actual=(float)rate.Invoke(null,[v,l]);if(n is "scar20" or "g3sg1" or "ak47") {if(Math.Abs(actual-(1+.01f*l))>1e-5)return false;}else if(l==50&&actual!=3.5f)return false;}
                return n is "ak47" || (float)Call("ScGunGrowth","Difficulty",v)==.6f;
            });
            var player=Blank<ComponentPlayer>();player.ComponentMiner=Blank<ComponentMiner>();var inv=new Inventory();player.ComponentMiner.Inventory=inv;
            var sights=Blank<ComponentAimingSights>();sights.m_componentPlayer=player;
            var loader=(ModLoader)Activator.CreateInstance(T("ScCsgoKnivesModLoader"));
            var ui=T("ScUiSettings").GetField("GunCrosshair");bool oldCross=(bool)ui.GetValue(null);
            try {foreach(string n in new[]{"scar20","g3sg1","awp","ssg08","aug","sg556"})foreach(bool enabled in new[]{false,true})Test($"sniper-crosshair/{n}/{enabled}",()=>{
                int v=Index(n);inv.Values[0]=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","MakeData",v,0,false));inv.Counts[0]=1;ui.SetValue(null,enabled);
                bool sniper=n is "scar20" or "g3sg1" or "awp" or "ssg08",visible=true;
                if((bool)Call("ScGunCrosshair","HideForSniper",player)!=sniper)return false;
                if(sniper){loader.IsCrosshairVisible(sights,ref visible);return !visible&&!(bool)Call("ScGunCrosshair","Active",player,null,false);}return true;
            });}finally{ui.SetValue(null,oldCross);}
            int knifeVariant=Enumerable.Range(0,22).First(v=>(int)Call("ScKnifeSkinCatalog","ForVariant",v)>0);
            int finish=(int)Call("ScKnifeSkinCatalog","ForVariant",knifeVariant),knife=Terrain.MakeBlockValue(700,11,knifeVariant|(37<<7));
            object Quote(IInventory i,bool free,int skin)=>Call("ScKnifeSkinning","Prepare",i,Activator.CreateInstance(T("ScKnifeSkinning+Candidate"),[0,i.GetSlotValue(0)]),skin,free);
            bool Apply(IInventory i,object q,bool free)=>(bool)Call("ScKnifeSkinning","Apply",i,q,free);
            Inventory Stock(){var i=new Inventory();i.Values[0]=knife;i.Counts[0]=1;int slot=1;foreach(var p in (Dictionary<int,int>)Call("ScKnifeSkinning","Cost",finish)){i.Values[slot]=p.Key;i.Counts[slot++]=p.Value;}return i;}
            Test("knife-finish-strip-preserves-bits-no-records",()=>{var i=Stock();var q=Quote(i,false,finish);if(!Apply(i,q,false)||Apply(i,q,false))return false;int changed=i.Values[0];if((changed&~(3<<19))!=(knife&~(3<<19)))return false;
                int paint=((Dictionary<int,int>)Call("ScKnifeSkinning","Cost",0)).Keys.Single();i.Values[1]=paint;i.Counts[1]=2;
                if(!Apply(i,Quote(i,false,0),false)||i.Values[0]!=knife)return false;
                return ((IDictionary)F(registry,"m_records")).Count==0;});
            foreach(bool after in new[]{false,true})Test("knife-rollback/"+after,()=>{var i=Stock();var q=Quote(i,false,finish);string before=i.Snapshot();i.Fail=0;i.After=after;return !Apply(i,q,false)&&i.Snapshot()==before;});
            Test("knife-stale-mode-materials",()=>{var i=Stock();var q=Quote(i,false,finish);string before=i.Snapshot();if(Apply(i,q,true)||i.Snapshot()!=before)return false;i.Counts[1]=0;before=i.Snapshot();return !Apply(i,q,false)&&i.Snapshot()==before;});
            Test("knife-creative-all-ten-owned-slots",()=>{var i=new ComponentCreativeInventory{OpenSlotsCount=10};for(int s=0;s<20;s++)i.m_slots.Add(knife);var c=((IEnumerable)Call("ScKnifeSkinning","Candidates",i)).Cast<object>().ToArray();if(c.Length!=10)return false;
                foreach(var k in c){var q=Call("ScKnifeSkinning","Prepare",i,k,finish,true);if(!Apply(i,q,true))return false;}return i.m_slots.Take(10).All(v=>(int)Call("ScKnifeBlock","SkinOf",v)==finish)&&i.m_slots.Skip(10).All(v=>v==knife);});
            Test("knife-full-gun-registry-still-works",()=>{var full=Activator.CreateInstance(T("ScGunRegistry"));Set(full,"RecoveryOwner",(Func<IInventory,string>)(_=>"player/987"));try{current.SetValue(null,full);var allocate=T("ScGunRegistry").GetMethod("Allocate");for(int n=0;n<1022;n++)allocate.Invoke(full,[0,1,false,1500,1500,0]);var i=Stock();return (int)T("ScGunRegistry").GetMethod("PeekNextId").Invoke(full,null)==-1&&Apply(i,Quote(i,false,finish),false)&&((IDictionary)F(full,"m_records")).Count==1022;}finally{current.SetValue(null,registry);}});
            foreach(int v in Enumerable.Range(0,22))Test("knife-available-finish-two-xml/"+v,()=>{int skin=(int)Call("ScKnifeSkinCatalog","ForVariant",v);var i=Stock();i.Values[0]=Terrain.MakeBlockValue(700,7,v|(37<<7));if(skin==0)return Quote(i,true,1) is null;
                int before=i.Values[0];if(!Apply(i,Quote(i,true,skin),true))return false;var values=new ValuesDictionary();values.SetValue("Slot0",i.Values[0]);for(int n=0;n<2;n++){var xml=new XElement("Values");values.Save(xml);values=new ValuesDictionary();values.ApplyOverrides(XElement.Parse(xml.ToString()));i.Values[0]=values.GetValue<int>("Slot0");if((int)Call("ScKnifeBlock","SkinOf",i.Values[0])!=skin)return false;}return (Terrain.ExtractData(before)&~96)==(Terrain.ExtractData(i.Values[0])&~96)&&Terrain.ExtractLight(before)==Terrain.ExtractLight(i.Values[0]);});
            Test("flash-hold-fade-range",()=>{float Op(float left)=>(float)Call("ScGrenadeState","FlashOpacity",left,5.5f);float last=1;for(float elapsed=0;elapsed<5.5;elapsed+=.025f){float a=Op(5.5f-elapsed);if(a>last+1e-5||a<0||a>1||elapsed<.59&&a!=1)return false;last=a;}
                return Op(0)==0&&(float)Call("ScGrenadeState","FlashDuration",2f,1f)==5.5f&&(float)Call("ScGrenadeState","FlashDuration",20f,1f)==0;});
            Test("flash-two-actual-subsystem-xml-rounds",()=>{
                var d=new ValuesDictionary();var flashes=new ValuesDictionary();var entry=new ValuesDictionary();entry.SetValue("Left",5.5f);entry.SetValue("Duration",5.5f);entry.SetValue("Immune",8.5f);flashes.SetValue("987",entry);d.SetValue("Blindness",flashes);
                for(int r=0;r<2;r++){var project=new Project();project.m_subsystems.Add(new SubsystemTime());project.m_subsystems.Add(new SubsystemTerrain());project.m_subsystems.Add(new SubsystemPlayers());project.m_subsystems.Add(new SubsystemBodies());project.m_subsystems.Add(new SubsystemGameInfo());
                    var sub=(Subsystem)Activator.CreateInstance(T("SubsystemScGrenades"));sub.m_project=project;sub.Load(d);d=new ValuesDictionary();sub.Save(d);var xml=new XElement("Values");d.Save(xml);d=new ValuesDictionary();d.ApplyOverrides(XElement.Parse(xml.ToString()));
                    var b=d.GetValue<ValuesDictionary>("Blindness").GetValue<ValuesDictionary>("987");if(b.GetValue<float>("Left")!=5.5f||b.GetValue<float>("Immune")!=8.5f)return false;}return true;});
            Test("smoke-ellipsoid-extents-and-core",()=>{var s=Activator.CreateInstance(T("ScGrenadeState"));Set(s,"Kind",2);Set(s,"Effect",true);Set(s,"Age",2f);Set(s,"Remaining",12f);
                float D(Vector3 p)=>(float)Call("ScSmokeVolume","Density",s,p,null);
                return D(new(0,1.75f,0))==1&&D(new(2,1.75f,0))==1&&D(new(2.75f,1.75f,0))==0&&D(new(0,3.5f,0))==0&&D(new(0,0,0))==0&&D(new(0,3,0))>0;
            });
            foreach(bool lite in new[]{false,true})foreach(float pitch in new[]{0f,.7f,MathF.PI/2})Test($"smoke-billboard-bounds/{lite}/{pitch}",()=>{
                var policy=T("ScResourcePolicy");string edition=(string)policy.GetProperty("Edition").GetValue(null);var configure=policy.GetMethod("ConfigureEdition",BindingFlags.Static|BindingFlags.NonPublic);
                try{configure.Invoke(null,[lite?"Optimized512":"Full"]);var s=Activator.CreateInstance(T("ScGrenadeState"));Set(s,"Kind",2);Set(s,"Effect",true);Set(s,"Age",2f);Set(s,"Remaining",12f);var sprites=((IEnumerable)Call("ScGrenadeVisuals","Smoke",s,5f)).Cast<object>().ToArray();if(sprites.Length!=(lite?32:64))return false;
                    foreach(var sp in sprites){var axes=((Vector3,Vector3))Call("ScGrenadeVisuals","SmokeAxes",sp,s,Vector3.UnitX,new Vector3(0,MathF.Cos(pitch),MathF.Sin(pitch)));Vector3 p=(Vector3)sp.GetType().GetProperty("Position").GetValue(sp);if(axes.Item1.Length()<.1||axes.Item2.Length()<.1)return false;foreach(int a in new[]{-1,1})foreach(int b in new[]{-1,1}){var corner=p+axes.Item1*a+axes.Item2*b;if(Math.Abs(corner.X)>2.751||Math.Abs(corner.Z)>2.751||corner.Y<-.001||corner.Y>3.501)return false;}}return true;
                }finally{configure.Invoke(null,[edition]);}
            });
            Test("presentation-clock-paused-and-stale-cues",()=>{clock.GetField("Virtual").SetValue(null,true);SettingsManager.SoundsVolume=0;clock.GetField("VirtualNow").SetValue(null,10d);
                var behavior=Activator.CreateInstance(T("SubsystemScGunBlockBehavior"));Set(behavior,"m_time",new SubsystemTime());var state=Activator.CreateInstance(T("SubsystemScGunBlockBehavior").GetNestedType("GunState",BindingFlags.NonPublic),true);
                var scheduled=(IList)F(state,"Scheduled");scheduled.Add((9d,"stale"));scheduled.Add((10d,"due"));scheduled.Add((11d,"future"));T("SubsystemScGunBlockBehavior").GetMethod("PlayScheduled",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(behavior,[null,state,0d]);return scheduled.Count==1&&((ValueTuple<double,string>)scheduled[0]).Item1==11;
            });
            Test("presentation-schedules-on-animation-clock-and-cancels-inspect",()=>{
                clock.GetField("Virtual").SetValue(null,true);clock.GetField("VirtualNow").SetValue(null,100d);SettingsManager.SoundsVolume=0;
                var behavior=Activator.CreateInstance(T("SubsystemScGunBlockBehavior"));Set(behavior,"m_time",new SubsystemTime{m_gameTime=42});var state=Activator.CreateInstance(T("SubsystemScGunBlockBehavior").GetNestedType("GunState",BindingFlags.NonPublic),true);
                T("SubsystemScGunBlockBehavior").GetMethod("Schedule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(behavior,[state,"ak47","inspect",42d,false]);
                var cues=((IEnumerable)F(state,"Scheduled")).Cast<(double At,string Name)>().ToArray();if(cues.Length==0||cues.Any(c=>c.At<100||c.At>110))return false;
                Set(state,"InspectSoundToken",99L);T("SubsystemScGunBlockBehavior").GetMethod("PlayScheduled",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(behavior,[null,state,42d]);return ((IList)F(state,"Scheduled")).Count==0;
            });
            Test("world-size-read-only",()=>{string dir=Path.Combine(Path.GetTempPath(),"size-report-"+Guid.NewGuid());Directory.CreateDirectory(dir);try{File.WriteAllBytes(Path.Combine(dir,"Project.xml"),new byte[1048576]);File.WriteAllBytes(Path.Combine(dir,"upgrade.snapshot"),new byte[2097152]);string report=(string)Call("ScWorldSizeReport","Read","system:"+dir);return report.Contains("3.00 MiB")&&report.Contains("2.00 MiB")&&Directory.GetFiles(dir).Length==2&&new FileInfo(Path.Combine(dir,"Project.xml")).Length==1048576;}finally{Directory.Delete(dir,true);}});
        } finally {current.SetValue(null,old);resolve.SetValue(null,oldResolve);clock.GetField("Virtual").SetValue(null,virtualOld);clock.GetField("VirtualNow").SetValue(null,timeOld);SettingsManager.SoundsVolume=volume;BlocksManager.BlockTypeToIndex.Clear();foreach(var p in types)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;BlocksManager.BlockNameToIndex.Clear();foreach(var p in names)BlocksManager.BlockNameToIndex[p.Key]=p.Value;}
        return result;
    }
}
