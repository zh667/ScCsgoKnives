using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// Source-DLL behavior check on SCAPI 1.9.3.1. No installed package, world or user settings are opened.
Dispatcher.Initialize();
var core=typeof(GunSpec).Assembly;var tactical=typeof(ComponentTacticalEnemy).Assembly;
var checks=new List<TacticalEnemyRegression.Result>();
void Check(string name,bool ok,string detail="")=>checks.Add(new(name,ok,detail));
void Test(string name,Action test){try{test();Check(name,true);}catch(Exception e){Check(name,false,e.ToString());}}
void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
void Block(int index,Block b){b.BlockIndex=index;BlocksManager.Blocks[index]=b;BlocksManager.BlockTypeToIndex[b.GetType()]=index;BlocksManager.BlockNameToIndex[b.GetType().Name]=index;}
Block(0,new AirBlock{IsCollidable=false});Block(2,new DirtBlock{IsCollidable=true});
Block(40,new IronIngotBlock{CraftingId="ironingot"});Block(41,new CopperIngotBlock{CraftingId="copperingot"});Block(42,new CoalChunkBlock{CraftingId="coalchunk"});
Block(700,new ScKnifeBlock{MaxStacking=1});Block(701,new ScGunBlock{MaxStacking=1});Block(702,new ScAmmoBlock());
Block(703,new ScGunSkinTemplateBlock());Block(704,new ScGunCounterTemplateBlock());
Block(705,new ScTacticalShieldBlock());Block(706,new ScTacticalBeaconBlock());Block(707,new ScTacticalDefuserBlock());
Block(708,new ScWeaponMaterialBlock());Block(709,new ScTacticalSquadBlock());Block(710,new ScChickenEggBlock());
Block(720,new ScGrenadeBlock());Block(721,new ScC4Block());
string[] scope=["feedback/","survival-and-creative-three-five","manual-floor","finite-ammo","shoot-wall","neutral-until","one-attacked","summoned-warmup","summoned-squad","death-once","supplies-and-chicken"];
checks.AddRange(TacticalEnemyRegression.Run(core,tactical,"","",false,null,n=>scope.Any(n.StartsWith)));
checks.AddRange(StarterEquipmentRegression.Run(core).Select(c=>new TacticalEnemyRegression.Result(c.Name,c.Ok,c.Detail)));

