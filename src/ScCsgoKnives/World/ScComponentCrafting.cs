namespace Game;

/// <summary>Large-count component recipes belong to the workbench, not the nine-cell crafting grid.</summary>
public static class ScComponentCrafting {
    /// <summary>The workbench tab of the components (and of the ammunition made beside them).</summary>
    public const string Category = "配件制作";
    public static Entry Find(int value) => Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScWeaponMaterialBlock>(true)
        ? All.FirstOrDefault(e=>e.Kind==Terrain.ExtractData(value)) : null;
    internal static Func<string,int> ResolveOverride; // headless fixture, never assigned by gameplay
    public sealed record Entry(int Kind, (string Id, int Count)[] Ingredients) {
        public int Value => ScWeaponMaterialBlock.Value(Kind);
        public string Name => ScWeaponMaterialBlock.NameOf(Kind);
        public Dictionary<int, int> Materials() => Ingredients.ToDictionary(p => Resolve(p.Id), p => p.Count);
    }
    public static readonly Entry[] All = [
        // 1.6.0 (feedback-1124-170 D11/D12): iron is scarce in vanilla worlds. The metal part takes half the iron, and the
        // mechanical parts no longer consume a metal part (some of the load moves to copper). Weapon recipes are unchanged.
        new(0, [("ironingot",4),("coalchunk",2)]),
        new(1, [("ironingot",2),("copperingot",4),("germaniumchunk",2)]),
        new(2, [("leather",4),("planks",2),("copperingot",1)]),
        // Optics no longer consume diamonds; first-time skin application still does.
        new(3, [("glass",4),("copperingot",2),("germaniumchunk",2)]),
        new(4, [("pigment:0",4),("canvas",2),("copperingot",2)])
    ];
    /// <summary>1.6.0 (feedback-1124-170 D13/P6): half of the fallen T drop CS components, so fighting feeds the economy.</summary>
    public const float EnemyDropChance = .5f;
    /// <summary>Which component a fallen T drops (P6): metal part 50 %, mechanical parts 30 %, grip or scope 20 % (evenly).</summary>
    public static int EnemyDropKind(float roll, bool scope) => roll < .5f ? ScWeaponMaterialBlock.Blank : roll < .8f ? ScWeaponMaterialBlock.Mechanism
        : scope ? ScWeaponMaterialBlock.Optics : ScWeaponMaterialBlock.Grip;
    public static int Resolve(string ingredient) {
        if (ResolveOverride is not null) return ResolveOverride(ingredient);
        string[] parts = ingredient.Split(':');
        if (parts[0] == "sccsgomaterial") return ScWeaponMaterialBlock.Value(int.Parse(parts[1]));
        var block = BlocksManager.Blocks.FirstOrDefault(b => b is not null && b.CraftingId == parts[0])
            ?? throw new InvalidOperationException("Unknown workbench ingredient: " + ingredient);
        return Terrain.MakeBlockValue(block.BlockIndex, 0, parts.Length == 2 ? int.Parse(parts[1]) : 0);
    }
}
