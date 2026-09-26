// Actual Mini resources, engine image/audio/OBJ loaders and native PBR renders.
using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

if(args.Length!=4)throw new ArgumentException("MinimalCheck <Mini.scmod> <Lite.scmod> <Content.zip> <output>");
Dispatcher.Initialize();string output=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
var checks=new List<string>();var frames=new List<object>();int failed=0;
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);}
using var package=ZipFile.OpenRead(args[0]);using var baseline=ZipFile.OpenRead(args[1]);using var content=ZipFile.OpenRead(args[2]);
byte[] Bytes(System.IO.Compression.ZipArchive z,string name){using var s=z.GetEntry(name).Open();using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
Check("candidate under 30000000 bytes",new FileInfo(args[0]).Length<30_000_000);
Check("tested core is packaged core",Hash(File.ReadAllBytes(typeof(GunSpec).Assembly.Location))==Hash(Bytes(package,"ScCsgoKnives.dll")));
Check("tested resources are packaged resources",Hash(File.ReadAllBytes(typeof(ScCsgoResources.ResourceMarker).Assembly.Location))==Hash(Bytes(package,"ScCsgoResources.dll")));
Check("independent minimal profile",ScMinimalEdition.Enabled&&package.GetEntry("ScCsgoTactical.dll")==null&&package.GetEntry("ScCsgoBundle.dll")==null);
Check("full registry and sparse available skin catalogue",GunSpec.All.Length==35&&ScGunSkinCatalog.All.Length==44&&ScGunSkinCatalog.Available.Count()==13);
Check("inspection refuses before touching player",!KnifeAnimationController.TriggerInspect(null));
var context=new AssemblyLoadContext("published baseline");
var oldResources=context.LoadFromStream(new MemoryStream(Bytes(baseline,"ScCsgoResources.dll")));
var oldCore=context.LoadFromStream(new MemoryStream(Bytes(baseline,"ScCsgoKnives.dll")));
var oldSpecs=(Array)oldCore.GetType("Game.GunSpec").GetField("All").GetValue(null);
foreach(var type in new[]{typeof(ScGunBlock),typeof(ScKnifeBlock),typeof(ScGunSkinTemplateBlock),typeof(ScGunCounterTemplateBlock),typeof(ScGrenadeBlock),typeof(ScC4Block)}){
    int id=700+BlocksManager.BlockTypeToIndex.Count;var block=(Block)Activator.CreateInstance(type);block.BlockIndex=id;BlocksManager.BlockTypeToIndex[type]=id;BlocksManager.Blocks[id]=block;
    if(oldCore.GetType(type.FullName) is {} oldType)BlocksManager.BlockTypeToIndex[oldType]=id;
}
foreach(var type in new[]{typeof(ScTacticalShieldBlock),typeof(ScTacticalBeaconBlock),typeof(ScTacticalDefuserBlock),typeof(ScTacticalSquadBlock)}){
    var b=(Block)Activator.CreateInstance(type);Check(type.Name+" inert identity",!b.IsPlaceable&&!b.GetCreativeValues().Any()&&b.SetDamage(1234,99)==1234);
}
for(int i=0;i<35;i++){
    var before=oldSpecs.GetValue(i);var after=GunSpec.All[i];
    foreach(var f in typeof(GunSpec).GetFields(BindingFlags.Public|BindingFlags.Instance)){
        var old=before.GetType().GetField(f.Name).GetValue(before);var now=f.GetValue(after);
        Check(after.Name+" stat "+f.Name,JsonSerializer.Serialize(old)==JsonSerializer.Serialize(now));
    }
    foreach(int level in Enumerable.Range(0,51))foreach(string method in new[]{"Capacity","MaxDurability","FireRateMultiplier"}){
        var signature=new[]{typeof(int),typeof(int)};
        var old=oldCore.GetType("Game.ScGunGrowth").GetMethod(method,signature).Invoke(null,[i,level]);
        var now=typeof(ScGunGrowth).GetMethod(method,signature).Invoke(null,[i,level]);
        Check(after.Name+" growth "+level+"/"+method,Equals(old,now));
    }
    foreach(int level in Enumerable.Range(0,51)){
        var old=oldCore.GetType("Game.ScGunGrowth").GetMethod("RechargeSeconds").Invoke(null,[before,level]);
        Check(after.Name+" recharge "+level,Equals(old,ScGunGrowth.RechargeSeconds(after,level)));
    }
}
var beforeCounter=(Block)Activator.CreateInstance(oldCore.GetType("Game.ScGunCounterTemplateBlock"));
var afterCounter=new ScGunCounterTemplateBlock{BlockIndex=BlocksManager.GetBlockIndex<ScGunCounterTemplateBlock>()};beforeCounter.BlockIndex=afterCounter.BlockIndex;
foreach(int value in beforeCounter.GetCreativeValues()){
    Check("counter template remains readable "+value,ScGunCounterTemplateBlock.TrySnapshot(value,out var current));
    object[] parameters=[value,null];oldCore.GetType("Game.ScGunCounterTemplateBlock").GetMethod("TrySnapshot").Invoke(null,parameters);
    foreach(string field in new[]{"Variant","SkinId","Rounds","Durability","MaxDurability","CounterInstalled","KillCount"})
        Check("counter identity "+value+"/"+field,JsonSerializer.Serialize(parameters[1].GetType().GetProperty(field).GetValue(parameters[1]))==JsonSerializer.Serialize(typeof(ScGunSnapshot).GetProperty(field).GetValue(current)));
}
Check("creative counters filtered without reindexing",afterCounter.GetCreativeValues().Count()==48);
Check("basic knives keep original IDs",new ScKnifeBlock().GetCreativeValues().Select(ScKnifeBlock.GetVariant).SequenceEqual(new[]{8,9}));
Check("all gun recipes remain",ScWeaponCrafting.All.Count(e=>!e.Knife)==35&&ScWeaponCrafting.All.Count(e=>e.Knife)==2);
foreach(var skin in ScGunSkinCatalog.All){
    Check(skin.Key+" saved paint still recognized",ScGunSkinCatalog.IsKnown(skin.PaintId));
    string material=ScGunSkinCatalog.Material(skin.Gun,skin.PaintId),icon=ScGunSkinCatalog.Icon(skin.Gun,skin.PaintId);
    Check(skin.Key+" resolves available visual",package.GetEntry("Assets/Textures/ScCsgoKnives/"+material+".webp")!=null&&package.GetEntry("Assets/Textures/ScCsgoKnives/"+icon+".webp")!=null);
    Check(skin.Key+" coherent factory fallback",ScMinimalEdition.SkinAvailable(skin.PaintId)||material==skin.Gun+"_hd"&&icon==skin.Gun+"_slot");
}
foreach(var e in package.Entries.Where(e=>e.FullName.StartsWith("Assets/Audio/"))){
    using var stream=new MemoryStream(Bytes(package,e.FullName));var sound=SoundData.Load(stream);Check("native audio "+e.FullName,sound.Data.Length>0&&sound.SamplingFrequency>=8000&&sound.ChannelsCount>=1);
}
// Clip identity, timing, events, additive parameters and skeleton are all unchanged.
int clips=0;
foreach(string name in oldResources.GetManifestResourceNames().Where(n=>n.EndsWith(".animation.json"))){
    using var a=oldResources.GetManifestResourceStream(name);using var b=typeof(ScCsgoResources.ResourceMarker).Assembly.GetManifestResourceStream(name);
    using var old=JsonDocument.Parse(a);using var now=JsonDocument.Parse(b);
    foreach(string field in new[]{"Skeleton","Bindings"})Check(name+" "+field,old.RootElement.GetProperty(field).GetRawText()==now.RootElement.GetProperty(field).GetRawText());
    var next=now.RootElement.GetProperty("Clips");
    foreach(var clip in old.RootElement.GetProperty("Clips").EnumerateObject()){
        var current=next.GetProperty(clip.Name);clips++;
        foreach(var prop in clip.Value.EnumerateObject().Where(p=>p.Name!="Bones"))
            Check(name+"/"+clip.Name+"/"+prop.Name,prop.Value.GetRawText()==current.GetProperty(prop.Name).GetRawText());
    }
}
Console.WriteLine($"CPU checks {checks.Count}, {clips} clips retain timing/events");
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(var e in content.Entries.Where(e=>e.FullName.Contains("Shaders/")&&!e.FullName.EndsWith('/'))){using var r=new StreamReader(e.Open());caches[e.FullName.Replace("Assets/","")]=[r.ReadToEnd()];}
ContentManager.AddContentReader(new Game.IContentReader.ObjModelReader());
foreach(var e in package.Entries.Where(e=>e.FullName.EndsWith(".obj"))){var info=new ContentInfo(e.FullName[7..]);info.SetContentStream(new MemoryStream(Bytes(package,e.FullName)));ContentManager.Add(info);}
Texture2D Texture(string stem){string key="Textures/ScCsgoKnives/"+stem;if(caches.TryGetValue(key,out var found))return (Texture2D)found[0];string name="Assets/"+key+".webp";using var stream=(package.GetEntry(name)??throw new FileNotFoundException(name)).Open();var texture=Texture2D.Load(Image.Load(stream));caches[key]=[texture];return texture;}
bool done=false;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    foreach(var e in package.Entries.Where(e=>e.FullName.StartsWith("Assets/Textures/ScCsgoKnives/")&&e.FullName.EndsWith(".webp")))Texture(Path.GetFileNameWithoutExtension(e.FullName));
    using var target=new RenderTarget2D(640,480,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    var projection=Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(Cs2Placement.FovYDegrees(68)),640f/480,.02f,64);
    var light=new KnifePbrRenderer.Lighting{Dir1=Vector3.Normalize(new(-1,1,2)),Dir2=Vector3.Normalize(new(1,0,2)),Intensity=1};
    var bg=new Color(22,27,34);var placement=Cs2Placement.Placement();
    foreach(int variant in Enumerable.Range(0,CsmcKnifeRig.AssetCount)){
        string asset=CsmcKnifeRig.GetAssetName(variant);bool gun=GunSpec.ForAsset(asset)!=null;
        using var clipStream=typeof(ScCsgoResources.ResourceMarker).Assembly.GetManifestResourceStream("Game.AnimationData."+asset+".cs2.animation.json");
        using var clipDoc=JsonDocument.Parse(clipStream);
        var aliases=clipDoc.RootElement.GetProperty("Clips").EnumerateObject().Where(c=>c.Value.GetProperty("Bones").EnumerateObject().Any()).Select(c=>c.Name).ToArray();
        foreach(string alias in aliases)foreach(float phase in new[]{0f,.5f,1f}){
            float duration=Cs2Rig.Duration(asset,alias);var pose=Cs2Rig.Sample(asset,alias,duration*phase);Check(asset+" "+alias+" finite pose",pose!=null&&pose.Bones.Values.All(KnifeDiagnostics.IsFinite));
            Display.RenderTarget=target;Display.Viewport=new(0,0,640,480);Display.ScissorRectangle=new(0,0,640,480);Display.Clear(bg,1,0);
            string material=gun?asset+"_hd":asset+"_cs2";
            var rigid=Cs2RigidMesh.For(asset);var skinned=Cs2SkinnedMesh.Weapon(asset);
            var objParts=rigid==null&&skinned==null?Cs2Rig.GetMeshParts(asset).ToArray():[];
            Check(asset+" weapon geometry exists",rigid!=null||skinned!=null||objParts.Length>0);
            foreach(string binding in objParts){
                var model=ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{asset}_cs2_{binding}");
                Check(asset+" OBJ PBR "+binding,KnifePbrRenderer.TryDrawPart(model,Texture(material),variant,pose.GetPart(binding)*placement,projection,Matrix.Identity,in light,applyBoneTransform:true,material));
            }
            if(rigid!=null){
                Check(asset+" rigid pose",rigid.SetPose(pose,placement));
                foreach(var part in rigid.Parts){if(part.Material=="shared_scope_lens")continue;Check(asset+" part transform",rigid.TryPartWorld(part,out var world));string m=material;
                    Check(asset+" rigid PBR",KnifePbrRenderer.TryDrawSkinned(rigid.Vertices,part.Indices,Texture(m),m,world,projection,Matrix.Identity,in light,variant,rigid:true));}
                rigid.SkinBlended();foreach(var part in rigid.BlendedParts??[])Check(asset+" blended PBR",KnifePbrRenderer.TryDrawSkinned(rigid.BlendedSkinned,part.Indices,Texture(material),material,Matrix.Identity,projection,Matrix.Identity,in light,variant));
            }
            if(skinned!=null){Check(asset+" skinned pose",skinned.SetPose(pose,placement));skinned.Skin();foreach(var part in skinned.Primitives)Check(asset+" skinned PBR",KnifePbrRenderer.TryDrawSkinned(skinned.Skinned,part.Indices,Texture(material),material,Matrix.Identity,projection,Matrix.Identity,in light,variant));}
            if(alias.StartsWith("idle"))Check(asset+" weapon pixels before arms "+phase,target.GetData(new Rectangle(0,0,640,480)).Pixels.Count(p=>p!=bg)>20);
            var arms=Cs2SkinnedMesh.Arms;Check(asset+" arms pose",arms.SetPose(pose,placement));arms.Skin();foreach(var part in arms.Primitives){string m=part.Material.Contains("glove")?"cs2_glove":"cs2_arm";Check(asset+" arms PBR",KnifePbrRenderer.TryDrawSkinned(arms.Skinned,part.Indices,Texture(m),m,Matrix.Identity,projection,Matrix.Identity,in light,variant));}
            var pixels=target.GetData(new Rectangle(0,0,640,480));int visible=pixels.Pixels.Count(p=>p!=bg);
            // Draw/reload can intentionally start below the viewport. Idle must be visible.
            if(alias.StartsWith("idle"))Check(asset+" visible "+alias+phase,visible>50);
            frames.Add(new{asset,alias,phase,visible});
            if(alias.StartsWith("idle")&&phase==0||asset is "ak47" or "awp" or "m249" or "nova"&&alias.StartsWith("reload")&&phase==.5f){using var png=File.Create(Path.Combine(output,asset+"-"+alias+"-"+phase+".png"));Image.Save(pixels,png,ImageFileFormat.Png,false);}
        }
    }
    // Actual native legacy OBJ/UV material routes for every retained finish.
    ScGunRegistry.Current=new ScGunRegistry();
    foreach(var skin in ScGunSkinCatalog.Available){
        var parts=ScGunNativeMesh.Resolve(skin.Gun,skin.PaintId,out var color,out var material);Check(skin.Key+" correct material",color!=null&&material==skin.Material);
        if(ScGunSkinCatalog.LegacyBody(skin.Gun,material))Check(skin.Key+" native legacy parts",parts!=null&&parts.All(p=>p.Model!=null));
        if(parts!=null){
            int variant=Enumerable.Range(0,CsmcKnifeRig.AssetCount).Single(i=>CsmcKnifeRig.GetAssetName(i)==skin.Gun);
            var pose=Cs2Rig.Sample(skin.Gun,"idle",0);
            Display.Clear(bg,1,0);
            foreach(var part in parts)Check(skin.Key+" first person native part",KnifePbrRenderer.TryDrawPart(part.Model,part.Texture??color,variant,part.World(pose)*placement,projection,Matrix.Identity,in light,applyBoneTransform:true,part.Material??material));
            var skinPixels=target.GetData(new Rectangle(0,0,640,480));Check(skin.Key+" first person weapon pixels",skinPixels.Pixels.Count(p=>p!=bg)>20);
            using var png=File.Create(Path.Combine(output,"skin-"+skin.Gun+".png"));Image.Save(skinPixels,png,ImageFileFormat.Png,false);
        }
        var block=(ScGunBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGunBlock>()];var renderer=new PrimitivesRenderer3D();var transform=Matrix.CreateTranslation(0,0,-2);
        int v=Array.FindIndex(GunSpec.All,g=>g.Name==skin.Gun);int id=ScGunRegistry.Current.Allocate(v,1,false,100,100,skin.PaintId);
        int value=Terrain.MakeBlockValue(block.BlockIndex,0,GunSpec.MakeData(v,id));
        Display.Clear(bg,1,0);block.DrawBlock(renderer,value,Color.White,1,ref transform,new(){Light=15});renderer.Flush(projection);
        var pixels=target.GetData(new Rectangle(0,0,640,480));Check(skin.Key+" native dropped render",pixels.Pixels.Count(p=>p!=bg)>50);
    }
}catch(Exception e){failed++;Console.Error.WriteLine(e);}finally{Display.RenderTarget=null;Window.Close();}};
Window.Run(640,480,WindowMode.Fixed,"Mini resource verification");
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed,packageSha256=Hash(File.ReadAllBytes(args[0])),checks,frames,clips},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Mini: {checks.Count} checks, {frames.Count} frames, failed={failed}");return failed;
