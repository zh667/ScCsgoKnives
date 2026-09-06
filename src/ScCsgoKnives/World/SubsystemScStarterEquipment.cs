using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

/// <summary>A one-time gift for the player's first survival spawn, saved per world.</summary>
public sealed class SubsystemScStarterEquipment : Subsystem, IUpdateable {
    public const int MagazineCount = 3;
    readonly HashSet<int> m_granted = [];
    readonly HashSet<int> m_pendingCheckpoint = [];
    float m_checkpointRetry;
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public override void Load(ValuesDictionary values) {
        base.Load(values);
        m_granted.Clear();
        m_pendingCheckpoint.Clear();
        m_checkpointRetry = 0;
        foreach (string id in values.GetValue("GrantedPlayers", "").Split(','))
            if (int.TryParse(id, out int index)) m_granted.Add(index);
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values);
        values.SetValue("GrantedPlayers", string.Join(",", m_granted.Order()));
    }
    public static bool Eligible(GameMode mode, PlayerData.SpawnMode spawnMode, int spawnsCount) =>
        mode is GameMode.Harmless or GameMode.Survival or GameMode.Challenging or GameMode.Cruel
        && spawnMode is PlayerData.SpawnMode.InitialIntro or PlayerData.SpawnMode.InitialNoIntro && spawnsCount == 1;
    public static (int Value, int Count)[] Items() {
        int knife = Enumerable.Range(0, CsmcKnifeRig.KnifeCount).Single(v => CsmcKnifeRig.GetAssetName(v) == "default_ct");
        int pistol = Enumerable.Range(0, GunSpec.All.Length).Single(v => GunSpec.All[v].Name == "usp_silencer");
        return [
            (Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScKnifeBlock>(true), 0, knife), 1),
            (Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true), 0, GunSpec.MakeData(pistol, GunSpec.All[pistol].Magazine)), 1),
            (ScAmmoBlock.Value(ScAmmoBlock.Magazine), MagazineCount)
        ];
    }
    public bool TryGrant(GameMode mode, PlayerData.SpawnMode spawnMode, int playerIndex, int spawnsCount,
        IInventory inventory, Action<int, int> dropOverflow) {
        if (!Eligible(mode, spawnMode, spawnsCount) || m_granted.Contains(playerIndex)
            || inventory is null or ComponentCreativeInventory) return false;
        var items = Items();
        // Mark before delivery so reentrant spawn hooks cannot grant twice.
        m_granted.Add(playerIndex);
        foreach (var item in items) {
            int remaining = ComponentInventoryBase.AcquireItems(inventory, item.Value, item.Count);
            if (remaining > 0) dropOverflow(item.Value, remaining);
        }
        ScInventoryTransaction.Changed(inventory);
        m_pendingCheckpoint.Add(playerIndex);
        return true;
    }

    // Spawn hooks run inside PlayerData.SpawnPlayer. Wait until the player has
    // entered Playing, then snapshot the complete entity through the normal save path.
    public void Update(float dt) {
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
