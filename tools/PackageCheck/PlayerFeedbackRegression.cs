using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class PlayerFeedbackRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Inventory(int slots=8) : IInventory {
        public readonly int[] Values=new int[slots],Counts=new int[slots];
        public int FailAdd=-1; public bool ThrowAfterAdd;
        public Project Project=>null;public int SlotsCount=>Values.Length;public int VisibleSlotsCount{get;set;}public int ActiveSlotIndex{get;set;}
        public int GetSlotValue(int i)=>Values[i];public int GetSlotCount(int i)=>Counts[i];
        public int GetSlotCapacity(int i,int v)=>v==201?1:40;
        public int GetSlotProcessCapacity(int i,int v)=>0;
        public void AddSlotItems(int i,int v,int n) {
            if(i==FailAdd){FailAdd=-1;if(!ThrowAfterAdd)throw new Exception("fixture add refused");}
            if(Counts[i]>0&&Values[i]!=v||Counts[i]+n>GetSlotCapacity(i,v))throw new Exception("invalid stack");
            Values[i]=v;Counts[i]+=n;
            if(ThrowAfterAdd){ThrowAfterAdd=false;throw new Exception("fixture mutation before throw");}
        }
        public int RemoveSlotItems(int i,int n){n=Math.Min(n,Counts[i]);Counts[i]-=n;return n;}
        public void ProcessSlotItems(int i,int v,int n,int p,out int rv,out int rn){rv=0;rn=0;}
        public void DropAllItems(Vector3 p)=>Array.Clear(Counts);
        public string Snapshot()=>string.Join(";",Values.Select((v,i)=>Counts[i]>0?$"{v}:{Counts[i]}":"empty"));
        public void Put(int i,int v,int n){Values[i]=v;Counts[i]=n;}
    }
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Func<bool> test){try{results.Add(new("player-feedback/"+name,test(),""));}catch(Exception e){results.Add(new("player-feedback/"+name,false,e.ToString()));}}
        object Call(string t,string m,params object[] a)=>mod.GetType("Game."+t).GetMethod(m).Invoke(null,a);
        var regType=mod.GetType("Game.ScGunRegistry");var current=regType.GetField("Current");var old=current.GetValue(null);
        var owner=mod.GetType("Game.ScWeaponCrafting").GetField("RecoveryOwnerOverride",BindingFlags.Static|BindingFlags.NonPublic);var oldOwner=owner.GetValue(null);
        bool Craft(Inventory inv,int output,int count,Dictionary<int,int> cost)=> (bool)Call("ScCraftBatch","TryCraft",inv,output,cost,count);
        try {
            current.SetValue(null,Activator.CreateInstance(regType));owner.SetValue(null,(Func<IInventory,string>)(_=>"test/feedback"));
            Test("merge-with-no-empty-slot",()=>{var i=new Inventory(2);i.Put(0,200,39);i.Put(1,100,20);return Craft(i,200,1,new(){[100]=2})&&i.Counts[0]==40&&i.Counts[1]==18;});
            Test("post-deduction-space-and-overflow",()=>{var i=new Inventory(2);i.Put(0,200,39);i.Put(1,100,4);return Craft(i,200,2,new(){[100]=2})&&i.Counts[0]==40&&i.Values[1]==200&&i.Counts[1]==1;});
            foreach(int count in new[]{1,10,37,100}) Test("batch-exact/"+count,()=>{
                var i=new Inventory(12);int left=count*2;for(int s=0;left>0;s++){int n=Math.Min(40,left);i.Put(s,100,n);left-=n;}
                if(!Craft(i,200,count,new(){[100]=2}))return false;
                return i.Counts.Sum()==count&&i.Values.Where((v,s)=>i.Counts[s]>0).All(v=>v==200);
            });
            Test("independent-weapons",()=>{var i=new Inventory(4);i.Put(0,100,6);return Craft(i,201,3,new(){[100]=2})&&i.Counts.Sum()==3&&i.Counts.Max()==1;});
            foreach(int count in new[]{0,-1,101,int.MaxValue})Test("reject-invalid-quantity/"+count,()=>{var i=new Inventory();i.Put(0,100,40);var before=i.Snapshot();return !Craft(i,200,count,new(){[100]=1})&&i.Snapshot()==before;});
            Test("insufficient-whole-batch-unchanged",()=>{var i=new Inventory();i.Put(0,100,19);var before=i.Snapshot();return !Craft(i,200,10,new(){[100]=2})&&i.Snapshot()==before;});
            Test("no-output-space-unchanged",()=>{var i=new Inventory(2);i.Put(0,200,40);i.Put(1,100,40);var before=i.Snapshot();return !Craft(i,200,1,new(){[100]=1})&&i.Snapshot()==before;});
            Test("overflow-cost-unchanged",()=>{var i=new Inventory();i.Put(0,100,40);var before=i.Snapshot();return !Craft(i,200,100,new(){[100]=int.MaxValue})&&i.Snapshot()==before;});
            foreach(bool after in new[]{false,true})Test("rollback-output-failure/"+after,()=>{var i=new Inventory();i.Put(0,100,20);i.FailAdd=1;i.ThrowAfterAdd=after;var before=i.Snapshot();return !Craft(i,200,3,new(){[100]=2})&&i.Snapshot()==before;});
            Test("repeat-no-free-output",()=>{var i=new Inventory();i.Put(0,100,20);if(!Craft(i,200,10,new(){[100]=2}))return false;var before=i.Snapshot();return !Craft(i,200,10,new(){[100]=2})&&i.Snapshot()==before;});
            Test("creative-only-ten-owned-slots",()=>{
                var i=new ComponentCreativeInventory{OpenSlotsCount=10,VisibleSlotsCount=10};for(int s=0;s<20;s++)i.m_slots.Add(s<10?0:100);
                var m=mod.GetType("Game.ScCraftBatch").GetMethod("TryCraft");
                int output=Terrain.MakeBlockValue(302,0,1023<<6);
                if(!(bool)m.Invoke(null,[i,output,new Dictionary<int,int>(),10]))return false;
                var before=i.m_slots.ToArray();return i.m_slots.Take(10).All(v=>v==output)&&i.m_slots.Skip(10).All(v=>v==100)
                    &&!(bool)m.Invoke(null,[i,output,new Dictionary<int,int>(),1])&&i.m_slots.SequenceEqual(before);
            });
            var charge=mod.GetType("Game.ScC4Charge");
            object New(float fuse){var c=Activator.CreateInstance(charge);charge.GetField("Fuse").SetValue(c,fuse);charge.GetField("Remaining").SetValue(c,fuse);return c;}
            float Remaining(object c)=>(float)charge.GetField("Remaining").GetValue(c);
            bool Tick(object c,float dt)=>(bool)charge.GetMethod("Tick").Invoke(c,[dt]);
            string Cue(object c)=>(string)charge.GetMethod("NextCue").Invoke(c,null);
            foreach(float fuse in new[]{5f,20f,60f,300f})Test("c4-timing-and-single-timbre/"+fuse,()=>{
                var c=New(fuse);int warnings=0,triggers=0,beeps=0;float elapsed=0;
                while(Remaining(c)>0){float dt=Math.Min(.05f,Remaining(c));elapsed+=dt;Tick(c,dt);string cue=Cue(c);if(cue=="c4_warning")warnings++;else if(cue=="c4_trigger_trip")triggers++;else if(cue=="c4_beep2")beeps++;else if(cue is not null)return false;}
                return Math.Abs(elapsed-fuse)<.03&&warnings==1&&triggers==1&&beeps>0&&Cue(c) is null;
            });
            Test("c4-read-rounds-do-not-repeat-warning",()=>{
                object c=New(20);Tick(c,19);if(Cue(c)!="c4_warning")return false;
                for(int round=0;round<2;round++){var d=(ValuesDictionary)charge.GetMethod("Save").Invoke(c,null);var xml=new XElement("Values");d.Save(xml);var copy=new ValuesDictionary();copy.ApplyOverrides(XElement.Parse(xml.ToString()));c=charge.GetMethod("Load").Invoke(null,[copy]);if(Remaining(c)!=1||Cue(c) is not null)return false;}
                return !Tick(c,.95f)&&Cue(c)=="c4_trigger_trip"&&Tick(c,1)&&Cue(c) is null;
            });
            Test("c4-large-step-no-audio-catchup",()=>{var c=New(300);return Tick(c,500)&&Cue(c) is null;});
            Test("c4-pause-invalid-dt",()=>{var c=New(20);return !Tick(c,0)&&!Tick(c,float.NaN)&&!Tick(c,-1)&&Remaining(c)==20;});
            Test("c4-independent-fuses",()=>{var a=New(5);var b=New(60);Tick(a,5);Tick(b,5);return Remaining(a)==0&&Remaining(b)==55;});
            Test("electric-control-duration-cooldown-and-injury",()=>{
                T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                var entity=Blank<Entity>();var body=Blank<ComponentBody>();var health=Blank<ComponentHealth>();var motion=Blank<ComponentLocomotion>();
                body.m_entity=health.m_entity=motion.m_entity=entity;entity.m_components=[body,health,motion];health.Health=1;
                Call("ScElectricStun","Clear");
                bool Apply(float before,float after,double now)=>(bool)Call("ScElectricStun","Apply",body,before,after,now);
                bool Active(double now)=>(bool)Call("ScElectricStun","ActiveAt",entity,now);
                if(Apply(1,1,0)||Apply(1,0,0)||!Apply(1,.9f,0)||motion.StunTime!=2.5f||!Active(2.49)||Active(2.5)||Apply(.9f,.8f,2))return false;
                if(!Apply(.8f,.7f,5)||!Active(7.49))return false;health.Health=0;if(Active(6))return false;Call("ScElectricStun","Clear");return !Active(6);
            });
            Test("electric-explicit-denial-and-duration-cap",()=>{
                var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));var body=new ComponentBody();var motion=new ComponentLocomotion();
                body.m_entity=motion.m_entity=entity;entity.m_components=[body,motion];
                bool Apply(float duration)=>(bool)Call("ScElectricStun","ApplyAttack",body,1f,.9f,0d,duration);
                return !Apply(0)&&!Apply(float.NaN)&&Apply(20)&&motion.StunTime==2.5f;
            });
            Test("electric-standard-attacks-suppressed-until-expiry",()=>{
                var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));var body=new ComponentBody();var health=new ComponentHealth{Health=1};var motion=new ComponentLocomotion();
                var chase=new ComponentChaseBehavior();body.m_entity=health.m_entity=motion.m_entity=chase.m_entity=entity;entity.m_components=[body,health,motion,chase];
                var project=new Project();var time=new SubsystemTime();project.m_subsystems.Add(time);entity.m_project=project;
                Call("ScElectricStun","Clear");Call("ScElectricStun","Apply",body,1f,.9f,0d);
                var loader=(ModLoader)Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
                Attackment Attack(){var a=(Attackment)RuntimeHelpers.GetUninitializedObject(typeof(Attackment));a.Attacker=entity;a.AttackPower=50;a.ImpulseFactor=1;a.StunTimeSet=1;return a;}
                var first=Attack();loader.ProcessAttackment(first);bool hit=true,sound=true;float chaseTime=10;
                loader.OnChaseBehaviorAttacked(chase,10,ref chaseTime,ref hit,ref sound);
                if(first.AttackPower!=0||first.ImpulseFactor!=0||first.StunTimeSet!=0||hit||sound)return false;
                time.m_gameTime=2.5;var next=Attack();loader.ProcessAttackment(next);hit=sound=true;loader.OnChaseBehaviorAttacked(chase,10,ref chaseTime,ref hit,ref sound);
                return next.AttackPower==50&&hit&&sound;
            });
            Test("count-only-migration-preserves-state",()=>{
                var t=mod.GetType("Game.ScGunRecord");var record=Activator.CreateInstance(t);
                void Set(string f,object v)=>t.GetField(f).SetValue(record,v);
                Set("Variant",0);Set("CounterInstalled",true);Set("KillCount",3000L);Set("AppliedGrowthLevel",0);Set("PendingGrowthLevel",-1);Set("GrowthRulesVersion",2);Set("Durability",750);Set("MaxDurability",1500);Set("Rounds",7);
                Call("ScGunGrowthMigration","Convert",record,0d,false,4);
                return (int)t.GetField("AppliedGrowthLevel").GetValue(record)==0&&(int)t.GetField("MaxDurability").GetValue(record)==1500&&(int)t.GetField("Durability").GetValue(record)==750&&(long)t.GetField("KillCount").GetValue(record)==3000&&(int)t.GetField("Rounds").GetValue(record)==7;
            });
        }finally{current.SetValue(null,old);owner.SetValue(null,oldOwner);Call("ScElectricStun","Clear");}
        return results;
    }
}
