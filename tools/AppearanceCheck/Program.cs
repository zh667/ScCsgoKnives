// Native GPU and lifecycle diagnostic. Not a full-game/multiplayer/Android acceptance test.
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Engine;
using Engine.Animation;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;
using NekoMeko;
using NekoMeko.Common;
using Neorxna.Common;
using Neorxna.Components;
using Neorxna.NeoModel;
using TemplatesDatabase;

if (args.Length is not (3 or 4)) throw new ArgumentException("AppearanceCheck <repo> <Content.zip> <output> [--ui-only]");
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context,name) => {
    string file=Path.Combine(AppContext.BaseDirectory,name.Name+".dll");
    return File.Exists(file)?context.LoadFromAssemblyPath(file):null;
};
string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
using var content = ZipFile.OpenRead(args[1]);
string Read(string suffix) { using var reader = new StreamReader(content.Entries.Single(e => e.FullName.EndsWith(suffix)).Open()); return reader.ReadToEnd(); }
string source = Path.Combine(root, "src/ScCsgoAppearance");
string nmm = Path.Combine(root, ".tmp/nmm-player-appearance-audit-20260920");
var caches = (IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches", BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
caches["Animations/ScTactical.json"] = [File.ReadAllText(Path.Combine(root,"src/ScCsgoTactical/Assets/Animations/ScTactical.json"))];
foreach (var e in content.Entries.Where(e => e.FullName.Contains("Shaders/") && !e.FullName.EndsWith('/'))) { using var r = new StreamReader(e.Open()); caches[e.FullName.Replace("Assets/","")] = [r.ReadToEnd()]; }
var results = new List<object>();
void Check(string name, bool passed) { if (!passed) throw new Exception(name); results.Add(new { name, passed }); }
Entity Attach(Project project, params Component[] components) { var e = (Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity)); e.m_project=project; e.m_components=components.ToList(); foreach(var c in components)c.m_entity=e; return e; }
var modelDict=(Dictionary<string,NekoResModel>)typeof(NekoMekoDataManager).GetField("m_modelsByKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
var skinDict=(Dictionary<string,NekoResSkin>)typeof(NekoMekoDataManager).GetField("m_skinsByKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
foreach(var file in Directory.GetFiles(Path.Combine(source,"Assets/NekoMekoRes/NekoResModel"),"*.json")){var m=JsonSerializer.Deserialize<NekoResModel>(File.ReadAllText(file));modelDict[m.Key]=m;}
foreach(var file in Directory.GetFiles(Path.Combine(source,"Assets/NekoMekoRes/NekoResSkin"),"*.json")){var m=JsonSerializer.Deserialize<NekoResSkin>(File.ReadAllText(file));skinDict[m.Key]=m;}
bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    if(args.Length==4&&args[3]=="--ui-only"){
        BlocksManager.Blocks[0]=new AirBlock();
        GloveChecks.Run(root,output,[],null,null,null,Check,caches,true);
        GloveUiChecks.Run(root,output,content,Check,caches);Console.WriteLine($"PASS {results.Count} UI/icon checks");return;
    }
    foreach(var pair in new[]{(typeof(AirBlock),0),(typeof(ScKnifeBlock),700),(typeof(ScGunBlock),701),(typeof(ScGrenadeBlock),702),(typeof(ScTacticalShieldBlock),705)}){var block=(Block)Activator.CreateInstance(pair.Item1);block.BlockIndex=pair.Item2;BlocksManager.Blocks[pair.Item2]=block;BlocksManager.BlockTypeToIndex[pair.Item1]=pair.Item2;}
    using var target=new RenderTarget2D(480,640,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    using var shader=new ModelShader(Read("Shaders/Model.vsh"),Read("Shaders/Model.psh"),false,1,48);
    ModelWidget.m_shaderSkinnedOpaque=new ModelShader(Read("Shaders/Model.vsh"),Read("Shaders/Model.psh"),false,7,SubsystemModelsRenderer.MaxJointsCount);
    ModelWidget.m_shaderSkinnedAlphaTested=new ModelShader(Read("Shaders/Model.vsh"),Read("Shaders/Model.psh"),true,7,SubsystemModelsRenderer.MaxJointsCount);
    var db=XElement.Parse(Read("Database.xml"));
    using(var s=File.OpenRead(Path.Combine(nmm,"Assets/nekomekomodel.xdb")))ModsManager.CombineDataBase(db,s,"appearance-fixture");
    new AppearanceModLoader().OnXdbLoad(db);
    Check("native serialization initialization",Engine.Serialization.HumanReadableConverter.ConvertFromString<string>("Name")=="Name");
    DatabaseManager.LoadDataBaseFromXml(db);
    var player=DatabaseManager.FindEntityValuesDictionary("Player",true);
    Check("database replaces NMM component and preserves load order",player.GetValue<ValuesDictionary>("NekoMekoModel").GetValue<string>("Class")==typeof(ComponentCsPlayerAppearance).FullName && player.GetValue<ValuesDictionary>("NekoMekoModel").GetValue<int>("LoadOrder")==int.MaxValue);
    Check("database replaces direct-call HUD",player.GetValue<ValuesDictionary>("NekoHUD").GetValue<string>("Class")==typeof(ComponentCsAppearanceHud).FullName);
    var models=new Dictionary<string,Model>();
    foreach(string name in new[]{"ct","t"}){using var s=File.OpenRead(Path.Combine(root,"src/ScCsgoTactical/Assets/Models/ScCsgoTactical/"+name+".glb"));var m=Model.Load(GltfLoader.Load(s),true);models[name]=m;caches["Models/ScCsgoTactical/"+name]=[m];Check(name+" mobile joint palette",m.HasSkin&&m.Skin.JointCount<=48);}
    using(var s=File.OpenRead(Path.Combine(nmm,"Assets/Models/BoneSet_ScMale/FirstPersonArms2.dae")))caches["Models/BoneSet_ScMale/FirstPersonArms2"]=[Model.Load(Collada.Load(s),true)];
    using(var s=File.OpenRead(Path.Combine(nmm,"Assets/Models/BoneSet_ScMale/HumanMale.dae")))caches["Fixture/Default"]=[Model.Load(Collada.Load(s),true)];
    modelDict["fixture.default"]=new NekoResModel{Key="fixture.default",BoneSet="ScMale",ResPath=new(){{"ModelName","Fixture/Default"}}};
    var project=new Project();var data=new SubsystemNekoPlayerData();
    foreach(var sub in new Subsystem[]{new SubsystemSky(),new SubsystemTime(),new SubsystemGameInfo(),new SubsystemTerrain(),new SubsystemModelsRenderer(),new SubsystemNoise(),new SubsystemAudio(),data}){sub.m_project=project;project.m_subsystems.Add(sub);}
    Neorxna.NeorxnaResources.SubsystemTime=project.FindSubsystem<SubsystemTime>();
    Neorxna.NeorxnaResources.SubsystemGameInfo=project.FindSubsystem<SubsystemGameInfo>();
    var body=new ComponentBody{Position=new Vector3(105,70,-213),BoxSize=new Vector3(.65f,1.8f,.65f)};
    var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=new ComponentLocomotion()};
    var human=new ComponentHumanModel();var adapter=new ComponentCsPlayerAppearance();var neo=new ComponentNeoModel();
    Attach(project,body,creature,creature.ComponentHealth,creature.ComponentSpawn,creature.ComponentLocomotion,human,adapter,neo);
    human.Load(new ValuesDictionary{{"ModelName","Fixture/Default"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"WalkAnimationSpeed",1f},{"WalkBobHeight",0f},{"WalkLegsAngle",1f}},null);
    neo.Load(new(),null);adapter.Load(new(),null);
    ModsManager.RegisterHook("OnAnimateModel",new Neorxna.NeorxnaModLoader());
    ModsManager.DealWithTempModHooks();
    var controllerBefore=human.AnimationController;
    foreach(string key in new[]{"zh667.cs.ct","zh667.cs.t","fixture.default","zh667.cs.ct"}){
        Check("selection "+key,adapter.SetResModel(key));
        Check("selected FPP role "+key,((IScFirstPersonAppearance)adapter).FirstPersonRole==(key=="zh667.cs.ct"?"ct":key=="zh667.cs.t"?"t":null));
        if(key=="fixture.default"){
            ((INeoModel)adapter).Animate();
            Check("restore vanilla bone references",human.m_bodyBone!=null&&human.m_hand1Bone!=null&&human.m_hand2Bone!=null);
            Check("restore native controller identity",ReferenceEquals(controllerBefore,human.AnimationController));continue;
        }
        Check("skin "+key,adapter.SetResSkin(key+".default"));
        ((INeoModel)adapter).Animate();
        human.Animate();
        human.ProcessBoneHierarchy(human.Model.RootBone,Matrix.Identity,human.AbsoluteBoneTransformsForCamera);
        var rootWorld=human.AbsoluteBoneTransformsForCamera[human.Model.RootBone.Index].Translation;
        human.Animate();
        human.ProcessBoneHierarchy(human.Model.RootBone,Matrix.Identity,human.AbsoluteBoneTransformsForCamera);
        Check("native hook and repeated cameras "+key,Vector3.Distance(rootWorld,body.Position)<.01f&&Vector3.Distance(rootWorld,human.AbsoluteBoneTransformsForCamera[human.Model.RootBone.Index].Translation)<.001f);
        Check("CS local transforms are populated",human.m_boneTransforms.Count(m=>m.HasValue)>20);
        Check("no incompatible human limb override",human.m_hand1Bone==null&&human.m_hand2Bone==null);
        Check("embedded textures and empty-hand fallback",human.TextureOverride==null&&neo.FirstPersonModel==null);
        var saved=new ValuesDictionary();adapter.Save(saved,null);
        Check("NMM save keys",saved.GetValue<string>("ModelKey")==key&&saved.GetValue<string>("SkinKey")==key+".default");
        for(int round=0;round<2;round++){var xml=new XElement("Values");saved.Save(xml);var reloaded=new ValuesDictionary();reloaded.ApplyOverrides(XElement.Parse(xml.ToString()));adapter.SetResModel("fixture.default");adapter.LoadResModelAndSkin(reloaded);Check("reload "+key+" "+round,adapter.ModelKey==key&&adapter.SkinKey==key+".default");}
    }
    data.SetData(0,"zh667.cs.ct","zh667.cs.ct.default");data.SetData(1,"zh667.cs.t","zh667.cs.t.default");
    data.GetData(0,out var key0,out var skin0);data.GetData(1,out var key1,out var skin1);
    Check("respawn cache is per player",key0=="zh667.cs.ct"&&key1=="zh667.cs.t"&&skin0!=skin1);
    foreach(string name in new[]{"ct","t"})foreach(var shot in new[]{PlayerModelWidget.Shot.Body,PlayerModelWidget.Shot.Bust}){
        var widget=new PlayerModelWidget{CameraShot=shot,ExtraData=new ExtraData("NMM-PlayerModelWidget"){Data=new[]{"zh667.cs."+name,"zh667.cs."+name+".default"}}};
        new NekoMeko.Managers.NekoMekoModLoader().OnPlayerModelWidgetMeasureOverride(widget);
        new AppearanceModLoader().OnPlayerModelWidgetMeasureOverride(widget);
        var preview=widget.m_modelWidget;
        widget.Arrange(Vector2.Zero,new Vector2(480,640));
        preview.Measure(new Vector2(480,640));preview.Arrange(Vector2.Zero,new Vector2(480,640));
        Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.ScissorRectangle=new Rectangle(0,0,480,640);Display.Clear(new Color(22,27,34),1,0);
        preview.Draw(new Widget.DrawContext());var pixels=target.GetData(new Rectangle(0,0,480,640));
        Check(name+" NMM "+shot+" preview native GPU",pixels.Pixels.Count(p=>p.R!=22||p.G!=27||p.B!=34)>1000);
        using var png=File.Create(Path.Combine(output,name+"-preview-"+shot+".png"));Image.Save(pixels,png,ImageFileFormat.Png,false);
    }
    int frame=100;
    foreach(var pair in models)foreach(var state in new[]{"idle","walk","armed","shield","crouch","lie"}){
        var model=pair.Value;adapter.SetResModel("zh667.cs."+pair.Key);((INeoModel)adapter).Animate();
        var pose=new CsPlayerPose(model);pose.Sample(frame++,.1f,state=="walk"?2:0,state=="armed",state=="shield",state=="crouch"?1:0,state=="lie"?1:0);
        Array.Copy(pose.Local,human.m_boneTransforms,pose.Local.Length);human.m_boneTransforms[model.RootBone.Index]*=body.Matrix;
        human.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,human.AbsoluteBoneTransformsForCamera);
        var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(human,model,Matrix.Identity,joints);
        Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
        foreach(var buffer in model.ModelData.Buffers){var decl=buffer.VertexDeclaration;int po=decl.VertexElements.Single(e=>e.SemanticName=="POSITION").Offset,jo=decl.VertexElements.Single(e=>e.SemanticName=="BLENDINDICES").Offset,wo=decl.VertexElements.Single(e=>e.SemanticName=="BLENDWEIGHTS").Offset;
            for(int offset=0;offset<buffer.Vertices.Length;offset+=decl.VertexStride){float F(int o)=>BitConverter.ToSingle(buffer.Vertices,offset+o);var v=new Vector3(F(po),F(po+4),F(po+8));Vector3 skinned=Vector3.Zero;for(int k=0;k<4;k++)skinned+=Vector3.Transform(v,joints[(int)F(jo+k*4)])*F(wo+k*4);lo=Vector3.Min(lo,skinned);hi=Vector3.Max(hi,skinned);}}
        Console.WriteLine($"{pair.Key}/{state}: {lo-body.Position} .. {hi-body.Position}");
        Check(pair.Key+"/"+state+" finite bounded skin",float.IsFinite(lo.X)&&float.IsFinite(hi.Y)&&(hi-lo).Length()<4&&lo.Y>=body.Position.Y-.35f);
        var view=Matrix.CreateLookAt(body.Position+new Vector3(0,1,3.7f),body.Position+new Vector3(0,.9f,0),Vector3.UnitY);
        Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.Clear(new Color(22,27,34),1,0);
        Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;Display.ScissorRectangle=new Rectangle(0,0,480,640);
        shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=Matrix.CreatePerspectiveFieldOfView(.75f,.75f,.1f,100);
        shader.InstancesCount=1;shader.JointMatrices=joints;shader.MaterialColor=Vector4.One;shader.EmissionColor=Vector4.Zero;
        shader.AmbientLightColor=new Vector3(.8f);shader.DiffuseLightColor1=shader.DiffuseLightColor2=new Vector3(.2f);shader.LightDirection1=Vector3.UnitY;shader.LightDirection2=Vector3.UnitZ;
        shader.FogColor=Vector3.Zero;shader.FogBottomTopDensity=Vector3.Zero;shader.HazeStartDensity=new Vector2(100,0);shader.FogYMultiplier=1;shader.WorldUp=Vector3.UnitY;shader.SamplerState=SamplerState.LinearWrap;
        foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
        var pixels=target.GetData(new Rectangle(0,0,480,640));Check(pair.Key+"/"+state+" GPU pixels",pixels.Pixels.Count(p=>p.R!=22||p.G!=27||p.B!=34)>1000);
        using var file=File.Create(Path.Combine(output,pair.Key+"-"+state+".png"));Image.Save(pixels,file,ImageFileFormat.Png,false);
    }
    GloveChecks.Run(root,output,models,human,body,shader,Check,caches);
    {
        var tactical=new SubsystemScTactical{m_project=project};tactical.Load(new());project.m_subsystems.Add(tactical);
        var actualPlayer=(ComponentPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ComponentPlayer));actualPlayer.PlayerData=(PlayerData)RuntimeHelpers.GetUninitializedObject(typeof(PlayerData));actualPlayer.PlayerData.PlayerIndex=0;
        actualPlayer.m_entity=human.Entity;human.Entity.m_components.Add(actualPlayer);adapter.Load(new(),null);
        foreach(string role in new[]{"ct","t"}){
            adapter.SetResModel("zh667.cs."+role);tactical.SetGlove(0,"");((INeoModel)adapter).Animate();var original=human.MeshDrawOrders.ToArray();
            foreach(string glove in TacticalArms.Gloves.Select(g=>g.Key)){
                tactical.SetGlove(0,glove);((INeoModel)adapter).Animate();
                Check("live adapter hides own original glove "+role+glove,human.MeshDrawOrders.Length==original.Length-1&&human.Model.Meshes.All(m=>m.IsVisible));
                ((INeoModel)adapter).Animate();Check("repeated animate keeps selection "+role+glove,human.MeshDrawOrders.Length==original.Length-1);
                adapter.SetResModel("zh667.cs."+role);((INeoModel)adapter).Animate();Check("same role reselected keeps glove replacement "+role+glove,human.MeshDrawOrders.Length==original.Length-1);
            }
            tactical.SetGlove(0,"");((INeoModel)adapter).Animate();Check("live adapter reset restores original meshes "+role,human.MeshDrawOrders.SequenceEqual(original));
        }
        tactical.SetGlove(0,"sporty_green");adapter.SetResModel("fixture.default");((INeoModel)adapter).Animate();Check("non-CS role unchanged by gloves",human.Model==caches["Fixture/Default"][0]);
        adapter.SetResModel("zh667.cs.ct");((INeoModel)adapter).Animate();Check("role switch reapplies saved glove",human.MeshDrawOrders.Length==human.Model.Meshes.Count-1);
        tactical.SetGlove(0,"");((INeoModel)adapter).Animate();
    }
    GloveUiChecks.Run(root,output,content,Check,caches);
    ActionChecks.Run(root,output,models,human,body,shader,target,Check,caches);
    Console.WriteLine($"PASS {results.Count} checks");
}catch(Exception e){Console.Error.WriteLine(e);results.Add(new{error=e.ToString()});exit=1;}finally{File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed=exit,checks=results},new JsonSerializerOptions{WriteIndented=true}));Display.RenderTarget=null;Window.Close();}};
Window.Run(480,640,WindowMode.Fixed,"CS player appearance diagnostic");return exit;
