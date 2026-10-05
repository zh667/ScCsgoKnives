using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;

// post-mp-bugs-20260930 item 3: the native attack of a CS shot carries a bullet (ProjectileAttackment.Projectile) with the
// members a mod reads there. A probe plays such a mod through the engine's own ProcessAttackment hook, on the delivered
// DLL's real attack path (ScSurvivalBalance.Attack -> ScDamageIndicator.Deliver -> ComponentMiner.AttackBody ->
// Attackment.ProcessAttackment -> ComponentHealth.Injure). No GPU, no world; the target is a bare creature entity.
static class ProjectileContextRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Inventory : IInventory {
        public int[] Values=new int[12], Counts=new int[12];
        public Project Project => null; public int SlotsCount => 12; public int VisibleSlotsCount { get; set; } = 10; public int ActiveSlotIndex { get; set; }
        public int GetSlotValue(int s) => Values[s]; public int GetSlotCount(int s) => Counts[s]; public int GetSlotCapacity(int s,int v) => 100; public int GetSlotProcessCapacity(int s,int v) => 0;
        public void AddSlotItems(int s,int v,int n) { Values[s]=v; Counts[s]+=n; } public int RemoveSlotItems(int s,int n) { n=Math.Min(n,Counts[s]); Counts[s]-=n; return n; }
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn) { rv=rn=0; } public void DropAllItems(Vector3 p) { }
    }
    sealed class Probe : ModLoader {
        public bool Active, RefuseWithoutBullet, RefuseSlow; public int Calls; public readonly List<string> Seen=[]; public Projectile Last; public Attackment LastAttack;
        public override void ProcessAttackment(Attackment attackment) {
            if(!Active) return;
            Calls++; LastAttack=attackment; attackment.EnableHitValueParticleSystem=false; // no particle text in a headless run
            var p=(attackment as ProjectileAttackment)?.Projectile; Last=p;
            Seen.Add(p is null?"null":$"{p.GetType().Name} owner={p.OwnerEntity?.Id} creature={(p.Owner is null?"none":p.Owner.GetType().Name)} value={p.Value} speed={p.Velocity.Length():0.#} pos={p.Position} project={(p.Project is null?"null":"set")}");
            if(RefuseWithoutBullet&&p is null) attackment.AttackPower=0;
            if(RefuseSlow&&p is not null&&p.Velocity.Length()<p.MinVelocityToAttack) attackment.AttackPower=0;
        }
    }
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Action test) { try { test(); results.Add(new("projectile-context/"+name,true,"delivered DLL, engine ProcessAttackment hook, real Injure")); }
            catch(Exception e) { results.Add(new("projectile-context/"+name,false,e.ToString())); } }
        void Require(bool ok,string why) { if(!ok) throw new Exception(why); }
        Type T(string n)=>mod.GetType("Game."+n,true);
        U Blank<U>()=>(U)RuntimeHelpers.GetUninitializedObject(typeof(U));
        var savedTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex); var savedNames=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var probe=new Probe(); ModsManager.ModHooks.TryGetValue("ProcessAttackment",out var oldHook); // RegisterHook only queues until DealWithTempModHooks: install the hook directly
        var hook=new ModsManager.ModHook("ProcessAttackment"); hook.Add(probe); ModsManager.ModHooks["ProcessAttackment"]=hook;
        try {
            foreach(var pair in new[]{("ScKnifeBlock",700),("ScGunBlock",701),("ScGrenadeBlock",702),("ScAmmoBlock",703)}) { BlocksManager.BlockTypeToIndex[T(pair.Item1)]=pair.Item2; BlocksManager.BlockNameToIndex[pair.Item1]=pair.Item2; }
            var specs=((Array)T("GunSpec").GetField("All").GetValue(null)).Cast<object>().ToArray();
            object Spec(string name)=>specs.First(s=>(string)T("GunSpec").GetField("Name").GetValue(s)==name);
            int Variant(string name)=>Array.IndexOf(specs,Spec(name));
            var attack=T("ScSurvivalBalance").GetMethod("Attack");
            var hitsType=T("ScShotHits"); var part=T("ScHitPart");
            object Hits(int pellets,float total,Vector3 point,Vector3 direction){ var h=Activator.CreateInstance(hitsType); for(int i=0;i<pellets;i++) hitsType.GetMethod("Add").Invoke(h,[Enum.Parse(part,"Body"),total/pellets,point,direction]); return h; }
            int fresh=(int)T("GunSpec").GetField("FreshFull").GetValue(null); // fresh v5 data is usable without a registry record (an id would need one)
            void Hold(ComponentPlayer shooter,string gun) { var inv=(Inventory)shooter.ComponentMiner.Inventory; inv.Values[0]=Terrain.MakeBlockValue(701,0,(int)T("GunSpec").GetMethod("WithId").Invoke(null,[Variant(gun),fresh])); inv.Counts[0]=1; inv.ActiveSlotIndex=0; }
            (Project Project,Entity Target,ComponentHealth Health,ComponentBody Body,ComponentPlayer Shooter,ComponentLocomotion Locomotion) World() {
                var project=new Project(); var time=new SubsystemTime{m_gameTime=10}; var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};
                info.WorldSettings.GameMode=GameMode.Survival; info.WorldSettings.IsFriendlyFireEnabled=true; project.m_subsystems.Add(time); project.m_subsystems.Add(info);
                var target=Blank<Entity>(); target.m_project=project; target.Id=7;
                var body=new ComponentBody{Position=new Vector3(0,60,5),Mass=60}; var health=new ComponentHealth{Health=1,AttackResilience=1000f,AttackResilienceFactor=1,FallResilienceFactor=1}; /* the factors are 0 on a bare component (Load sets 1): resilience 0 would make any shot fatal */ var locomotion=new ComponentLocomotion();
                var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health,ComponentLocomotion=locomotion}; health.m_componentCreature=creature;
                target.m_components=[creature,body,health,locomotion]; foreach(var c in target.m_components) c.m_entity=target;
                var shooter=Blank<ComponentPlayer>(); shooter.PlayerData=Blank<PlayerData>(); shooter.PlayerData.PlayerIndex=1; shooter.DisplayName="shooter";
                shooter.m_killVerbs=["shot"]; // Attackment's cause of death (a public string[] in 1.9.3.1)
                shooter.ComponentBody=new ComponentBody{Position=new Vector3(0,60,0),Mass=60}; shooter.ComponentHealth=new ComponentHealth{Health=1,AttackResilienceFactor=1,FallResilienceFactor=1};
                shooter.ComponentMiner=Blank<ComponentMiner>(); shooter.ComponentMiner.Inventory=new Inventory();
                var owner=Blank<Entity>(); owner.m_project=project; owner.Id=1; owner.m_components=[shooter,shooter.ComponentBody,shooter.ComponentHealth]; foreach(var c in owner.m_components) c.m_entity=owner;
                return (project,target,health,body,shooter,locomotion);
            }
            Vector3 point=new(0,61,5), direction=Vector3.UnitZ;
            Test("gun-shot-carries-the-bullet-and-settles-once",()=>{
                var w=World(); Hold(w.Shooter,"ak47"); probe.Active=true; probe.Calls=0; probe.Seen.Clear();
                attack.Invoke(null,[w.Body,w.Shooter,point,direction,15f,10d,false,false,false,null,Hits(1,15,point,direction)]);
                Require(probe.Calls==1,$"hook ran {probe.Calls} times");
                Require(probe.Last is not null&&probe.Last.GetType().Name=="ScBulletProjectile",$"bullet: {probe.Seen.LastOrDefault()}");
                var b=probe.Last; int ammo=(int)T("ScAmmoBlock").GetMethod("Value").Invoke(null,[(int)T("ScReloadTransaction").GetMethod("AmmoKind").Invoke(null,[Spec("ak47")])]);
                Require(b.OwnerEntity?.Id==1&&b.Owner is ComponentPlayer,"owner is not the shooting player: "+probe.Seen.Last());
                Require(b.Value==ammo,$"round {b.Value} is not the gun's ammunition {ammo}");
                Require(Math.Abs(b.Velocity.Length()-120f)<1e-3f&&Vector3.Dot(Vector3.Normalize(b.Velocity),direction)>.9999f,"velocity is not the vanilla bullet's speed along the shot: "+probe.Seen.Last());
                Require(b.Position==point&&ReferenceEquals(b.Project,w.Project)&&Math.Abs(b.AttackPower-15f)<1e-4f&&b.CreationTime==10d,"position/world/power/time wrong: "+probe.Seen.Last());
                Require(Math.Abs((1f-w.Health.Health)-15f/1000f)<1e-5f,$"health lost {1f-w.Health.Health}, expected exactly one settlement of 0.015");
                Require(probe.LastAttack.GetType().Name=="GunAttack","attack type "+probe.LastAttack.GetType().Name);
            });
            Test("a-mod-refusing-shots-without-a-bullet-accepts-ours-and-refuses-a-bare-attack",()=>{
                var w=World(); Hold(w.Shooter,"ak47"); probe.Active=true; probe.RefuseWithoutBullet=true; probe.Calls=0;
                attack.Invoke(null,[w.Body,w.Shooter,point,direction,15f,10d,false,false,false,null,Hits(1,15,point,direction)]);
                Require(Math.Abs((1f-w.Health.Health)-.015f)<1e-5f,"the bullet did not satisfy the refusing mod");
                float before=w.Health.Health;
                ComponentMiner.AttackBody(new ProjectileAttackment(w.Body,w.Shooter.Entity,point,direction,15f,null){EnableHitValueParticleSystem=false});
                Require(w.Health.Health==before,"a bare attack without a bullet was not refused by the probe (the probe itself is broken)");
                Require(probe.Calls==2,$"hook ran {probe.Calls} times");
            });
            Test("shotgun-pellets-are-one-attack-one-bullet",()=>{
                var w=World(); Hold(w.Shooter,"nova"); probe.Active=true; probe.RefuseWithoutBullet=false; probe.Calls=0;
                attack.Invoke(null,[w.Body,w.Shooter,point,direction,22f,10d,false,false,false,null,Hits(9,22,point,direction)]);
                Require(probe.Calls==1,$"hook ran {probe.Calls} times for nine pellets");
                Require(Math.Abs((1f-w.Health.Health)-22f/1000f)<1e-5f,$"health lost {1f-w.Health.Health}, expected one settlement of 0.022");
                int shell=(int)T("ScAmmoBlock").GetMethod("Value").Invoke(null,[(int)T("ScReloadTransaction").GetMethod("AmmoKind").Invoke(null,[Spec("nova")])]);
                Require(probe.Last?.Value==shell,"the shotgun's round is not its shell");
            });
            Test("zeus-is-an-electric-bolt-with-its-own-attack",()=>{
                var w=World(); Hold(w.Shooter,"taser"); probe.Active=true; probe.Calls=0;
                attack.Invoke(null,[w.Body,w.Shooter,point,direction,150f,10d,false,true,false,null,Hits(1,150,point,direction)]);
                Require(probe.Calls==1&&probe.LastAttack.GetType().Name=="ElectricAttack","attack type "+probe.LastAttack?.GetType().Name);
                var b=probe.Last; Require(b is not null&&(bool)b.GetType().GetProperty("Electric").GetValue(b),"not marked electric: "+probe.Seen.LastOrDefault());
                Require(Math.Abs(b.Velocity.Length()-60f)<1e-3f,"the bolt's speed is not the musket ball's: "+probe.Seen.Last());
                Require(Terrain.ExtractContents(b.Value)==701,"the Zeus's round is not the Zeus itself: value "+b.Value);
                Require(Math.Abs((1f-w.Health.Health)-.15f)<1e-5f,$"health lost {1f-w.Health.Health}");
            });
            Test("a-slow-bullet-gate-passes-the-bullet",()=>{
                var w=World(); Hold(w.Shooter,"ak47"); probe.Active=true; probe.RefuseSlow=true; probe.Calls=0;
                attack.Invoke(null,[w.Body,w.Shooter,point,direction,15f,10d,false,false,false,null,Hits(1,15,point,direction)]);
                Require(Math.Abs((1f-w.Health.Health)-.015f)<1e-5f,"a MinVelocityToAttack gate refused the bullet"); probe.RefuseSlow=false;
            });
            Test("knife-stays-a-melee-attack",()=>{
                var w=World(); probe.Active=true; probe.Calls=0; ((Inventory)w.Shooter.ComponentMiner.Inventory).Values[0]=Terrain.MakeBlockValue(700,0,0); ((Inventory)w.Shooter.ComponentMiner.Inventory).Counts[0]=1;
                attack.Invoke(null,[w.Body,w.Shooter,point,direction,2f,10d,true,false,false,null,null]);
                Require(probe.Calls==1&&probe.LastAttack is MeleeAttackment&&probe.Last is null,"knife attack "+probe.LastAttack?.GetType().Name+" bullet "+probe.Seen.LastOrDefault());
            });
            Test("no-second-bullet-is-fired-or-registered",()=>{
                var bullet=T("ScBulletProjectile"); var calls=CombatRegression.Calls(attack).Concat(CombatRegression.Calls(bullet.GetMethod("For"))).Select(m=>m.Name).ToHashSet();
                Require(!calls.Overlaps(["AddProjectile","FireProjectile","FireProjectileFast","CreateProjectile"]),"the attack path adds or fires a projectile: "+string.Join(",",calls.Where(n=>n.Contains("Projectile"))));
                var w=World(); Hold(w.Shooter,"ak47"); probe.Active=true; attack.Invoke(null,[w.Body,w.Shooter,point,direction,15f,10d,false,false,false,null,Hits(1,15,point,direction)]);
                var b=probe.Last; Require(b.NoChunk&&!b.BodyCollidable&&!b.TerrainCollidable&&b.ProjectileStoppedAction==ProjectileStoppedAction.Disappear&&b.Damping==0,"the bullet could live on as a projectile");
                Require(!(bool)T("ScSurvivalBalance").GetNestedType("BulletAttack").IsAbstract,"BulletAttack shape changed");
            });
        } finally {
            probe.Active=false; if(oldHook!=null) ModsManager.ModHooks["ProcessAttackment"]=oldHook; else ModsManager.ModHooks.Remove("ProcessAttackment");
            BlocksManager.BlockTypeToIndex.Clear(); foreach(var p in savedTypes) BlocksManager.BlockTypeToIndex[p.Key]=p.Value;
            BlocksManager.BlockNameToIndex.Clear(); foreach(var p in savedNames) BlocksManager.BlockNameToIndex[p.Key]=p.Value;
        }
        return results;
    }
}
