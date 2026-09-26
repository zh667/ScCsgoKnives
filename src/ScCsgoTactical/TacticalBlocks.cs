using Engine;
using Engine.Graphics;
namespace Game;

// In the split build these item identities live in core; the addon owns their gameplay.
public abstract class ScOptionalTacticalBlock : Block {
#if SC_SPLIT
    protected static bool Available => ScOptionalAgents.Available;
#else
    protected static bool Available => true;
#endif
    public override IEnumerable<int> GetCreativeValues()=>Available?base.GetCreativeValues():[];
    public override int SetDamage(int value,int damage)=>Available?base.SetDamage(value,damage):value;
    public override int GetDamageDestructionValue(int value)=>Available?base.GetDamageDestructionValue(value):value;
    public override string GetDisplayName(SubsystemTerrain terrain,int value)=>Available?base.GetDisplayName(terrain,value):DefaultDisplayName+" · 需要探员包";
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env)=>BlocksManager.DrawCubeBlock(r,value,new Vector3(size*.35f),ref matrix,color*new Color(130,155,175),color,env);
    protected ScOptionalTacticalBlock(){DefaultTextureSlot=15;}
}
public sealed class ScTacticalShieldBlock : ScOptionalTacticalBlock {
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    public const int Life=2000;
    static BlockMesh mesh;
    static Texture2D texture;
    public ScTacticalShieldBlock(){DefaultDisplayName="防爆盾";DefaultCategory="CS武器";CraftingId="sctacticalshield";IsPlaceable=false;IsCollidable=false;MaxStacking=1;Durability=Life;DefaultMeleePower=0;DefaultProjectilePower=0;FirstPersonScale=1;FirstPersonOffset=new(0,-.35f,-.65f);FirstPersonRotation=Vector3.Zero;InHandScale=1;InHandOffset=new(0,.3f,0);DefaultIconViewScale=.7f;DefaultIconViewOffset=new(1,1,2);}
    public static bool IsShield(int value)=>Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScTacticalShieldBlock>(true);
    public static int Wear(int value)=>Math.Clamp(Terrain.ExtractData(value),0,Life);
    public override int GetDamage(int value)=>Wear(value);
    public override int SetDamage(int value,int damage)=>Available?Terrain.ReplaceData(value,Math.Clamp(damage,0,Life)):value;
    public override int GetDamageDestructionValue(int value)=>SetDamage(value,Life);
    public override string GetDisplayName(SubsystemTerrain terrain,int value)=>$"防爆盾 · {(Life-Wear(value))*100/Life}%"+(Available?"":" · 需要探员包");
    public override string GetDescription(int value)=>"持在手中抵挡正面盾面覆盖内的子弹和近战，不能同时用枪。侧后方、脚部与环境伤害仍危险。耐久耗尽保留损坏盾；使用战术维修包维修。";
    public override void Initialize(){base.Initialize();if(!Available)return;var model=ContentManager.Get<Model>("Models/ScCsgoTactical/shield");texture=ContentManager.Get<Texture2D>("Textures/ScCsgoTactical/shield");mesh=new();foreach(var m in model.Meshes)foreach(var part in m.MeshParts)mesh.AppendModelMeshPart(part,Matrix.Identity,false,false,false,false,Color.White);}
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){if(!Available){base.DrawBlock(r,value,color,size,ref matrix,env);return;}BlocksManager.DrawMeshBlock(r,mesh,texture,(Wear(value)>=Life?new Color(100,100,100):Color.White)*color,size,ref matrix,env);}
}
public sealed class ScTacticalBeaconBlock : ScOptionalTacticalBlock {
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    public static readonly string[] Names=["已停用的救援信标","CT 招募信标","T 招募信标","战术维修包"];
    public ScTacticalBeaconBlock(){DefaultCategory="CS武器";CraftingId="sctacticalbeacon";IsPlaceable=false;IsCollidable=false;MaxStacking=10;Durability=-1;FirstPersonScale=1;FirstPersonOffset=new(.25f,-.25f,-.55f);InHandScale=1;DefaultIconViewScale=2.4f;Behaviors=Available?"ScTactical":"";}
    public override void Initialize(){base.Initialize();if(!Available)return;TacticalItemMesh.LoadRadios();TacticalItemMesh.Load("repair_item");}
    public override Vector3 GetIconViewOffset(int value,DrawBlockEnvironmentData env)=>Terrain.ExtractData(value)==3?base.GetIconViewOffset(value,env):new(.7f,.4f,2);
    // This is one standalone image, not a tile of the vanilla 16 x 16 atlas.
    public override int GetTextureSlotCount(int value)=>1;
    public override int GetFaceTextureSlot(int face,int value)=>0;
    public override string GetDisplayName(SubsystemTerrain terrain,int value)=>Names[Math.Clamp(Terrain.ExtractData(value),0,3)]+(Available?"":" · 需要探员包");
    public override string GetDescription(int value)=>Terrain.ExtractData(value)==3?"使用后维修快捷栏第一面受损盾牌，恢复 1000 耐久。":"对近处地面使用。每位玩家最多 1 名同伴，召唤后自动跟随；对准同伴按 E／交互键打开装备和指令。";
    public override IEnumerable<int> GetCreativeValues()=>Available?Enumerable.Range(1,3).Select(i=>Terrain.MakeBlockValue(BlockIndex,0,i)):[];
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){
        if(!Available){base.DrawBlock(r,value,color,size,ref matrix,env);return;}
        int kind=Terrain.ExtractData(value);
        if(kind==3)TacticalItemMesh.Draw("repair_item",r,color,size,ref matrix,env);
        else TacticalItemMesh.DrawRadio(TacticalItemMesh.RecruitRadioKind(kind),r,color,size,ref matrix,env);
    }
}
public sealed class ScTacticalDefuserBlock : ScOptionalTacticalBlock {
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    public ScTacticalDefuserBlock(){DefaultDisplayName="拆弹钳";DefaultCategory="CS武器";CraftingId="sctacticaldefuser";IsPlaceable=false;IsCollidable=false;MaxStacking=1;Durability=-1;FirstPersonScale=1;FirstPersonOffset=new(.25f,-.25f,-.55f);InHandScale=1;DefaultIconViewScale=3;}
    public override void Initialize(){base.Initialize();if(!Available)return;TacticalItemMesh.Load("defuser_item");}
    public override int GetTextureSlotCount(int value)=>1;
    public override int GetFaceTextureSlot(int face,int value)=>0;
    public override string GetDescription(int value)=>"在枪械台制作。放在随身背包或快捷栏即可将敌方 C4 拆除时间从 10 秒缩短到 5 秒。按住 C4 操作键（默认 E）或“拆除 C4”按钮；可重复使用。";
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){if(!Available){base.DrawBlock(r,value,color,size,ref matrix,env);return;}TacticalItemMesh.Draw("defuser_item",r,color,size,ref matrix,env);}
}
public sealed class ScTacticalSquadBlock : ScOptionalTacticalBlock {
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    public ScTacticalSquadBlock(){DefaultDisplayName="敌队挑战信标";DefaultCategory="CS武器";CraftingId="sctacticalsquad";IsPlaceable=false;IsCollidable=false;MaxStacking=1;Durability=-1;FirstPersonScale=1;FirstPersonOffset=new(.25f,-.25f,-.55f);InHandScale=1;DefaultIconViewScale=2.4f;Behaviors=Available?"ScTactical":"";}
    public override void Initialize(){base.Initialize();if(!Available)return;TacticalItemMesh.LoadRadios();}
    public override Vector3 GetIconViewOffset(int value,DrawBlockEnvironmentData env)=>new(.7f,.4f,2);
    public override string GetDisplayName(SubsystemTerrain terrain,int value)=>(Terrain.ExtractData(value)==1?"敌对 T 五人小队 · 挑战信标":"敌对 T 三人小队 · 挑战信标")+(Available?"":" · 需要探员包");
    public override string GetDescription(int value)=>"在武器装配台制作。对准 12 格内开阔地面使用，立即召唤会攻击你的敌队！生存成功召唤消耗 1 个，失败退回；创造不消耗。三人为狙击／步枪／近距突击；五人增加机枪与 C4 手枪手，不受天数限制。手动召唤不受敌队人数、玩家距离及自然刷新冷却限制。";
    public override IEnumerable<int> GetCreativeValues()=>Available?Enumerable.Range(0,2).Select(i=>Terrain.MakeBlockValue(BlockIndex,0,i)):[];
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){if(!Available){base.DrawBlock(r,value,color,size,ref matrix,env);return;}TacticalItemMesh.DrawRadio(TacticalItemMesh.SquadRadioKind(Terrain.ExtractData(value)),r,color,size,ref matrix,env);}
}
public static class TacticalItemMesh {
    public static readonly Dictionary<string,(BlockMesh Mesh,Texture2D Texture)> Items=new();
    static readonly Dictionary<int,(BlockMesh World,BlockMesh Icon)> Radios=new();
    // Map the existing item data; never allocate new IDs for visual variants.
    public static int RecruitRadioKind(int data)=>data==1?9:data==2?10:8;
    public static int SquadRadioKind(int data)=>data==1?12:11;
    public static void LoadRadios(){
        ScSurvivalMesh.Preload();
        for(int kind=8;kind<=12;kind++)if(!Radios.ContainsKey(kind)){
            var mesh=ScSurvivalMesh.Build(kind);Radios.Add(kind,(mesh,ScSurvivalMesh.InventoryMesh(mesh)));
        }
    }
    public static void DrawRadio(int kind,PrimitivesRenderer3D r,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){
        var item=Radios[kind];bool ui=env?.DrawBlockMode==DrawBlockMode.UI;
        BlocksManager.DrawMeshBlock(r,ui?item.Icon:item.World,ScSurvivalMesh.Surface,ui?Color.White:color,size,ref matrix,env);
    }
    public static void Load(string name){
        var model=ContentManager.Get<Model>("Models/ScCsgoTactical/"+name);var mesh=new BlockMesh();
        foreach(var part in model.Meshes.SelectMany(m=>m.MeshParts))mesh.AppendModelMeshPart(part,Matrix.Identity,false,false,false,false,Color.White);
        Items[name]=(mesh,ContentManager.Get<Texture2D>("Textures/ScCsgoTactical/"+name));
    }
    public static void Draw(string name,PrimitivesRenderer3D r,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env){var item=Items[name];BlocksManager.DrawMeshBlock(r,item.Mesh,item.Texture,color,size,ref matrix,env);}
}
