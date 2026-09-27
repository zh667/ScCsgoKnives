using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;

string package=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);Dispatcher.Initialize();
var checks=new List<string>();var rows=new List<object>();int failed=0;
void Check(string label,bool ok){if(!ok)throw new Exception(label);checks.Add(label);}
using var zip=System.IO.Compression.ZipFile.OpenRead(package);
using var agents=System.IO.Compression.ZipFile.OpenRead(args.Length>2?args[2]:package);
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
ContentManager.AddContentReader(new Game.IContentReader.ImageReader());
ContentManager.AddContentReader(new Game.IContentReader.Texture2DReader());
var names=new[]{"ak47_hd","m4a4_hd","awp_hd","negev_hd","glock18_hd"};
var original=new Dictionary<string,byte[]>();var sourceStreams=new List<MemoryStream>();
foreach(string name in names){
    var entry=zip.Entries.First(e=>e.FullName=="Assets/Textures/ScCsgoKnives/"+name+".webp"||e.FullName=="Assets/Textures/ScCsgoKnives/"+name+".png");
    using var s=entry.Open();using var m=new MemoryStream();s.CopyTo(m);original[name]=m.ToArray();
    var owned=new MemoryStream(m.ToArray());sourceStreams.Add(owned);var info=new ContentInfo(entry.FullName[7..]);info.SetContentStream(owned);ContentManager.Add(info);
}
bool done=false;Window.Frame+=()=>{if(done)return;done=true;try{
    BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)]=701;BlocksManager.Blocks[701]=new ScGunBlock{BlockIndex=701};
    // Actual saved-entity equipment path: no new CreateSquad call is involved.
    var project=new Project();
    var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;
    var enemy=new ComponentTacticalEnemy{State=TacticalEnemyState.Create(TacticalRole.Rifle,"restored",new Engine.Random(9))};
    enemy.State.Variant=Array.FindIndex(GunSpec.All,s=>s.Name=="ak47");
    var model=new ComponentTacticalModel();model.m_entity=entity;enemy.m_entity=entity;entity.m_components=[enemy,model];
    project.m_entities[entity]=true;
    string geometryName="Assets/"+ScNpcWeaponGeometry.PathFor("ak47",false);
    byte[] geometry;
    using(var source=agents.GetEntry(geometryName).Open()){using var copy=new MemoryStream();source.CopyTo(copy);geometry=copy.ToArray();}
    var geometryInfo=new ContentInfo(geometryName[7..]);geometryInfo.SetContentStream(new MemoryStream(geometry));ContentManager.Add(geometryInfo);
    using(var geometryInput=new MemoryStream(geometry)){
        foreach(string tex in ScNpcWeaponGeometry.Read(geometryInput,"ak47",false).Groups.Select(g=>g.Texture).Distinct()){
            string path="Assets/Textures/ScCsgoKnives/"+tex;
            var entry=zip.GetEntry(path+".webp")??zip.GetEntry(path+".png")??agents.GetEntry(path+".webp")??agents.GetEntry(path+".png");
            if(entry==null)throw new Exception("Missing texture "+tex);
            using var source=entry.Open();var copy=new MemoryStream();source.CopyTo(copy);copy.Position=0;
            var info=new ContentInfo(entry.FullName[7..]);info.SetContentStream(copy);ContentManager.Add(info);
        }
    }
    foreach(int round in new[]{0,1}){
        ScNpcWeaponGeometry.Clear();ScNpcWeaponRenderer.Clear();ScTexturePreparation.Clear();
        ScWeaponPreparation.Restore(project);
        var ready=ScNpcWeaponGeometry.For("ak47");
        int gpu=ScNpcWeaponRenderer.CachedMeshes;
        Check("restored geometry and GPU ready before draw "+round,gpu>0&&ScNpcWeaponGeometry.PendingCount==0&&ScTexturePreparation.PendingCount==0);
        ScWeaponPreparation.Restore(project);
        Check("repeat restore reuses geometry and buffers "+round,ReferenceEquals(ready,ScNpcWeaponGeometry.For("ak47"))&&gpu==ScNpcWeaponRenderer.CachedMeshes);
    }
    int before=GraphicsResource.m_resources.Count;
    foreach(string name in names){ScTexturePreparation.Request(name);ScTexturePreparation.Request(name);}
    Check("bounded task count",ScTexturePreparation.PendingCount<=names.Length&&ScTexturePreparation.PendingCount>0);
    // Workers may finish whenever they finish; no thread is permitted to create GL resources.
    Check("request never creates GPU objects",GraphicsResource.m_resources.Count==before);
    foreach(string name in names){
        using var raw=new MemoryStream(original[name]);var reference=Image.Load(raw);
        var texture=ScTexturePreparation.Load("Textures/ScCsgoKnives/"+name);
        var image=(Image)texture.Tag;
        Check("original dimensions "+name,texture.Width==reference.Width&&texture.Height==reference.Height);
        Check("original native pixels "+name,image.Pixels.SequenceEqual(reference.Pixels));
        Check("native upload parameters "+name,texture.MipLevelsCount==1&&texture.ColorFormat==ColorFormat.Rgba8888);
        Check("normal loader reuses prepared texture "+name,ReferenceEquals(texture,ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+name)));
        rows.Add(new{name,width=texture.Width,height=texture.Height,encodedBytes=original[name].Length});
        reference.Dispose();
    }
    Check("shared source streams still open",sourceStreams.All(s=>s.CanRead&&s.CanWrite));
    ScTexturePreparation.Clear();
    Check("clear does not dispose native shared textures",names.All(n=>ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+n).Width>0));
    // Poison a preparation, clear it before publication, replace the source and
    // ensure the cancelled result cannot contaminate a later project.
    var extra="Textures/ScCsgoKnives/fixture-preparation.png";
    var input=new MemoryStream(original["ak47_hd"]);var ci=new ContentInfo(extra);ci.SetContentStream(input);ContentManager.Add(ci);
    ScTexturePreparation.Request("fixture-preparation");ScTexturePreparation.Clear();
    using(var fresh=new MemoryStream(original["glock18_hd"])){
        var newInfo=new ContentInfo(extra);var retained=new MemoryStream(fresh.ToArray());newInfo.SetContentStream(retained);ContentManager.Add(newInfo);
        ScTexturePreparation.Request("fixture-preparation");
        var result=ScTexturePreparation.Load("Textures/ScCsgoKnives/fixture-preparation");
        var reference=Image.Load(fresh);
        Check("cancelled old-world pixels never published",((Image)result.Tag).Pixels.SequenceEqual(reference.Pixels));
        reference.Dispose();
    }
    Check("no pending requests after demand reads",ScTexturePreparation.PendingCount==0);
}catch(Exception e){Console.Error.WriteLine(e);failed=1;}finally{ScTexturePreparation.Clear();ScNpcWeaponRenderer.Clear();Window.Close();}};
Window.Run(200,160,WindowMode.Fixed,"Native texture preparation check");
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed,checks,rows,packageSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(package)))},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"texture checks={checks.Count}, failed={failed}");return failed;
