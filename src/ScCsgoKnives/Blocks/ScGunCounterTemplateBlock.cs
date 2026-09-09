using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Creative-only counter guns. They are templates, not v5 instances: taking one
/// materializes a normal gun record and installs the counter in one transaction. This keeps
/// creative catalogue entries from sharing an ID or polluting a survival world.</summary>
public sealed class ScGunCounterTemplateBlock : ScNoDurabilityBlock {
    public ScGunCounterTemplateBlock() {
        DefaultDisplayName = "CS2 击杀计数器枪械"; DefaultCategory = "Weapons";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 1; CraftingId = "sccsgocountertemplate";
        DefaultMeleePower = 0; DefaultProjectilePower = 0; DefaultIconViewScale = .8f;
    }
    public override int GetDisplayOrder(int value) => 223;
    public override IEnumerable<int> GetCreativeValues() {
        // Factory counter version for every gun.
        for (int variant = 0; variant < GunSpec.All.Length; variant++)
            yield return Terrain.MakeBlockValue(BlockIndex, 0, variant);
        // Every currently supported CS2 finish also gets a counter version.
        foreach (var skin in ScGunSkinCatalog.All)
            yield return Terrain.MakeBlockValue(BlockIndex, 0, GunSpec.All.First(g => g.Name == skin.Gun) is { } spec
                ? GunSpec.All.ToList().IndexOf(spec) + 1000 + skin.PaintId : skin.PaintId);
    }
    static bool TrySpec(int data, out int variant, out int skin) {
        variant = skin = -1;
        if (data >= 0 && data < GunSpec.All.Length) { variant = data; skin = ScGunSkinCatalog.None; return true; }
        foreach (var paint in ScGunSkinCatalog.All) {
            int v = Array.FindIndex(GunSpec.All, g => g.Name == paint.Gun);
            if (data == v + 1000 + paint.PaintId) { variant = v; skin = paint.PaintId; return true; }
        }
        return false;
    }
    public static bool IsTemplate(int value) => BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunCounterTemplateBlock), out int index)
        && Terrain.ExtractContents(value) == index;
    public static bool TrySnapshot(int value, out ScGunSnapshot snapshot) {
        snapshot = default;
        if (!IsTemplate(value) || !TrySpec(Terrain.ExtractData(value), out int variant, out int skin)) return false;
        snapshot = ScGunSnapshot.ForFresh(variant, true) with { SkinId = skin, CounterInstalled = true };
        return true;
    }
    public static ScGunResult Materialize(IInventory inventory, int slot, string holder) {
        if (!TrySnapshot(inventory.GetSlotValue(slot), out var template)) return ScGunResult.Invalid;
        var tx = ScGunMutation.Prepare(inventory, slot, holder, out var why);
        // Counter templates use the normal gun transaction; the mutation's template branch
        // is deliberately extended here by the dedicated materialization seam below.
        return tx is null ? why : ScGunCounterTemplateMaterialize.Commit(tx, template);
    }
    public override string GetDisplayName(SubsystemTerrain terrain, int value) {
        if (!TrySnapshot(value, out var s)) return "未知计数器枪械";
        return GunSpec.All[s.Variant].Name + (s.SkinId == 0 ? " · 原厂 · 击杀计数器" : $" · {ScGunSkinCatalog.NameOf(s.SkinId)} · 击杀计数器");
    }
    public override string GetDescription(int value) => "创造模式计数器枪械；拿到手中后保留计数器状态，击杀从 0 开始。";
    public override int GetTextureSlotCount(int value) => 1;
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override Vector3 GetIconViewOffset(int value, DrawBlockEnvironmentData env) => Vector3.UnitZ;
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;
    public override void GenerateTerrainVertices(BlockGeometryGenerator g, TerrainGeometry geometry, int value, int x, int y, int z) { }
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        if (!TrySnapshot(value, out var s)) return;
        var gun = (ScGunBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGunBlock>(true)];
        gun.DrawVisual(renderer, Terrain.MakeBlockValue(gun.BlockIndex, 0, GunSpec.MakeData(s.Variant, GunSpec.All[s.Variant].Magazine)), s.Variant, s.SkinId, color, size, ref matrix, env);
    }
}

/// <summary>Template conversion kept separate so normal ScGunMutation's skin-template rules remain strict.</summary>
static class ScGunCounterTemplateMaterialize {
    public static ScGunResult Commit(ScGunMutation tx, ScGunSnapshot template) => tx.Commit(r => {
        r.SkinId = template.SkinId; r.CounterInstalled = true; r.GrowthRulesVersion = ScGunGrowth.RulesVersion;
        r.KillCount = 0; r.AppliedGrowthLevel = 0; r.PendingGrowthLevel = ScGunGrowth.NoPending;
    });
}
