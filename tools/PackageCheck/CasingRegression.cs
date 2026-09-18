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
        string detail="";
        void Test(string name,Func<bool> f){try{detail="";bool ok=f();result.Add(new("casings/"+name,ok,detail));}catch(Exception e){result.Add(new("casings/"+name,false,e.ToString()));}}
        var t=mod.GetType("Game.ScCasingEffects");var renderer=mod.GetType("Game.CsmcFirstPersonRenderer");
        object P(object o,string n)=>o.GetType().GetProperty(n).GetValue(o);
        void F(object o,string n,object v)=>o.GetType().GetField(n).SetValue(o,v);
        var data=t.GetField("Definitions").GetValue(null);var cues=(IDictionary)P(data,"Cues");var models=(IDictionary)P(data,"Models");
        using var zip=ZipFile.OpenRead(package);
        Test("obj-reader-contract",()=>{
            // Missing Model-vs-ObjModel dispatch caused silent invisible debris even with valid OBJ entries.
            var gets=CombatRegression.Calls(t.GetMethod("Draw")).OfType<MethodInfo>()
                .Where(m=>m.DeclaringType==typeof(ContentManager)&&m.Name=="Get"&&m.IsGenericMethod).ToArray();
            var reader=new Game.IContentReader.ObjModelReader();
            return gets.Any(m=>m.GetGenericArguments().Single().FullName==reader.Type)
                &&!gets.Any(m=>m.GetGenericArguments().Single()==typeof(Engine.Graphics.Model));
        });
        Test("no-added-impact-tink",()=>!CombatRegression.Calls(t.GetMethod("Update")).Any(m=>m.DeclaringType==typeof(SubsystemAudio)&&m.Name=="PlaySound"));
        Test("flight-and-ground-keep-identical-size",()=>{
            var d=Activator.CreateInstance(t.GetNestedType("Debris"));F(d,"Position",new Vector3(10,64,-5));F(d,"Spin",new Vector3(2,3,4));
            foreach(float distance in new[]{.3f,1f,2.5f,10f})foreach(float age in new[]{0f,.4f,1.8f})foreach(bool resting in new[]{false,true}) {
                F(d,"Age",age);F(d,"Resting",resting);Vector3 eye=new(10,64,-5-distance);Matrix m=(Matrix)t.GetMethod("DebrisTransform").Invoke(null,[d,eye]);
                if(Vector3.Distance(m.Translation,new Vector3(10,64,-5))>.0001f)return false;
                float scale=(float)t.GetMethod("DisplayScaleAtDistance").Invoke(null,[distance]);
                foreach(var axis in new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ})if(Math.Abs(Vector3.TransformNormal(axis,m).Length()-scale)>.0001f)return false;
            }return true;
        });
        Test("bounded-continuous-perspective-compensation",()=>{
            float S(float d)=>(float)t.GetMethod("DisplayScaleAtDistance").Invoke(null,[d]);float last=S(0);
            for(float d=.01f;d<30;d+=.01f){float s=S(d);if(s<.8f||s>2.4f||s<last||s-last>.02f)return false;last=s;}
            // At the measured M4 emitter depth versus a floor two metres away, apparent size
            // changes ~2.2x instead of >4.6x. Perspective remains; landing doesn't switch scale.
            float ratio=(S(.43f)/.43f)/(S(2f)/2f);return ratio>1.5f&&ratio<2.5f;
        });
        Test("floor-rests-horizontal-wall-does-not-freeze",()=>{
            var d=Activator.CreateInstance(t.GetNestedType("Debris"));F(d,"Spin",new Vector3(3,4,5));F(d,"Age",.5f);
            var contact=t.GetMethod("ResolveContact");
            for(int i=0;i<3;i++){F(d,"Velocity",new Vector3(-3,-2,0));contact.Invoke(null,[d,Vector3.Zero,Vector3.UnitX]);if((bool)d.GetType().GetField("Resting").GetValue(d))return false;}
            contact.Invoke(null,[d,Vector3.Zero,Vector3.UnitY]);if(!(bool)d.GetType().GetField("Resting").GetValue(d))return false;
            Matrix a=(Matrix)t.GetMethod("DebrisTransform").Invoke(null,[d,new Vector3(0,2,0)]);F(d,"Age",2f);
            Matrix b=(Matrix)t.GetMethod("DebrisTransform").Invoke(null,[d,new Vector3(0,2,0)]);
            return a==b&&Math.Abs(Vector3.TransformNormal(Vector3.UnitZ,a).Y)<.0001f&&a.Translation.Y>.02f;
        });
        Test("eight-authentic-models-33-guns",()=>models.Count==8&&cues.Keys.Cast<string>().Select(k=>k.Split(':')[0]).Distinct().Count()==33);
        foreach(DictionaryEntry entry in models)Test("mesh-and-texture/"+entry.Key,()=>{
            string mesh=(string)P(entry.Value,"Model"),texture=(string)P(entry.Value,"Texture");
            var obj=zip.GetEntry("Assets/Models/ScCsgoKnives/"+mesh+".obj");if(obj is null)return false;
            using var stream=new StreamReader(obj.Open());var lines=stream.ReadToEnd().Split('\n');
            var vertices=lines.Where(l=>l.StartsWith("v ")).Select(l=>l.Split(' ').Skip(1).Select(float.Parse).ToArray()).ToArray();
            return vertices.Length>30&&vertices.All(v=>v.All(float.IsFinite)&&v.All(x=>Math.Abs(x)<.15)&&Math.Abs(v[1]*2.4f)<.025f)&&lines.Count(l=>l.StartsWith("f "))>=40
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
                detail=$"view attachment metres: {world.Translation}";
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
