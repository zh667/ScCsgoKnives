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
        if(entity==null||Event==null)return;
        foreach(Action<Entity,string,string> receiver in Event.GetInvocationList())try{receiver(entity,role,action);}catch(Exception e){KnifeDiagnostics.WarnOnce("agent-voice-provider",e.Message);}
    }
    public static string PlayerRole(ComponentPlayer p)=>p?.Entity?.Components.OfType<IScFirstPersonAppearance>().FirstOrDefault()?.FirstPersonRole;
}
