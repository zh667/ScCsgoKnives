using System.Reflection;
namespace Game;

/// <summary>Optional ControllerHaptics 1.2.0 integration. No reference or bundled provider DLL.</summary>
public static class ScControllerFeedback {
    static Action<ComponentPlayer,WidgetInputDevice,float,int,bool> pulse;
    static bool initialized;
    public static void Initialize() {
        initialized=true;pulse=null;
        try {
            var provider=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="ControllerHaptics");
            var method=provider?.GetType("Game.ControllerHaptics")?.GetMethod("Pulse",BindingFlags.Public|BindingFlags.Static,
                null,[typeof(ComponentPlayer),typeof(WidgetInputDevice),typeof(float),typeof(int),typeof(bool)],null);
            if(method is not null)pulse=method.CreateDelegate<Action<ComponentPlayer,WidgetInputDevice,float,int,bool>>();
        } catch(Exception e) { Disable(e); }
    }
    static void Disable(Exception error) {
        pulse=null;
        Engine.Log.Warning("[CS haptics] Optional feedback disabled for this session: "+error.Message);
    }
    static void Emit(ComponentPlayer player,float strength,int milliseconds,bool trigger=false) {
        if(!initialized)Initialize();
        if(pulse is null)return;
        // Pass the owning player's actual device mask. Provider handles current input source,
        // hardware, phone fallback, intensity settings and stopping/overlapping short pulses.
        try {
            var input=player?.GameWidget?.Input;
            if(input is null || !ScGunBindings.Available(player))return;
            pulse(player,input.Devices,strength,milliseconds,trigger);
        } catch(Exception e) { Disable(e); }
    }
    public static void Shot(ComponentPlayer player,string gun) {
        bool heavy=gun is "awp" or "ssg08" or "scar20" or "g3sg1" or "nova" or "xm1014" or "mag7" or "sawedoff";
        Emit(player,heavy?.55f:.3f,heavy?85:45,true);
    }
    public static void Reloaded(ComponentPlayer player)=>Emit(player,.2f,65);
    public static void KnifeHit(ComponentPlayer player,bool heavy)=>Emit(player,heavy?.45f:.3f,heavy?80:50);
    public static void Thrown(ComponentPlayer player)=>Emit(player,.2f,45);
}
