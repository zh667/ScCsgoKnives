using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class StarterEquipmentRegression {
    internal record Result(string Name,bool Ok,string Detail);
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    internal static List<Result> Run(Assembly mod){
        var results=new List<Result>();
        void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
        void Test(string name,Action action){try{action();results.Add(new("starter/"+name,true,""));}catch(Exception e){results.Add(new("starter/"+name,false,e.ToString()));}}
        Type T(string name)=>mod.GetType("Game."+name,true);
        object Plan(int n)=>Enum.ToObject(T("ScStarterPlan"),n);
        var oldTypes=BlocksManager.BlockTypeToIndex.ToArray();var oldNames=BlocksManager.BlockNameToIndex.ToArray();var oldBlocks=(Block[])BlocksManager.Blocks.Clone();
        var registryField=T("ScGunRegistry").GetField("Current");var oldRegistry=registryField.GetValue(null);
        try{
            foreach(var (name,index) in new[]{("ScKnifeBlock",700),("ScGunBlock",701)}){
                var type=T(name);var block=(Block)Activator.CreateInstance(type);block.BlockIndex=index;block.MaxStacking=1;
                BlocksManager.Blocks[index]=block;BlocksManager.BlockTypeToIndex[type]=index;BlocksManager.BlockNameToIndex[name]=index;
            }
            foreach(var mode in Enum.GetValues<GameMode>())foreach(var spawn in Enum.GetValues<PlayerData.SpawnMode>())foreach(int count in new[]{0,1,2}){
                Test($"eligibility/{mode}/{spawn}/{count}",()=>Require((bool)T("SubsystemScStarterEquipment").GetMethod("Eligible").Invoke(null,[mode,spawn,count])
                    ==(mode is GameMode.Harmless or GameMode.Survival or GameMode.Challenging or GameMode.Cruel&&spawn is PlayerData.SpawnMode.InitialIntro or PlayerData.SpawnMode.InitialNoIntro&&count==1),"unexpected eligibility"));
            }
            foreach(int plan in new[]{0,1,2,3})foreach(bool full in new[]{false,true})Test($"choice-save-reload-once/{plan}/full-{full}",()=>{
                var project=new Project();var players=new SubsystemPlayers{m_project=project};var info=new SubsystemGameInfo{m_project=project,WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=GameMode.Survival;
                project.m_subsystems.Add(players);project.m_subsystems.Add(info);
                var inventory=new ComponentInventory();for(int i=0;i<4;i++)inventory.m_slots.Add(full?new(){Value=700,Count=1}:new());
                var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=3;
                player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentMiner=new ComponentMiner{Inventory=inventory};
                var entity=Blank<Entity>();entity.m_project=project;entity.m_isAddedToProject=true;entity.m_components=[player,inventory];player.m_entity=entity;inventory.m_entity=entity;
                project.m_entities[entity]=true;players.m_componentPlayers.Add(player);
                dynamic starter=Activator.CreateInstance(T("SubsystemScStarterEquipment"));((Subsystem)starter).m_project=project;
                dynamic registry=Activator.CreateInstance(T("ScGunRegistry"));registryField.SetValue(null,(object)registry);
                bool Queue()=>starter.TryGrant(GameMode.Survival,PlayerData.SpawnMode.InitialNoIntro,3,1,inventory,(Action<int,int>)((_,_)=>throw new Exception("premature gift")));
                Require(Queue()&&!Queue(),"queue repeats");Require(inventory.m_slots.Sum(s=>s.Count)==(full?4:0),"items before confirmation");
                var saved=new ValuesDictionary();starter.Save(saved);starter=Activator.CreateInstance(T("SubsystemScStarterEquipment"));((Subsystem)starter).m_project=project;starter.Load(saved);
                Require(starter.Pending(3)&&starter.Choose(player,(dynamic)Plan(plan)),"lost pending choice");
                Require(!starter.Choose(player,(dynamic)Plan(plan))&&!Queue(),"duplicate confirmation gave items");
                Require(inventory.m_slots.Sum(s=>s.Count)==(full?4:plan),"wrong inventory mutation");
                if(full&&plan>0)Require(registry.Recovery.Count==1,"full inventory lost durable gift");
                starter.Save(saved);
                var recovery=(ValuesDictionary)registry.Recovery.Save();
                for(int round=0;round<2;round++){
                    starter=Activator.CreateInstance(T("SubsystemScStarterEquipment"));((Subsystem)starter).m_project=project;starter.Load(saved);
                    Require(starter.Granted(3)&&!starter.Pending(3)&&!Queue(),"save/reload resets claim");starter.Save(saved);
                    dynamic restored=T("ScGunRecovery").GetMethod("Load").Invoke(null,[recovery]);recovery=restored.Save();
                }
                if(full){
                    foreach(var slot in inventory.m_slots){slot.Count=0;slot.Value=0;}
                    dynamic restored=T("ScGunRecovery").GetMethod("Load").Invoke(null,[recovery]);
                    restored.Retry((Func<string,IInventory>)(owner=>owner=="player/3"?inventory:null));
                    Require(inventory.m_slots.Sum(s=>s.Count)==plan,"saved pending kit not delivered exactly");
                    Require(restored.Retry((Func<string,IInventory>)(_=>inventory))==0&&inventory.m_slots.Sum(s=>s.Count)==plan,"retry duplicated gift");
                }
                var checkpoint=T("SubsystemScStarterEquipment").GetMethod("TryCheckpoint",BindingFlags.NonPublic|BindingFlags.Instance);
                Require(!(bool)checkpoint.Invoke((object)starter,[true,new[]{3},(Action)(()=>throw new Exception("loaded claim saved twice"))]),"loaded checkpoint repeated");
            });
        }finally{
            registryField.SetValue(null,oldRegistry);BlocksManager.BlockTypeToIndex.Clear();foreach(var p in oldTypes)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;
            BlocksManager.BlockNameToIndex.Clear();foreach(var p in oldNames)BlocksManager.BlockNameToIndex[p.Key]=p.Value;Array.Copy(oldBlocks,BlocksManager.Blocks,oldBlocks.Length);
        }
        return results;
    }
}
