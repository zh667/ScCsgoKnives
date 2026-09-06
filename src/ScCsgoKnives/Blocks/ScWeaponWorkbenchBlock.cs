using Engine;
using Engine.Graphics;
namespace Game;

// Independent mesh; do not inherit CraftingTableBlock's static Index field.
public sealed class ScWeaponWorkbenchBlock : ScNoDurabilityBlock {
    public ScWeaponWorkbenchBlock() {
        DefaultDisplayName = "武器装配台"; DefaultCategory = "Items"; CraftingId = "sccsgoworkbench";
        // Match vanilla workbench presentation; world meshes remain full size.
        FirstPersonScale = .4f; FirstPersonOffset = new(.5f, -.5f, -.6f); FirstPersonRotation = new(0, 40, 0);
        InHandScale = .5f; InHandOffset = new(0, .1f, -.26f); InHandRotation = new(0, 45, 0);
        IsTransparent = true; DefaultIsInteractive = true; MaxStacking = 40; DefaultSoundMaterialName = "Metal";
    }
    static readonly BoundingBox[] Collision = [
        new(new Vector3(.01f,.72f,.07f),new Vector3(.99f,.84f,.93f)),
        new(new Vector3(.11f,.335f,.185f),new Vector3(.39f,.705f,.815f)),
        new(new Vector3(.06f,0f,.14f),new Vector3(.16f,.72f,.24f)),
        new(new Vector3(.84f,0f,.14f),new Vector3(.94f,.72f,.24f)),
        new(new Vector3(.06f,0f,.76f),new Vector3(.16f,.72f,.86f)),
        new(new Vector3(.84f,0f,.76f),new Vector3(.94f,.72f,.86f)),
        new(new Vector3(.07f,.1475f,.16f),new Vector3(.93f,.2125f,.84f))
    ];
    public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain,int value) => Collision;
    BlockMesh m_world, m_item, m_icon;
    public override void Initialize() {
        ScSurvivalMesh.Preload();   // main thread; the terrain thread must never be the first to load the atlas
        m_item=ScSurvivalMesh.Build(6); m_world=ScSurvivalMesh.Build(6);
        m_icon=ScSurvivalMesh.InventoryMesh(m_item);
        m_world.TransformPositions(Matrix.CreateTranslation(.5f,.5f,.5f));
        base.Initialize();
    }
    public override Texture2D GetDefaultTexture(int value) => ScSurvivalMesh.Surface;
    public override bool IsFaceTransparent(SubsystemTerrain terrain,int face,int value) => true;
    public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) =>
        generator.GenerateShadedMeshVertices(this,x,y,z,m_world,Color.White,null,null,geometry.GetGeometry(GetDefaultTexture(value)).SubsetOpaque);
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) =>
        BlocksManager.DrawMeshBlock(renderer,env?.DrawBlockMode == DrawBlockMode.UI ? m_icon : m_item,GetDefaultTexture(value),
            env?.DrawBlockMode == DrawBlockMode.UI ? Color.White : color,size,ref matrix,env);
    public override string GetDescription(int value) => "交互后选择枪械或刀具，查看材料与等级再组装；枪械以空枪交付。";
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield return ScAmmoBlock.Recipe(Terrain.MakeBlockValue(BlockIndex), 1, "武器装配台", ["ironingot", "ironingot", "ironingot", "ironingot", "copperingot", "copperingot", "planks", "planks", "planks"]);
    }
}
