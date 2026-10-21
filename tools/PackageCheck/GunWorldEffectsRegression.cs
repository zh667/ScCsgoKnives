using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;

static class GunWorldEffectsRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class TerrainProbe : SubsystemTerrain {
        public int Destroyed;
        public bool NativeFlags=true;
        public override void DestroyCell(int toolLevel,int x,int y,int z,int newValue,bool noDrop,bool noParticleSystem,MovingBlock movingBlock=null) {
            NativeFlags &= toolLevel==0 && newValue==0 && noDrop && !noParticleSystem;
            Destroyed++;Terrain.SetCellValueFast(x,y,z,newValue);
        }
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    sealed class Audio:SubsystemAudio {public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay){}public override void PlayRandomSound(string n,float v,float pitch,Vector3 p,float d,bool delay){}}
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void T(string name,Func<bool> test){try{results.Add(new("world-effects/"+name,test(),name));}catch(Exception e){results.Add(new("world-effects/"+name,false,e.ToString()));}}
        object Call(string type,string method,params object[] args)=>mod.GetType("Game."+type).GetMethod(method).Invoke(null,args);
        foreach(bool creative in new[]{false,true})foreach(bool silenced in new[]{false,true}) T($"noise-native-run-away/{creative}/{silenced}",()=>{
            var bodies=new SubsystemBodies();var noise=new SubsystemNoise{m_subsystemBodies=bodies};
            (ComponentBody,ComponentRunAwayBehavior) Animal(float distance){
                var body=new ComponentBody{Position=new Vector3(distance,0,0)};var listener=new ComponentRunAwayBehavior();
                var entity=Blank<Entity>();entity.m_components=[body,listener];body.m_entity=entity;listener.m_entity=entity;bodies.AddBody(body);return(body,listener);
            }
            var near=Animal(5);var middle=Animal(20);var far=Animal(45);
            Call("ScGunWorldEffects","NotifyNoise",noise,Vector3.Zero,silenced,false);
            return near.Item2.m_heardNoise && middle.Item2.m_heardNoise==!silenced && !far.Item2.m_heardNoise && near.Item2.m_lastNoiseSourcePosition==Vector3.Zero;
        });
        T("noise-native-bird-listener",()=>{
            var bodies=new SubsystemBodies();var noise=new SubsystemNoise{m_subsystemBodies=bodies};
            var body=new ComponentBody{Position=new Vector3(4,0,0)};var bird=new ComponentFlyAwayBehavior{AffectedByNoise=true};
            bird.m_stateMachine.AddState("Idle",null,null,null);bird.m_stateMachine.AddState("DangerDetected",null,null,null);bird.m_stateMachine.TransitionTo("Idle");
            var entity=Blank<Entity>();entity.m_components=[body,bird];body.m_entity=entity;bird.m_entity=entity;bodies.AddBody(body);
            Call("ScGunWorldEffects","NotifyNoise",noise,Vector3.Zero,false,false);
            return bird.m_stateMachine.CurrentState=="DangerDetected";
        });
        T("resilience-native-leaves-only",()=>{
            var leaves=new OakLeavesBlock{ProjectileResilience=.25f};
            return !(bool)Call("ScGunWorldEffects","ShouldBreakLeaf",leaves,710,.2f)
                && (bool)Call("ScGunWorldEffects","ShouldBreakLeaf",leaves,710,.9f)
                && !(bool)Call("ScGunWorldEffects","ShouldBreakLeaf",new OakWoodBlock(),711,.9f);
        });
        foreach(bool creative in new[]{false,true}) T("leaf-trace-before-target-and-wall/"+creative,()=>{
            var old=new[]{BlocksManager.Blocks[710],BlocksManager.Blocks[711]};
            using var terrain=new Terrain();terrain.AllocateChunk(0,0);var sub=new TerrainProbe{Terrain=terrain};
            try{
                BlocksManager.Blocks[710]=new OakLeavesBlock{ProjectileResilience=.1f};BlocksManager.Blocks[711]=new OakWoodBlock();
                terrain.SetCellValueFast(2,10,1,710);terrain.SetCellValueFast(4,10,1,710);terrain.SetCellValueFast(6,10,1,711);terrain.SetCellValueFast(8,10,1,710);
                var trace=Activator.CreateInstance(mod.GetType("Game.ScGunRange+BulletTrace"));
                var hit=(TerrainRaycastResult?)Call("ScGunRange","TraceBullet",sub,new Vector3(.5f,10.5f,1.5f),Vector3.UnitX,12f,trace);
                object leaves=trace.GetType().GetField("Leaves").GetValue(trace);var visited=new HashSet<Point3>();
                int count=(int)Call("ScGunWorldEffects","BreakLeaves",sub,leaves,3f,visited,(Func<float>)(()=>.9f)); // target before x4
                if(count!=1 || terrain.GetCellValue(2,10,1)!=0 || terrain.GetCellValue(4,10,1)!=710 || terrain.GetCellValue(8,10,1)!=710)return false;
                int duplicate=(int)Call("ScGunWorldEffects","BreakLeaves",sub,leaves,3f,visited,(Func<float>)(()=>.9f));
                return hit?.CellFace.X==6 && duplicate==0 && sub.Destroyed==1 && sub.NativeFlags && terrain.GetCellValue(6,10,1)==711;
            }finally{BlocksManager.Blocks[710]=old[0];BlocksManager.Blocks[711]=old[1];}
        });
        // 2026-10-21 player request ("希望动物听到手雷爆炸声会吓到逃跑"): grenade and C4 bangs make the engine's explosion noise,
        // which the native run-away behaviour hears (loudness 1); before, they only played a sound and animals stayed put.
        (SubsystemBodies Bodies,SubsystemNoise Noise) Ears(){var bodies=new SubsystemBodies();return(bodies,new SubsystemNoise{m_subsystemBodies=bodies});}
        ComponentRunAwayBehavior Animal(Project project,SubsystemBodies bodies,Vector3 at){
            var body=new ComponentBody{Position=at};var listener=new ComponentRunAwayBehavior();
            var entity=Blank<Entity>();entity.m_project=project;entity.m_components=[body,listener];body.m_entity=entity;listener.m_entity=entity;bodies.AddBody(body);return listener;
        }
        float Range(string name)=>(float)mod.GetType("Game.ScGunWorldEffects").GetField(name).GetValue(null);
        foreach(var (name,heard,missed) in new[]{("HeNoiseRange",45f,55f),("FlashNoiseRange",35f,45f),("BombNoiseRange",65f,75f)}) T("noise-explosion-range/"+name,()=>{
            var (bodies,noise)=Ears();var near=Animal(null,bodies,new Vector3(heard,0,0));var far=Animal(null,bodies,new Vector3(missed,0,0));
            Call("ScGunWorldEffects","NotifyExplosion",noise,Vector3.Zero,Range(name));
            // heard, and scared off: the engine's "attacked" importance (100) is above a cow's cattle drive (7) and its
            // herding (up to 50), so the run-away behaviour wins and runs from the blast; out of range nothing changes
            return near.m_heardNoise && !far.m_heardNoise && near.m_lastNoiseSourcePosition==Vector3.Zero
                && near.m_importanceLevel==100 && far.m_importanceLevel==0 && far.m_lastNoiseSourcePosition==null;
        });
        // the real detonation of each kind: HE and the flashbang bang (heard 30 blocks away, outside every damage radius);
        // smoke, the molotov's airburst and the decoy do not
        foreach(int kind in new[]{0,1,2,3,5}) T($"grenade-detonation-noise/{kind}",()=>{
            var project=new Project();var (bodies,noise)=Ears();var time=new SubsystemTime();var terrain=new SubsystemTerrain{Terrain=new Terrain()};
            var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};var players=new SubsystemPlayers();
            var type=mod.GetType("Game.SubsystemScGrenades");var grenades=(Subsystem)Activator.CreateInstance(type);
            foreach(var s in new Subsystem[]{time,terrain,bodies,noise,info,players,new Audio(),grenades}){s.m_project=project;project.m_subsystems.Add(s);}
            foreach(var (field,value) in new (string,object)[]{("m_time",time),("m_terrain",terrain),("m_players",players),("m_bodies",bodies),("m_info",info)})
                type.GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(grenades,value);
            var cow=Animal(project,bodies,new Vector3(30,61.5f,0));
            var state=Activator.CreateInstance(mod.GetType("Game.ScGrenadeState"));
            foreach(var (field,value) in new (string,object)[]{("Kind",kind),("Owner",0),("Position",new Vector3(0,61.5f,0))})state.GetType().GetField(field).SetValue(state,value);
            type.GetMethod("Detonate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grenades,[state]);
            return cow.m_heardNoise==(kind is 0 or 1) && cow.m_importanceLevel==(kind is 0 or 1?100:0) && (!cow.m_heardNoise || cow.m_lastNoiseSourcePosition==new Vector3(0,61.5f,0));
        });
        T("c4-detonation-noise",()=>{
            var project=new Project();var (bodies,noise)=Ears();var time=new SubsystemTime();var terrain=new SubsystemTerrain{Terrain=new Terrain()};terrain.Terrain.AllocateChunk(0,0).State=TerrainChunkState.Valid;
            var info=Blank<SubsystemGameInfo>();info.WorldSettings=Blank<WorldSettings>();info.WorldSettings.GameMode=GameMode.Survival;
            var type=mod.GetType("Game.SubsystemScC4");var c4=(Subsystem)Activator.CreateInstance(type);
            foreach(var s in new Subsystem[]{time,new Audio(),new SubsystemPlayers(),info,terrain,bodies,noise,new SubsystemParticles(),c4}){s.m_project=project;project.m_subsystems.Add(s);}
            c4.Load(new TemplatesDatabase.ValuesDictionary());
            var near=Animal(project,bodies,new Vector3(68,61,8));var far=Animal(project,bodies,new Vector3(88,61,8));
            var charge=Activator.CreateInstance(mod.GetType("Game.ScC4Charge"));charge.GetType().GetField("Position").SetValue(charge,new Vector3(8,61,8));
            // the blast's particles need the game's textures, which this runner has only in some suites: the noise comes before them
            try{type.GetMethod("Detonate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c4,[charge]);}
            catch(TargetInvocationException e) when (e.InnerException?.StackTrace?.Contains("ExplosionParticleSystem")==true){}
            return near.m_heardNoise && !far.m_heardNoise && near.m_importanceLevel==100 && far.m_importanceLevel==0;
        });
        return results;
    }
}
