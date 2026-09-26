#if SC_SPLIT
using System.Runtime.CompilerServices;
[assembly:TypeForwardedTo(typeof(Game.ScTacticalShieldBlock))]
[assembly:TypeForwardedTo(typeof(Game.ScTacticalBeaconBlock))]
[assembly:TypeForwardedTo(typeof(Game.ScTacticalDefuserBlock))]
[assembly:TypeForwardedTo(typeof(Game.ScTacticalSquadBlock))]
[assembly:TypeForwardedTo(typeof(Game.TacticalItemMesh))]
namespace Game;
/// <summary>Only the matching optional package activates the core's stable item identities.</summary>
public static class ScSplitAgentMarker {
    public const int Protocol=1;
    public static void ValidateCore(){
        var core=ModsManager.Dlls.Values.FirstOrDefault(a=>a.GetName().Name=="ScCsgoKnives");
        if(core?.GetType("Game.ScOptionalAgents")?.GetField("Split")?.GetRawConstantValue() is not true)
            throw new InvalidOperationException("此探员包需要配套新的1.3.0轻量包，不能与旧全量、轻量或极简包混装。");
    }
}
#endif
