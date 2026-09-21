using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using System.Runtime.CompilerServices;

static class GloveChecks {
    public static void Run(string root,string output,Dictionary<string,Model> models,ComponentHumanModel human,ComponentBody body,ModelShader shader,Action<string,bool> check,IDictionary<string,List<object>> caches,bool iconsOnly=false){
        foreach(string folder in new[]{"ScCsgoKnives","ScCsgoTactical"})foreach(string file in Directory.GetFiles(Path.Combine(root,$"src/{folder}/Assets/Textures/ScCsgoKnives"),"*.png")){
            string name=Path.GetFileNameWithoutExtension(file);
            if(!name.StartsWith("tactical_arm_")&&!name.StartsWith("cs2_arm")&&!name.StartsWith("cs2_glove")&&!name.StartsWith("env_"))continue;
            using var stream=File.OpenRead(file);caches["Textures/ScCsgoKnives/"+name]=[Texture2D.Load(stream)];
        }
        string icons=Path.Combine(root,"src/ScCsgoTactical/Assets/Textures/ScCsgoTactical/Gloves");Directory.CreateDirectory(icons);
        using(var target=new RenderTarget2D(512,384,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8)){
            var lighting=new KnifePbrRenderer.Lighting{Dir1=Vector3.Normalize(new Vector3(-1,1,2)),Dir2=Vector3.Normalize(new Vector3(1,0,2)),Intensity=1};
            var view=Matrix.CreateLookAt(new Vector3(0,.085f,.8f),new Vector3(0,.085f,0),Vector3.UnitY);
            var projection=Matrix.CreateOrthographic(.43f,.3225f,.01f,4);
            foreach(string key in new[]{"default_ct","default_t","default_arms"}.Concat(TacticalArms.Gloves.Select(g=>g.Key))){
                var glove=TacticalArms.Gloves.FirstOrDefault(g=>g.Key==key);
                Cs2SkinnedMesh mesh;string[] materials;Texture2D[] textures;
                if(glove!=null){mesh=TacticalArms.Mesh("world_"+glove.Mesh);materials=mesh.Primitives.Select(p=>"tactical_arm_"+key+(p.Material.EndsWith("left")?"_left":"_right")).ToArray();textures=materials.Select(m=>ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+m)).ToArray();}
                else if(key=="default_arms"){mesh=Cs2SkinnedMesh.Arms;materials=mesh.Primitives.Select(p=>p.Material.Contains("glove")?"cs2_glove":"cs2_arm").ToArray();textures=materials.Select(m=>ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+m)).ToArray();}
                else{var layer=TacticalArms.ResolveSet(key.EndsWith("ct")?"ct":"t","")[0];mesh=layer.Mesh;materials=layer.Materials;textures=layer.Textures;}
                var bind=new Cs2Rig.Pose{Bones=mesh.Joints.Select((j,i)=>(j,i)).ToDictionary(p=>p.j,p=>Matrix.Invert(mesh.InverseBind[p.i]))};
                mesh.SetPose(bind,Matrix.Identity);mesh.Skin();var original=mesh.Skinned.ToArray();
                Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,512,384);Display.ScissorRectangle=new Rectangle(0,0,512,384);Display.Clear(Color.Transparent,1,0);
                for(int p=0;p<mesh.Primitives.Length;p++){
                    if(mesh.Primitives[p].Material.Contains("bare_arm")||materials[p]=="cs2_arm")continue;
                    foreach(bool left in new[]{true,false}){
                        string side=left?"_L":"_R";float s=left?1:-1;
                        var orient=new Matrix(0,s,0,0,0,0,-s,0,-1,0,0,0,left?-.095f:.095f,0,0,1);
                        var transform=mesh.InverseBind[Array.IndexOf(mesh.Joints,"hand"+side)]*Matrix.CreateScale(.0254f)*orient;
                        int[] indices=mesh.Primitives[p].Indices.Chunk(3).Where(t=>t.All(v=>mesh.VertexUsesJoint(v,n=>n.EndsWith(side)||n.Contains(side+"_TWIST")))).SelectMany(t=>t).ToArray();
                        for(int v=0;v<original.Length;v++){var vertex=original[v];vertex.Position=Vector3.Transform(vertex.Position,transform);vertex.Normal=Vector3.Normalize(Vector3.TransformNormal(vertex.Normal,transform));mesh.Skinned[v]=vertex;}
                        if(indices.Length>0)check("icon "+key+side,KnifePbrRenderer.TryDrawSkinned(mesh.Skinned,indices,textures[p],materials[p],view,projection,Matrix.Invert(view),in lighting,0));
                    }
                }
                using var file=File.Create(Path.Combine(icons,key+".png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,true);
            }
        }
        if(iconsOnly)return;
        using var render=new RenderTarget2D(768,768,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        int frame=4000;
        foreach(var pair in models){
            var model=pair.Value;human.m_model=model;human.m_boneTransforms=new Matrix?[model.Bones.Count];human.AbsoluteBoneTransformsForCamera=new Matrix[model.Bones.Count];
            foreach(var glove in TacticalArms.Gloves){
                var binding=new TacticalWorldGloves(model,glove);
                CheckWorldLighting(binding,human,body,check);
                check(pair.Key+glove.Key+" removes only default gloves",binding.BodyMeshOrders.Length==model.Meshes.Count-1&&binding.BodyMeshOrders.All(i=>model.Meshes[i].Name.Contains("thirdperson_body")));
                var originalOrders=Enumerable.Range(0,model.Meshes.Count).ToArray();
                foreach(var state in new[]{"idle","walk","reload","knife","crouch","lie"}){
                    var pose=new CsPlayerPose(model);var action=state=="reload"?new ScWeaponActionTimeline():null;
                    action?.Start("ak47",ScWeaponActionKind.Reload,"reload",0,2.8f);
                    pose.Sample(frame++,.1f,state=="walk"?2:0,state=="reload"||state=="knife",false,state=="crouch"?1:0,state=="lie"?1:0,action?.Read(1.3)??default,state=="reload"?"ak47":state=="knife"?"butterfly":null);
                    Array.Copy(pose.Local,human.m_boneTransforms,pose.Local.Length);human.m_boneTransforms[model.RootBone.Index]*=body.Matrix;
                    human.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,human.AbsoluteBoneTransformsForCamera);binding.Sample(human.AbsoluteBoneTransformsForCamera);
                    var first=binding.Mesh.Skinned.Select(v=>v.Position).ToArray();
                    check("finite glove "+pair.Key+glove.Key+state,first.All(pos=>float.IsFinite(pos.X+pos.Y+pos.Z)&&Vector3.Distance(pos,body.Position)<3));
                    foreach(bool left in new[]{true,false}){
                        string side=left?"_L":"_R";var hand=human.AbsoluteBoneTransformsForCamera[model.FindBone("hand"+side).Index].Translation;
                        var vertices=first.Where((v,i)=>binding.Mesh.VertexUsesJoint(i,n=>n.EndsWith(side))).ToArray();
                        float max=vertices.Max(v=>Vector3.Distance(v,hand)),min=vertices.Min(v=>Vector3.Distance(v,hand));
                        check("glove wrist connection "+pair.Key+glove.Key+state+side,min<.07f&&max<.30f);
                    }
                    var view=Matrix.CreateLookAt(body.Position+new Vector3(-.7f,1.3f,-2.6f),body.Position+new Vector3(0,1,0),Vector3.UnitY);
                    var projection=Matrix.CreatePerspectiveFieldOfView(.68f,1,.02f,100);
                    human.ProcessBoneHierarchy(model.RootBone,view,human.AbsoluteBoneTransformsForCamera);binding.Sample(human.AbsoluteBoneTransformsForCamera);
                    check("camera transform idempotent "+pair.Key+glove.Key+state,first.Select((v,i)=>Vector3.Distance(Vector3.Transform(v,view),binding.Mesh.Skinned[i].Position)).Max()<.0002f);
                    Display.RenderTarget=render;Display.Viewport=new Viewport(0,0,768,768);Display.ScissorRectangle=new Rectangle(0,0,768,768);Display.Clear(new Color(22,27,34),1,0);
                    Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;
                    var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(human,model,Matrix.Invert(view),joints);
                    shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=projection;
                    shader.InstancesCount=1;shader.JointMatrices=joints;shader.MaterialColor=Vector4.One;shader.EmissionColor=Vector4.Zero;shader.AmbientLightColor=new Vector3(LightingManager.LightAmbient);shader.DiffuseLightColor1=shader.DiffuseLightColor2=Vector3.One;
                    shader.LightDirection1=-Vector3.TransformNormal(LightingManager.DirectionToLight1,view);shader.LightDirection2=-Vector3.TransformNormal(LightingManager.DirectionToLight2,view);shader.FogColor=Vector3.Zero;shader.FogBottomTopDensity=Vector3.Zero;shader.HazeStartDensity=new Vector2(100,0);shader.FogYMultiplier=1;shader.WorldUp=Vector3.UnitY;shader.SamplerState=SamplerState.LinearWrap;
                    foreach(int index in binding.BodyMeshOrders)foreach(var part in model.Meshes[index].MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
                    var renderer=new PrimitivesRenderer3D();binding.Queue(renderer,Matrix.Invert(view),1,1);renderer.Flush(projection);
                    if(state is "idle" or "reload" or "knife"){using var file=File.Create(Path.Combine(output,$"gloves-{pair.Key}-{glove.Key}-{state}.png"));RenderTarget2D.Save(render,file,ImageFileFormat.Png,false);}
                }
            }
        }
        human.MeshDrawOrders=Enumerable.Range(0,human.Model.Meshes.Count).ToArray();
    }
    static void CheckWorldLighting(TacticalWorldGloves binding,ComponentHumanModel human,ComponentBody body,Action<string,bool> check){
        // Exercise Draw, not only Queue at a fixed full-bright value: the root sits on dark terrain.
        var widget=(GameWidget)RuntimeHelpers.GetUninitializedObject(typeof(GameWidget));
        widget.Target=human.m_componentCreature;
        var camera=new TppCamera(widget);widget.m_activeCamera=camera;
        camera.SetupPerspectiveCamera(body.Position+new Vector3(0,1,3),-Vector3.UnitZ,Vector3.UnitY);
        human.ProcessBoneHierarchy(binding.Model.RootBone,camera.ViewMatrix,human.AbsoluteBoneTransformsForCamera);
        var renderer=human.m_subsystemModelsRenderer;
        var data=new SubsystemModelsRenderer.ModelData{ComponentModel=human,ComponentBody=body,Light=.8f};
        renderer.m_componentModels[human]=data;
        var originalTerrain=human.m_subsystemTerrain.Terrain;
        using var terrain=new Terrain();human.m_subsystemTerrain.Terrain=terrain;
        var root=Vector3.Transform(human.AbsoluteBoneTransformsForCamera[binding.Model.RootBone.Index].Translation,camera.InvertedViewMatrix);
        int x=Terrain.ToCell(root.X),y=Terrain.ToCell(root.Y),z=Terrain.ToCell(root.Z);
        terrain.AllocateChunk(x>>4,z>>4);
        var texture=ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/tactical_arm_"+binding.Key+"_left");
        Color[] Colors()=>renderer.PrimitivesRenderer.TexturedBatch(texture,false,0,DepthStencilState.Default,RasterizerState.CullNoneScissor,BlendState.Opaque,SamplerState.LinearWrap).TriangleVertices.Select(v=>v.Color).ToArray();
        try{
            binding.Sample(human.AbsoluteBoneTransformsForCamera);
            binding.Queue(renderer.PrimitivesRenderer,camera.InvertedViewMatrix,.8f,1);
            var expected=Colors();renderer.PrimitivesRenderer.Clear();
            foreach(int cellLight in new[]{0,15,0,15}){
                terrain.SetCellValueFast(x,y,z,Terrain.MakeBlockValue(0,cellLight,0));
                binding.Draw(human,camera);var actual=Colors();
                check("world glove uses body lighting across dark ground "+binding.Key+cellLight,expected.Length>0&&expected.SequenceEqual(actual));
                renderer.PrimitivesRenderer.Clear();
            }
            data.Light=.1f;binding.Draw(human,camera);var dim=Colors();
            check("world glove still follows real darkness "+binding.Key,dim.Length==expected.Length&&dim.Zip(expected).Any(v=>v.First.R<v.Second.R));
            renderer.PrimitivesRenderer.Clear();
            widget.m_activeCamera=new FppCamera(widget);binding.Draw(human,widget.m_activeCamera);
            check("world glove hidden for first-person target "+binding.Key,Colors().Length==0);
        }finally{renderer.PrimitivesRenderer.Clear();renderer.m_componentModels.Remove(human);human.m_subsystemTerrain.Terrain=originalTerrain;}
    }
}
