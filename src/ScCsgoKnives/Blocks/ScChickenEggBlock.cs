using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Own block identity avoids competing for other mods' vanilla egg data IDs.</summary>
public sealed class ScChickenEggBlock : ScNoDurabilityBlock {
    public ScChickenEggBlock(){DefaultDisplayName="CS 小鸡生成蛋";DefaultCategory="CS武器";CraftingId="sccsgochickenegg";
        IsPlaceable=false;IsCollidable=false;MaxStacking=40;DefaultMeleePower=0;DefaultProjectilePower=0;
        // Same presentation as the native egg; Block defaults put its mesh at the camera origin.
        FirstPersonScale=.4f;FirstPersonOffset=new(.5f,-.5f,-.6f);FirstPersonRotation=new(0,40,0);
        InHandScale=.3f;InHandOffset=new(0,.12f,0);DefaultIconViewOffset=Vector3.One;
    }
    public override int GetDisplayOrder(int value)=>220;
    public override IEnumerable<int> GetCreativeValues(){yield return Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScChickenEggBlock>(true));}
    public override string GetDescription(int value)=>"对地面使用生成 CS 小鸡。对准小鸡按 E／交互键切换跟随；枪杀会爆炸，刀杀正常死亡。";
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int value,int x,int y,int z){}
    public override void DrawBlock(PrimitivesRenderer3D renderer,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){
        var eggs=(EggBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<EggBlock>(true)];
        var egg=eggs.EggTypes.FirstOrDefault(e=>e.ShowEgg);if(egg is null)return;
        eggs.DrawBlock(renderer,Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<EggBlock>(true),0,EggBlock.SetEggType(0,egg.EggTypeIndex)),new Color(235,210,140)*color,size,ref matrix,env);
    }
}
