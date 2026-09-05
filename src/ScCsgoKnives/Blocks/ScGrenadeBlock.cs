using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ScGrenadeBlock : Block {
    public static readonly string[] Assets = ["grenade_hegrenade", "grenade_flashbang", "grenade_smokegrenade", "grenade_molotov", "grenade_incendiary", "grenade_decoy"];
    public static readonly string[] Names = ["高爆手雷", "闪光弹", "烟雾弹", "燃烧瓶", "燃烧弹", "诱饵弹"];
    public static bool Enabled(int kind) => kind is 0 or 1 or 2;
    readonly List<(BlockMesh Mesh, Texture2D Texture)>[] m_parts = new List<(BlockMesh, Texture2D)>[6];
    public ScGrenadeBlock() {
        DefaultDisplayName = "CS2 投掷物"; DefaultCategory = "Weapons"; CraftingId = "sccsgogrenade";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 4; DefaultTextureSlot = 0;
    }
    public static int Value(int kind) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGrenadeBlock>(true), 0, kind);
    public static int Kind(int value) => Terrain.ExtractData(value);
    public static int AssetIndex(int value) => Kind(value) is >= 0 and < 6 ? CsmcKnifeRig.GrenadeOffset + Kind(value) : -1;
    public static string MaterialKey(string asset, string material) => material is "weapon_molotov_flame" or "weapon_molotov_liquid" ? material : asset + "_cs2";
    public override void Initialize() {
        for (int i = 0; i < 6; i++) {
            var mesh = Cs2SkinnedMesh.Weapon(Assets[i]) ?? throw new InvalidOperationException("Missing grenade mesh: " + Assets[i]);
            if (!mesh.SetPose(Cs2Rig.Sample(Assets[i], "idle", 0), Cs2Placement.Placement())) throw new InvalidOperationException("Missing grenade pose");
            mesh.Skin(); m_parts[i] = [];
            foreach (var part in mesh.Primitives) {
                // Same full vertex bounds for each material, so all parts remain aligned.
                if (part.Material.EndsWith("_flame")) continue; // world/inventory bottle is unlit
                var block = new BlockMesh(); Cs2BlockMesh.Append(block, mesh.Skinned, part.Indices);
                m_parts[i].Add((block, ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + MaterialKey(Assets[i], part.Material))));
            }
        }
        base.Initialize();
    }
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        int kind = Kind(value); if (kind < 0 || kind >= 6) return;
        foreach (var part in m_parts[kind]) BlocksManager.DrawMeshBlock(renderer, part.Mesh, part.Texture, color, .65f * size, ref matrix, env);
    }
    public override void GenerateTerrainVertices(BlockGeometryGenerator g, TerrainGeometry t, int value, int x, int y, int z) { }
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => Kind(value) is >= 0 and < 6 ? Names[Kind(value)] : "未知投掷物（保留数据）";
    public override string GetDescription(int value) => "左键强投，右键或“轻投”按钮近抛；出手才消耗。每人最多 4 个活动投掷物或效果，全场最多 16 个。编辑/检视可调整闪光显示。";
    public override IEnumerable<int> GetCreativeValues() { for (int i = 0; i < 6; i++) if (Enabled(i)) yield return Value(i); }
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        string b = "sccsgomaterial:0";
        string[][] recipes = [[b,"ironingot","gunpowder","gunpowder","gunpowder"], [b,"glass","gunpowder"],
            [b,"coalchunk","coalchunk","gunpowder"], ["glass","glass","coalchunk","canvas","gunpowder"],
            [b,"copperingot","gunpowder","gunpowder","coalchunk"], [b,"copperingot","gunpowder"]];
        for (int i = 0; i < 6; i++) if (Enabled(i)) yield return ScAmmoBlock.Recipe(Value(i), 1, Names[i], recipes[i], i is 0 or 2 or 4 ? 3 : 2);
    }
}
