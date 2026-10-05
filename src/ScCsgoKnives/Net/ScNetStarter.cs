namespace Game;

/// <summary>Only the server decides eligibility and grants items; the client chooses its own pending kit.</summary>
public static class ScNetStarter {
    public const ushort OpQuery=74,OpStatus=75,OpChoose=76;
    public static void Register(){
        ScNet.OnServer(OpQuery,(peer,player,r)=>Reply(peer,player));
        ScNet.OnServer(OpChoose,(peer,player,r)=>{
            var plan=(ScStarterPlan)r.Byte();
            if(Enum.IsDefined(plan))player.Project.FindSubsystem<SubsystemScStarterEquipment>(true).Choose(player,plan);
            Reply(peer,player);
        });
        ScNet.OnClient(OpStatus,r=>{
            int index=r.Int();bool pending=r.Bool(),granted=r.Bool();
            GameManager.Project?.FindSubsystem<SubsystemScStarterEquipment>(false)?.ApplyStatus(index,pending,granted);
        });
    }
    static void Reply(ScNetPeer peer,ComponentPlayer player){
        var starter=player.Project.FindSubsystem<SubsystemScStarterEquipment>(true);int index=player.PlayerData.PlayerIndex;
        ScNet.SendTo(peer,OpStatus,w=>w.Int(index).Bool(starter.Pending(index)).Bool(starter.Granted(index)));
    }
    public static bool Query()=>ScNet.Send(OpQuery,_=>{});
    public static bool Choose(ScStarterPlan plan)=>ScNet.Send(OpChoose,w=>w.Byte((byte)plan));
}
