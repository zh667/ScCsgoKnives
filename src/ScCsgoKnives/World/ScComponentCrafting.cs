namespace Game;

/// <summary>Large-count component recipes belong to the workbench, not the nine-cell crafting grid.</summary>
public static class ScComponentCrafting {
    internal static Func<string,int> ResolveOverride; // headless fixture, never assigned by gameplay
    public sealed record Entry(int Kind, (string Id, int Count)[] Ingredients) {
        public int Value => ScWeaponMaterialBlock.Value(Kind);
        public string Name => ScWeaponMaterialBlock.Names[Kind];
        public Dictionary<int, int> Materials() => Ingredients.ToDictionary(p => Resolve(p.Id), p => p.Count);
    }
    public static readonly Entry[] All = [
        new(0, [("ironingot",12),("coalchunk",4)]),
        new(1, [("sccsgomaterial:0",2),("copperingot",8),("germaniumchunk",4)]),
        new(2, [("leather",8),("planks",4),("copperingot",2)]),
        // SurvivalCraft's vanilla diamond item uses crafting id "diamond";
        // "diamondchunk" is not registered and would crash when the workbench
        // tries to resolve this recipe for display.
        new(3, [("glass",8),("copperingot",4),("germaniumchunk",4),("diamond",1)]),
        new(4, [("pigment:0",8),("canvas",4),("copperingot",4)])
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
