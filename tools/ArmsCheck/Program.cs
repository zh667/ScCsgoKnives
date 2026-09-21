// Native GPU diagnostic; not full-game/Android/network acceptance.
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var results=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);results.Add(name);}
Engine.Dispatcher.Initialize();
var project=new Project();var terrain=new SubsystemTerrain{m_project=project};project.m_subsystems.Add(terrain);
var sub=new SubsystemScTactical{m_project=project};sub.Load(new());
sub.SetGlove(0,"sporty_green");sub.SetGlove(1,"slick_red");sub.SetGlove(0,"bad-value");
Check("invalid choice preserves selection",sub.GloveFor(0)=="sporty_green");
for(int round=0;round<2;round++){
    var saved=new ValuesDictionary();sub.Save(saved);var xml=new XElement("Values");saved.Save(xml);
    var loaded=new ValuesDictionary();loaded.ApplyOverrides(XElement.Parse(xml.ToString()));sub=new(){m_project=project};sub.Load(loaded);
    Check("two player XML round "+round,sub.GloveFor(0)=="sporty_green"&&sub.GloveFor(1)=="slick_red"&&sub.GloveFor(3)=="");
}
sub.SetGlove(0,"");Check("reset does not affect other player",sub.GloveFor(0)==""&&sub.GloveFor(1)=="slick_red");
var firstA=new ComponentFirstPersonModel();var firstB=new ComponentFirstPersonModel();
var roleA=new RoleComponent{FirstPersonRole="ct"};var roleB=new RoleComponent{FirstPersonRole="t"};
void Attach(params Component[] cs){var e=new Entity{m_components=cs.ToList()};foreach(var c in cs)c.m_entity=e;}
Attach(firstA,roleA);Attach(firstB,roleB);
Check("entity-local role",TacticalArms.Role(firstA)=="ct"&&TacticalArms.Role(firstB)=="t");
roleA.FirstPersonRole=null;Check("default role isolated",TacticalArms.Role(firstA)==null&&TacticalArms.Role(firstB)=="t");
Check("default without glove uses original arms",TacticalArms.ResolveSet(null,"")==null);
using var catalog=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src/ScCsgoKnives/AnimationData/cs2_catalog.json")));
string[] meshNames=["ct_default","t_default","ct_sleeves","t_sleeves","glove_sporty","glove_specialist","glove_slick"];
int samples=0;float maxRadius=0;
foreach(var asset in catalog.RootElement.EnumerateObject()){
    foreach(var clip in asset.Value.GetProperty("Clips").EnumerateObject()){
        string alias=clip.Value.TryGetProperty("Alias",out var a)?a.GetString():clip.Name;
        if(!new[]{"idle","deploy","draw","reload","inspect","lookat","throw","plant","shoot1"}.Any(s=>alias.StartsWith(s)))continue;
        float duration=clip.Value.GetProperty("Duration").GetSingle();
        foreach(float phase in new[]{0f,.25f,.5f,.75f,1f}){
            var pose=Cs2Rig.Sample(asset.Name,alias,Math.Max(0,duration*phase-.0001f));Check(asset.Name+"/"+alias+" pose",pose!=null);
            foreach(string meshName in meshNames){
                var m=TacticalArms.Mesh(meshName);
                Check(meshName+" resolved "+asset.Name+"/"+alias,m.UnresolvedWeight(pose)<.001f);
                m.SetPose(pose,Cs2Placement.Placement());m.Skin();
                float radius=m.Skinned.Max(v=>v.Position.Length());maxRadius=Math.Max(radius,maxRadius);
                Check(meshName+" finite bounded "+asset.Name+"/"+alias,float.IsFinite(radius)&&radius<8);
            }
            samples++;
        }
    }
}
Console.WriteLine($"Pose samples {samples}; max radius {maxRadius}");
bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
    foreach(var source in new[]{"src/ScCsgoKnives/Assets/Textures/ScCsgoKnives","src/ScCsgoTactical/Assets/Textures/ScCsgoKnives"})
    foreach(var file in Directory.GetFiles(Path.Combine(root,source),"*.png")){
        string name=Path.GetFileNameWithoutExtension(file);
        if(!name.StartsWith("tactical_arm_")&&!name.StartsWith("cs2_arm")&&!name.StartsWith("cs2_glove")&&!name.StartsWith("env_"))continue;
        using var stream=File.OpenRead(file);caches["Textures/ScCsgoKnives/"+name]=[Texture2D.Load(stream)];
    }
    LightingManager.Initialize();
    var light=new KnifePbrRenderer.Lighting{Dir1=Vector3.Normalize(new Vector3(-1,1,1)),Dir2=Vector3.Normalize(new Vector3(1,0,1)),Intensity=1};
    using var target=new RenderTarget2D(960,540,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    var projection=Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(Cs2Placement.FovYDegrees(68)),960f/540,.02f,64);
    foreach(string role in new[]{"ct","t",""})foreach(string key in new[]{""}.Concat(TacticalArms.Gloves.Select(g=>g.Key))){
        var layers=TacticalArms.ResolveSet(role,key);if(layers==null)continue;
        foreach(var shot in new[]{("ak47","reload",.45f),("butterfly","inspect",.35f),("default_t","idle",0f),("c4","idle",0f)}){
            var pose=Cs2Rig.Sample(shot.Item1,shot.Item2,Cs2Rig.Duration(shot.Item1,shot.Item2)*shot.Item3);
            Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,960,540);Display.ScissorRectangle=new Rectangle(0,0,960,540);Display.Clear(new Color(35,43,54),1,0);
            foreach(var layer in layers){
                layer.Mesh.SetPose(pose,Cs2Placement.Placement());layer.Mesh.Skin();
                for(int i=0;i<layer.Materials.Length;i++)Check("native PBR "+layer.Materials[i],KnifePbrRenderer.TryDrawSkinned(layer.Mesh.Skinned,layer.Mesh.Primitives[i].Indices,layer.Textures[i],layer.Materials[i],Matrix.Identity,projection,Matrix.Identity,in light,0));
            }
            using var file=File.Create(Path.Combine(output,$"{(role==""?"default":role)}-{(key==""?"original":key)}-{shot.Item1}-{shot.Item2}.png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
        }
    }
    var setA=TacticalArms.ResolveSet("ct","sporty_green");var setB=TacticalArms.ResolveSet("t","slick_red");
    Check("sequential cameras retain selected materials",setA[0].Materials.Any(n=>n.Contains("sporty_green"))&&setB[0].Materials.Any(n=>n.Contains("slick_red")));
    ScUiSettings.SimpleMaterials=true;var part=setA[0];Check("simple shader",KnifePbrRenderer.TryDrawSkinned(part.Mesh.Skinned,part.Mesh.Primitives[0].Indices,part.Textures[0],part.Materials[0],Matrix.Identity,projection,Matrix.Identity,in light,0));
    File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{passed=true,checks=results.Count,samples,maxRadius,results},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("All arms checks passed: "+results.Count);
}catch(Exception e){Console.Error.WriteLine(e);exit=1;}finally{Window.Close();}};
Window.Run(960,540,WindowMode.Fixed,"CS arms native diagnostic");return exit;
sealed class RoleComponent:Component,IScFirstPersonAppearance{public string FirstPersonRole{get;set;}}
