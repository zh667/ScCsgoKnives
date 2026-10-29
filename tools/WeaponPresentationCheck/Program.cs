using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

if(args.Length!=4)throw new ArgumentException("WeaponPresentationCheck <repo> <Content.zip> <report directory> <baseline Full.scmod>");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
var checks=new List<string>();var errors=new List<string>();
void Check(string name,bool pass){if(!pass)throw new Exception(name);checks.Add(name);}
Dispatcher.Initialize();
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
using var content=ZipFile.OpenRead(args[1]);
using var package=ZipFile.OpenRead(args[3]);
string Read(string suffix){using var reader=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return reader.ReadToEnd();}
ContentManager.AddContentReader(new Game.IContentReader.ObjModelReader());
foreach(var entry in package.Entries.Where(e=>e.FullName.StartsWith("Assets/Models/ScCsgoKnives/")&&e.FullName.EndsWith(".obj"))){
    using var source=entry.Open();var copy=new MemoryStream();source.CopyTo(copy);copy.Position=0;
    var info=new ContentInfo(entry.FullName[7..]);info.SetContentStream(copy);ContentManager.Add(info);
}
foreach(string role in new[]{"ct","t"}){
    var info=new ContentInfo("Animations/ScCsgoTactical/"+role+".scanim");
    using var source=package.GetEntry("Assets/Animations/ScCsgoTactical/"+role+".scanim").Open();var copy=new MemoryStream();source.CopyTo(copy);copy.Position=0;
    info.SetContentStream(copy);ContentManager.Add(info);
}
var textures=new Dictionary<string,Texture2D>();
Texture2D Texture(string name){
    if(textures.TryGetValue(name,out var hit))return hit;
    using var stream=(package.GetEntry("Assets/Textures/ScCsgoKnives/"+name+".png")??throw new FileNotFoundException(name)).Open();var texture=Texture2D.Load(Image.Load(stream));textures[name]=texture;
    caches["Textures/ScCsgoKnives/"+name]=[texture];return texture;
}
bool done=false;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    BaseTexturedBatch.m_shaderAlphaTest=new UnlitShader(Read("Shaders/Unlit.vsh"),Read("Shaders/Unlit.psh"),true,true,false,true);
    BaseTexturedBatch.m_shader=new UnlitShader(Read("Shaders/Unlit.vsh"),Read("Shaders/Unlit.psh"),true,false,false,true);
    foreach(string locale in new[]{"zh-CN","en-US"}){
        using var source=package.GetEntry("Assets/Lang/"+locale+".json").Open();using var copy=new MemoryStream();source.CopyTo(copy);
        Check("package/locale/"+locale,copy.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"src/ScCsgoKnives/Assets/Lang/"+locale+".json"))));
    }
    LocaleChecks.Run(root,Check);
    CounterChecks.Run(package,Check);
    MaterialChecks.Run(package,output,Texture,Check);
    ScCreativeSkinsSelfTest.Run((name,ok,detail)=>{if(ok)checks.Add(name);else errors.Add(name+": "+detail);});
    ScCreativeCountersSelfTest.Run((name,ok,detail)=>{if(ok)checks.Add(name);else errors.Add(name+": "+detail);});
}catch(Exception e){errors.Add(e.ToString());Console.Error.WriteLine(e);}finally{ScNpcWeaponRenderer.Clear();foreach(var texture in textures.Values)texture.Dispose();Window.Close();}};
Window.Run(480,480,WindowMode.Fixed,"Weapon presentation regression");
string Sha(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{
    engine=typeof(BlocksManager).Assembly.FullName,engineApiVersion=ModsManager.APINuGetVersion.ToString(),engineSha256=Sha(typeof(BlocksManager).Assembly.Location),
    coreSha256=Sha(typeof(ScGunBlock).Assembly.Location),tacticalSha256=Sha(typeof(ComponentTacticalModel).Assembly.Location),resourcePackageSha256=Sha(args[3]),
    runnerSha256=Sha(Assembly.GetExecutingAssembly().Location),
    checks,errors},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count} passed; {errors.Count} failed");return errors.Count==0?0:1;
