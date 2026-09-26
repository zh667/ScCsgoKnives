using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Creative-only counter guns. They are templates, not v5 instances: taking one
/// materializes a normal gun record and installs the counter in one transaction. This keeps
/// creative catalogue entries from sharing an ID or polluting a survival world.</summary>
public sealed class ScGunCounterTemplateBlock : ScNoDurabilityBlock {
    public ScGunCounterTemplateBlock() {
        DefaultDisplayName = "CS2 击杀计数器枪械"; DefaultCategory = "CS武器";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 1; CraftingId = "sccsgocountertemplate";
        DefaultMeleePower = 0; DefaultProjectilePower = 0; DefaultIconViewScale = .8f;
    }
    public override int GetDisplayOrder(int value) => 216;
    const int EntryMask = 1023, LevelShift = 10, LevelMask = 31;
    static readonly (int Variant, int Skin)[] Entries = Enumerable.Range(0, GunSpec.All.Length)
        .Select(v => (v, ScGunSkinCatalog.None))
        .Concat(ScGunSkinCatalog.All.Select(s => (Array.FindIndex(GunSpec.All, g => g.Name == s.Gun), s.PaintId)))
        .Where(x => x.Item1 >= 0).ToArray();
    static int Encode(int entry, int level) => (entry & EntryMask) | (ScGunGrowth.Clamp(level) << LevelShift);
    public override IEnumerable<int> GetCreativeValues() {
        // Keep one entry per gun/finish in the creative tab.  Level selection is
        // performed at the workbench for a real hotbar instance, avoiding a 31x
        // catalogue explosion.
        for (int entry = 0; entry < Entries.Length; entry++)
            if (ScMinimalEdition.SkinAvailable(Entries[entry].Skin))
            yield return Terrain.MakeBlockValue(BlockIndex, 0, Encode(entry, 0));
    }
    static bool TrySpec(int data, out int variant, out int skin, out int level) {
        int entry = data & EntryMask; level = (data >> LevelShift) & LevelMask;
        variant = skin = -1;
        if (entry < 0 || entry >= Entries.Length || level > ScGunGrowth.MaxLevel) return false;
        (variant, skin) = Entries[entry]; return true;
    }
    public static bool IsTemplate(int value) => BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunCounterTemplateBlock), out int index)
        && Terrain.ExtractContents(value) == index;
    public static bool TrySnapshot(int value, out ScGunSnapshot snapshot) {
        snapshot = default;
        if (!IsTemplate(value) || !TrySpec(Terrain.ExtractData(value), out int variant, out int skin, out int level)) return false;
        snapshot = ScGunSnapshot.ForFresh(variant, true) with { SkinId = skin, CounterInstalled = true,
            KillCount = ScGunGrowth.KillsFor(variant, level), AppliedGrowthLevel = level, GrowthRulesVersion = ScGunGrowth.RulesVersion };
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
        return ScGunNames.Item(s);
    }
    public override string GetDescription(int value) => "创造模式计数器枪械；同一型号按 Lv0-Lv31 分列，拿出后立即带有所选等级。击杀门槛随等级递增。";
    public override int GetTextureSlotCount(int value) => 1;
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override Vector3 GetIconViewOffset(int value, DrawBlockEnvironmentData env) => Vector3.UnitZ;
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;
    public override void GenerateTerrainVertices(BlockGeometryGenerator g, TerrainGeometry geometry, int value, int x, int y, int z) { }
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        // A catalogue template has no animated first-person pose. Update materializes it before
        // normal use; if registration is refused, do not draw its inventory mesh over the camera.
        if (env?.DrawBlockMode == DrawBlockMode.FirstPerson) return;
        if (!TrySnapshot(value, out var s)) return;
        var gun = (ScGunBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGunBlock>(true)];
        gun.DrawVisual(renderer, Terrain.MakeBlockValue(gun.BlockIndex, 0, GunSpec.MakeData(s.Variant, GunSpec.All[s.Variant].Magazine)), s.Variant, s.SkinId, color, size, ref matrix, env);
    }
}

/// <summary>Template conversion kept separate so normal ScGunMutation's skin-template rules remain strict.</summary>
static class ScGunCounterTemplateMaterialize {
    public static ScGunResult Commit(ScGunMutation tx, ScGunSnapshot template) => tx.Commit(r => {
        r.SkinId = template.SkinId; r.CounterInstalled = true; r.GrowthRulesVersion = ScGunGrowth.RulesVersion;
        r.KillCount = template.KillCount; r.AppliedGrowthLevel = template.AppliedGrowthLevel; r.PendingGrowthLevel = ScGunGrowth.NoPending;
    });
}
