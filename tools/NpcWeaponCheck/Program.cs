// Native isolated geometry/render probe. Includes actual primitive flush, not game FPS.
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

if(args.Length!=4)throw new ArgumentException("NpcWeaponCheck <repo> <package> <Content.zip> <output>");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
var checks=new List<string>();var rows=new List<object>();int exit=0;
void Check(string name,bool pass){if(!pass)throw new Exception(name);checks.Add(name);}
Dispatcher.Initialize();

var geometries=new List<(string Asset,bool Legacy,ScNpcWeaponGeometry Geometry)>();
using var package=ZipFile.OpenRead(args[1]);using var content=ZipFile.OpenRead(args[2]);
var textures=new Dictionary<string,Texture2D>();
Texture2D Texture(string name){if(textures.TryGetValue(name,out var texture))return texture;using var stream=package.GetEntry("Assets/Textures/ScCsgoKnives/"+name+".png").Open();return textures[name]=Texture2D.Load(Image.Load(stream));}
string Read(string suffix){using var reader=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return reader.ReadToEnd();}
ContentManager.AddContentReader(new Game.IContentReader.ObjModelReader());
foreach(var e in package.Entries.Where(e=>e.FullName.StartsWith("Assets/Models/ScCsgoKnives/")&&e.FullName.EndsWith(".obj"))){var info=new ContentInfo(e.FullName[7..]);using var input=e.Open();var copy=new MemoryStream();input.CopyTo(copy);copy.Position=0;info.SetContentStream(copy);ContentManager.Add(info);}
bool done=false;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
foreach(string asset in GunSpec.All.Select(g=>g.Name))foreach(bool legacy in ScGunNativeMesh.Parts(asset).Length>0?new[]{false,true}:new[]{false}){
    ScResourceCaches.ClearAll();if(legacy)foreach(var part in ScGunNativeMesh.Parts(asset))part.Model=ContentManager.Get<ObjModel>(ScGunNativeMesh.ModelPath(asset,part));var sw=Stopwatch.StartNew();var source=ScThirdPersonWeapon.For(asset,legacy);double sourceMs=sw.Elapsed.TotalMilliseconds;
    Check(asset+" source",source!=null&&source.Vertices>0);
    using var data=new MemoryStream();ScNpcWeaponGeometry.Write(data,source,legacy);data.Position=0;sw.Restart();var copy=ScNpcWeaponGeometry.Read(data,asset,legacy);double cacheMs=sw.Elapsed.TotalMilliseconds;
    Check(asset+" root/groups",copy.WorldRootInverse==source.WorldRootInverse&&copy.HasRightGrip==source.HasRightGrip&&copy.Groups.Length==source.Groups.Length);
    for(int i=0;i<source.Groups.Length;i++){
        var a=source.Groups[i];var b=copy.Groups[i];
        Check(asset+" group "+i,a.Texture==b.Texture&&a.Silencer==b.Silencer&&a.Bone==b.Bone&&a.BindInverse==b.BindInverse&&a.WorldBone==b.WorldBone&&a.WorldInverse==b.WorldInverse&&a.Mesh.Vertices.SequenceEqual(b.Mesh.Vertices)&&a.Mesh.Indices.SequenceEqual(b.Mesh.Indices));
    }
    string path=Path.Combine(root,"src/ScCsgoTactical/Assets",ScNpcWeaponGeometry.PathFor(asset,legacy));Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,data.ToArray());
    if(package.GetEntry("Assets/"+ScNpcWeaponGeometry.PathFor(asset,legacy)) is {} packaged){using var input=packaged.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);Check(asset+" packaged native geometry",bytes.ToArray().SequenceEqual(data.ToArray()));}
    var info=new ContentInfo(ScNpcWeaponGeometry.PathFor(asset,legacy));info.SetContentStream(new MemoryStream(data.ToArray()));ContentManager.Add(info);
    Check(asset+" ContentManager reader",ScNpcWeaponGeometry.For(asset,legacy).Groups.Length==copy.Groups.Length);
    foreach(int size in new[]{0,4,100,(int)data.Length-1}){bool rejected=false;try{ScNpcWeaponGeometry.Read(new MemoryStream(data.ToArray()[..size]),asset,legacy);}catch{rejected=true;}Check(asset+" truncated "+size,rejected);}
    bool wrong=false;try{data.Position=0;ScNpcWeaponGeometry.Read(data,"wrong",legacy);}catch{wrong=true;}Check(asset+" identity rejected",wrong);
    foreach(string fault in new[]{"group-limit","nonfinite","index-range","trailing"}){
        byte[] damaged=data.ToArray();int matrix=4+4+System.Text.Encoding.UTF8.GetByteCount(asset)+2;
        if(fault=="group-limit")BitConverter.GetBytes(257).CopyTo(damaged,matrix+64);
        else if(fault=="nonfinite")BitConverter.GetBytes(float.NaN).CopyTo(damaged,matrix);
        else if(fault=="index-range")BitConverter.GetBytes(int.MaxValue).CopyTo(damaged,damaged.Length-4);
        else damaged=[..damaged,1];
        bool rejected=false;try{ScNpcWeaponGeometry.Read(new MemoryStream(damaged),asset,legacy);}catch(InvalidDataException){rejected=true;}Check(asset+" rejects "+fault,rejected);
    }
    rows.Add(new{stage="geometry",asset,legacy,sourceMs,cacheMs,bytes=data.Length,vertices=source.Vertices,groups=source.Groups.Length});
    geometries.Add((asset,legacy,copy));Console.WriteLine($"baked {asset} legacy={legacy}: {sourceMs:F2} -> {cacheMs:F2}ms");
}

    // Actual engine batch shaders, loaded from the user's API content.
    BaseTexturedBatch.m_shaderAlphaTest=new UnlitShader(Read("Shaders/Unlit.vsh"),Read("Shaders/Unlit.psh"),true,true,false,true);
    using var target=new RenderTarget2D(384,384,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,384,384);Display.ScissorRectangle=new Rectangle(0,0,384,384);
    var projection=Matrix.CreatePerspectiveFieldOfView(.8f,1,.01f,100);
    var bg=new Color(22,27,34);
    Image Render(ScNpcWeaponGeometry geometry,bool legacy,bool gpu,int light,float angle){
        Display.Clear(bg,1,0);var renderer=new PrimitivesRenderer3D();
        var bounds=geometry.Groups[0].Mesh.CalculateBoundingBox();foreach(var g in geometry.Groups.Skip(1)){var b=g.Mesh.CalculateBoundingBox();bounds.Min=Vector3.Min(bounds.Min,b.Min);bounds.Max=Vector3.Max(bounds.Max,b.Max);}
        float distance=Math.Max(.3f,(bounds.Max-bounds.Min).Length()*1.6f);
        var center=bounds.Center();var view=Matrix.CreateRotationY(angle)*Matrix.CreateLookAt(center+new Vector3(distance*.8f,distance*.3f,distance*.8f),center,Vector3.UnitY);
        foreach(var g in geometry.Groups){var transform=view;var texture=Texture(g.Texture);
            if(gpu)Check("queue "+geometry.Asset,ScNpcWeaponRenderer.Queue(renderer,g.Mesh,texture,transform,light,legacy));
            else if(legacy)ScGunNativeMesh.DrawWorld(renderer,g.Mesh,texture,Color.White,1,ref transform,new(){Light=light});
            else BlocksManager.DrawMeshBlock(renderer,g.Mesh,texture,Color.White,1,ref transform,new(){Light=light});
        }
        renderer.Flush(projection);Check("flush clears command queue",renderer.m_allBatches.All(b=>b.IsEmpty()));return target.GetData(new Rectangle(0,0,384,384));
    }
    foreach(var (asset,legacy,geometry) in geometries){
        foreach(int light in new[]{0,7,15}){
            var old=Render(geometry,legacy,false,light,.23f);var current=Render(geometry,legacy,true,light,.23f);
            int changed=0,visible=0,max=0;long error=0;
            for(int i=0;i<old.Pixels.Length;i++){var a=old.Pixels[i];var b=current.Pixels[i];if(a!=bg)visible++;int delta=Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));if(delta>2)changed++;max=Math.Max(max,delta);error+=delta;}
            rows.Add(new{stage="pixels",asset,legacy,light,visible,changed,max,meanError=error/(double)old.Pixels.Length});
            Check(asset+" pixel equivalence "+legacy+"/"+light,visible>10&&changed<old.Pixels.Length*.005&&error/(double)old.Pixels.Length<.5);
            if(light==15&&asset is "ak47" or "awp" or "revolver"){using var png=File.Create(Path.Combine(output,asset+(legacy?"-legacy":"")+".png"));Image.Save(current,png,ImageFileFormat.Png,false);}
        }
    }
    // Shader byte lighting, emissive vertices, alpha test and both sampling modes.
    var colored=new BlockMesh();
    colored.Vertices.Add(new(){Position=new(-.5f,-.5f,0),TextureCoordinates=new(0,0),Color=new(123,89,231,255),IsEmissive=true});
    colored.Vertices.Add(new(){Position=new(.5f,-.5f,0),TextureCoordinates=new(2,0),Color=new(17,201,115,128)});
    colored.Vertices.Add(new(){Position=new(0,.5f,0),TextureCoordinates=new(1,2),Color=new(235,98,52,0)});
    foreach(int i in new[]{0,1,2,0,2,1})colored.Indices.Add(i);
    var synthetic=ScNpcWeaponGeometry.Wrap(new ScThirdPersonWeapon{Asset="shader-fixture",Groups=[new(colored,"ak47_hd")]});
    foreach(int light in new[]{0,7,15})foreach(bool legacy in new[]{false,true}){
        var old=Render(synthetic,legacy,false,light,0);var current=Render(synthetic,legacy,true,light,0);
        Check("emissive/alpha/sampling "+light+"/"+legacy,old.Pixels.Zip(current.Pixels).Count(p=>Math.Abs(p.First.R-p.Second.R)>2||Math.Abs(p.First.G-p.Second.G)>2||Math.Abs(p.First.B-p.Second.B)>2)<384*384*.005);
    }
    // Exercise loss/reset on only the buffers/shader owned here, with existing queued draws.
    var owned=GraphicsResource.m_resources.Where(r=>r.GetType().Assembly==typeof(ScNpcWeaponRenderer).Assembly||r is Shader s&&s.m_vertexShaderCode?.Contains("a_emissive")==true).ToArray();
    foreach(var r in owned)r.HandleDeviceLost();foreach(var r in owned)r.HandleDeviceReset();
    var fixture=geometries.First(g=>g.Asset=="ak47"&&!g.Legacy);
    var restored=Render(fixture.Geometry,false,true,15,.23f);var reference=Render(fixture.Geometry,false,false,15,.23f);
    Check("device reset restores geometry",restored.Pixels.Zip(reference.Pixels).Count(p=>p.First!=p.Second)<384*384*.01);
    foreach(int count in new[]{1,3,5,28})foreach(bool gpu in new[]{false,true}){
        if(gpu)ScNpcWeaponRenderer.Clear();
        var renderer=new PrimitivesRenderer3D();var guns=new[]{"ak47","awp","mp9","m249","deagle"}.Select(a=>geometries.First(g=>g.Asset==a&&!g.Legacy).Geometry).ToArray();
        void Draw(){Display.Clear(bg,1,0);for(int i=0;i<count;i++)foreach(var g in guns[i%guns.Length].Groups){var transform=Matrix.CreateTranslation(i*.03f,0,-2);var texture=Texture(g.Texture);if(gpu){if(!ScNpcWeaponRenderer.Queue(renderer,g.Mesh,texture,transform,12,false))throw new Exception("GPU fallback");}else BlocksManager.DrawMeshBlock(renderer,g.Mesh,texture,Color.White,1,ref transform,new(){Light=12});}renderer.Flush(projection);}
        long start=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();Draw();double coldMs=sw.Elapsed.TotalMilliseconds;long coldBytes=GC.GetAllocatedBytesForCurrentThread()-start;
        var pixel=new Color[1];target.GetData(pixel,0,new Rectangle(0,0,1,1));start=GC.GetAllocatedBytesForCurrentThread();sw.Restart();
        for(int f=0;f<30;f++)Draw();double warmMs=sw.Elapsed.TotalMilliseconds/30;long bytes=(GC.GetAllocatedBytesForCurrentThread()-start)/30;
        target.GetData(pixel,0,new Rectangle(0,0,1,1));double completedMs=sw.Elapsed.TotalMilliseconds/30;
        rows.Add(new{stage="submission",count,gpu,coldMs,coldBytes,warmMs,bytes,completedMs});Console.WriteLine($"count={count}, gpu={gpu}: {warmMs:F2} ms, {bytes} bytes/frame");
    }
    var aRenderer=new PrimitivesRenderer3D();var bRenderer=new PrimitivesRenderer3D();var group=fixture.Geometry.Groups[0];
    ScNpcWeaponRenderer.Queue(aRenderer,group.Mesh,Texture(group.Texture),Matrix.Identity,15,false);ScNpcWeaponRenderer.Queue(bRenderer,group.Mesh,Texture(group.Texture),Matrix.Identity,15,false);
    aRenderer.Clear();Check("independent cameras",aRenderer.m_allBatches.All(b=>b.IsEmpty())&&bRenderer.m_allBatches.Any(b=>!b.IsEmpty()));
    ScNpcWeaponRenderer.Clear();Check("world disposal clears queued buffers",ScNpcWeaponRenderer.CachedMeshes==0&&owned.All(r=>r.m_isDisposed)&&bRenderer.m_allBatches.All(b=>b.IsEmpty()));
    Render(fixture.Geometry,false,true,15,.23f);Check("reentry rebuilds buffers",ScNpcWeaponRenderer.CachedMeshes>0);ScNpcWeaponRenderer.Clear();ScNpcWeaponRenderer.Clear();
}catch(Exception e){Console.Error.WriteLine(e);rows.Add(new{error=e.ToString()});exit=1;}finally{ScNpcWeaponRenderer.Clear();foreach(var t in textures.Values)t.Dispose();Display.RenderTarget=null;Window.Close();}};
Window.Run(384,384,WindowMode.Fixed,"NPC geometry verification");
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed=exit,checks,measurements=rows},new JsonSerializerOptions{WriteIndented=true}));return exit;
