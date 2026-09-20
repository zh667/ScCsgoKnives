namespace Game;

/// <summary>Optional world actions can reserve weapon input without owning the player's bindings.</summary>
public static class ScWeaponActionGate {
    public static event Func<ComponentPlayer,bool> Reserved;
    public static bool Blocks(ComponentPlayer player) {
        if(Reserved is null)return false;
        foreach(Func<ComponentPlayer,bool> handler in Reserved.GetInvocationList())if(handler(player))return true;
        return false;
    }
}
