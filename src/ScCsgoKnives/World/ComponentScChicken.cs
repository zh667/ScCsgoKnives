using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Passive chicken: follow is a normal navigation behavior, never an attack.</summary>
public sealed class ComponentScChicken : ComponentBehavior, IUpdateable {
    public const string Template="ScCsgoChicken";
    ComponentCreature creature;
    ComponentPathfinding path;
    SubsystemTime time;
    SubsystemPlayers players;
    double nextPath;
    public int FollowerPlayer=-1;
    public bool DeathHandled,PendingBlast;
    public int BlastOwner=-1;
    public override float ImportanceLevel => !DeathHandled && FollowerPlayer>=0 && Target() is not null ? 30 : 0;
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    ComponentPlayer Target()=>players.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==FollowerPlayer && p.ComponentHealth.Health>0);
    public override void Load(ValuesDictionary values,IdToEntityMap map) {
        creature=Entity.FindComponent<ComponentCreature>(true);path=Entity.FindComponent<ComponentPathfinding>(true);
        time=Project.FindSubsystem<SubsystemTime>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);
        FollowerPlayer=values.GetValue("FollowerPlayer",-1);DeathHandled=values.GetValue("DeathHandled",false);
        PendingBlast=values.GetValue("PendingBlast",false);BlastOwner=values.GetValue("BlastOwner",-1);
    }
    public override void Save(ValuesDictionary values,EntityToIdMap map) {
        values.SetValue("FollowerPlayer",FollowerPlayer);values.SetValue("DeathHandled",DeathHandled);
        values.SetValue("PendingBlast",PendingBlast);values.SetValue("BlastOwner",BlastOwner);
    }
    public bool ToggleFollow(int player) {
        if(DeathHandled || creature.ComponentHealth.Health<=0)return false;
        FollowerPlayer=FollowerPlayer==player?-1:player;nextPath=0;
        // Keep a recruited chicken through the engine's ordinary random despawn.
        creature.ConstantSpawn=true;
        if(FollowerPlayer<0 && IsActive) {path.Stop();IsActive=false;}
        return true;
    }
    public static bool ExplodesFrom(Attackment attack) => attack is ScSurvivalBalance.GunAttack;
    public bool MarkDeath(bool bullet,int owner) {
        if(DeathHandled)return false;
        DeathHandled=true;FollowerPlayer=-1;PendingBlast=bullet;BlastOwner=owner;return true;
    }
    public void Died(Injury injury) {
        if(!MarkDeath(ExplodesFrom(injury?.Attackment),injury?.AttackerPlayer?.PlayerData.PlayerIndex??-1))return;
        Project.FindSubsystem<SubsystemAudio>(true).PlayRandomSound("Audio/ScCsgoKnives/Chicken/death",1,0,creature.ComponentBody.Position,5,true);
    }
    public void Update(float dt) {
        if(PendingBlast) {
            PendingBlast=false; // one shot even if a surrounding creature dies during blast dispatch
            Project.FindSubsystem<SubsystemScGrenades>(true).ChickenBlast(creature.ComponentBody.BoundingBox.Center(),BlastOwner);
        }
        if(DeathHandled || creature.ComponentHealth.Health<=0) {if(IsActive){path.Stop();IsActive=false;}return;}
        if(!IsActive)return;
        var target=Target();
        if(target is null) {path.Stop();IsActive=false;return;}
        float distance=Vector3.DistanceSquared(target.ComponentBody.Position,creature.ComponentBody.Position);
        if(distance>64*64) {FollowerPlayer=-1;path.Stop();IsActive=false;return;}
        if(time.GameTime<nextPath)return;nextPath=time.GameTime+.5;
        if(distance<2.25f)path.Stop();
        else path.SetDestination(target.ComponentBody.Position,.65f,1.2f,200,true,false,true,target.ComponentBody);
        creature.ComponentCreatureModel.LookAtOrder=target.ComponentBody.Position+Vector3.UnitY;
    }
}
