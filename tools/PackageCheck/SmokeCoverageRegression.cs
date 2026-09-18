using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Engine;

// Uses the delivered DLL's billboards and the delivered atlas alpha. No GPU/game-frame claim.
static class SmokeCoverageRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string package) {
        List<Result> results=[];
        using var zip=ZipFile.OpenRead(package);
        var entry=zip.GetEntry("Assets/Textures/ScCsgoKnives/grenade_smoke_atlas.png")??zip.GetEntry("Assets/Textures/ScCsgoKnives/grenade_smoke_atlas.webp");
        using var data=new MemoryStream();using(var stream=entry.Open())stream.CopyTo(data);data.Position=0;
        var atlas=Engine.Media.Image.Load(data);
        try {
        var state=Activator.CreateInstance(mod.GetType("Game.ScGrenadeState"));
        void Set(string n,object v)=>state.GetType().GetField(n).SetValue(state,v);
        Set("Kind",2);Set("Effect",true);Set("Age",2f);Set("Remaining",12f);
        var visuals=mod.GetType("Game.ScGrenadeVisuals");
        foreach(bool overhead in new[]{false,true}) {
            Vector3 right=Vector3.UnitX,up=overhead?Vector3.UnitZ:Vector3.UnitY;
            var sprites=((IEnumerable)visuals.GetMethod("Smoke").Invoke(null,[state,5f])).Cast<object>().ToArray();
            var mapped=sprites.Select(s=>{
                object P(string n)=>s.GetType().GetProperty(n).GetValue(s);
                var axes=((Vector3,Vector3))visuals.GetMethod("SmokeAxes").Invoke(null,[s,state,right,up]);
                Vector3 p=(Vector3)P("Position");return new {x=Vector3.Dot(p,right),y=Vector3.Dot(p,up),rx=Vector3.Dot(axes.Item1,right),ry=Vector3.Dot(axes.Item1,up),ux=Vector3.Dot(axes.Item2,right),uy=Vector3.Dot(axes.Item2,up),frame=(int)P("Frame"),alpha=((Color)P("Color")).A/255f};
            }).ToArray();
            float Opacity(float x,float y) {
                float clear=1;
                foreach(var s in mapped) {
                    float dx=x-s.x,dy=y-s.y,det=s.rx*s.uy-s.ry*s.ux;
                    float u=(dx*s.uy-dy*s.ux)/det*.5f+.5f,v=.5f-(dy*s.rx-dx*s.ry)/det*.5f;
                    if(u<0||u>1||v<0||v>1)continue;
                    int tx=Math.Clamp((int)((s.frame%4*.25f+.004f+u*.242f)*atlas.Width),0,atlas.Width-1);
                    int ty=Math.Clamp((int)((s.frame/4*.25f+.004f+v*.242f)*atlas.Height),0,atlas.Height-1);
                    clear*=1-atlas.Pixels[ty*atlas.Width+tx].A/255f*s.alpha;
                }
                return 1-clear;
            }
            // Dense, contiguous screen coverage including ankle level: transparent quad corners don't count.
            foreach(float y in overhead?new[]{0f,2f}:new[]{.1f,.5f,1.5f}) {
                int dense=0;float minimum=1;
                for(float x=-3.5f;x<=3.5f;x+=.05f) {float a=Opacity(x,y);if(a>.85f)dense++;if(Math.Abs(x)<2.5f)minimum=Math.Min(minimum,a);}
                float width=dense*.05f;
                results.Add(new($"smoke-visible/{overhead}/{y}",width>=5.5f&&minimum>.9f,$"opaque width={width:F2}m; central min alpha={minimum:F3}"));
            }
            string folder=Environment.GetEnvironmentVariable("SC_CSGO_VISUAL_REPORT");
            if(!string.IsNullOrEmpty(folder)) {Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,$"smoke-{(overhead?"top":"side")}.json"),JsonSerializer.Serialize(mapped));}
        }
        return results;
        } finally {atlas.Dispose();}
    }
}
