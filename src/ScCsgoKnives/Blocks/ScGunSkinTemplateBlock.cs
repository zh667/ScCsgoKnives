using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Immutable creative catalogue entries. Separate block data stores the stable Paint ID;
/// ScGunBlock's v5 bits and registry schema are untouched. A held template is atomically converted
/// to an ordinary instance gun before use. Templates in containers/drops can persist without an ID.</summary>
public sealed class ScGunSkinTemplateBlock : ScNoDurabilityBlock {
    public ScGunSkinTemplateBlock() {
        DefaultDisplayName = "CS2 涂装枪械"; DefaultCategory = "Weapons";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 1; CraftingId = "sccsgoskintemplate";
        DefaultMeleePower = 0; DefaultProjectilePower = 0;
    }
    public override int GetDisplayOrder(int value) => 222;
    public override IEnumerable<int> GetCreativeValues() => ScGunSkinCatalog.All.Select(s => Terrain.MakeBlockValue(BlockIndex, 0, s.PaintId));
    public static bool IsTemplate(int value) => BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunSkinTemplateBlock), out int index) && Terrain.ExtractContents(value) == index;
    public static bool TrySnapshot(int value, out ScGunSnapshot snapshot) {
        snapshot = default;
        if (!IsTemplate(value) || ScGunSkinCatalog.Find(Terrain.ExtractData(value)) is not { } skin) return false;
        int variant = Array.FindIndex(GunSpec.All, g => g.Name == skin.Gun);
        if (variant < 0) return false;
        snapshot = ScGunSnapshot.ForFresh(variant, true) with { SkinId = skin.PaintId }; return true;
    }
    public static ScGunResult Materialize(IInventory inventory, int slot, string holder) {
        var tx = ScGunMutation.Prepare(inventory, slot, holder, out var why);
        return tx is null ? why : tx.Commit(_ => { });
    }
    public override string GetDisplayName(SubsystemTerrain terrain, int value) {
        if (!TrySnapshot(value, out var s)) return "未知涂装枪械（数据保留）";
        string name = s.Variant switch { 0 => "AK-47", 1 => "M4A1 消音型", 2 => "AWP", _ => GunSpec.All[s.Variant].Name };
        return name + " · " + ScGunSkinCatalog.NameOf(s.SkinId);
    }
    public override string GetDescription(int value) => "创造模式涂装枪械，拿到手中即可使用。满弹、满耐久；生存换肤请使用武器装配台。";
    public override int GetTextureSlotCount(int value) => 1;
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override Vector3 GetIconViewOffset(int value, DrawBlockEnvironmentData env) => Vector3.UnitZ;
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;
    public override void GenerateTerrainVertices(BlockGeometryGenerator g, TerrainGeometry geometry, int value, int x, int y, int z) { }
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        if (!TrySnapshot(value, out var s)) return;
        var gun = (ScGunBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGunBlock>(true)];
        gun.DrawVisual(renderer, value, s.Variant, s.SkinId, color, size, ref matrix, env);
    }
}
