using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class DecoyRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Idle : ComponentBehavior {public override float ImportanceLevel=>5;}
    sealed class Fixture {
        public Entity Entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));
        public ComponentCreature Creature=new(); public ComponentBody Body=new(); public ComponentHealth Health=new(){Health=1};
        public ComponentPathfinding Path=new();public ComponentBehaviorSelector Selector=new();public ComponentChaseBehavior Chase=new();
        public SubsystemTime Time=new(); public ComponentBehavior Decoy;public Project Project=new();
        public Fixture(Assembly mod,CreatureCategory category) {
            Decoy=(ComponentBehavior)Activator.CreateInstance(mod.GetType("Game.ComponentScDecoyBehavior"));
            Creature.ComponentBody=Body;Creature.ComponentHealth=Health;Creature.Category=category;
            Path.m_componentPilot=new ComponentPilot{m_componentCreature=Creature};
            Entity.m_components=[Creature,Body,Health,Path,Selector,Chase,Decoy,new Idle()];Entity.m_project=Project;
            foreach(var c in Entity.m_components)c.m_entity=Entity;
            Project.m_subsystems.Add(Time);Selector.Load(new(),null);Decoy.Load(new(),null);
        }
        public void Hear()=>Decoy.GetType().GetMethod("HearDecoy").Invoke(Decoy,[new Vector3(8,0,0)]);
        public void Step(){Selector.Update(.1f);((IUpdateable)Decoy).Update(.1f);}
    }
    internal static List<Result> Run(Assembly mod) {
        List<Result> result=[];
        void Test(string n,Func<bool> f){try{result.Add(new("decoy-v112/"+n,f(),""));}catch(Exception e){result.Add(new("decoy-v112/"+n,false,e.ToString()));}}
        foreach(var category in new[]{CreatureCategory.LandPredator,CreatureCategory.LandOther,CreatureCategory.Bird})Test("native-selector-and-path/"+category,()=>{
            var f=new Fixture(mod,category);f.Hear();f.Step();
            if(!f.Decoy.IsActive||f.Path.Destination?.X!=8||f.Path.Speed!=.7f)return false;
            f.Time.m_gameTime=8;f.Hear();f.Time.m_gameTime=10;f.Step();if(f.Decoy.ImportanceLevel!=0||f.Path.Destination is not null)return false;
            f.Time.m_gameTime=17;f.Hear();if(f.Decoy.ImportanceLevel!=0)return false;
            f.Time.m_gameTime=18;f.Hear();f.Step();return f.Decoy.IsActive;
        });
        Test("invalid-attempt-does-not-spend-cooldown",()=>{var f=new Fixture(mod,CreatureCategory.LandOther);f.Health.Health=.4f;f.Hear();f.Health.Health=1;f.Hear();f.Step();return f.Decoy.IsActive;});
        foreach(float flySpeed in new[]{0f,8f})Test("bird-category-navigation/"+flySpeed,()=>{
            var f=new Fixture(mod,CreatureCategory.Bird);
            var locomotion=new ComponentLocomotion{FlySpeed=flySpeed};locomotion.m_entity=f.Entity;f.Entity.m_components.Add(locomotion);
            f.Hear();f.Step();return f.Path.Destination==new Vector3(8,flySpeed>0?2:0,0);
        });
        Test("injury-releases-control",()=>{var f=new Fixture(mod,CreatureCategory.LandPredator);f.Hear();f.Step();f.Health.Health=.9f;f.Step();return !f.Decoy.IsActive&&f.Path.Destination is null;});
        Test("distant-chase-diverted-close-chase-kept",()=>{
            var f=new Fixture(mod,CreatureCategory.LandPredator);var target=new ComponentCreature{ComponentBody=new ComponentBody{Position=new Vector3(10,0,0)}};
            f.Chase.m_target=target;f.Chase.m_importanceLevel=200;f.Hear();f.Step();if(!f.Decoy.IsActive||f.Chase.Target!=target)return false;
            target.ComponentBody.Position=Vector3.UnitX;f.Step();return !f.Decoy.IsActive&&f.Chase.IsActive&&f.Chase.Target==target;
        });
        Test("water-target-does-not-walk-onto-dry-land",()=>{var f=new Fixture(mod,CreatureCategory.WaterPredator);f.Hear();f.Step();return !f.Decoy.IsActive&&f.Path.Destination is null;});
        Test("aquatic-target-stays-at-swimming-depth",()=>{
            var f=new Fixture(mod,CreatureCategory.WaterOther);f.Body.Position=new Vector3(0,10,0);
            var old=BlocksManager.Blocks[710];using var terrain=new Terrain();terrain.AllocateChunk(0,0);
            try{BlocksManager.Blocks[710]=new WaterBlock();terrain.SetCellValueFast(8,10,0,710);
                f.Decoy.GetType().GetField("m_terrain",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(f.Decoy,new SubsystemTerrain{Terrain=terrain});
                f.Hear();f.Step();return f.Decoy.IsActive&&f.Path.Destination==new Vector3(8,10,0);
            }finally{BlocksManager.Blocks[710]=old;}
        });
        Test("yield-does-not-delete-another-ai-destination",()=>{
            var f=new Fixture(mod,CreatureCategory.LandOther);f.Hear();f.Step();f.Decoy.IsActive=false;f.Path.Destination=new Vector3(20,0,0);
            ((IUpdateable)f.Decoy).Update(.1f);return f.Path.Destination==new Vector3(20,0,0)&&f.Decoy.ImportanceLevel==0;
        });
        Test("missing-custom-ai-path-is-inert",()=>{var f=new Fixture(mod,CreatureCategory.LandOther);f.Entity.m_components=f.Entity.m_components.Where(c=>c!=f.Path).ToList();f.Decoy.Load(new(),null);f.Hear();return f.Decoy.ImportanceLevel==0;});
        Test("saved-cooldown-two-xml-rounds",()=>{
            var f=new Fixture(mod,CreatureCategory.LandOther);f.Hear();
            for(int round=0;round<2;round++) {var d=new ValuesDictionary();f.Decoy.Save(d,null);var xml=new System.Xml.Linq.XElement("Values");d.Save(xml);var read=new ValuesDictionary();read.ApplyOverrides(System.Xml.Linq.XElement.Parse(xml.ToString()));
                f=new Fixture(mod,CreatureCategory.LandOther);f.Decoy.Load(read,null);f.Hear();if(f.Decoy.ImportanceLevel!=0)return false;}
            f.Time.m_gameTime=18;f.Hear();return f.Decoy.ImportanceLevel>0;
        });
        var growth=mod.GetType("Game.ScGunGrowth");var guns=(Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null);
        for(int variant=0;variant<guns.Length;variant++){int v=variant;Test("linear-rate/"+v,()=>{
            bool sniper=(bool)growth.GetMethod("IsBoltSniper").Invoke(null,[v]);
            for(int l=0;l<=50;l++) {float expected=sniper?1f+.05f*Math.Clamp(l-10,0,10)+.05f*Math.Clamp(l-20,0,10)+.075f*Math.Clamp(l-30,0,10)+.075f*Math.Clamp(l-40,0,10):1+l*.01f;
                float actual=(float)growth.GetMethod("FireRateMultiplier",[typeof(int),typeof(int)]).Invoke(null,[v,l]);if(Math.Abs(actual-expected)>.00001f)return false;}
            return true;
        });}
        return result;
    }
}