Test("knife/all-factory-and-finish-damage",()=>{
    for(int variant=0;variant<CsmcKnifeRig.KnifeCount;variant++){
        int value=Terrain.MakeBlockValue(700,0,variant);
        Require(ScKnifeStrike.PowerFor(value,false)==7&&ScKnifeStrike.PowerFor(value,true)==12&&BlocksManager.Blocks[700].GetMeleePower(value)==7,"factory knife damage");
        int skin=ScKnifeSkinCatalog.ForVariant(variant);
        if(skin==0)continue;
        int painted=Terrain.MakeBlockValue(700,0,ScKnifeSkinCatalog.With(variant,skin));
        Require(ScKnifeStrike.PowerFor(painted,false)==21&&ScKnifeStrike.PowerFor(painted,true)==36,"finish knife damage changed");
    }
});
Test("starter/four-random-loadouts-factory-weapons-and-five-magazines",()=>{
    foreach(var plan in Enum.GetValues<ScStarterPlan>())for(int seed=0;seed<100;seed++){
        var items=ScStarterLoadout.Items(plan,new Engine.Random(seed));
        Require(items.Length==(plan==ScStarterPlan.None?0:(int)plan+1),"wrong item count");
        if(plan==ScStarterPlan.None)continue;
        Require(items[^1].Value==ScAmmoBlock.Value(ScAmmoBlock.Magazine)&&items[^1].Count==5&&items[..^1].All(p=>p.Count==1),"must grant exactly five magazines");
        Require(Terrain.ExtractContents(items[0].Value)==700&&ScKnifeBlock.SkinOf(items[0].Value)==0,"not factory knife");
        for(int i=1;i<items.Length-1;i++){
            var spec=ScGunBlock.SpecOf(items[i].Value);
            Require(ScStarterLoadout.Pistol(spec.Name)==(i==1)&&spec.Name!="taser","wrong gun class");
            Require(GunSpec.GetRounds(Terrain.ExtractData(items[i].Value))==spec.Magazine,"not full gun");
        }
    }
});
Test("starter/pending-save-reload-confirm-none-and-repeated-claim",()=>{
    var project=new Project();var players=new SubsystemPlayers{m_project=project};var info=new SubsystemGameInfo{m_project=project,WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=GameMode.Survival;
    project.m_subsystems.Add(players);project.m_subsystems.Add(info);
    var inventory=new ComponentInventory();for(int i=0;i<10;i++)inventory.m_slots.Add(new());
    var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=3;
    player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentMiner=new ComponentMiner{Inventory=inventory};
    var entity=Blank<Entity>();entity.m_project=project;entity.m_isAddedToProject=true;entity.m_components=[player,inventory];player.m_entity=entity;inventory.m_entity=entity;
    project.m_entities[entity]=true;players.m_componentPlayers.Add(player);
    var starter=new SubsystemScStarterEquipment{m_project=project};ScGunRegistry.Current=new();
    Require(starter.TryGrant(GameMode.Survival,PlayerData.SpawnMode.InitialNoIntro,3,1,inventory,(_,_)=>throw new Exception("spawn delivered prematurely")),"not queued");
    Require(inventory.m_slots.All(s=>s.Count==0),"default kit granted before choice");
    var saved=new ValuesDictionary();starter.Save(saved);starter=new(){m_project=project};starter.Load(saved);
    Require(starter.Pending(3)&&starter.Choose(player,ScStarterPlan.None),"pending choice lost on reload");
    Require(!starter.Choose(player,ScStarterPlan.Full)&&inventory.m_slots.All(s=>s.Count==0),"none allowed a later grant");
    starter.Save(saved);starter=new(){m_project=project};starter.Load(saved);
    Require(starter.Granted(3)&&!starter.TryGrant(GameMode.Survival,PlayerData.SpawnMode.InitialNoIntro,3,1,inventory,(_,_)=>{}),"claim reset on reload");
    Require(!starter.TryGrant(GameMode.Survival,PlayerData.SpawnMode.Respawn,4,2,inventory,(_,_)=>{}),"respawn got choice");
    Require(!starter.TryGrant(GameMode.Creative,PlayerData.SpawnMode.InitialNoIntro,4,1,inventory,(_,_)=>{}),"creative got choice");
});
Test("grenades/moderate-damage-and-far-visible-flash",()=>{
    Require(ScGrenadeState.HePower(0)==120&&ScGrenadeState.HePower(3.9f)==60&&ScGrenadeState.HePower(7.8f)==0,"HE budget/falloff");
    var fire=new ScGrenadeState{Kind=3,Effect=true,Remaining=6};
    Require(ScFireArea.Exposure([fire,fire],Vector3.Zero,1,_=>true).Power==7.5f,"overlapping fire stacked");
    Require(ScFireArea.Exposure([fire],Vector3.Zero,1,_=>false).Power==0,"fire ignored cover");
    Require(ScGrenadeState.VisibleFlashDuration(500,1,true)==1&&ScGrenadeState.VisibleFlashDuration(500,-1,false)==0,"far flash missing or behind-camera flash");
});
Test("starter/network-authorization-malformed-and-duplicate-requests",()=>{
    var oldProject=GameManager.Project;var oldTransport=ScNet.Transport;var oldClock=ScNet.Clock;
    try{
        var project=new Project();GameManager.m_project=project;
        var players=new SubsystemPlayers{m_project=project};var info=new SubsystemGameInfo{m_project=project,WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=GameMode.Survival;
        var starter=new SubsystemScStarterEquipment{m_project=project};project.m_subsystems.Add(players);project.m_subsystems.Add(info);project.m_subsystems.Add(starter);
        var inventory=new ComponentInventory();for(int i=0;i<10;i++)inventory.m_slots.Add(new());
        var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=3;player.PlayerData.ComponentPlayer=player;
        player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentMiner=new ComponentMiner{Inventory=inventory};
        var entity=Blank<Entity>();entity.m_project=project;entity.m_isAddedToProject=true;entity.m_components=[player,inventory];player.m_entity=entity;inventory.m_entity=entity;project.m_entities[entity]=true;
        players.m_componentPlayers.Add(player);players.m_playersData.Add(player.PlayerData);
        var transport=new Transport();ScNet.Attach(transport);ScNetStarter.Register();ScGunRegistry.Current=new();
        var peer=new ScNetPeer{PlayerIndex=3};
        transport.Clients.Add(peer);
        void Send(params byte[] payload)=>ScNet.ReceiveOnServer(peer,ScNetStarter.OpChoose,payload);
        Send(3);Require(!starter.Granted(3)&&inventory.m_slots.Sum(s=>s.Count)==0,"uneligible player got kit");
        starter.TryGrant(GameMode.Survival,PlayerData.SpawnMode.InitialNoIntro,3,1,inventory,(_,_)=>{});
        Send(255);Send(3,3);Send();Require(starter.Pending(3)&&inventory.m_slots.Sum(s=>s.Count)==0,"malformed packet granted kit");
        Send(3);Send(3);Require(starter.Granted(3)&&inventory.m_slots.Sum(s=>s.Count)==8,"duplicate network kit");
        var response=new ScNetReader(transport.Last);Require(response.Int()==3&&!response.Bool()&&response.Bool()&&response.End,"server ack is not authoritative");
        double now=10;ScNet.Clock=()=>now;ScNetGrenades.Register();
        var view=new ScFlashView(new Vector3(0,61,0),-Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,.6f);
        byte[] ViewPacket(float width)=>new ScNetWriter().Vector3(view.Position).Vector3(view.Forward).Vector3(view.Right).Vector3(view.Up).Float(width).Float(view.HalfHeight).ToArray();
        ScNet.ReceiveOnServer(peer,ScNetGrenades.OpFlashView,ViewPacket(1));
        Require(ScNetGrenades.FlashView(player) is {} received&&received.Contains(new Vector3(0,61,-500))&&!received.Contains(new Vector3(0,61,500))&&!received.Contains(new Vector3(600,61,-500)),"remote flash view ignores screen bounds");
        now=10.4;ScNet.ReceiveOnServer(peer,ScNetGrenades.OpFlashView,ViewPacket(-1));now=10.6;
        Require(ScNetGrenades.FlashView(player) is null,"invalid/stale camera input refreshed view lease");
        transport.Role=ScNetRole.Client;
        Require(!starter.Choose(player,ScStarterPlan.Full),"client mutated world");
    }finally{ScNet.Attach(oldTransport);GameManager.m_project=oldProject;ScNet.Clock=oldClock;}
});
Test("grenades/network-friend-reflash-server-event-client-and-save",()=>{
    var oldProject=GameManager.Project;var oldTransport=ScNet.Transport;var oldClock=ScNet.Clock;
    try{
        var project=new Project();GameManager.m_project=project;
        var time=new SubsystemTime();var players=new SubsystemPlayers();var bodies=new SubsystemBodies();
        var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.IsFriendlyFireEnabled=false;
        foreach(var sub in new Subsystem[]{time,players,bodies,info,new FlashGround(),new FlashAudio()}){sub.m_project=project;project.m_subsystems.Add(sub);}
        var server=new SubsystemScGrenades{m_project=project};project.m_subsystems.Add(server);server.Load(new());
        var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.8f,.65f)};
        var player=Blank<ComponentPlayer>();player.ComponentBody=body;player.ComponentHealth=new ComponentHealth{Health=1};player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=3;player.PlayerData.ComponentPlayer=player;player.PlayerData.m_gameWidget=Blank<GameWidget>();
        var entity=Blank<Entity>();entity.m_project=project;entity.m_isAddedToProject=true;entity.Id=987;entity.m_components=[body,player];body.m_entity=entity;player.m_entity=entity;
        project.m_entities[entity]=true;players.m_componentPlayers.Add(player);players.m_playersData.Add(player.PlayerData);bodies.AddBody(body);
        var transport=new Transport();transport.Clients.Add(new ScNetPeer{PlayerIndex=3});ScNet.Attach(transport);ScNet.Clock=()=>time.GameTime;ScNetGrenades.Register();
        void Burst(float z){
            var view=new ScNetWriter().Vector3(new Vector3(0,61.5f,0)).Vector3(-Vector3.UnitZ).Vector3(Vector3.UnitX).Vector3(Vector3.UnitY).Float(1).Float(.6f).ToArray();
            ScNet.ReceiveOnServer(transport.Clients[0],ScNetGrenades.OpFlashView,view);
            Require(ScNetGrenades.FlashView(player) is not null,"fixture: remote view was not accepted");
            typeof(SubsystemScGrenades).GetMethod("Detonate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(server,[new ScGrenadeState{Kind=1,Owner=0,Position=new Vector3(0,61.5f,z)}]);
        }
        Burst(-2);Require(transport.BlindEvents.Count==1,"friendly remote player got no blind event");
        time.m_gameTime=1;Burst(-200);Require(transport.BlindEvents.Count==2,"reflash event swallowed");
        Require(new ScNetReader(transport.BlindEvents[1]).Float()==4.5f,"server sent weak duration instead of preserved tail");
        project.m_subsystems.Remove(server);var client=new SubsystemScGrenades{m_project=project};project.m_subsystems.Add(client);client.Load(new());
        transport.Role=ScNetRole.Client;time.m_gameTime=0;ScNet.ReceiveOnClient(ScNetGrenades.OpBlind,transport.BlindEvents[0]);
        time.m_gameTime=1;ScNet.ReceiveOnClient(ScNetGrenades.OpBlind,transport.BlindEvents[1]);
        ScNet.ReceiveOnClient(ScNetGrenades.OpBlind,new ScNetWriter().Float(1).ToArray());
        var saved=new ValuesDictionary();client.Save(saved);
        for(int i=0;i<2;i++){
            var entry=saved.GetValue<ValuesDictionary>("Blindness").GetValue<ValuesDictionary>("987");
            Require(entry.GetValue<float>("Left")==4.5f&&entry.GetValue<float>("Duration")==4.5f,"network/reload shortened flash or failed to restart white");
            var xml=new System.Xml.Linq.XElement("Values");saved.Save(xml);var loaded=new ValuesDictionary();loaded.ApplyOverrides(System.Xml.Linq.XElement.Parse(xml.ToString()));
            var next=new SubsystemScGrenades{m_project=project};next.Load(loaded);saved=new();next.Save(saved);
        }
    }finally{ScNet.Attach(oldTransport);GameManager.m_project=oldProject;ScNet.Clock=oldClock;}
});
string Hash(Assembly assembly)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
var report=new {platform="SurvivalcraftAPI NuGet 1.9.3.1; native offline behavior, not a game session",engineAssemblyVersion=typeof(ComponentPlayer).Assembly.GetName().Version?.ToString(),engineSha256=Hash(typeof(ComponentPlayer).Assembly),coreSha256=Hash(core),tacticalSha256=Hash(tactical),scope,passed=checks.Count(c=>c.Ok),failed=checks.Count(c=>!c.Ok),checks};
string output=args.FirstOrDefault()??".tmp/dev-temp/gameplay-feedback.json";Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"GameplayFeedbackCheck: {report.passed}/{checks.Count}; failures={report.failed}");
foreach(var c in checks.Where(c=>!c.Ok))Console.WriteLine($"FAIL {c.Name}: {c.Detail}");
return report.failed==0?0:1;

sealed class Transport:IScNetTransport {
    public ScNetRole Role{get;set;}=ScNetRole.Host;
    public ScNetHandshake Handshake=>ScNetHandshake.Accepted;
    public string HandshakeDetail=>"fixture";
    public List<ScNetPeer> Clients=[];
    public IReadOnlyList<ScNetPeer> Peers=>Clients;
    public byte[] Last;
    public readonly List<byte[]> BlindEvents=[];
    public bool IsLocal(ComponentPlayer player)=>Role==ScNetRole.Client;
    public bool SendToServer(ushort op,byte[] payload)=>true;
    public bool SendTo(ScNetPeer peer,ushort op,byte[] payload){Last=payload;if(op==ScNetGrenades.OpBlind)BlindEvents.Add(payload);return true;}
    public void Broadcast(ushort op,byte[] payload,ScNetPeer except){}
}
sealed class FlashGround:SubsystemTerrain {
    public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool interaction,bool skipAir,Func<int,float,bool> predicate)=>null;
}
sealed class FlashAudio:SubsystemAudio {
    public override void PlaySound(string name,float volume,float pitch,Vector3 position,float distance,bool delay){}
}
