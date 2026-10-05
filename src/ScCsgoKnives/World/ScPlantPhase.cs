using Engine;
namespace Game;

/// <summary>What planting a C4 looks like right now (r2-c4-completion-20260929). Presentation only, produced from the
/// same gameplay clock that commits the charge (SubsystemScC4 for players, the enemy AI for NPCs), so first person,
/// CT/T actors, vanilla and third-party bodies agree on the moment the bomb leaves the hand.
/// Seconds run from the start of the plant: the charge is committed at <see cref="PlantSeconds"/> (the unchanged
/// 3.2 s rule), after which the hand is empty and recovers until <see cref="EndSeconds"/> (the end of CS2's 4 s
/// first-person clip). Value stays the C4 while the action runs, also for the last one of a stack and in creative
/// mode; after <see cref="Placed"/> no C4 is drawn in the hand.</summary>
public readonly record struct ScPlantPhase(int Value,bool Placed,float Seconds,long Sequence,Vector3 Position) {
    public const float PlantSeconds=ScC4Charge.PlantSeconds,EndSeconds=4f;
    public bool Active=>Value!=0;
    /// <summary>0..1 through the operating part (before the commit).</summary>
    public float Operating=>Math.Clamp(Seconds/PlantSeconds,0,1);
    /// <summary>0..1 through the recovery after the commit.</summary>
    public float Recovery=>Placed?Math.Clamp((Seconds-PlantSeconds)/(EndSeconds-PlantSeconds),0,1):0;
}
