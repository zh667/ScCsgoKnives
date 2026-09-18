using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;

static class CasingRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string package){
        List<Result> result=[];
        void Test(string name,Func<bool> f){try{result.Add(new("casings/"+name,f(),""));}catch(Exception e){result.Add(new("casings/"+name,false,e.ToString()));}}
        var t=mod.GetType("Game.ScCasingEffects");var renderer=mod.GetType("Game.CsmcFirstPersonRenderer");
        object P(object o,string n)=>o.GetType().GetProperty(n).GetValue(o);
        void F(object o,string n,object v)=>o.GetType().GetField(n).SetValue(o,v);
        var data=t.GetField("Definitions").GetValue(null);var cues=(IDictionary)P(data,"Cues");var models=(IDictionary)P(data,"Models");
        using var zip=ZipFile.OpenRead(package);
        Test("eight-authentic-models-33-guns",()=>models.Count==8&&cues.Keys.Cast<string>().Select(k=>k.Split(':')[0]).Distinct().Count()==33);
        foreach(DictionaryEntry entry in models)Test("mesh-and-texture/"+entry.Key,()=>{
            string mesh=(string)P(entry.Value,"Model"),texture=(string)P(entry.Value,"Texture");
            var obj=zip.GetEntry("Assets/Models/ScCsgoKnives/"+mesh+".obj");if(obj is null)return false;
            using var stream=new StreamReader(obj.Open());var lines=stream.ReadToEnd().Split('\n');
            var vertices=lines.Where(l=>l.StartsWith("v ")).Select(l=>l.Split(' ').Skip(1).Select(float.Parse).ToArray()).ToArray();
            return vertices.Length>30&&vertices.All(v=>v.All(float.IsFinite)&&v.All(x=>Math.Abs(x)<.15))&&lines.Count(l=>l.StartsWith("f "))>=40
                &&(zip.GetEntry("Assets/Textures/ScCsgoKnives/"+texture+".png")??zip.GetEntry("Assets/Textures/ScCsgoKnives/"+texture+".webp")) is not null;
        });
        var player=(ComponentPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ComponentPlayer));
        var table=renderer.GetField("s_casingFrames",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var frame=table.GetType().GetMethod("GetOrCreateValue").Invoke(table,[player]);
        Matrix root=(Matrix)mod.GetType("Game.Cs2Placement").GetMethod("Placement").Invoke(null,null);
        F(frame,"Root",root);F(frame,"View",Matrix.Identity);F(frame,"Ratio",1f);F(frame,"Frame",Engine.Time.FrameIndex);
        foreach(DictionaryEntry entry in cues)Test("source-attachment/"+entry.Key,()=>{
            string[] key=((string)entry.Key).Split(':');F(frame,"Gun",key[0]);
            foreach(var cue in (IEnumerable)entry.Value){
                var pose=mod.GetType("Game.Cs2Rig").GetMethod("Sample").Invoke(null,[key[0],key[1],(float)P(cue,"At")]);F(frame,"Pose",pose);
                object[] args=[player,key[0],cue,Matrix.Identity];if(!(bool)renderer.GetMethod("TryGetCasingFrame").Invoke(null,args))return false;
                Matrix world=(Matrix)args[3];if(world.Translation.Length()>3||world.Translation.Z>=0||!float.IsFinite(world.M11))return false;
            }return true;
        });
        Test("bolt-and-pump-event-times",()=>{
            float At(string gun,string clip)=>(float)P(((IEnumerable)t.GetMethod("Events").Invoke(null,[gun,clip])).Cast<object>().Single(),"At");
            return Math.Abs(At("awp","shoot1")-.74)<.001&&Math.Abs(At("ssg08","shoot1")-.840016)<.001&&At("nova","shoot1")>.3f&&At("ak47","shoot1")==.04f;
        });
        Test("gravity-finite-and-large-frame-budget",()=>{
            var advance=t.GetMethod("AdvanceVelocity");Vector3 v=(Vector3)advance.Invoke(null,[Vector3.Zero,10f]);return Math.Abs(v.Y+.762f)<.001f&&v.X==0&&v.Z==0;
        });
        Test("no-casings-for-taser-or-revolver-shots",()=>!((IEnumerable)t.GetMethod("Events").Invoke(null,["taser","shoot1"])).Cast<object>().Any()&&!((IEnumerable)t.GetMethod("Events").Invoke(null,["revolver","shoot1"])).Cast<object>().Any());
        Test("bounded-no-persistence",()=>t.GetMethod("Save") is null&&t.GetMethod("Load") is null&&(int)t.GetProperty("Limit").GetValue(null)<=48&&(float)t.GetProperty("Lifetime").GetValue(null)<=2.5f);
        return result;
    }
}
