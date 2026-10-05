using GameEntitySystem;
namespace Game;
/// <summary>Optional voice extension, no audio resources or required mod dependency in core.</summary>
public static class ScAgentVoice {
    public const int ApiVersion=1;
    public static Action<ContainerWidget> OpenSettings;
    public static Action<ComponentPlayer> OpenMenu;
    public static Action<ComponentInput> FilterInput;
    public static event Action<Entity,string,string> Event;
    public static void ResetProvider(){OpenSettings=null;OpenMenu=null;FilterInput=null;ScWorkbenchExtension.UnregisterAction("agent-voice");}
    public static void Emit(Entity entity,string role,string action){
        if(entity==null)return;
        if(ScNet.IsHost)ScNetFeedback.Voice(entity,role,action,false); // multiplayer clients hear the server's NPCs
        if(Event==null)return;
        foreach(Action<Entity,string,string> receiver in Event.GetInvocationList())try{receiver(entity,role,action);}catch(Exception e){KnifeDiagnostics.WarnOnce("agent-voice-provider",e.Message);}
    }
    /// <summary>A multiplayer client: an event the server raised, given to this client's voice package.</summary>
    public static void Deliver(Entity entity,string role,string action,bool player){
        var handlers=player?PlayerEvent:Event;if(entity==null||handlers==null)return;
        foreach(Action<Entity,string,string> receiver in handlers.GetInvocationList())try{receiver(entity,role,action);}catch(Exception e){KnifeDiagnostics.WarnOnce(player?"agent-voice-player":"agent-voice-provider",e.Message);}
    }
    /// <summary>Player-originated automatic callouts (a committed throw). Kept apart from NPC events so the voice
    /// package can gate them with their own setting; older voice packages simply do not subscribe.</summary>
    public const int PlayerEventsVersion=1;
    public static event Action<Entity,string,string> PlayerEvent;
    public static void EmitPlayer(ComponentPlayer player,string action){
        if(player?.Entity==null)return;string role=PlayerRole(player);if(role is not ("ct" or "t"))return;
        if(ScNet.IsHost)ScNetFeedback.Voice(player.Entity,role,action,true);
        if(PlayerEvent==null)return;
        foreach(Action<Entity,string,string> receiver in PlayerEvent.GetInvocationList())try{receiver(player.Entity,role,action);}catch(Exception e){KnifeDiagnostics.WarnOnce("agent-voice-player",e.Message);}
    }
    public static string PlayerRole(ComponentPlayer p)=>p?.Entity?.Components.OfType<IScFirstPersonAppearance>().FirstOrDefault()?.FirstPersonRole;
}
