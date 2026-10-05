using TemplatesDatabase;
using GameEntitySystem;
using Engine;
namespace Game;

/// <summary>A one-time gift for the player's first survival spawn, saved per world.</summary>
public sealed class SubsystemScStarterEquipment : Subsystem, IUpdateable {
    readonly HashSet<int> m_granted = [];
    readonly HashSet<int> m_pending = [];
    readonly HashSet<int> m_pendingCheckpoint = [];
    readonly Engine.Random m_random = new();
    ScStarterDialog m_dialog;
    double m_quietSince, m_nextQuery;
    float m_checkpointRetry;
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public override void Load(ValuesDictionary values) {
        base.Load(values);
        m_granted.Clear();
        m_pendingCheckpoint.Clear();
        m_pending.Clear();
        m_checkpointRetry = 0;
        foreach (string id in values.GetValue("GrantedPlayers", "").Split(','))
            if (int.TryParse(id, out int index)) m_granted.Add(index);
        foreach(string id in values.GetValue("PendingPlayers", "").Split(','))
            if(int.TryParse(id,out int index)&&!m_granted.Contains(index))m_pending.Add(index);
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values);
        values.SetValue("GrantedPlayers", string.Join(",", m_granted.Order()));
        values.SetValue("PendingPlayers", string.Join(",", m_pending.Order()));
    }
    public static bool Eligible(GameMode mode, PlayerData.SpawnMode spawnMode, int spawnsCount) =>
        mode is GameMode.Harmless or GameMode.Survival or GameMode.Challenging or GameMode.Cruel
        && spawnMode is PlayerData.SpawnMode.InitialIntro or PlayerData.SpawnMode.InitialNoIntro && spawnsCount == 1;
    public bool Pending(int playerIndex)=>m_pending.Contains(playerIndex)&&!m_granted.Contains(playerIndex);
    public bool Granted(int playerIndex)=>m_granted.Contains(playerIndex);
    public bool TryGrant(GameMode mode, PlayerData.SpawnMode spawnMode, int playerIndex, int spawnsCount,
        IInventory inventory, Action<int, int> dropOverflow) {
        if (!Eligible(mode, spawnMode, spawnsCount) || m_granted.Contains(playerIndex)
            || inventory is null or ComponentCreativeInventory) return false;
        // Spawn hooks only queue a choice. No items are issued before the player confirms it.
        return m_pending.Add(playerIndex);
    }

    public bool Choose(ComponentPlayer player,ScStarterPlan plan){
        if(!ScNet.IsAuthority||!Enum.IsDefined(plan)||player?.Project!=Project||player.ComponentHealth.Health<=0
            ||ScWorldModes.Dedicated(Project)||!Pending(player.PlayerData.PlayerIndex)
            ||Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode is GameMode.Creative or GameMode.Adventure)return false;
        var inventory=player.ComponentMiner.Inventory;var registry=ScGunRegistry.Current;
        string owner=ScGunHolders.RecoveryOwner(Project,inventory);
        if(inventory is null or ComponentCreativeInventory||registry is null||string.IsNullOrEmpty(owner))return false;
        var items=ScStarterLoadout.Items(plan,m_random);
        // Record the fixed randomized kit as a durable grant before acknowledging the choice. The normal recovery
        // runner measures partial writes and retries remaining items, including when the backpack is full.
        if(items.Length>0&&registry.Recovery.Grant(owner,items,"starter equipment")==0)return false;
        int index=player.PlayerData.PlayerIndex;m_granted.Add(index);m_pending.Remove(index);m_pendingCheckpoint.Add(index);
        registry.Recovery.Retry(key=>ScGunHolders.ResolveRecoveryOwner(Project,key));
        return true;
    }
    public void ApplyStatus(int index,bool pending,bool granted){
        if(!ScNet.IsRemoteClient)return;
        if(granted){m_granted.Add(index);m_pending.Remove(index);}
        else if(pending)m_pending.Add(index);
        else m_pending.Remove(index);
    }
    void UpdateChoice(){
        if(Project is null||!ReferenceEquals(Project,GameManager.Project)||ScWorldModes.Dedicated(Project))return;
        double now=Time.RealTime;
        var local=Project.FindSubsystem<SubsystemPlayers>(true).PlayersData
            .Where(p=>p.IsReadyForPlaying&&p.ComponentPlayer is {} player&&ScNet.IsLocal(player)&&player.ComponentHealth.Health>0).ToArray();
        if(ScNet.IsRemoteClient&&now>=m_nextQuery){m_nextQuery=now+3;foreach(var p in local)if(!Granted(p.PlayerIndex))ScNetStarter.Query();}
        if(m_dialog is not null){
            // A later third-party dialog gets priority too; retain the unconfirmed choice for the next quiet window.
            if(DialogsManager.Dialogs.Any(d=>d!=m_dialog))DialogsManager.HideDialog(m_dialog);
            if(!DialogsManager.Dialogs.Contains(m_dialog))m_dialog=null;
            m_quietSince=now;return;
        }
        if(DialogsManager.Dialogs.Count>0||DialogsManager.m_animationData.Count>0||ScreensManager.CurrentScreen is not GameScreen){m_quietSince=now;return;}
        if(now-m_quietSince<1)return;
        var data=local.FirstOrDefault(p=>Pending(p.PlayerIndex)&&p.ComponentPlayer.ComponentGui.ModalPanelWidget is null);
        if(data is null)return;
        var player=data.ComponentPlayer;
        m_dialog=new ScStarterDialog(ScUiSettings.StarterPlan,plan=>{
            if(!ReferenceEquals(Project,GameManager.Project)||data.ComponentPlayer!=player)return false;
            return ScNet.IsRemoteClient?ScNetStarter.Choose(plan):Choose(player,plan);
        });
        DialogsManager.ShowDialog(player.GuiWidget,m_dialog);
    }
    public override void Dispose(){if(m_dialog is not null)DialogsManager.HideDialog(m_dialog);m_dialog=null;m_pending.Clear();base.Dispose();}

    // Spawn hooks run inside PlayerData.SpawnPlayer. Wait until the player has
    // entered Playing, then snapshot the complete entity through the normal save path.
    public void Update(float dt) {
        UpdateChoice();
        if(!ScNet.IsAuthority)return;
        if (m_pendingCheckpoint.Count == 0) return;
        m_checkpointRetry = Math.Max(0, m_checkpointRetry - dt);
        if (m_checkpointRetry > 0 || Project is null || !ReferenceEquals(Project, GameManager.Project)) return;
        var players = Project.FindSubsystem<SubsystemPlayers>(true);
        var ready = players.PlayersData.Where(p => p.IsReadyForPlaying && p.ComponentPlayer?.ComponentHealth.Health > 0
            && ReferenceEquals(p.ComponentPlayer.Entity.Project, Project) && Project.Entities.Contains(p.ComponentPlayer.Entity))
            .Select(p => p.PlayerIndex);
        try {
            TryCheckpoint(true, ready, () => GameManager.SaveProject(waitForCompletion: false, showErrorDialog: true));
        }
        catch (Exception e) {
            m_checkpointRetry = 10;
            Engine.Log.Error($"[ScCsgoKnives] Initial player checkpoint could not be requested; retry in 10s: {e}");
        }
    }

    internal bool TryCheckpoint(bool currentProject, IEnumerable<int> readyPlayers, Action save) {
        if (!currentProject) return false;
        int[] ready = readyPlayers.Where(m_pendingCheckpoint.Contains).ToArray();
        if (ready.Length == 0) return false;
        save();
        foreach (int player in ready) m_pendingCheckpoint.Remove(player);
        return true;
    }
}
