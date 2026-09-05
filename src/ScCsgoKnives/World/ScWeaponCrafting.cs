namespace Game;

public static class ScWeaponCrafting {
    public sealed record Entry(string Name, bool Knife, int Variant, int Level, int B, int M, int H, int O = 0, int Diamond = 0, int Germanium = 0) {
        public int Value => Knife ? Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScKnifeBlock>(true), 0, Variant)
            : Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true), 0, GunSpec.MakeData(Variant, 0));
        public Dictionary<int, int> Materials() {
            var result = new Dictionary<int, int>();
            int[] parts = [B, M, H, O];
            for (int i = 0; i < parts.Length; i++) if (parts[i] > 0) result.Add(ScWeaponMaterialBlock.Value(i), parts[i]);
            if (Diamond > 0) result.Add(Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<DiamondChunkBlock>(true)), Diamond);
            if (Germanium > 0) result.Add(Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<GermaniumChunkBlock>(true)), Germanium);
            return result;
        }
    }
    public static readonly Entry[] All = Build();
    public static Entry Find(int value) {
        int contents = Terrain.ExtractContents(value);
        bool knife = contents == BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        if (!knife && contents != BlocksManager.GetBlockIndex<ScGunBlock>(true)) return null;
        int variant = knife ? ScKnifeBlock.GetVariant(value) : ScGunBlock.GetVariant(value);
        return All.FirstOrDefault(e => e.Knife == knife && e.Variant == variant);
    }
    public static string Help(int value) {
        var e = Find(value);
        if (e is null) return "";
        string text = $"\n武器装配台 · 等级 {e.Level}\n金属坯件 ×{e.B}";
        if (e.M > 0) text += $"，精密机构 ×{e.M}";
        text += $"，握持组件 ×{e.H}";
        if (e.O > 0) text += $"，光学组件 ×{e.O}";
        if (e.Diamond > 0) text += $"，钻石 ×{e.Diamond}";
        if (e.Germanium > 0) text += $"，锗 ×{e.Germanium}";
        return text + (e.Knife ? "。" : "。交付空枪，弹药另制。");
    }
    static Entry[] Build() {
        var entries = new List<Entry>();
        for (int v = 0; v < CsmcKnifeRig.KnifeCount; v++) {
            string name = CsmcKnifeRig.GetAssetName(v);
            bool collection = name is "karambit" or "butterfly";
            entries.Add(new(name, true, v, collection ? 3 : 1, 2, collection ? 1 : 0, 1, Diamond: collection ? 1 : 0));
        }
        for (int v = 0; v < GunSpec.All.Length; v++) {
            string n = GunSpec.All[v].Name;
            (int level, int b, int m, int o, int diamond, int germanium) = n switch {
                "glock18" or "hkp2000" or "p250" or "usp_silencer" => (2, 2, 1, 0, 0, 0),
                "fiveseven" or "tec9" or "cz75a" => (2, 2, 2, 0, 0, 0),
                "deagle" or "revolver" or "elite" or "mac10" or "mp9" or "mp7" or "ump45" or "mp5sd" or "mag7" or "xm1014" => (3, 3, 2, 0, 0, 0),
                "p90" or "bizon" or "galilar" or "famas" => (3, 4, 2, 0, 0, 0),
                "nova" or "sawedoff" => (2, 3, 1, 0, 0, 0),
                "ak47" or "m4a4" or "m4a1s" => (4, 4, 3, 0, 0, 0),
                "aug" or "sg556" => (4, 4, 3, 1, 0, 0),
                "ssg08" => (3, 3, 2, 1, 0, 0),
                "awp" => (5, 5, 3, 1, 1, 0),
                "scar20" or "g3sg1" => (6, 5, 4, 1, 1, 0),
                "m249" or "negev" => (6, 6, 4, 0, 1, 0),
                "taser" => (3, 2, 3, 0, 0, 2),
                _ => throw new InvalidOperationException("No survival recipe for " + n)
            };
            entries.Add(new(n, false, v, level, b, m, 1, o, diamond, germanium));
        }
        return entries.ToArray();
    }

    public static bool TryCraft(IInventory inventory, int result, IReadOnlyDictionary<int, int> materials) {
        int output = -1;
        for (int i = 0; i < inventory.SlotsCount; i++)
            if (inventory.GetSlotCount(i) == 0 && inventory.GetSlotCapacity(i, result) > 0) { output = i; break; }
        if (output < 0 || materials.Any(p => p.Value <= 0 || ScInventoryTransaction.Count(inventory, p.Key) < p.Value)) return false;
        var taken = new List<(int Slot, int Value, int Count)>();
        foreach (var material in materials) {
            int needed = material.Value;
            for (int i = 0; i < inventory.SlotsCount && needed > 0; i++) {
                if (inventory.GetSlotValue(i) != material.Key || inventory.GetSlotCount(i) == 0) continue;
                int want = Math.Min(needed, inventory.GetSlotCount(i)), got = inventory.RemoveSlotItems(i, want);
                taken.Add((i, material.Key, got)); needed -= got;
                if (got != want) break;
            }
            if (needed > 0) {
                foreach (var item in taken) inventory.AddSlotItems(item.Slot, item.Value, item.Count);
                return false;
            }
        }
        inventory.AddSlotItems(output, result, 1);
        ScInventoryTransaction.Changed(inventory);
        return true;
    }
}
