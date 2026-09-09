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
        return results;
    }
}
