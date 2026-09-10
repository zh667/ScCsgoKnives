using System.Reflection;
using System.Text.Json;
using Engine;

static class GunDiagnosticsRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Func<bool> check) {try {results.Add(new("gun-diagnostics/"+name,check(),name));}catch(Exception e){results.Add(new("gun-diagnostics/"+name,false,e.ToString()));}}
        var diagType=mod.GetType("Game.ScGunDiagnostics");var contextType=diagType.GetNestedType("Context");
        object Create(string mode,List<string> lines,int budget=8*1024*1024)=>Activator.CreateInstance(diagType,[mode,(Action<string>)lines.Add,budget]);
        object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method).Invoke(target,args);
        void Set(object target,string field,object value)=>target.GetType().GetField(field).SetValue(target,value);
        object Context(double time,int pellets=1) {
            var c=Activator.CreateInstance(contextType);Set(c,"Gun","ak47");Set(c,"Preset","survival");Set(c,"Time",time);Set(c,"Pellets",pellets);
            Set(c,"Cone",1f);Set(c,"Range",48f);Set(c,"AmmoBefore",30);Set(c,"AmmoAfter",29);return c;
        }
        object Begin(object d,double time,int pellets=1)=>Call(d,"Begin",Context(time,pellets));
        void Pellet(object shot,int outcome,float distance=10)=>Call(shot,"Pellet",Vector3.UnitZ,Vector3.UnitZ,outcome,distance,false,true,.1d,outcome<=1?5d:0d);
        List<JsonElement> Records(List<string> lines,string kind)=>lines.Select(l=>JsonDocument.Parse(l["[GUN_DIAG] ".Length..]).RootElement.Clone()).Where(e=>e.GetProperty("type").GetString()==kind).ToList();
        Test("off-is-silent",()=>{var lines=new List<string>();var d=Create("off",lines);return Begin(d,0) is null && lines.Count==0;});
        Test("release-ignores-old-sampled-and-summary",()=> {
            foreach(string mode in new[]{"sampled","summary"}) {
                var d=Activator.CreateInstance(diagType,[mode,null,10000]);
                if((bool)diagType.GetProperty("Active").GetValue(d)||Begin(d,0) is not null)return false;
            }
            return true;
        });
        Test("release-has-no-f7-render-toggle",()=>mod.GetType("Game.CsmcFirstPersonRenderer")
            .GetMethod("PollDiagnosticKey",BindingFlags.NonPublic|BindingFlags.Static) is null);
        Test("old-debug-settings-cannot-enable-capture-or-shader-view",()=> {
            var tuning=mod.GetType("Game.KnifeTuning");var change=tuning.GetMethod("Override");
            change.Invoke(null,["PbrDebug",3f]);change.Invoke(null,["QaCapture",1f]);
            return (float)tuning.GetField("PbrDebug").GetValue(null)==0f
                && !(bool)mod.GetType("Game.KnifeQa").GetProperty("Armed").GetValue(null);
        });
        Test("samples-limited-but-summary-counts-all",()=> {
            var lines=new List<string>();var d=Create("sampled",lines);
            for(int i=0;i<100;i++){var s=Begin(d,i*.01);Pellet(s,i%4);Call(d,"Complete",s);Call(d,"Complete",s);}
            Call(d,"Tick",10d);var summary=Records(lines,"summary").Single();var outcomes=summary.GetProperty("outcomes");
            return Records(lines,"shot").Count==2 && summary.GetProperty("shots").GetInt64()==100 && summary.GetProperty("pellets").GetInt64()==100
                && summary.GetProperty("detailSuppressed").GetInt64()==98 && outcomes.GetProperty("head").GetInt64()==25 && outcomes.GetProperty("body").GetInt64()==25
                && outcomes.GetProperty("terrain").GetInt64()==25 && outcomes.GetProperty("rangeEnd").GetInt64()==25;
        });
        Test("shotgun-counts-shots-separately-from-pellets",()=> {
            var lines=new List<string>();var d=Create("summary",lines);
            for(int i=0;i<3;i++){var s=Begin(d,i,9);for(int p=0;p<9;p++)Pellet(s,p%4);Call(d,"Complete",s);}
            Call(d,"Flush","world_dispose");var summary=Records(lines,"summary").Single();
            return Records(lines,"shot").Count==0 && summary.GetProperty("shots").GetInt64()==3 && summary.GetProperty("pellets").GetInt64()==27
                && summary.GetProperty("checks").GetProperty("pelletMismatches").GetInt64()==0;
        });
        Test("geometry-is-not-damage",()=> {
            var lines=new List<string>();var d=Create("sampled",lines);var s=Begin(d,0);Pellet(s,1);Call(s,"Health",1f,1f);Call(d,"Complete",s);Call(d,"Flush","test");
            var summary=Records(lines,"summary").Single();
            return summary.GetProperty("shotsWithGeometryHit").GetInt64()==1 && summary.GetProperty("damagedTargets").GetInt64()==0 && summary.GetProperty("observedHealthLoss").GetDouble()==0;
        });
        Test("miss-has-null-hit-distance",()=> {
            var lines=new List<string>();var d=Create("sampled",lines);var s=Begin(d,0);Pellet(s,3,48);Call(d,"Complete",s);
            var shot=Records(lines,"shot").Single();return shot.GetProperty("geometryDistance").GetProperty("min").ValueKind==JsonValueKind.Null;
        });
        Test("violations-visible",()=> {
            var lines=new List<string>();var d=Create("summary",lines);var s=Begin(d,0);
            Call(s,"Pellet",Vector3.UnitZ,Vector3.Normalize(new Vector3(1,0,1)),3,50f,false,false,0d,0d);
            Call(d,"Complete",s);Call(d,"Flush","test");var checks=Records(lines,"summary").Single().GetProperty("checks");
            return checks.GetProperty("coneViolations").GetInt64()==1 && checks.GetProperty("rangeViolations").GetInt64()==1;
        });
        Test("sink-failure-does-not-throw",()=> {
            var d=Activator.CreateInstance(diagType,["sampled",(Action<string>)(_=>throw new InvalidOperationException("injected sink failure")),10000]);
            return !(bool)diagType.GetProperty("Active").GetValue(d) && Begin(d,0) is null;
        });
        Test("byte-budget-stops-emission",()=> {
            var lines=new List<string>();var d=Create("sampled",lines,900);
            for(int i=0;i<100;i++){var s=Begin(d,i);if(s is null)break;Pellet(s,1);Call(d,"Complete",s);}
            return !(bool)diagType.GetProperty("Active").GetValue(d) && Records(lines,"budget_exhausted").Count==1;
        });
        Test("old-config-defaults-to-off-without-rewrite",()=> {
            var type=mod.GetType("Game.ScGunplaySettings").GetNestedType("Settings",BindingFlags.NonPublic);
            var settings=JsonSerializer.Deserialize("{\"Version\":1,\"Preset\":\"survival\"}",type);
            return (string)type.GetProperty("Diagnostics").GetValue(settings)=="off";
        });
        Test("explanation-is-bit-identical-to-original-cone",()=> {
            var handling=mod.GetType("Game.ScGunHandling");var stance=mod.GetType("Game.ScGunStance");var all=(Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null);
            float F(object o,string p)=>(float)o.GetType().GetProperty(p).GetValue(o);
            foreach(var spec in all){string gun=(string)spec.GetType().GetField("Name").GetValue(spec);var s=Activator.CreateInstance(stance);
                var hip=handling.GetMethod("ForMode").Invoke(null,[gun,false]);var alt=handling.GetMethod("ForMode").Invoke(null,[gun,true]);
                Call(s,"Update",0d,true,false,true);Call(s,"Update",.073d,true,false,true);
                foreach(float speed in new[]{0f,.5f,2.1f,5f})foreach(float crouch in new[]{0f,.5f,1f})foreach(bool scope in new[]{false,true}) {
                    var mode=scope?alt:hip;float b=MathUtils.Lerp(F(mode,"BaseCone"),F(mode,"CrouchingCone"),crouch),move=F(mode,"MovingExtra"),air=F(mode,"JumpExtra");
                    if(scope){float h=MathUtils.Lerp(F(hip,"BaseCone"),F(hip,"CrouchingCone"),crouch);b=MathUtils.Lerp(h,b,F(s,"AimBlend"));move=MathUtils.Lerp(F(hip,"MovingExtra"),move,F(s,"AimBlend"));air=MathUtils.Lerp(F(hip,"JumpExtra"),air,F(s,"AimBlend"));}
                    float expected=b+move*Math.Clamp((speed-.5f)/4,0,1)+air*F(s,"LandingFactor")+.2f;
                    var parts=Call(s,"ExplainCone",mode,hip,scope,speed,crouch,false,.2f);
                    if(BitConverter.SingleToInt32Bits(expected)!=BitConverter.SingleToInt32Bits(F(parts,"Total")))return false;
                }
            }return true;
        });
        Test("observed-raycast-does-not-change-selection",()=> {
            var hitTest=mod.GetType("Game.ScGunHitTest");var trace=Activator.CreateInstance(hitTest.GetNestedType("Trace"));
            var empty=new List<Game.ComponentBody>();
            var a=hitTest.GetMethod("Raycast").Invoke(null,[empty,null,Vector3.Zero,Vector3.UnitZ,48f]);
            var b=hitTest.GetMethod("RaycastObserved").Invoke(null,[empty,null,Vector3.Zero,Vector3.UnitZ,48f,trace]);
            return a is null && b is null && (int)trace.GetType().GetField("Visited").GetValue(trace)==0;
        });
        return results;
    }
}
