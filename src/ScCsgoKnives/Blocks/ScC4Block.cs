using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Separate equipment block: never a gun variant or a registry record.</summary>
public sealed class ScC4Block : ScNoDurabilityBlock {
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    Texture2D icon;
    public ScC4Block() {
        DefaultDisplayName = "C4"; DefaultCategory = "CS武器"; CraftingId = "sccsgoc4";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 1; DefaultTextureSlot = 0;
        DefaultMeleePower = 0; DefaultProjectilePower = 0;
    }
    public static int Value => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScC4Block>(true));
    public static bool IsValue(int value) {
        int index = BlocksManager.GetBlockIndex<ScC4Block>(false);
        return index > 0 && Terrain.ExtractContents(value) == index && Terrain.ExtractData(value) == 0;
    }
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => "C4 定时炸弹";
    public override int GetDisplayOrder(int value) => 212;
    public override Vector3 GetIconViewOffset(int value, DrawBlockEnvironmentData environmentData) => Vector3.UnitZ;
    public override string GetDescription(int value) => "按住 E 或“放置 C4”3.2 秒安装，松手、移动或切换物品取消。安装后 20 秒引爆，中心伤害 5000，半径 32 格；没有等级。";
    public override IEnumerable<int> GetCreativeValues() { yield return Value; }
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() { yield break; }
    public override int GetTextureSlotCount(int value) => 1;
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;
    public override void GenerateTerrainVertices(BlockGeometryGenerator g, TerrainGeometry t, int value, int x, int y, int z) { }
    ScThirdPersonWeapon.Group[] plantedGroups, droppedGroups;
    public static BlockMesh BuildWorldMesh() => ScC4Visuals.WorldGroups(true)[0].Mesh;
    public void DrawPlanted(PrimitivesRenderer3D renderer, Color color, ref Matrix matrix, DrawBlockEnvironmentData env) => DrawWorld(renderer,color,1,ref matrix,env,true);
    void DrawWorld(PrimitivesRenderer3D renderer, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env, bool planted) {
        var groups=planted ? plantedGroups??=ScC4Visuals.WorldGroups(true) : droppedGroups??=ScC4Visuals.WorldGroups(false);
        foreach(var group in groups)DrawOpaque(renderer,group.Mesh,ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+group.Texture),color,size,matrix,env);
    }
    // CS2 body alpha is a material mask, not opacity. Native cutout rendering
    // leaves only the white straps. Preserve the source texture and ignore its alpha.
    public static TexturedBatch3D OpaqueBatch(PrimitivesRenderer3D renderer,Texture2D texture) =>
        renderer.TexturedBatch(texture,false,0,DepthStencilState.Default,RasterizerState.CullNoneScissor,BlendState.Opaque,SamplerState.LinearWrap);
    static void DrawOpaque(PrimitivesRenderer3D renderer,BlockMesh mesh,Texture2D texture,Color color,float size,Matrix matrix,DrawBlockEnvironmentData env) {
        var batch=OpaqueBatch(renderer,texture);int first=batch.TriangleVertices.Count;
        Matrix transform=Matrix.CreateScale(size)*matrix;
        if(env?.ViewProjectionMatrix is Matrix projection)transform*=projection;
        float light=LightingManager.LightIntensityByLightValue[Math.Clamp(env?.Light??15,0,15)];
        var tint=new Color((byte)(color.R*light),(byte)(color.G*light),(byte)(color.B*light),(byte)255);
        foreach(var v in mesh.Vertices) {
            var p=Vector4.Transform(new Vector4(v.Position,1),transform);
            batch.TriangleVertices.Add(new VertexPositionColorTexture(new Vector3(p.X,p.Y,p.Z)/p.W,tint,v.TextureCoordinates));
        }
        foreach(int i in mesh.Indices)batch.TriangleIndices.Add(first+i);
    }
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        if (env?.DrawBlockMode == DrawBlockMode.UI) {
            icon ??= ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/c4_slot");
            ScInventoryIcon.Draw(renderer, value, size, ref matrix, icon, color, env); return;
        }
        DrawWorld(renderer,color,size,ref matrix,env,false);
    }
}
