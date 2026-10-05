using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// agent-feedback-20260928 P3: one defuse entry for enemy bombs and player C4, creative kit, and a one-time
// defuse reward recorded as a durable receipt. Headless; the live input/HUD path is TacticalDefuseRegression.
static class TacticalFeedbackRegression {
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static ValuesDictionary Round(ValuesDictionary v){var x=new XElement("Values");v.Save(x);var r=new ValuesDictionary();r.ApplyOverrides(XElement.Parse(x.ToString()));return r;}
    sealed class Gui:ComponentGui {public readonly List<string> Messages=[];public override void DisplaySmallMessage(string text,Color c,bool b,bool s)=>Messages.Add(text);}
    sealed class Audio:SubsystemAudio {public int Count;public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay)=>Count++;}
    sealed class FakeTerrain:SubsystemTerrain {public Func<Vector3,Vector3,TerrainRaycastResult?> Ray;public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool i,bool s,Func<int,float,bool> f)=>Ray(a,b);}
    sealed class Absorb:ModLoader {public override void ProcessAttackment(Attackment attackment){attackment.AttackPower=0;}}
    sealed class PlainHealth:ComponentHealth {public override void Injure(Injury injury){injury.Attackment.EnableHitValueParticleSystem=false;base.Injure(injury);}}
    sealed class Slab:DirtBlock {public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain t,int value)=>[new BoundingBox(Vector3.Zero,new Vector3(1,.5f,1))];}
    internal static List<TacticalRegression.Result> Run(Assembly core,Assembly dlc){
        var results=new List<TacticalRegression.Result>();
        void Test(string n,Action a){try{a();results.Add(new("agent-feedback/"+n,true,""));}catch(Exception ex){results.Add(new("agent-feedback/"+n,false,ex.ToString()));}}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        Type C(string n)=>core.GetType("Game."+n,true);Type T(string n)=>dlc.GetType("Game."+n,true);
        var rf=C("ScGunRegistry").GetField("Current");var originalRegistry=rf.GetValue(null);
        (Project P,dynamic Bombs,Subsystem C4,SubsystemGameInfo Info) World(){
            var p=new Project();var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=GameMode.Survival;
            var bombs=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalBombs"));var c4=(Subsystem)Activator.CreateInstance(C("SubsystemScC4"));
            foreach(var s in new Subsystem[]{new SubsystemTime(),new SubsystemTerrain{Terrain=new Terrain()},new SubsystemPlayers(),new Audio(),new SubsystemBodies(),info,bombs,c4}){s.m_project=p;p.m_subsystems.Add(s);}
            C("SubsystemScC4").GetField("audio",Fields).SetValue(c4,p.FindSubsystem<SubsystemAudio>(true));
            return(p,bombs,c4,info);
        }
        (ComponentPlayer Player,Gui Gui,ComponentInventoryBase Inventory) Player(Project p,int index,bool creative=false){
            var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=index;player.ComponentBody=new ComponentBody();player.ComponentHealth=new ComponentHealth{Health=1};
            var gui=new Gui{m_modalPanelContainerWidget=new CanvasWidget(),ControlsContainerWidget=new CanvasWidget()};player.ComponentGui=gui;
            // Creative players carry the catalogue, not an ordinary slot inventory.
            ComponentInventoryBase inv=creative?null:new ComponentInventory();Component carried=creative?new ComponentCreativeInventory():inv;
            if(inv is not null)for(int i=0;i<36;i++)inv.m_slots.Add(new());player.ComponentMiner=new ComponentMiner{Inventory=(IInventory)carried};
            var e=Blank<Entity>();e.m_project=p;e.m_isAddedToProject=true;e.m_components=[player,gui,player.ComponentBody,player.ComponentHealth,player.ComponentMiner,carried];foreach(var c in e.m_components)c.m_entity=e;p.m_entities[e]=true;
            p.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(player);return(player,gui,inv);
        }
        object Charge(Vector3 at,int owner,float remaining){var c=Activator.CreateInstance(C("ScC4Charge"));C("ScC4Charge").GetField("Position").SetValue(c,at);C("ScC4Charge").GetField("Owner").SetValue(c,owner);C("ScC4Charge").GetField("Remaining").SetValue(c,remaining);return c;}
        System.Collections.IList Charges(Subsystem c4)=>(System.Collections.IList)C("SubsystemScC4").GetField("charges",Fields).GetValue(c4);
        try{
            Test("creative-player-always-has-kit-survival-checks-all-carried-slots",()=>{
                var w=World();var hasKit=T("SubsystemTacticalBombs").GetMethod("HasKit");
                var survivor=Player(w.P,0);Check(!(bool)hasKit.Invoke(null,[survivor.Player]),"kit granted without a tool in survival");
                survivor.Inventory.m_slots[30]=new(){Value=707,Count=1};Check((bool)hasKit.Invoke(null,[survivor.Player]),"backpack tool not counted in survival");
                w.Info.WorldSettings.GameMode=GameMode.Creative;var creative=Player(w.P,1,true);Check((bool)hasKit.Invoke(null,[creative.Player]),"creative player still needs the catalogue tool in the hotbar");
            });
            Test("own-player-c4-is-defusable-others-follow-world-setting-disarm-once",()=>{
                var w=World();var owner=Player(w.P,0);var other=Player(w.P,1);var c=Charge(new Vector3(0,60,0),0,15f);Charges(w.C4).Add(c);
                T("SubsystemTacticalBombs").GetMethod("SyncPlayerCharges",Fields).Invoke((object)w.Bombs,null);
                var proxy=((System.Collections.IEnumerable)w.Bombs.Defusable).Cast<object>().Single();
                Check((bool)proxy.GetType().GetField("PlayerCharge").GetValue(proxy)&&ReferenceEquals(proxy.GetType().GetField("Charge").GetValue(proxy),c),"player C4 not offered through the unified entry");
                Check(((System.Collections.IList)w.Bombs.Bombs).Count==0,"player C4 copied into the enemy bomb save stream");
                var may=T("SubsystemTacticalBombs").GetMethod("MayDefuse");
                Check((bool)may.Invoke((object)w.Bombs,[owner.Player,proxy]),"owner cannot defuse own C4");
                w.Info.WorldSettings.IsFriendlyFireEnabled=false;Check(!(bool)may.Invoke((object)w.Bombs,[other.Player,proxy]),"another player's C4 defusable without world permission");
                w.Info.WorldSettings.IsFriendlyFireEnabled=true;Check((bool)may.Invoke((object)w.Bombs,[other.Player,proxy]),"world permission ignored");
                var disarm=C("SubsystemScC4").GetMethod("Disarm");Check((bool)disarm.Invoke(w.C4,[c])&&Charges(w.C4).Count==0,"disarm did not remove the armed charge");
                Check(!(bool)disarm.Invoke(w.C4,[c]),"disarm reported twice");
                T("SubsystemTacticalBombs").GetMethod("SyncPlayerCharges",Fields).Invoke((object)w.Bombs,null);Check(!((System.Collections.IEnumerable)w.Bombs.Defusable).Cast<object>().Any(),"stale proxy survives a disarmed charge");
                Check((float)C("ScC4Charge").GetField("Power").GetValue(c)==5000f&&(float)C("ScC4Charge").GetField("Radius").GetValue(c)==32f,"unified entry rewrote player C4 power/radius");
            });
            Test("defuse-reward-once-survival-source-only-durable-receipt",()=>{
                foreach(string scenario in new[]{"survival","creative-now","creative-source","old-save","player-c4"}){
                    var registry=Activator.CreateInstance(C("ScGunRegistry"));rf.SetValue(null,registry);
                    var w=World();var p=Player(w.P,0);var reward=T("SubsystemTacticalBombs").GetMethod("Reward",Fields);
                    var bomb=Activator.CreateInstance(T("SubsystemTacticalBombs").GetNestedType("Bomb"));var bt=bomb.GetType();
                    bt.GetField("BombId").SetValue(bomb,scenario=="old-save"?"":"fixture-bomb");bt.GetField("Rewardable").SetValue(bomb,scenario is "survival" or "creative-now" or "player-c4");bt.GetField("PlayerCharge").SetValue(bomb,scenario=="player-c4");
                    if(scenario=="creative-now")w.Info.WorldSettings.GameMode=GameMode.Creative;
                    reward.Invoke((object)w.Bombs,[p.Player,bomb]);
                    var recovery=C("ScGunRegistry").GetProperty("Recovery").GetValue(registry);var batches=((System.Collections.IEnumerable)recovery.GetType().GetProperty("Batches").GetValue(recovery)).Cast<object>().ToArray();
                    if(scenario!="survival"){Check(batches.Length==0,"reward granted for "+scenario);continue;}
                    Check(batches.Length==1,"no durable reward receipt");var steps=((System.Collections.IEnumerable)batches[0].GetType().GetField("Steps").GetValue(batches[0])).Cast<object>().ToArray();
                    int Amount(int kind)=>steps.Where(s=>(int)s.GetType().GetField("Value").GetValue(s)==(int)C("ScWeaponMaterialBlock").GetMethod("Value").Invoke(null,[kind])).Sum(s=>(int)s.GetType().GetField("Count").GetValue(s));
                    Check(Amount(0)==2&&Amount(1)==1&&steps.Length==2,"wrong reward contents");
                    // The receipt is world state: it survives a save/reload before delivery and is delivered exactly once.
                    var saved=(ValuesDictionary)recovery.GetType().GetMethod("Save").Invoke(recovery,null);var reloaded=C("ScGunRecovery").GetMethod("Load").Invoke(null,[Round(saved)]);
                    int done=(int)reloaded.GetType().GetMethod("Retry").Invoke(reloaded,[(Func<string,IInventory>)(_=>(IInventory)p.Inventory)]);
                    int blank=(int)C("ScWeaponMaterialBlock").GetMethod("Value").Invoke(null,[0]),mech=(int)C("ScWeaponMaterialBlock").GetMethod("Value").Invoke(null,[1]);
                    Check(done==1&&p.Inventory.m_slots.Where(s=>s.Value==blank).Sum(s=>s.Count)==2&&p.Inventory.m_slots.Where(s=>s.Value==mech).Sum(s=>s.Count)==1,"receipt not delivered after reload");
                    Check((int)reloaded.GetType().GetMethod("Retry").Invoke(reloaded,[(Func<string,IInventory>)(_=>(IInventory)p.Inventory)])==0&&p.Inventory.m_slots.Sum(s=>s.Count)==3,"reward delivered twice");
                }
            });
            Test("enemy-bomb-id-and-source-save-old-saves-never-rewarded",()=>{
                var w=World();w.Bombs.Load(new ValuesDictionary());
                var bomb=Activator.CreateInstance(T("SubsystemTacticalBombs").GetNestedType("Bomb"));bomb.GetType().GetField("BombId").SetValue(bomb,"abc");bomb.GetType().GetField("Rewardable").SetValue(bomb,true);
                ((System.Collections.IList)w.Bombs.Bombs).Add(bomb);var saved=new ValuesDictionary();w.Bombs.Save(saved);
                var again=World();again.Bombs.Load(Round(saved));var read=((System.Collections.IList)again.Bombs.Bombs).Cast<object>().Single();
                Check((string)read.GetType().GetField("BombId").GetValue(read)=="abc"&&(bool)read.GetType().GetField("Rewardable").GetValue(read),"bomb identity/source lost on save");
                var old=Round(saved);foreach(var entry in old.GetValue<ValuesDictionary>("Bombs").Values.Cast<ValuesDictionary>()){entry.Remove("BombId");}
                var legacy=World();legacy.Bombs.Load(old);var l=((System.Collections.IList)legacy.Bombs.Bombs).Cast<object>().Single();Check(!(bool)l.GetType().GetField("Rewardable").GetValue(l),"old bomb without identity guessed as rewardable");
                w.Bombs.Dispose();again.Bombs.Dispose();legacy.Bombs.Dispose();
            });
            // P2: a companion walking at 0.7 never triggers the native random jump; a real step must be detected.
            Test("step-probe-flat-slab-block-wall-ceiling-and-bounded-jump",()=>{
                var terrain=new SubsystemTerrain{Terrain=new Terrain()};terrain.Terrain.AllocateChunk(0,0).State=TerrainChunkState.Valid;
                var oldBlocks=(Block[])BlocksManager.Blocks.Clone();
                try{
                    BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};BlocksManager.Blocks[62]=new Slab{BlockIndex=62,IsCollidable=true};
                    for(int x=4;x<14;x++)for(int z=4;z<14;z++)terrain.Terrain.SetCellValueFast(x,60,z,2);
                    var body=new ComponentBody{BoxSize=new Vector3(.65f,1.8f,.65f),Position=new Vector3(8.5f,61,8.5f),MaxSmoothRiseHeight=.51f,Rotation=Quaternion.CreateFromYawPitchRoll(-MathF.PI/2,0,0)};
                    var nav=T("TacticalNavigation");var probe=nav.GetMethod("Probe");string Probe()=>probe.Invoke(null,[terrain,body,new Vector2(1,0)]).ToString();
                    void Set(int x,int y,int z,int v)=>terrain.Terrain.SetCellValueFast(x,y,z,v);
                    Check(Probe()=="Clear","flat ground reported as an obstacle");
                    Set(9,61,8,62);Check(Probe()=="Smooth","half slab should use native smooth rise, not a jump");
                    Set(9,61,8,2);Check(Probe()=="Jump","one-block step not recognised as climbable");
                    Set(9,62,8,2);Check(Probe()=="Wall","two-block wall treated as a step");Set(9,62,8,0);
                    Set(9,63,8,2);Check(Probe()=="Wall","step under a low ceiling treated as climbable");Set(9,63,8,0);
                    Set(8,63,8,2);Check(Probe()=="Wall","jump ordered with a block over the agent's head");Set(8,63,8,0);
                    var creature=new ComponentCreature{ComponentBody=body,ComponentLocomotion=new ComponentLocomotion()};var assist=nav.GetMethod("StepAssist");
                    object[] args=[creature,terrain,(Vector3?)new Vector3(14,61,8.5f),0d,1d];
                    Check(!(bool)assist.Invoke(null,args),"jump ordered while airborne");body.StandingOnValue=2;
                    Check((bool)assist.Invoke(null,args)&&creature.ComponentLocomotion.JumpOrder==1,"no jump at a detected step");
                    creature.ComponentLocomotion.JumpOrder=0;args[4]=1.2d;Check(!(bool)assist.Invoke(null,args)&&creature.ComponentLocomotion.JumpOrder==0,"jump repeated inside the cooldown");
                    Set(9,61,8,0);args[4]=5d;Check(!(bool)assist.Invoke(null,args),"jump ordered on flat ground");
                }finally{Array.Copy(oldBlocks,BlocksManager.Blocks,oldBlocks.Length);}
            });
            Test("navigation-replans-only-on-real-change",()=>{
                var p=new Project();var time=new SubsystemTime();time.m_project=p;p.m_subsystems.Add(time);
                var creature=new ComponentCreature{ComponentBody=new ComponentBody(),ComponentLocomotion=new ComponentLocomotion()};var path=new ComponentPathfinding{m_componentPilot=new ComponentPilot{m_componentCreature=creature}};
                var navigate=T("TacticalNavigation").GetMethod("Navigate");void Go(Vector3 to,float speed)=>navigate.Invoke(null,[path,to,speed,1f,200,true,false,true,null,1.2f]);
                Go(new Vector3(10,60,0),.65f);var changed=typeof(ComponentPathfinding).GetField("m_destinationChanged",Fields);changed.SetValue(path,false);
                Go(new Vector3(10.5f,60,.5f),.65f);Check(!(bool)changed.GetValue(path)&&path.Destination==new Vector3(10,60,0),"path search restarted for an unchanged target");
                Go(new Vector3(14,60,0),.65f);Check((bool)changed.GetValue(path)&&path.Destination==new Vector3(14,60,0),"moved target not re-planned");
                changed.SetValue(path,false);path.IsStuck=true;Go(new Vector3(14,60,0),.65f);Check((bool)changed.GetValue(path),"stuck path not re-planned");
            });
            // agent-followup-140 F2: CS2-style damage direction, frozen at the hit, only for real damage.
            Test("damage-direction-sectors-freeze-merge-expiry-and-real-damage-only",()=>{
                var ind=C("ScDamageIndicator");int Sector(Vector2 from,Vector3 forward)=>(int)ind.GetMethod("Sector").Invoke(null,[from,forward]);
                var north=new Vector3(0,0,-1);
                foreach(var (deg,expected) in new[]{(0,0),(35,0),(-35,0),(90,1),(55,1),(125,1),(180,2),(145,2),(-145,2),(-90,3),(-55,3),(-125,3)}){
                    float rad=MathUtils.DegToRad(deg);var from=new Vector2(MathF.Sin(rad),-MathF.Cos(rad));Check(Sector(from,north)==expected,$"sector for {deg} deg = {Sector(from,north)}");}
                Check(Sector(new Vector2(1,0),new Vector3(1,0,0))==0&&Sector(new Vector2(1,0),north)==1,"frozen direction not re-mapped when the player turns");
                var p=new Project();var time=new SubsystemTime();time.m_project=p;p.m_subsystems.Add(time);
                var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};
                var health=new PlainHealth{Health=1,AttackResilience=10,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health};health.m_componentCreature=creature;creature.m_subsystemPlayerStats=new SubsystemPlayerStats();player.ComponentHealth=health;
                var e=Blank<Entity>();e.m_project=p;e.m_components=[player,body,health,creature];foreach(var c in e.m_components)c.m_entity=e;p.m_entities[e]=true;
                float[] Now(double t,Vector3 forward)=>(float[])ind.GetMethod("Intensities").Invoke(null,[player,forward,t]);
                bool Report(Vector3 toward,double t)=>(bool)ind.GetMethod("Report").Invoke(null,[player,toward,t]);
                Check(!Report(new Vector3(0,1,0),0)&&Now(0,north)==null,"straight-above hit shown with a guessed direction");
                Report(new Vector3(0,0,-1),0);Report(new Vector3(.3f,0,-1),.1);var a=Now(.1,north);Check(a!=null&&a[0]>.99f&&a.Count(x=>x>0)==1,"nearby hits not merged into one front mark");
                // Six directions 60 degrees apart never merge; the cap keeps the newest four (120/180/240/300 degrees),
                // so the front (0) and 60-degree marks are evicted and sectors right/back/left remain lit.
                foreach(int deg in new[]{60,120,180,240,300}){float r=MathUtils.DegToRad(deg);Report(new Vector3(MathF.Sin(r),0,-MathF.Cos(r)),.2);}
                a=Now(.2,north);Check(a[0]==0&&a[1]>0&&a[2]>0&&a[3]>0,"cap did not keep exactly the newest four marks: "+string.Join(",",a));
                Check(Now(.95,north)==null,"marks outlived the 0.7 s fade");
                ind.GetMethod("Clear").Invoke(null,[player]);
                var attack=new ProjectileAttackment(body,null,body.Position+Vector3.UnitY,new Vector3(1,0,0),0,null){AttackSoundVolume=0};
                ind.GetMethod("AttackBody").Invoke(null,[attack]);Check(Now(0,north)==null&&health.Health==1,"zero-damage CS hit produced a mark");
                attack=new ProjectileAttackment(body,null,body.Position+Vector3.UnitY,new Vector3(1,0,0),2,null){AttackSoundVolume=0};
                ind.GetMethod("AttackBody").Invoke(null,[attack]);a=Now(0,north);
                Check(health.Health<1&&a!=null&&a[3]>0&&a.Count(x=>x>0)==1,"real CS damage from the west not shown on the left");
            });
            // video-feedback-20260929 R3: real hurt against the creative-only preview, and every zero-damage reason kept apart.
            Test("damage-direction-hurt-creative-preview-and-zero-damage-reasons",()=>{
                var ind=C("ScDamageIndicator");var north=new Vector3(0,0,-1);// Round 12: the creative preview follows the one damage-direction switch.
                var preview=C("ScUiSettings").GetField("DamageIndicator");bool oldPreview=(bool)preview.GetValue(null);
                ModsManager.ModHooks.TryGetValue("ProcessAttackment",out var oldHook);
                try{
                    (ComponentPlayer Player,ComponentBody Body,PlainHealth Health,SubsystemGameInfo Info,Project P) Victim(GameMode mode,bool invulnerable){
                        var p=new Project();var time=new SubsystemTime();var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=mode;info.WorldSettings.IsFriendlyFireEnabled=true;
                        foreach(var sub in new Subsystem[]{time,info}){sub.m_project=p;p.m_subsystems.Add(sub);}
                        var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};
                        var health=new PlainHealth{Health=1,AttackResilience=10,AttackResilienceFactor=1,IsInvulnerable=invulnerable};var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health};health.m_componentCreature=creature;creature.m_subsystemPlayerStats=new SubsystemPlayerStats();player.ComponentHealth=health;
                        var e=Blank<Entity>();e.m_project=p;e.m_components=[player,body,health,creature];foreach(var c in e.m_components)c.m_entity=e;p.m_entities[e]=true;
                        return(player,body,health,info,p);
                    }
                    string Hit(ComponentBody body,float power,Entity attacker=null)=>ind.GetMethod("Deliver").Invoke(null,[new ProjectileAttackment(body,attacker,body.Position+Vector3.UnitY,new Vector3(1,0,0),power,null){AttackSoundVolume=0}]).ToString();
                    float[] Marks(ComponentPlayer player)=>(float[])ind.GetMethod("Intensities").Invoke(null,[player,north,0d]);
                    preview.SetValue(null,true);ModsManager.ModHooks.Remove("ProcessAttackment");
                    var hurt=Victim(GameMode.Survival,false);
                    Check(Hit(hurt.Body,2)=="Hurt"&&hurt.Health.Health<1&&Marks(hurt.Player) is {} full&&full[3]>.99f,"survival damage not shown at full strength");
                    var creative=Victim(GameMode.Creative,true);
                    Check(Hit(creative.Body,2)=="CreativePreview"&&creative.Health.Health==1,"creative hit not previewed, or it changed health");
                    var weak=Marks(creative.Player);float strength=(float)ind.GetField("PreviewStrength").GetRawConstantValue();
                    Check(weak!=null&&Math.Abs(weak[3]-strength)<1e-3f&&weak.Count(x=>x>0)==1&&strength<.6f,"creative preview not the weaker mark on the left: "+(weak==null?"none":string.Join(",",weak)));
                    preview.SetValue(null,false);var off=Victim(GameMode.Creative,true);
                    Check(Hit(off.Body,2)=="None"&&Marks(off.Player)==null,"creative preview shown although the damage direction is switched off");
                    preview.SetValue(null,true);
                    var other=Victim(GameMode.Survival,true);
                    Check(Hit(other.Body,2)=="None"&&Marks(other.Player)==null&&other.Health.Health==1,"invulnerability outside creative mode shown as a hit");
                    var zero=Victim(GameMode.Creative,true);
                    Check(Hit(zero.Body,0)=="None"&&Marks(zero.Player)==null,"zero-power attack previewed");
                    // A shield (or any other filter) that takes all the power in the ProcessAttackment hook.
                    var hook=new ModsManager.ModHook("ProcessAttackment");hook.Add(new Absorb());ModsManager.ModHooks["ProcessAttackment"]=hook;
                    var shielded=Victim(GameMode.Creative,true);
                    Check(Hit(shielded.Body,2)=="None"&&Marks(shielded.Player)==null,"fully blocked attack previewed in creative mode");
                    var blocked=Victim(GameMode.Survival,false);
                    Check(Hit(blocked.Body,2)=="None"&&Marks(blocked.Player)==null&&blocked.Health.Health==1,"fully blocked attack shown in survival mode");
                    ModsManager.ModHooks.Remove("ProcessAttackment");
                    // Friendly fire switched off in the world: player against player shows nothing in either mode.
                    foreach(var mode in new[]{GameMode.Creative,GameMode.Survival}){
                        var friend=Victim(mode,mode==GameMode.Creative);friend.Info.WorldSettings.IsFriendlyFireEnabled=false;
                        var shooter=Blank<ComponentPlayer>();shooter.m_killVerbs=["shot"];var se=Blank<Entity>();se.m_project=friend.P;se.m_components=[shooter];shooter.m_entity=se;
                        Check(Hit(friend.Body,2,se)=="None"&&Marks(friend.Player)==null&&friend.Health.Health==1,"friendly fire that is switched off shown as a hit ("+mode+")");
                    }
                }finally{preview.SetValue(null,oldPreview);if(oldHook!=null)ModsManager.ModHooks["ProcessAttackment"]=oldHook;else ModsManager.ModHooks.Remove("ProcessAttackment");}
            });
            // agent-followup-140 F4: one side-effect-free flight step shared by real grenades and the preview.
            Test("grenade-step-identical-to-original-and-preview-matches-live-detonation",()=>{
                // Floor at y=60 (up face) and a wall at x=5 (face toward -X); water below y=58.
                TerrainRaycastResult? Solid(Vector3 a,Vector3 b){
                    TerrainRaycastResult? best=null;var d=b-a;float len=d.Length();if(len<1e-6f)return null;var dir=d/len;
                    void Try(float t,int face){if(t>=0&&t<=1&&(!best.HasValue||t*len<best.Value.Distance))best=new TerrainRaycastResult{Ray=new Ray3(a,dir),Distance=t*len,CellFace=new CellFace(0,0,0,face)};}
                    if(a.Y>=60&&b.Y<60)Try((a.Y-60)/(a.Y-b.Y),4);
                    if(a.X<=5&&b.X>5)Try((5-a.X)/(b.X-a.X),3);
                    return best;}
                bool Water(Vector3 q)=>q.Y<58;
                var state=C("ScGrenadeState");var integrate=C("ScGrenadeBallistics").GetMethod("Integrate");
                object NewState(int kind,Vector3 pos,Vector3 vel){var st=Activator.CreateInstance(state);state.GetField("Kind").SetValue(st,kind);state.GetField("Position").SetValue(st,pos);state.GetField("Velocity").SetValue(st,vel);return st;}
                foreach(int kind in new[]{0,1,2,3,4,5})foreach(var (pos,vel) in new[]{(new Vector3(0,62,0),new Vector3(9,4,0)),(new Vector3(0,61,0),new Vector3(2,-1,0)),(new Vector3(0,58.5f,0),new Vector3(1,-3,0))}){
                    var st=NewState(kind,pos,vel);
                    // Reference: the pre-refactor SubsystemScGrenades.Move arithmetic, verbatim.
                    Vector3 P=pos,V=vel;bool G=false;float R=0,A=0,NB=0;
                    for(int i=0;i<300;i++){
                        A+=.02f;state.GetField("Age").SetValue(st,A);
                        if(G&&!Solid(P,P-Vector3.UnitY*.15f).HasValue){G=false;R=0;}
                        if(G)R+=.02f;
                        else{bool w=Water(P);V+=Vector3.UnitY*(w?-3f:-10f)*.02f;V*=MathF.Exp(-(w?3:.08f)*.02f);var next=P+V*.02f;var hit=Solid(P,next);
                            if(hit.HasValue){var normal=CellFace.FaceToVector3(hit.Value.CellFace.Face);P=hit.Value.HitPoint()+normal*.06f;
                                if(kind is not (3 or 4)&&V.LengthSquared()>1&&A>=NB)NB=A+.15f;
                                V=(V-2*Vector3.Dot(V,normal)*normal)*.48f;if(kind is 3 or 4&&normal.Y>.5f){G=true;V=Vector3.Zero;}if(normal.Y>.5f&&V.LengthSquared()<.5f){G=true;V=Vector3.Zero;}}
                            else P=next;}
                        object[] args=[st,.02f,(Func<Vector3,Vector3,TerrainRaycastResult?>)Solid,(Func<Vector3,bool>)Water,null,false];integrate.Invoke(null,args);
                        Check((Vector3)state.GetField("Position").GetValue(st)==P&&(Vector3)state.GetField("Velocity").GetValue(st)==V&&(bool)state.GetField("Grounded").GetValue(st)==G
                            &&(float)state.GetField("Rested").GetValue(st)==R&&(float)state.GetField("NextBounceSound").GetValue(st)==NB,$"flight drifted from the original step: kind {kind} step {i}");
                    }
                }
                var predict=C("ScGrenadeTrajectory").GetMethod("Predict");
                dynamic Predict(int kind,Vector3 pos,Vector3 vel,Func<Vector3,bool> loaded)=>predict.Invoke(null,[kind,pos,vel,(Func<Vector3,Vector3,TerrainRaycastResult?>)Solid,(Func<Vector3,bool>)Water,loaded]);
                // Live subsystem, driven frame by frame exactly like the game at 50 fps.
                var p=new Project();var time=new SubsystemTime();var audio=new Audio();var bodies=new SubsystemBodies();var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};
                var terrain=new FakeTerrain{Terrain=new Terrain(),Ray=Solid};terrain.Terrain.AllocateChunk(0,0).State=TerrainChunkState.Valid; // air: the live water check reads real cells
                var grenades=(Subsystem)Activator.CreateInstance(C("SubsystemScGrenades"));
                foreach(var sub in new Subsystem[]{time,audio,bodies,new SubsystemPlayers(),info,terrain,grenades}){sub.m_project=p;p.m_subsystems.Add(sub);}
                foreach(var (f,v) in new (string,object)[]{("m_time",time),("m_terrain",terrain),("m_bodies",bodies),("m_players",p.FindSubsystem<SubsystemPlayers>(true)),("m_info",info)})C("SubsystemScGrenades").GetField(f,Fields).SetValue(grenades,v);
                var active=(System.Collections.IList)C("SubsystemScGrenades").GetField("m_active",Fields).GetValue(grenades);
                // HE/flash burst where they are; smoke/decoy pop where they come to rest after the wall rebound and the
                // settling hops on the floor (those small hops must not use up the preview's rebound budget).
                foreach(var (kind,end) in new[]{(0,"Detonate"),(1,"Detonate"),(2,"Settle"),(5,"Settle")}){
                    var launch=(new Vector3(0,62,0),new Vector3(6,5,0));var live=NewState(kind,launch.Item1,launch.Item2);state.GetField("Remaining").SetValue(live,C("ScGrenadeBallistics").GetMethod("Fuse").Invoke(null,[kind]));active.Add(live);
                    for(int i=0;i<700&&!(bool)state.GetField("Effect").GetValue(live);i++)((IUpdateable)grenades).Update(.02f);
                    dynamic path=Predict(kind,launch.Item1,launch.Item2,_=>true);
                    Check((bool)state.GetField("Effect").GetValue(live)&&path.Kind.ToString()==end&&Vector3.Distance((Vector3)path.EndPoint,(Vector3)state.GetField("Position").GetValue(live))<1e-4f,
                        $"preview end differs from the live grenade: kind {kind} predicted {path.Kind} at {path.EndPoint} live effect {state.GetField("Effect").GetValue(live)} at {state.GetField("Position").GetValue(live)}");
                    active.Clear();
                }
                dynamic steep=Predict(0,new Vector3(0,61,0),new Vector3(1,-8,0),_=>true);Check(steep.Kind.ToString()=="Detonate","floor hops of a grenade thrown at the ground were counted as rebounds");
                dynamic fire=Predict(3,new Vector3(0,61,0),new Vector3(2,1,0),_=>true);Check(fire.Kind.ToString()=="Ignite"&&Math.Abs(((Vector3)fire.EndPoint).Y-60.06f)<1e-3f,"molotov preview does not ignite on the floor");
                dynamic smoke=Predict(2,new Vector3(0,61,0),new Vector3(2,1,0),_=>true);Check(smoke.Kind.ToString()=="Settle"&&(float)smoke.Time>=1.5f,"smoke preview pops before settling after its fuse");
                dynamic far=Predict(0,new Vector3(0,62,0),new Vector3(0,30,-20),_=>true);Check(((System.Collections.ICollection)far.Points).Count<=97,"preview draws more than the point budget");
                dynamic cut=Predict(0,new Vector3(0,62,0),new Vector3(3,2,0),q=>q.X<2);Check(cut.Kind.ToString()=="Truncated"&&((Vector3)cut.EndPoint).X<2.2f,"preview invented a path over unloaded terrain");
            });
        }finally{rf.SetValue(null,originalRegistry);}
        return results;
    }
}
