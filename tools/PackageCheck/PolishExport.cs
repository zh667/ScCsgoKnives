using System.Collections;
using System.Reflection;
using System.Text.Json;
using Engine;
using Game;

static class PolishExport {
    internal static void Write(Assembly mod,string directory) {
        Directory.CreateDirectory(directory);
        var build=mod.GetType("Game.ScSurvivalMesh").GetMethod("Build");
        for(int kind=0;kind<7;kind++) {
            var mesh=(BlockMesh)build.Invoke(null,[kind]);
            var vertices=mesh.Vertices.Select(v=>new float[]{v.Position.X,v.Position.Y,v.Position.Z,v.TextureCoordinates.X,v.TextureCoordinates.Y,
                v.Color.R/255f,v.Color.G/255f,v.Color.B/255f,v.Color.A/255f}).ToArray();
            File.WriteAllText(Path.Combine(directory,$"item{kind}.json"),JsonSerializer.Serialize(new{vertices,indices=mesh.Indices.ToArray()}));
        }
        var visuals=mod.GetType("Game.ScGrenadeVisuals");var stateType=mod.GetType("Game.ScGrenadeState");
        foreach(int kind in new[]{0,1,2,4}) foreach(float time in kind==0?new[]{.08f,.3f,.85f}:kind==1?new[]{.03f,.12f,.25f}:kind==2?new[]{.3f,2f,14.5f}:new[]{.2f,2f,6.7f}) {
            object result;
            if(kind<2) result=visuals.GetMethod("Burst").Invoke(null,[Vector3.Zero,time,kind==1,false,8f]);
            else {
                var state=Activator.CreateInstance(stateType);
                stateType.GetField("Kind").SetValue(state,kind);stateType.GetField("Effect").SetValue(state,true);
                stateType.GetField("Age").SetValue(state,time);stateType.GetField("Remaining").SetValue(state,(kind==2?15:7)-time);
                if(kind==2) result=visuals.GetMethod("Smoke").Invoke(null,[state,8f]);
                else {
                    var points=new List<Vector3>();
                    for(float x=-2.6f;x<3;x+=.8f)for(float z=-2.6f;z<3;z+=.8f)if(x*x+z*z<=9)points.Add(new(x,0,z));
                    result=visuals.GetMethod("Fire").Invoke(null,[state,points,8f]);
                }
            }
            List<object> sprites=[];
            foreach(object sprite in (IEnumerable)result) {
                object P(string name)=>sprite.GetType().GetProperty(name).GetValue(sprite);
                Vector3 p=(Vector3)P("Position");Color c=(Color)P("Color");
                sprites.Add(new {position=new[]{p.X,p.Y,p.Z},width=P("Width"),height=P("Height"),texture=P("Texture"),frame=P("Frame"),rotation=P("Rotation"),additive=P("Additive"),upright=P("Upright"),color=new[]{c.R/255f,c.G/255f,c.B/255f,c.A/255f}});
            }
            File.WriteAllText(Path.Combine(directory,$"effect{kind}-{time.ToString(System.Globalization.CultureInfo.InvariantCulture)}.json"),JsonSerializer.Serialize(new{kind,time,sprites}));
        }
    }
}
