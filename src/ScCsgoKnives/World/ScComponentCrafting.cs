namespace Game;

/// <summary>Large-count component recipes belong to the workbench, not the nine-cell crafting grid.</summary>
public static class ScComponentCrafting {
    public static Entry Find(int value) => Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScWeaponMaterialBlock>(true)
        ? All.FirstOrDefault(e=>e.Kind==Terrain.ExtractData(value)) : null;
    internal static Func<string,int> ResolveOverride; // headless fixture, never assigned by gameplay
    public sealed record Entry(int Kind, (string Id, int Count)[] Ingredients) {
        public int Value => ScWeaponMaterialBlock.Value(Kind);
        public string Name => ScWeaponMaterialBlock.Names[Kind];
        public Dictionary<int, int> Materials() => Ingredients.ToDictionary(p => Resolve(p.Id), p => p.Count);
    }
    public static readonly Entry[] All = [
        new(0, [("ironingot",8),("coalchunk",3)]),
        new(1, [("sccsgomaterial:0",1),("copperingot",6),("germaniumchunk",2)]),
        new(2, [("leather",4),("planks",2),("copperingot",1)]),
        // Optics no longer consume diamonds; first-time skin application still does.
        new(3, [("glass",4),("copperingot",2),("germaniumchunk",2)]),
        new(4, [("pigment:0",4),("canvas",2),("copperingot",2)])
    ];
    public static int Resolve(string ingredient) {
        if (ResolveOverride is not null) return ResolveOverride(ingredient);
        string[] parts = ingredient.Split(':');
        if (parts[0] == "sccsgomaterial") return ScWeaponMaterialBlock.Value(int.Parse(parts[1]));
        var block = BlocksManager.Blocks.FirstOrDefault(b => b is not null && b.CraftingId == parts[0])
            ?? throw new InvalidOperationException("Unknown workbench ingredient: " + ingredient);
        return Terrain.MakeBlockValue(block.BlockIndex, 0, parts.Length == 2 ? int.Parse(parts[1]) : 0);
    }
}
