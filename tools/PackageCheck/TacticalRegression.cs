using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Engine.Animation;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class TacticalRegression {
    internal record Result(string Name,bool Ok,string Detail);
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static Entity Entity(Project p,params Component[] components){var e=Blank<Entity>();e.m_project=p;e.m_isAddedToProject=true;e.m_components=components.ToList();foreach(var c in components)c.m_entity=e;p.m_entities[e]=true;return e;}
    sealed class Drops:SubsystemPickables {public readonly List<Pickable> Added=[];public override Pickable AddPickable(int value,int count,Vector3 pos,Vector3? velocity,Matrix? stuck,Entity owner){var p=new Pickable{Value=value,Count=count,Position=pos};Added.Add(p);return p;}}
    sealed class TerrainProbe:SubsystemTerrain {public bool Blocked;public override TerrainRaycastResult? Raycast(Vector3 start,Vector3 end,bool interaction,bool air,Func<int,float,bool> action)=>Blocked?new TerrainRaycastResult{Distance=1}:null;}
    sealed class AudioProbe:SubsystemAudio {public int Shots;public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay)=>Shots++;public override void PlayRandomSound(string n,float v,float pitch,Vector3 p,float d,bool delay){}}
    sealed class HealthProbe:ComponentHealth {public override void Injure(Injury injury){injury.Attackment.EnableHitValueParticleSystem=false;base.Injure(injury);}}
    static ValuesDictionary Round(ValuesDictionary v){var xml=new XElement("Values");v.Save(xml);var read=new ValuesDictionary();read.ApplyOverrides(XElement.Parse(xml.ToString()));return read;}
    internal static List<Result> Run(Assembly core,Assembly dlc,string corePath,string dlcPath,string content){
        List<Result> result=[];
        void Test(string n,Action f){try{f();result.Add(new(n,true,""));}catch(Exception e){result.Add(new(n,false,e.ToString()));}}
        void Require(bool v,string message){if(!v)throw new Exception(message);}
        Type T(string n)=>dlc.GetType("Game."+n,true);Type C(string n)=>core.GetType("Game."+n,true);
        object Call(string type,string method,params object[] args)=>T(type).GetMethod(method).Invoke(null,args);
        using var zip=ZipFile.OpenRead(dlcPath);
        using(var vanilla=ZipFile.OpenRead(content))using(var stream=vanilla.Entries.Single(e=>e.FullName.EndsWith("Simple.template.json",StringComparison.OrdinalIgnoreCase)).Open())AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(stream));
        byte[] Bytes(string path){using var s=zip.GetEntry(path)?.Open()??throw new Exception("Missing "+path);using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
        Test("optional-package-identity-and-no-bundled-engine",()=>{
            var meta=JsonNode.Parse(Bytes("modinfo.json"));Require((string)meta["PackageName"]=="zh667.ScCsgoTactical","wrong package identity");
            var native=ModsManager.DeserializeJson(System.Text.Encoding.UTF8.GetString(Bytes("modinfo.json")));
            Require(!native.NonPersistentMod&&native.DependencyRanges.TryGetValue("zh667.ScCsgoKnives",out var dependency)&&dependency.Satisfies(NuGet.Versioning.NuGetVersion.Parse("1.4.9"))&&!dependency.Satisfies(NuGet.Versioning.NuGetVersion.Parse("1.4.8")),"core dependency not enforced by engine");
            Require(zip.Entries.Where(e=>e.FullName.EndsWith(".dll")).Select(e=>e.FullName).SequenceEqual(new[]{"ScCsgoTactical.dll"}),"bundled dependency overwrites engine/core");
        });
        Test("glove-selection-player-isolation-and-xml",()=>{
            var project=new Project();var terrain=new SubsystemTerrain{m_project=project};project.m_subsystems.Add(terrain);
            var sub=(Subsystem)Activator.CreateInstance(T("SubsystemScTactical"));sub.m_project=project;sub.Load(new());
            var set=T("SubsystemScTactical").GetMethod("SetGlove");var get=T("SubsystemScTactical").GetMethod("GloveFor");
            set.Invoke(sub,[0,"sporty_green"]);set.Invoke(sub,[1,"slick_red"]);set.Invoke(sub,[0,"unknown"]);
            for(int i=0;i<2;i++){
                var data=new ValuesDictionary();sub.Save(data);sub=(Subsystem)Activator.CreateInstance(T("SubsystemScTactical"));sub.m_project=project;sub.Load(Round(data));
                Require((string)get.Invoke(sub,[0])=="sporty_green"&&(string)get.Invoke(sub,[1])=="slick_red"&&(string)get.Invoke(sub,[2])=="","player selection leaked or lost");
            }
            set.Invoke(sub,[0,""]);Require((string)get.Invoke(sub,[0])==""&&(string)get.Invoke(sub,[1])=="slick_red","reset affects another player");
        });
        Test("glove-optional-default-is-original",()=>Require(Call("TacticalArms","ResolveSet",null,"")==null,"default replaced without selected role or gloves"));
        Test("glove-gallery-assets-and-function-entry",()=>{
            Call("TacticalArms","Register");
            var actions=((System.Collections.IEnumerable)C("ScWorkbenchExtension").GetProperty("Actions").GetValue(null)).Cast<object>();
            var item=actions.Single(a=>(string)a.GetType().GetProperty("Key").GetValue(a)=="tactical-gloves");
            Require((string)item.GetType().GetProperty("Category").GetValue(item)=="功能","appearance is still a separate category");
            foreach(string key in new[]{"default_ct","default_t","default_arms","sporty_green","sporty_purple","specialist_kimono_diamonds_red","sporty_blue_pink","slick_red"}){
                using var png=new MemoryStream(Bytes("Assets/Textures/ScCsgoTactical/Gloves/"+key+".png"));var image=Engine.Media.Image.Load(png);
                Require(image.Width==512&&image.Height==384,"wrong preview dimensions");
                Require(image.Pixels.Any(p=>p.A==0)&&image.Pixels.Any(p=>p.A>0),"preview missing transparent background or glove");
            }
        });
        Test("glove-original-cs2-assets-and-pbr-maps",()=>{
            foreach(string key in new[]{"sporty_green","sporty_purple","specialist_kimono_diamonds_red","sporty_blue_pink","slick_red"})
                foreach(string side in new[]{"left","right"})foreach(string suffix in new[]{"","_normal","_orm"})
                    Require(Bytes($"Assets/Textures/ScCsgoKnives/tactical_arm_{key}_{side}{suffix}.png").Length>100,"missing glove map");
            foreach(string name in new[]{"ct_default","t_default","ct_sleeves","t_sleeves","glove_sporty","glove_specialist","glove_slick"}){
                var mesh=Call("TacticalArms","Mesh",name);var pose=C("Cs2Rig").GetMethod("Sample").Invoke(null,["ak47","reload",1.2f]);
                Require((float)mesh.GetType().GetMethod("UnresolvedWeight").Invoke(mesh,[pose])<.001f,"weighted sleeve/glove bone missing: "+name);
            }
        });
        foreach(string name in new[]{"ct","t","hostage","shield"})Test("native-gltf/"+name,()=>{
            using var stream=new MemoryStream(Bytes("Assets/Models/ScCsgoTactical/"+name+".glb"));var data=GltfLoader.Load(stream);
            Require(data.Bones.Count>0&&data.Meshes.Count>0,"empty model");
            if(name=="shield")return;
            Require(data.Skin is not null&&data.Skin.JointCount<=48,"skin exceeds mobile bone palette");
            using var model=new Model{ModelData=data,Skin=data.Skin,Animations=data.Animations};
            foreach(var b in data.Bones)model.m_bones.Add(new ModelBone{Model=model,Index=model.m_bones.Count,Name=b.Name,Transform=b.Transform});
            for(int i=0;i<data.Bones.Count;i++){int parent=data.Bones[i].ParentBoneIndex;if(parent>=0){model.m_bones[i].ParentBone=model.m_bones[parent];model.m_bones[parent].m_childBones.Add(model.m_bones[i]);}else model.m_rootBone=model.m_bones[i];}
            Require(data.Animations.Count==(name=="hostage"?7:170),"missing selected animation");
            model.Skin.ResolveJoints(model.m_bones);
            foreach(var mesh in data.Meshes)model.m_meshes.Add(new ModelMesh{Name=mesh.Name,IsVisible=mesh.IsVisible,ParentBone=model.m_bones[mesh.ParentBoneIndex]});
            var project=new Project();
            foreach(var s in new Subsystem[]{new SubsystemSky(),new SubsystemTime(),new SubsystemGameInfo()}){s.m_project=project;project.m_subsystems.Add(s);}
            var body=new ComponentBody{Position=new Vector3(105,70,-213),BoxSize=new Vector3(.65f,1.8f,.65f)};
            var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=new ComponentLocomotion()};
            var component=(ComponentCreatureModel)Activator.CreateInstance(T("ComponentTacticalModel"));
            Entity(project,component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,creature.ComponentLocomotion);
            var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            string route="Fixture/Tactical/"+name,configRoute="Fixture/TacticalConfig";
            caches[route]=[model];caches[configRoute+".json"]=[System.Text.Encoding.UTF8.GetString(Bytes("Assets/Animations/ScTactical.json"))];
            try{
                component.Load(new ValuesDictionary{{"ModelName",route},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath",configRoute}},null);
                // Check the steady animated pose after the initial 0.18 s blend from bind pose.
                component.AnimationController.Update(.35f);component.Animate();component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                Require(component.Animated&&component.Opacity==1&&component.MeshDrawOrders.Length==data.Meshes.Count&&model.Meshes.All(m=>m.IsVisible),"body disabled/invisible");
                var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(component,model,Matrix.Identity,joints);
                Vector3 min=new(float.MaxValue),max=new(float.MinValue);
                foreach(var buffer in data.Buffers){
                    var decl=buffer.VertexDeclaration;int p=decl.VertexElements.Single(e=>e.SemanticName=="POSITION").Offset,j=decl.VertexElements.Single(e=>e.SemanticName=="BLENDINDICES").Offset,w=decl.VertexElements.Single(e=>e.SemanticName=="BLENDWEIGHTS").Offset;
                    for(int offset=0;offset<buffer.Vertices.Length;offset+=decl.VertexStride){
                        float F(int o)=>BitConverter.ToSingle(buffer.Vertices,offset+o);var v=new Vector3(F(p),F(p+4),F(p+8));Vector3 skinned=Vector3.Zero;
                        for(int k=0;k<4;k++)skinned+=Vector3.Transform(v,joints[(int)F(j+4*k)])*F(w+4*k);
                        min=Vector3.Min(min,skinned);max=Vector3.Max(max,skinned);
                    }
                }
                Require((min+max)*.5f is Vector3 center&&Vector3.Distance(center,body.Position+Vector3.UnitY)<.8f&&min.Y-body.Position.Y>-.15f&&max.Y-body.Position.Y>1.5f&&max.Y-min.Y<2.5f,$"native skinned body misplaced: {min} .. {max}, body {body.Position}");
                if(name is "ct" or "t")foreach(var glove in (Array)T("TacticalArms").GetField("Gloves").GetValue(null)){
                    string key=(string)glove.GetType().GetProperty("Key").GetValue(glove);
                    string[] textureKeys=new[]{"left","right"}.Select(side=>"Textures/ScCsgoKnives/tactical_arm_"+key+"_"+side).ToArray();
                    try{
                        foreach(string tk in textureKeys)caches[tk]=[Blank<Texture2D>()];
                        var binding=Activator.CreateInstance(T("TacticalWorldGloves"),model,glove);var type=binding.GetType();
                        Require(((int[])type.GetProperty("BodyMeshOrders").GetValue(binding)).Length==model.Meshes.Count-1&&model.Meshes.All(m=>m.IsVisible),"shared model changed");
                        type.GetMethod("Sample").Invoke(binding,[component.AbsoluteBoneTransformsForCamera]);
                        var mesh=type.GetProperty("Mesh").GetValue(binding);var vertices=(Array)mesh.GetType().GetProperty("Skinned").GetValue(mesh);
                        foreach(var v in vertices){var p=(Vector3)v.GetType().GetField("Position").GetValue(v);Require(float.IsFinite(p.X+p.Y+p.Z)&&Vector3.Distance(p,body.Position)<3,"packaged world glove escaped the player");}
                    }finally{foreach(string tk in textureKeys)caches.Remove(tk);}
                }
            }finally{caches.Remove(route);caches.Remove(configRoute+".json");}
            foreach(var animation in data.Animations){Require(!animation.Channels.Any(c=>c.TargetBoneName=="root_motion"&&c.Property==ModelAnimation.AnimationProperty.Translation),"root travel will drift");
                var player=new AnimationPlayer();player.SetAnimation(model,animation);player.Play(true);
                for(int i=0;i<=20;i++){var pose=new Matrix?[data.Bones.Count];player.SampleAtTime(animation.Duration*i/20,pose);Require(pose.Where(p=>p.HasValue).All(p=>float.IsFinite(p.Value.M11+p.Value.M22+p.Value.M33+p.Value.M41+p.Value.M42+p.Value.M43)),"invalid animation transform");
                    var absolute=new Matrix[data.Bones.Count];Matrix Compose(int index){var b=data.Bones[index];return absolute[index]=(pose[index]??b.Transform)*(b.ParentBoneIndex<0?Matrix.Identity:Compose(b.ParentBoneIndex));}
                    for(int bi=0;bi<absolute.Length;bi++)Compose(bi);
                    var actor=data.Bones.Select((b,bi)=>(b,bi)).Where(x=>!x.b.Name.StartsWith("cswp_")&&x.b.Name!="cs_weapon_mount").Select(x=>absolute[x.bi]).ToArray();
                    float height=actor.Max(m=>m.Translation.Y)-actor.Min(m=>m.Translation.Y);Require(height>1.4f&&height<2.5f,$"{animation.Name}@{animation.Duration*i/20} actor skeleton height {height}");
                }
            }
            var loader=new AnimationConfigLoader();var cfg=loader.LoadFromJsonNode(JsonNode.Parse(Bytes("Assets/Animations/ScTactical.json")));var controller=loader.CreateController(cfg,model);
            foreach(bool armed in new[]{false,true})foreach(bool shield in new[]{false,true})foreach(float speed in new[]{0f,1f,4f}){controller.Parameters.SetBool("Armed",armed);controller.Parameters.SetBool("Shield",shield);controller.Parameters.SetBool("IsDead",false);controller.Parameters.SetFloat("SpeedAbs",speed);controller.Update(.25f);var matrices=new Matrix?[data.Bones.Count];controller.ComputeBoneTransforms(matrices);Require(matrices.Any(p=>p.HasValue),"no native animation output");}
        });
        Test("database-templates-inherit-native-creature",()=>{
            var old=DatabaseManager.m_gameDatabase;var oldV=new Dictionary<string,ValuesDictionary>(DatabaseManager.m_valueDictionaries);
            try{using var vanilla=ZipFile.OpenRead(content);using var s=vanilla.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();var root=XElement.Load(s);using var addition=new MemoryStream(Bytes("Assets/ScTactical.xdb"));ModsManager.CombineDataBase(root,addition,"zh667.ScCsgoTactical");DatabaseManager.LoadDataBaseFromXml(root);
                foreach(string name in new[]{"ScTacticalCT","ScTacticalT","ScTacticalHostage"}){
                    var v=DatabaseManager.FindEntityValuesDictionary(name,true);foreach(string component in new[]{"Creature","Health","Spawn","Body","Locomotion","Pilot","Pathfinding","BehaviorSelector","TacticalInventory","TacticalCompanion","TacticalModel"})Require(v.ContainsKey(component),"missing "+component);
                    var iv=v.GetValue<ValuesDictionary>("TacticalInventory");var inv=(ComponentInventoryBase)Activator.CreateInstance(T("ComponentTacticalInventory"));inv.Load(iv,null);Require(inv.SlotsCount==5,"native inventory template fails");
                    var md=v.GetValue<ValuesDictionary>("TacticalModel");foreach(string key in new[]{"ModelName","PrepareOrder","CastsShadow","BoundingSphereRadius"})Require(md.ContainsKey(key),"missing Model.Load field "+key);
                    Require(md.GetValue("Transparent",1f)>0&&!md.GetValue("DisableDrawing",false)&&!md.GetValue("DisableAnimation",false),"inherited model invisible: "+string.Join(";",md.Select(p=>$"{p.Key}={p.Value}")));
                    Require(!v.ContainsKey("ChaseBehavior"),"automatic neutral attack added");
                }
                var enemy=DatabaseManager.FindEntityValuesDictionary("ScTacticalEnemy",true);
                Require(enemy.ContainsKey("TacticalEnemy")&&!enemy.ContainsKey("TacticalCompanion")&&!enemy.GetValue<ValuesDictionary>("Creature").GetValue<bool>("ConstantSpawn")&&enemy.GetValue<ValuesDictionary>("Spawn").GetValue<bool>("AutoDespawn"),"enemy inherits friendly or permanent lifecycle");
            }finally{DatabaseManager.m_gameDatabase=old;DatabaseManager.m_valueDictionaries.Clear();foreach(var v in oldV)DatabaseManager.m_valueDictionaries[v.Key]=v.Value;}
        });
        Test("shield-front-back-side-height-and-edge",()=>{
            var pose=Matrix.Identity;
            foreach(var pair in new[]{(new Vector3(0,0,-3),Vector3.Zero,true),(new Vector3(0,0,3),Vector3.Zero,false),(new Vector3(3,0,0),Vector3.Zero,false),(new Vector3(.47f,0,-3),new Vector3(.47f,0,0),false),(new Vector3(0,-.73f,-3),new Vector3(0,-.73f,0),false),(new Vector3(.45f,.71f,-3),new Vector3(.45f,.71f,1),true)}){
                object[] a=[pair.Item1,pair.Item2,pose,0f];Require((bool)T("ScShieldProtection").GetMethod("Intersect").Invoke(null,a)==pair.Item3,"incorrect shield intersection "+pair);
            }
        });
        Test("first-person-cs2-arm-resource-and-shield-grip",()=>{
            var pose=C("Cs2Rig").GetMethod("Sample").Invoke(null,["c4","idle",0f]);Require(pose!=null,"missing C4 base pose");var mesh=C("Cs2SkinnedMesh").GetProperty("Arms").GetValue(null);Require(mesh!=null,"missing actual CS2 arms");
            Require((float)mesh.GetType().GetMethod("UnresolvedWeight").Invoke(mesh,[pose])<.001f,"unresolved arm weights");
            var placement=(Matrix)C("Cs2Placement").GetMethod("Placement").Invoke(null,null);
            foreach(var pair in new[]{("hand_L",-.125f),("hand_R",.125f)}){var p=(Vector3)pose.GetType().GetMethod("GetBoneOrigin").Invoke(pose,[pair.Item1]);p=Vector3.Transform(p,placement)+new Vector3(-.10f,-.22f,-.12f);Require(Math.Abs(p.X-pair.Item2)<.025f&&p.Y>-.66f&&p.Y<-.32f&&p.Z>-.77f&&p.Z<-.66f,"hand misses rear grip "+p);}
        });
        var savedIndices=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var oldBlocks=(Block[])BlocksManager.Blocks.Clone();
        var registryField=C("ScGunRegistry").GetField("Current");var oldRegistry=registryField.GetValue(null);
        try{
            foreach(var pair in new[]{(T("ScTacticalShieldBlock"),705),(T("ScTacticalBeaconBlock"),706),(T("ScTacticalDefuserBlock"),707),(C("ScWeaponMaterialBlock"),708),(C("ScGunBlock"),701),(C("ScAmmoBlock"),702),(C("ScGunSkinTemplateBlock"),703),(C("ScGunCounterTemplateBlock"),704)}){var b=(Block)Activator.CreateInstance(pair.Item1);b.BlockIndex=pair.Item2;BlocksManager.Blocks[pair.Item2]=b;BlocksManager.BlockTypeToIndex[pair.Item1]=pair.Item2;}
            Test("static-items-native-mesh-and-draw-all-variants-and-modes",()=>{
                var items=(System.Collections.IDictionary)T("TacticalItemMesh").GetField("Items").GetValue(null);
                foreach(string name in new[]{"radio","repair_item","defuser_item"}){
                    using var stream=new MemoryStream(Bytes("Assets/Models/ScCsgoTactical/"+name+".glb"));var data=GltfLoader.Load(stream);var mesh=new BlockMesh();
                    foreach(var part in data.Meshes.SelectMany(m=>m.MeshParts)){
                        var b=data.Buffers[part.BuffersDataIndex];var vb=Blank<VertexBuffer>();vb.VertexDeclaration=b.VertexDeclaration;vb.VerticesCount=b.Vertices.Length/b.VertexDeclaration.VertexStride;vb.Tag=b.Vertices;
                        var ib=Blank<IndexBuffer>();ib.Tag=b.Indices;ib.IndexFormat=IndexFormat.ThirtyTwoBits;ib.IndicesCount=b.Indices.Length/4;
                        var mp=new ModelMeshPart{VertexBuffer=vb,IndexBuffer=ib,StartIndex=part.StartIndex,IndicesCount=part.IndicesCount};
                        mesh.AppendModelMeshPart(mp,Matrix.Identity,false,false,false,false,Color.White);
                    }
                    var bounds=mesh.CalculateBoundingBox();var span=bounds.Max-bounds.Min;Require(span.X>.01f&&span.Y>.01f&&span.Z>.01f&&mesh.Indices.Count>30,"flat/empty item "+name);
                    if(name=="repair_item")Require(mesh.Vertices.Select(v=>v.TextureCoordinates.X).Distinct().Count()>50,"toolbox repeated UV collapsed to a single atlas edge");
                    using var png=new MemoryStream(Bytes("Assets/Textures/ScCsgoTactical/"+name+".png"));Require(Image.Load(png).Pixels.All(p=>p.A==255),"unexpected transparent item atlas");
                    items[name]=(mesh,Blank<Texture2D>());
                }
                var squad=(Block)Activator.CreateInstance(T("ScTacticalSquadBlock"));squad.BlockIndex=709;BlocksManager.Blocks[709]=squad;BlocksManager.BlockTypeToIndex[squad.GetType()]=709;
                Require(squad.GetCreativeValues().Count()==2,"missing three/five-member beacons");
                foreach(var block in new[]{BlocksManager.Blocks[706],BlocksManager.Blocks[707],squad})foreach(int value in block.GetCreativeValues())foreach(var mode in Enum.GetValues<DrawBlockMode>()){
                    var renderer=new PrimitivesRenderer3D();var matrix=Matrix.Identity;block.DrawBlock(renderer,value,Color.White,1,ref matrix,new DrawBlockEnvironmentData{DrawBlockMode=mode,Light=15});
                    var vertices=renderer.TexturedBatches.SelectMany(b=>b.TriangleVertices).ToArray();
                    Require(vertices.Length>24&&vertices.All(v=>float.IsFinite(v.Position.X+v.Position.Y+v.Position.Z)&&v.TexCoord.X>=0&&v.TexCoord.X<=1&&v.TexCoord.Y>=0&&v.TexCoord.Y<=1),"invalid 3D item draw");
                    Require(block.DefaultCategory=="CS武器","wrong creative category");
                }
            });
            ComponentInventoryBase Inv(){var inv=(ComponentInventoryBase)Activator.CreateInstance(T("ComponentTacticalInventory"));inv.Load(new ValuesDictionary{{"SlotsCount",5},{"Slots",new ValuesDictionary()}},null);return inv;}
            Test("seven-recipes-no-hostage-stable-ids-craft-and-refusal",()=>{
                using var vanilla=ZipFile.OpenRead(content);using var reader=new StreamReader(vanilla.GetEntry("Assets/BlocksData.txt").Open());var lines=reader.ReadToEnd().Split('\n');int column=Array.IndexOf(lines[0].Trim().Split(';'),"CraftingId"),index=740;
                var ids=new HashSet<string>{"ironingot","copperingot","glass","leather","germaniumchunk","canvas","coalchunk","gunpowder"};foreach(var line in lines.Skip(1)){var cells=line.Trim().Split(';');if(cells.Length<=column||!ids.Contains(cells[column]))continue;var block=(Block)Activator.CreateInstance(typeof(Block).Assembly.GetType("Game."+cells[0],true));block.BlockIndex=index;block.CraftingId=cells[column];block.MaxStacking=40;BlocksManager.Blocks[index++]=block;}
                Call("SubsystemScTactical","RegisterRecipes");Call("SubsystemScTactical","RegisterRecipes");var recipes=((System.Collections.IEnumerable)C("ScWorkbenchExtension").GetProperty("All").GetValue(null)).Cast<object>().ToArray();Require(recipes.Length==7&&recipes.All(r=>(int)r.GetType().GetProperty("Value").GetValue(r)!=706),"hostage recipe remains, duplicate or missing recipe");
                Require(C("ScWorkbenchExtension").GetMethod("Find").Invoke(null,[Terrain.MakeBlockValue(705,15,999)]) is not null,"worn shield lost recipe help");
                Require(recipes.All(r=>!(bool)r.GetType().GetProperty("CreativeOnly").GetValue(r)),"survival squad recipe still locked");
                Require(BlocksManager.Blocks[705].GetCreativeValues().Single()==705&&BlocksManager.Blocks[706].GetCreativeValues().Select(Terrain.ExtractData).SequenceEqual(new[]{1,2,3}),"hostage visible or existing data IDs shifted");
                var registry=Activator.CreateInstance(C("ScGunRegistry"));registryField.SetValue(null,registry);C("ScGunRegistry").GetField("RecoveryOwner").SetValue(registry,(Func<IInventory,string>)(_=>"fixture/tactical"));
                foreach(var recipe in recipes){int output=(int)recipe.GetType().GetProperty("Value").GetValue(recipe);var cost=(Dictionary<int,int>)recipe.GetType().GetMethod("Materials").Invoke(recipe,null);Require(cost.Count>0&&cost.Values.All(n=>n>0),"invalid native material resolution");var inventory=new ComponentInventory();for(int i=0;i<16;i++)inventory.m_slots.Add(new());int slot=0;foreach(var item in cost){inventory.m_slots[slot++]=new(){Value=item.Key,Count=1};inventory.m_slots[slot++]=new(){Value=item.Key,Count=item.Value-1};}
                    var craft=C("ScCraftBatch").GetMethod("TryCraft");Require((bool)craft.Invoke(null,[inventory,output,cost,1]),"cannot craft "+recipe);Require(inventory.m_slots.Sum(s=>s.Count)==1&&inventory.m_slots.Any(s=>s.Value==output&&s.Count==1),"incorrect craft spend/result");Require(!(bool)craft.Invoke(null,[inventory,output,cost,1])&&inventory.m_slots.Sum(s=>s.Count)==1,"second craft consumed/duplicated");}
            });
            (Component Npc,ComponentCreature Creature,ComponentInventoryBase Inventory,ComponentPlayer Owner,ComponentPathfinding Path,SubsystemTime Time,Drops Drops) Npc(){
                var project=new Project();var time=new SubsystemTime();var players=new SubsystemPlayers();var drops=new Drops();foreach(var s in new Subsystem[]{time,players,drops,new TerrainProbe{Terrain=new Terrain()},new AudioProbe(),new SubsystemBodies()}){s.m_project=project;project.m_subsystems.Add(s);}
                var owner=Blank<ComponentPlayer>();owner.PlayerData=Blank<PlayerData>();owner.PlayerData.PlayerIndex=3;owner.ComponentBody=new ComponentBody{Position=new Vector3(8,0,0)};owner.ComponentHealth=new ComponentHealth{Health=1};owner.ComponentGui=Blank<ComponentGui>();owner.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();var ownerInventory=new ComponentInventory();owner.ComponentMiner=new ComponentMiner{Inventory=ownerInventory};Entity(project,owner,owner.ComponentBody,owner.ComponentHealth,owner.ComponentGui,owner.ComponentMiner,ownerInventory);players.m_componentPlayers.Add(owner);
                var npc=(Component)Activator.CreateInstance(T("ComponentTacticalCompanion"));var inv=Inv();var body=new ComponentBody{BoxSize=new Vector3(.65f,1.8f,.65f)};var health=new ComponentHealth{Health=1};var creature=new ComponentCreature{DisplayName="CT · SAS",ComponentBody=body,ComponentHealth=health,ComponentLocomotion=new ComponentLocomotion()};
                var pilot=new ComponentPilot{m_componentCreature=creature};var path=new ComponentPathfinding{m_componentPilot=pilot};var selector=new ComponentBehaviorSelector();Entity(project,npc,inv,body,health,creature,pilot,path,selector);npc.Load(new ValuesDictionary{{"OwnerIndex",3}},null);selector.Load(new(),null);selector.Update(.1f);return(npc,creature,inv,owner,path,time,drops);
            }
            Test("npc-follow-guard-cover-shield-speed-owner-save",()=>{
                var f=Npc();((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination==f.Owner.ComponentBody.Position&&f.Path.Speed==.7f,"follow navigation failed");
                var command=T("ComponentTacticalCompanion").GetMethod("Command");command.Invoke(f.Npc,[Enum.ToObject(T("TacticalOrder"),1)]);f.Time.m_gameTime=1;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination is null,"guard walks away");
                f.Inventory.AddSlotItems(0,705,1);command.Invoke(f.Npc,[Enum.ToObject(T("TacticalOrder"),2)]);f.Time.m_gameTime=2;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination==f.Owner.ComponentBody.Position+f.Owner.ComponentBody.Matrix.Forward*2.5f&&f.Path.Speed==.35f,"shield cover speed/position wrong");
                T("ComponentTacticalCompanion").GetField("CeaseFire").SetValue(f.Npc,true);
                for(int round=0;round<2;round++){var v=new ValuesDictionary();f.Npc.Save(v,null);f.Npc.Load(Round(v),null);Require((int)T("ComponentTacticalCompanion").GetField("OwnerIndex").GetValue(f.Npc)==3&&(bool)T("ComponentTacticalCompanion").GetField("CeaseFire").GetValue(f.Npc),"lost owner/order");}
                var other=Blank<ComponentPlayer>();other.PlayerData=Blank<PlayerData>();other.PlayerData.PlayerIndex=7;Require(!(bool)T("ComponentTacticalCompanion").GetMethod("OwnedBy").Invoke(f.Npc,[other]),"foreign owner granted");
                f.Owner.ComponentHealth.Health=0;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination is null,"owner dead still follows");
            });
            Test("npc-blocked-path-stops-and-retries-without-roaming",()=>{
                var f=Npc();((IUpdateable)f.Npc).Update(.1f);f.Path.IsStuck=true;f.Path.m_componentPilot.m_turnOrder=Vector2.One;
                f.Time.m_gameTime=1;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination is null&&f.Path.m_componentPilot.m_turnOrder==Vector2.Zero,"blocked navigator continues turning");
                f.Time.m_gameTime=2;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination is null,"blocked path retries every frame");
                f.Time.m_gameTime=3;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination==f.Owner.ComponentBody.Position&&!f.Path.UseRandomMovements,"blocked companion never resumes");
            });
            Test("npc-death-drops-equipped-gun-and-ammo-once",()=>{
                var f=Npc();f.Inventory.AddSlotItems(0,701,1);f.Inventory.AddSlotItems(1,702,7);f.Creature.ComponentHealth.Health=0;
                T("ComponentTacticalCompanion").GetMethod("Died").Invoke(f.Npc,null);T("ComponentTacticalCompanion").GetMethod("Died").Invoke(f.Npc,null);((IUpdateable)f.Npc).Update(.1f);
                Require(f.Drops.Added.Count==2&&f.Drops.Added.Sum(p=>p.Count)==8&&Enumerable.Range(0,5).All(i=>f.Inventory.GetSlotCount(i)==0),"lost/duplicated death equipment");
            });
            Test("npc-native-body-ray-attack-health-wall-friendly-and-ceasefire",()=>{
                registryField.SetValue(null,Activator.CreateInstance(C("ScGunRegistry")));var f=Npc();var p=f.Npc.Project;var bodies=p.FindSubsystem<SubsystemBodies>(true);var terrain=(TerrainProbe)p.FindSubsystem<SubsystemTerrain>(true);var audio=(AudioProbe)p.FindSubsystem<SubsystemAudio>(true);f.Creature.m_killVerbs=["shot"];
                var target=new ComponentBody{Position=new Vector3(0,0,-8),BoxSize=new Vector3(.7f,1.8f,.7f),Mass=75};var health=new HealthProbe{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;Entity(p,target,health,creature);bodies.AddBody(target);
                int gun=Terrain.MakeBlockValue(701,0,(int)C("GunSpec").GetMethod("MakeData").Invoke(null,[0,20,false]));f.Inventory.AddSlotItems(0,gun,1);
                void Update(double now){f.Time.m_gameTime=now;((IUpdateable)f.Npc).Update(.1f);}
                Update(0);Require(audio.Shots==0&&health.Health==1,"unprovoked shooting");
                T("ComponentTacticalCompanion").GetMethod("Alert").Invoke(f.Npc,[target]);terrain.Blocked=true;Update(1);Require(audio.Shots==0&&f.Inventory.GetSlotValue(0)==gun,"shot through terrain or consumed blocked ammo");
                terrain.Blocked=false;var ally=new ComponentBody{Position=new Vector3(0,0,-3),BoxSize=new Vector3(.7f,1.8f,.7f)};Entity(p,ally);bodies.AddBody(ally);Update(2);Require(audio.Shots==0,"shot through intervening body");bodies.RemoveBody(ally);
                T("ComponentTacticalCompanion").GetField("CeaseFire").SetValue(f.Npc,true);Update(3);Require(audio.Shots==0,"ceasefire ignored");T("ComponentTacticalCompanion").GetField("CeaseFire").SetValue(f.Npc,false);Update(4);
                Require(audio.Shots==1&&health.Health<1&&health.Health>0,$"native gun damage did not reach health: shots={audio.Shots}, health={health.Health}, status={T("ComponentTacticalCompanion").GetField("Status").GetValue(f.Npc)}, active={((ComponentBehavior)f.Npc).IsActive}, threat={T("ComponentTacticalCompanion").GetField("threat",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.Npc)}, ray={bodies.Raycast(new Vector3(0,1.45f,0),target.BoundingBox.Center(),0,(b,d)=>true)?.ComponentBody==target}");int rounds=(int)C("GunSpec").GetMethod("GetRounds").Invoke(null,[Terrain.ExtractData(f.Inventory.GetSlotValue(0))]);Require(rounds==19,"not exactly one round spent");Update(4.01);Require(audio.Shots==1,"cadence gate bypassed");
            });
            Test("npc-proactive-hostiles-owner-assist-and-unarmed-attack",()=>{
                registryField.SetValue(null,Activator.CreateInstance(C("ScGunRegistry")));var f=Npc();var p=f.Npc.Project;var bodies=p.FindSubsystem<SubsystemBodies>(true);var terrain=(TerrainProbe)p.FindSubsystem<SubsystemTerrain>(true);var audio=(AudioProbe)p.FindSubsystem<SubsystemAudio>(true);f.Creature.m_killVerbs=["shot"];f.Creature.ComponentBody.Mass=75;f.Owner.Category=CreatureCategory.LandOther;
                f.Owner.m_killVerbs=["hit"];f.Owner.ComponentBody.Mass=75;
                var tactical=(Subsystem)Activator.CreateInstance(T("SubsystemScTactical"));tactical.m_project=p;p.m_subsystems.Add(tactical);tactical.OnEntityAdded(f.Npc.Entity);
                ComponentCreature Target(Vector3 pos,CreatureCategory category){var b=new ComponentBody{Position=pos,BoxSize=new Vector3(.7f,1.8f,.7f),Mass=75};var h=new HealthProbe{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var c=new ComponentCreature{ComponentBody=b,ComponentHealth=h,Category=category};h.m_componentCreature=c;Entity(p,b,h,c);bodies.AddBody(b);return c;}
                var neutral=Target(new Vector3(3,0,-5),CreatureCategory.LandOther);var predator=Target(new Vector3(0,0,-8),CreatureCategory.LandPredator);
                var threat=T("ComponentTacticalCompanion").GetField("threat",BindingFlags.Instance|BindingFlags.NonPublic);
                void Update(double now){f.Time.m_gameTime=now;((IUpdateable)f.Npc).Update(.1f);}
                terrain.Blocked=true;Update(0);Require(threat.GetValue(f.Npc)==null,"acquired hostile through wall");terrain.Blocked=false;Update(1);
                Require(ReferenceEquals(threat.GetValue(f.Npc),predator.ComponentBody)&&f.Path.Destination==predator.ComponentBody.Position,"unarmed companion did not approach nearby hostile");
                var rotation=f.Creature.ComponentBody.Rotation;Update(2);Require(f.Creature.ComponentBody.Rotation==rotation&&!f.Path.UseRandomMovements&&f.Path.IgnoreHeightDifference,"combat overrides native walk rotation/random roaming remains");
                var loader=(ModLoader)Activator.CreateInstance(T("TacticalModLoader"));loader.ProcessAttackment(new MeleeAttackment(neutral.ComponentBody,f.Owner.Entity,neutral.ComponentBody.Position,Vector3.UnitZ,2));
                Require(ReferenceEquals(threat.GetValue(f.Npc),neutral.ComponentBody),"owner attack did not prioritize neutral victim");
                T("ComponentTacticalCompanion").GetMethod("Alert").Invoke(f.Npc,[f.Owner.ComponentBody]);Require(ReferenceEquals(threat.GetValue(f.Npc),neutral.ComponentBody),"owner/allies accepted as target");
                neutral.ComponentBody.Position=new Vector3(0,0,-1.4f);bodies.UpdateBody(neutral.ComponentBody);Update(3);
                Require(neutral.ComponentHealth.Health<1&&f.Path.Destination is null&&f.Inventory.GetSlotCount(0)==0,"unarmed summon cannot attack or generates free weapon");float hp=neutral.ComponentHealth.Health;Update(3.1);Require(neutral.ComponentHealth.Health==hp,"melee cooldown bypassed");
                T("ComponentTacticalCompanion").GetField("CeaseFire").SetValue(f.Npc,true);Update(4);Require(neutral.ComponentHealth.Health==hp,"ceasefire did not stop melee");
                T("ComponentTacticalCompanion").GetField("CeaseFire").SetValue(f.Npc,false);threat.SetValue(f.Npc,null);predator.ComponentHealth.Health=0;neutral.ComponentBody.Position=new Vector3(3,0,-5);bodies.UpdateBody(neutral.ComponentBody);
                var modCreature=Target(new Vector3(0,0,-8),CreatureCategory.LandOther);var chase=new ComponentChaseBehavior{m_autoChaseMask=CreatureCategory.LandOther};chase.m_entity=modCreature.Entity;modCreature.Entity.m_components.Add(chase);
                int gun=Terrain.MakeBlockValue(701,0,(int)C("GunSpec").GetMethod("MakeData").Invoke(null,[0,20,false]));f.Inventory.AddSlotItems(0,gun,1);bodies.RemoveBody(predator.ComponentBody);Update(5);
                Require(ReferenceEquals(threat.GetValue(f.Npc),modCreature.ComponentBody)&&audio.Shots==1&&modCreature.ComponentHealth.Health<1,"native mod chase hostile not acquired and fired on");
            });
            Test("npc-stationary-aim-converges-without-forced-body-rotation",()=>{
                var f=Npc();var body=f.Creature.ComponentBody;f.Inventory.AddSlotItems(0,705,1);f.Owner.ComponentBody.Position=body.Position;f.Owner.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,1.8f);
                var command=T("ComponentTacticalCompanion").GetMethod("Command");command.Invoke(f.Npc,[Enum.ToObject(T("TacticalOrder"),1)]);
                f.Path.m_componentPilot.m_turnOrder=new Vector2(1,0);f.Path.m_componentPilot.m_walkOrder=Vector2.One;
                for(int i=0;i<100;i++){
                    f.Time.m_gameTime=i*.05;var before=body.Rotation;((IUpdateable)f.Npc).Update(.05f);Require(body.Rotation==before,"AI directly teleports rotation");
                    // Apply the engine locomotion yaw equation to the order produced by the packaged AI.
                    float yaw=MathF.Atan2(2*body.Rotation.Y*body.Rotation.W,1-2*body.Rotation.Y*body.Rotation.Y);body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw-7*f.Creature.ComponentLocomotion.TurnOrder.X*.05f);f.Creature.ComponentLocomotion.TurnOrder=Vector2.Zero;
                }
                Require(Math.Abs(Vector2.Angle(body.Matrix.Forward.XZ,f.Owner.ComponentBody.Matrix.Forward.XZ))<.01f&&f.Path.m_componentPilot.m_turnOrder==Vector2.Zero&&f.Path.m_componentPilot.m_walkOrder is null,"aim circles or cached pilot orders still fight stationary facing");
            });
            Test("npc-full-registry-reload-refuses-without-ammo-loss",()=>{
                var registry=Activator.CreateInstance(C("ScGunRegistry"));registryField.SetValue(null,registry);C("ScGunRegistry").GetProperty("Next").SetValue(registry,1023);var f=Npc();int gun=Terrain.MakeBlockValue(701,0,(int)C("GunSpec").GetMethod("MakeData").Invoke(null,[0,0,false]));f.Inventory.AddSlotItems(0,gun,1);f.Inventory.AddSlotItems(1,702,2);
                ((IUpdateable)f.Npc).Update(.1f);f.Time.m_gameTime=4;((IUpdateable)f.Npc).Update(.1f);Require(f.Inventory.GetSlotValue(0)==gun&&f.Inventory.GetSlotCount(1)==2&&(int)C("ScGunRegistry").GetProperty("Next").GetValue(registry)==1023,"failed reload consumed or allocated");
            });
            Test("npc-reload-real-ammo-cancel-on-equip-change",()=>{
                registryField.SetValue(null,Activator.CreateInstance(C("ScGunRegistry")));var f=Npc();
                int empty=Terrain.MakeBlockValue(701,0,(int)C("GunSpec").GetMethod("MakeData").Invoke(null,[0,0,false]));f.Inventory.AddSlotItems(0,empty,1);f.Inventory.AddSlotItems(1,702,2);
                ((IUpdateable)f.Npc).Update(.1f);Require(f.Inventory.GetSlotCount(1)==2,"charged before reload insert");
                object Action()=>T("ComponentTacticalCompanion").GetProperty("VisualAction").GetValue(f.Npc);
                string Kind()=>Action().GetType().GetProperty("Kind").GetValue(Action()).ToString();
                Require(Kind()=="Reload","actual transaction did not start third-person reload");
                f.Inventory.RemoveSlotItems(0,1);f.Inventory.AddSlotItems(0,705,1);f.Time.m_gameTime=4;((IUpdateable)f.Npc).Update(.1f);Require(f.Inventory.GetSlotCount(1)==2&&f.Inventory.GetSlotValue(0)==705,"stale reload modified shield");
                Require(Kind()!="Reload","cancelled transaction left stale third-person reload");
                f.Inventory.RemoveSlotItems(0,1);f.Inventory.AddSlotItems(0,empty,1);f.Time.m_gameTime=5;((IUpdateable)f.Npc).Update(.1f);f.Time.m_gameTime=9;((IUpdateable)f.Npc).Update(.1f);
                Require(f.Inventory.GetSlotCount(1)==1&&(int)C("GunSpec").GetMethod("GetRounds").Invoke(null,[Terrain.ExtractData(f.Inventory.GetSlotValue(0))])>0,"reload failed or wrong magazine cost");
                Require(Kind()!="Reload","finished reload remains visible");
                Require((bool)T("ComponentTacticalCompanion").GetMethod("InspectWeapon").Invoke(f.Npc,null)&&Kind()=="Inspect","companion inspect command did not start");
                f.Creature.ComponentHealth.Health=0;Require(Kind()=="Idle","dead companion still inspecting");
            });
            Test("native-panel-all-slots-responsive-and-resume-after-close",()=>{
                var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var oldCaches=caches.ToArray();var atlas=TextureAtlasManager.m_subtextures.ToArray();var font=LabelWidget.m_bitmapFont;
                try{
                    using var vanilla=ZipFile.OpenRead(content);var texture=Blank<Texture2D>();
                    foreach(var e in vanilla.Entries.Where(e=>e.FullName.StartsWith("Assets/")&&e.FullName.Contains('.'))){string key=e.FullName[7..];key=key[..key.LastIndexOf('.')];if(e.FullName.EndsWith(".xml")){using var s=e.Open();var xml=XElement.Load(s);caches[key]=[xml];foreach(var a in xml.DescendantsAndSelf().Attributes().Where(a=>a.Value.StartsWith("{Textures/Atlas/")&&a.Value.EndsWith('}')))TextureAtlasManager.m_subtextures[a.Value[1..^1]]=new Subtexture(texture,Vector2.Zero,Vector2.One);}else if(e.FullName.EndsWith(".png"))caches[key]=[texture];}
                    using(var glyphs=vanilla.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize((Texture2D)null,glyphs);
                    foreach(string n in new[]{"ProgressBar","InteractiveItemOverlay","EditItemOverlay","FoodItemOverlay"})TextureAtlasManager.m_subtextures["Textures/Atlas/"+n]=new Subtexture(texture,Vector2.Zero,Vector2.One);
                    caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];BlocksManager.Blocks[0]=new AirBlock();
                    var f=Npc();f.Owner.ComponentInput=Blank<ComponentInput>();f.Owner.ComponentInput.SplitSourceSlotIndex=-1;var inv=(ComponentInventory)f.Owner.ComponentMiner.Inventory;for(int i=0;i<36;i++)inv.m_slots.Add(new ComponentInventoryBase.Slot());
                    var panel=(CanvasWidget)Activator.CreateInstance(T("TacticalPanel"),f.Owner,f.Npc);panel.WidgetsHierarchyInput=new WidgetInput();f.Owner.ComponentGui.m_modalPanelContainerWidget.Children.Add(panel);
                    foreach(var size in new[]{new Vector2(850,480),new Vector2(640,360),new Vector2(420,720),new Vector2(480,540)}){
                        panel.Measure(size);panel.Arrange(Vector2.Zero,new Vector2(Math.Min(620,size.X),Math.Min(520,size.Y)));panel.Measure(size);panel.Arrange(Vector2.Zero,new Vector2(Math.Min(620,size.X),Math.Min(520,size.Y)));
                        var slots=panel.AllChildren.OfType<InventorySlotWidget>().ToArray();Require(slots.Length==41,"hidden/missing player slots");Require(slots.All(s=>s.ActualSize.X>=48&&s.GlobalBounds.Min.X>=panel.GlobalBounds.Min.X&&s.GlobalBounds.Max.X<=panel.GlobalBounds.Max.X+.1f),"slots overflow narrow screen");
                    }
                    var equipment=panel.AllChildren.OfType<InventorySlotWidget>().First();
                    var center=equipment.GlobalBounds.Center();
                    Require(equipment.HitTestGlobal(center)==equipment,"native slot center intercepted by "+equipment.HitTestGlobal(center)?.GetType().Name);
                    var input=panel.Input;panel.WidgetsHierarchyInput=null;var host=new DragHostWidget();
                    var root=new CanvasWidget{WidgetsHierarchyInput=input};f.Owner.ComponentGui.m_modalPanelContainerWidget.Children.Remove(panel);root.Children.Add(panel);root.Children.Add(host);root.Measure(new Vector2(620,720));root.Arrange(Vector2.Zero,new Vector2(620,720));
                    var slotsNow=panel.AllChildren.OfType<InventorySlotWidget>().ToArray();foreach(var s in slotsNow)s.m_dragHostWidget=host;
                    f.Owner.ComponentInput=Blank<ComponentInput>();f.Owner.ComponentInput.SplitSourceSlotIndex=-1;
                    var source=slotsNow[5];var targetSlot=slotsNow[0];inv.m_slots[0]=new(){Value=Terrain.MakeBlockValue(705,0,317),Count=1};
                    var scroll=panel.AllChildren.OfType<ScrollPanelWidget>().Single();
                    void Transfer(InventorySlotWidget from,InventorySlotWidget to){
                        input.Clear();input.Tap=input.Press=from.GlobalBounds.Center();from.Update();scroll.Update();input.Tap=null;input.Drag=input.Press=from.GlobalBounds.Center();input.DragMode=DragMode.AllItems;from.Update();scroll.Update();
                        Require(host.IsDragInProgress&&input.Drag.HasValue,"slot cannot start native drag or scroll consumed it");
                        input.Drag=input.Press=to.GlobalBounds.Center();host.Update();scroll.Update();Require(input.Drag.HasValue,"scroll stole moving item");
                        input.Drag=input.Press=null;host.Update();scroll.Update();Require(!host.IsDragInProgress,"native release failed");
                    }
                    Transfer(source,targetSlot);Require(inv.GetSlotCount(0)==0&&f.Inventory.GetSlotValue(0)==Terrain.MakeBlockValue(705,0,317),"drag-in lost shield/wear");
                    Transfer(targetSlot,source);Require(f.Inventory.GetSlotCount(0)==0&&inv.GetSlotValue(0)==Terrain.MakeBlockValue(705,0,317),"drag-out lost shield/wear");
                    root.Children.Remove(panel);f.Owner.ComponentGui.m_modalPanelContainerWidget.Children.Add(panel);
                    ((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination is null,"NPC moves while equipping");f.Owner.ComponentGui.m_modalPanelContainerWidget.Children.Clear();f.Time.m_gameTime=3;((IUpdateable)f.Npc).Update(.1f);Require(f.Path.Destination.HasValue,"NPC frozen after native inventory close");
                }finally{caches.Clear();foreach(var c in oldCaches)caches[c.Key]=c.Value;TextureAtlasManager.m_subtextures.Clear();foreach(var a in atlas)TextureAtlasManager.m_subtextures[a.Key]=a.Value;LabelWidget.m_bitmapFont=font;}
            });
            Test("native-inventory-capacity-and-two-xml-rounds",()=>{
                var inv=Inv();Require(inv.GetSlotCapacity(0,705)==1&&inv.GetSlotCapacity(1,705)==0&&inv.GetSlotCapacity(1,702)==40&&inv.GetSlotCapacity(0,702)==0,"equipment restrictions");
                inv.AddSlotItems(0,Terrain.MakeBlockValue(705,0,1777),1);inv.AddSlotItems(1,702,23);
                for(int round=0;round<2;round++){var values=new ValuesDictionary();inv.Save(values,null);values.SetValue("SlotsCount",5);var next=Inv();next.m_slots.Clear();next.Load(Round(values),null);inv=next;Require(inv.GetSlotValue(0)==Terrain.MakeBlockValue(705,0,1777)&&inv.GetSlotCount(1)==23,"saved inventory altered");}
            });
            Test("real-attack-filter-protects-body-behind-shield-once",()=>{
                var project=new Project();var bodies=new SubsystemBodies();bodies.m_project=project;project.m_subsystems.Add(bodies);
                var inv=Inv();inv.AddSlotItems(0,705,1);var shield=new ComponentBody{Position=Vector3.Zero,BoxSize=new Vector3(.65f,1.8f,.65f)};Entity(project,shield,new ComponentHealth{Health=1},inv);bodies.AddBody(shield);
                var target=new ComponentBody{Position=new Vector3(0,0,2),BoxSize=new Vector3(.65f,1.8f,.65f)};Entity(project,target,new ComponentHealth{Health=1});bodies.AddBody(target);
                var attacker=new ComponentBody{Position=new Vector3(0,0,-5)};var ae=Entity(project,attacker);
                var hit=new ProjectileAttackment(target,ae,new Vector3(0,1,1.7f),Vector3.UnitZ,100,null);
                Call("ScShieldProtection","Filter",hit);Require(hit.AttackPower==0&&Terrain.ExtractData(inv.GetSlotValue(0))==100,"does not protect ally behind shield");
                Call("ScShieldProtection","Filter",hit);Require(Terrain.ExtractData(inv.GetSlotValue(0))==100,"double wear");
                inv.m_slots[0].Value=Terrain.MakeBlockValue(705,0,2000);hit=new ProjectileAttackment(target,ae,new Vector3(0,1,1.7f),Vector3.UnitZ,100,null);Call("ScShieldProtection","Filter",hit);Require(hit.AttackPower==100,"broken shield blocks");
            });
            Test("shield-retained-break-no-nan-mutation",()=>{var inv=Inv();inv.AddSlotItems(0,Terrain.MakeBlockValue(705,0,1999),1);Require(!(bool)Call("ScShieldProtection","Spend",inv,float.NaN),"NaN wear accepted");Require((bool)Call("ScShieldProtection","Spend",inv,10f)&&inv.GetSlotCount(0)==1&&Terrain.ExtractData(inv.GetSlotValue(0))==2000,"break deletes or wraps");Require(!(bool)Call("ScShieldProtection","Spend",inv,1f),"broken shield spends");});
            Test("companion-real-gun-registry-transaction-and-xml",()=>{
                var registry=Activator.CreateInstance(C("ScGunRegistry"));registryField.SetValue(null,registry);
                var specs=(Array)C("GunSpec").GetField("All").GetValue(null);int variant=Enumerable.Range(0,specs.Length).Single(i=>(string)C("GunSpec").GetField("Name").GetValue(specs.GetValue(i))=="m4a1s");
                var project=new Project();var inv=Inv();var entity=Entity(project,inv);int value=Terrain.MakeBlockValue(701,0,(int)C("GunSpec").GetMethod("MakeData").Invoke(null,[variant,20,false]));inv.AddSlotItems(0,value,1);inv.AddSlotItems(1,702,2);
                string holder=(string)C("ScGunHolders").GetMethod("Key").Invoke(null,[inv,0]);object[] args=[inv,0,holder,Enum.ToObject(C("ScGunResult"),0)];var tx=C("ScGunMutation").GetMethod("Prepare").Invoke(null,args);Require(tx!=null,"NPC transaction refused "+args[3]);
                var method=tx.GetType().GetMethod("Commit");var arg=System.Linq.Expressions.Expression.Parameter(C("ScGunRecord"));
                var expected=new Dictionary<string,object>{{"Rounds",17},{"SilencerOff",true},{"SkinId",984},{"CounterInstalled",true},{"KillCount",610L},{"GrowthKillCredit",25L},{"AppliedGrowthLevel",17},{"GrowthRulesVersion",(int)C("ScGunGrowth").GetField("RulesVersion").GetRawConstantValue()},{"ReserveOverflowRounds",7},{"Durability",113}};
                var change=System.Linq.Expressions.Expression.Lambda(method.GetParameters()[0].ParameterType,System.Linq.Expressions.Expression.Block(expected.Select(p=>(System.Linq.Expressions.Expression)System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(arg,p.Key),System.Linq.Expressions.Expression.Constant(p.Value))).Append(System.Linq.Expressions.Expression.Empty())),arg).Compile();
                var r=method.Invoke(tx,[change,0,0,null]);Require(r.ToString()=="Success","transaction failed "+r);
                int recordValue=inv.GetSlotValue(0);var snapshot=C("GunSpec").GetMethod("TryGetSnapshot");object[] snapArgs=[Terrain.ExtractData(recordValue),null];Require((bool)snapshot.Invoke(null,snapArgs),"not a real gun instance");
                var owner=(string)C("ScGunHolders").GetMethod("RecoveryOwner").Invoke(null,[project,inv]);Require(owner!=null&&ReferenceEquals(C("ScGunHolders").GetMethod("ResolveRecoveryOwner").Invoke(null,[project,owner]),inv),"NPC durable owner missing");
                for(int i=0;i<2;i++){var v=new ValuesDictionary();inv.Save(v,null);v.SetValue("SlotsCount",5);inv.m_slots.Clear();inv.Load(Round(v),null);
                    var rv=(ValuesDictionary)C("ScGunRegistry").GetMethod("Save").Invoke(registry,[10d]);registry=C("ScGunRegistry").GetMethod("Load").Invoke(null,[Round(rv),0d]);registryField.SetValue(null,registry);
                    Require(inv.GetSlotValue(0)==recordValue&&inv.GetSlotCount(1)==2,"inventory loses gun identity");snapArgs=[Terrain.ExtractData(recordValue),null];Require((bool)snapshot.Invoke(null,snapArgs),"registry state not saved");foreach(var field in expected)Require(Equals(snapArgs[1].GetType().GetProperty(field.Key).GetValue(snapArgs[1]),field.Value),"lost saved "+field.Key);}
                var returned=new ComponentInventory();returned.m_slots.Add(new ComponentInventoryBase.Slot());Entity(project,returned);Require(inv.RemoveSlotItems(0,1)==1,"cannot recover gun");returned.AddSlotItems(0,recordValue,1);Require(returned.GetSlotValue(0)==recordValue&&inv.GetSlotCount(0)==0,"recovering equipment copied or changed identity");
            });
            Test("all-35-gun-shot-audio-exists-in-core",()=>{using var coreZip=ZipFile.OpenRead(corePath);foreach(var spec in (Array)C("GunSpec").GetField("All").GetValue(null))foreach(bool silenced in new[]{false,true}){string path=(string)C("SubsystemScGunBlockBehavior").GetMethod("ExtensionShotSound").Invoke(null,[spec,silenced]);Require(coreZip.GetEntry("Assets/"+path+".ogg")!=null,"missing "+path);}});
            result.AddRange(TacticalEnemyRegression.Run(core,dlc,corePath,dlcPath));
            result.AddRange(TacticalDefuseRegression.Run(core,dlc,content));
        }finally{registryField.SetValue(null,oldRegistry);BlocksManager.BlockTypeToIndex.Clear();foreach(var p in savedIndices)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;Array.Copy(oldBlocks,BlocksManager.Blocks,oldBlocks.Length);}
        return result;
    }
}
