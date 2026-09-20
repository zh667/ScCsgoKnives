// Isolated native GPU probe, not a screenshot of a running game or a mod-combination test.
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Engine.Animation;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

if(args.Length!=3)throw new ArgumentException("TacticalRenderCheck <Assets folder> <Content.zip> <output folder>");
string assets=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
using var content=ZipFile.OpenRead(args[1]);
string Read(string suffix){using var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return r.ReadToEnd();}
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(var e in content.Entries.Where(e=>e.FullName.Contains("Shaders/")&&!e.FullName.EndsWith('/'))){using var reader=new StreamReader(e.Open());caches[e.FullName.Replace("Assets/","")]=[reader.ReadToEnd()];}
bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    using var target=new RenderTarget2D(480,640,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    using var shader=new ModelShader(Read("Shaders/Model.vsh"),Read("Shaders/Model.psh"),false,1,48);
    var report=new List<object>();
    foreach(string name in new[]{"ct","t","hostage"}){
        using var stream=File.OpenRead(Path.Combine(assets,"Models/ScCsgoTactical/"+name+".glb"));
        using var model=Model.Load(GltfLoader.Load(stream),true);
        var project=new Project();foreach(var sub in new Subsystem[]{new SubsystemSky(),new SubsystemTime(),new SubsystemGameInfo()}){sub.m_project=project;project.m_subsystems.Add(sub);}
        var body=new ComponentBody{Position=new Vector3(105,70,-213),BoxSize=new Vector3(.65f,1.8f,.65f)};
        var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=new ComponentLocomotion()};
        var component=new ComponentTacticalModel();var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;entity.m_components=[component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,creature.ComponentLocomotion];foreach(var c in entity.m_components)c.m_entity=entity;
        caches["Fixture/Model"]=[model];caches["Fixture/Config.json"]=[File.ReadAllText(Path.Combine(assets,"Animations/ScTactical.json"))];
        component.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Fixture/Config"}},null);
        foreach(float phase in new[]{-1f,0f,.5f,1f}){
            creature.ComponentHealth.Health=phase<0?1:0;component.DeathPhase=Math.Max(phase,0);
            component.AnimationController.Update(.35f);component.Animate();component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
            var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(component,model,Matrix.Identity,joints);
            Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
            foreach(var buffer in model.ModelData.Buffers){var decl=buffer.VertexDeclaration;int po=decl.VertexElements.Single(e=>e.SemanticName=="POSITION").Offset,jo=decl.VertexElements.Single(e=>e.SemanticName=="BLENDINDICES").Offset,wo=decl.VertexElements.Single(e=>e.SemanticName=="BLENDWEIGHTS").Offset;
                for(int offset=0;offset<buffer.Vertices.Length;offset+=decl.VertexStride){float F(int o)=>BitConverter.ToSingle(buffer.Vertices,offset+o);var v=new Vector3(F(po),F(po+4),F(po+8));Vector3 skinned=Vector3.Zero;for(int k=0;k<4;k++)skinned+=Vector3.Transform(v,joints[(int)F(jo+k*4)])*F(wo+k*4);lo=Vector3.Min(lo,skinned);hi=Vector3.Max(hi,skinned);}}
            Console.WriteLine($"{name} phase={phase}: bounds {lo-body.Position} .. {hi-body.Position}");
            var view=Matrix.CreateLookAt(body.Position+new Vector3(0,1,3.7f),body.Position+new Vector3(0,.9f,0),Vector3.UnitY);
            Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.Clear(new Color(22,27,34),1,0);
            Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;
            Display.ScissorRectangle=new Rectangle(0,0,480,640);
            shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=Matrix.CreatePerspectiveFieldOfView(.75f,.75f,.1f,100);
            shader.InstancesCount=1;shader.JointMatrices=joints;shader.MaterialColor=Vector4.One;shader.EmissionColor=Vector4.Zero;
            shader.AmbientLightColor=new Vector3(.8f);shader.DiffuseLightColor1=shader.DiffuseLightColor2=new Vector3(.2f);shader.LightDirection1=Vector3.UnitY;shader.LightDirection2=Vector3.UnitZ;
            shader.FogColor=Vector3.Zero;shader.FogBottomTopDensity=Vector3.Zero;shader.HazeStartDensity=new Vector2(100,0);shader.FogYMultiplier=1;shader.WorldUp=Vector3.UnitY;shader.SamplerState=SamplerState.LinearWrap;
            foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
            var pixels=target.GetData(new Rectangle(0,0,480,640));int count=pixels.Pixels.Count(p=>p.R!=22||p.G!=27||p.B!=34);
            string file=Path.Combine(output,$"{name}-{phase}.png");using(var png=File.Create(file))Image.Save(pixels,png,ImageFileFormat.Png,false);
            report.Add(new{name,phase,pixels=count,bones=model.Bones.Count,meshes=model.Meshes.Count,min=(lo-body.Position).ToString(),max=(hi-body.Position).ToString()});
            Console.WriteLine($"{name} phase={phase}: {count} pixels");if(count<1000)exit=1;
            if(phase<0&&(lo.Y<body.Position.Y-.15f||hi.Y<body.Position.Y+1.5f))throw new Exception(name+" is not standing above the ground");
        }
    }
    foreach(string name in new[]{"radio","repair_item","defuser_item"}){
        using var stream=File.OpenRead(Path.Combine(assets,"Models/ScCsgoTactical/"+name+".glb"));using var model=Model.Load(GltfLoader.Load(stream),true);
        using var png=File.OpenRead(Path.Combine(assets,"Textures/ScCsgoTactical/"+name+".png"));using var texture=Texture2D.Load(Image.Load(png));
        caches["Models/ScCsgoTactical/"+name]=[model];caches["Textures/ScCsgoTactical/"+name]=[texture];TacticalItemMesh.Load(name);
        Display.RenderTarget=target;Display.Clear(new Color(22,27,34),1,0);
        var view=Matrix.CreateLookAt(new Vector3(.55f,.35f,.65f),Vector3.Zero,Vector3.UnitY);var renderer=new PrimitivesRenderer3D();
        TacticalItemMesh.Draw(name,renderer,Color.White,1,ref view,new DrawBlockEnvironmentData{Light=15,DrawBlockMode=DrawBlockMode.UI});renderer.Flush(Matrix.CreatePerspectiveFieldOfView(.75f,.75f,.1f,100));
        using var file=File.Create(Path.Combine(output,name+".png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
        var span=TacticalItemMesh.Items[name].Mesh.CalculateBoundingBox();Console.WriteLine($"{name}: {span.Max-span.Min}");
    }
    File.WriteAllText(Path.Combine(output,"gpu.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
}catch(Exception e){Console.Error.WriteLine(e);exit=1;}finally{Display.RenderTarget=null;Window.Close();}};
Window.Run(480,640,WindowMode.Fixed,"Tactical native renderer diagnostic");return exit;
