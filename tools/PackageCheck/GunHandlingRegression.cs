using System.Reflection;
using Engine;
using Game;

static class GunHandlingRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Body : ComponentBody {
        public BoundingBox Box;
        public override BoundingBox BoundingBox=>Box;
    }
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Func<bool> test) { try { results.Add(new("gun-handling/"+name,test(),name)); } catch(Exception e) {results.Add(new("gun-handling/"+name,false,e.ToString()));} }
        object Call(string type,string method,params object[] args)=>mod.GetType("Game."+type).GetMethod(method).Invoke(null,args);
        object Prop(object o,string name)=>o.GetType().GetProperty(name).GetValue(o);
        float Number(object o,string name)=>Convert.ToSingle(Prop(o,name));
        var specs=((Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null)).Cast<object>().ToArray();
        var enabled=mod.GetType("Game.ScGunplaySettings").GetField("Enabled");var old=enabled.GetValue(null);
        try {
            enabled.SetValue(null,true);
            Test("35-approved-records",()=>specs.Length==35 && specs.All(s=>Call("ScGunHandling","For",s.GetType().GetField("Name").GetValue(s)) is not null));
            foreach(var spec in specs) {
                string name=(string)spec.GetType().GetField("Name").GetValue(spec);
                var gun=Call("ScGunHandling","For",name);
                Test(name+"/range-and-falloff",()=> {
                    float max=Number(gun,"Range"),start=Number(gun,"FalloffStart"),floor=Number(gun,"FalloffFloor");
                    float F(float d)=>(float)Call("ScGunHandling","Falloff",gun,d);
                    if(F(0)!=1 || F(start)!=1 || Math.Abs(F(max)-floor)>.0001 || F(max+.01f)!=0) return false;
                    float last=1;
                    for(int i=0;i<=100;i++) {float f=F(max*i/100); if(f>last+.00001 || f<0 || f>1)return false; last=f;}
                    return true;
                });
                foreach(bool alternate in new[]{false,true}) {
                    var mode=Call("ScGunHandling","ForMode",name,alternate);
                    Test(name+"/cone1000/"+alternate,()=> {
                        float degrees=Number(mode,"BaseCone");var random=new System.Random(437);
                        for(int i=0;i<1000;i++) {
                            var d=(Vector3)Call("ScGunHandling","Scatter",-Vector3.UnitZ,degrees,(float)random.NextDouble(),(float)random.NextDouble());
                            if(!float.IsFinite(d.X+d.Y+d.Z) || Math.Abs(d.Length()-1)>.00001) return false;
                            double a=Math.Acos(Math.Clamp(-d.Z,-1,1))*180/Math.PI;
                            if(a>degrees+.002) return false;
                        }
                        return true;
                    });
                    Test(name+"/bloom-cap-and-recovery/"+alternate,()=> {
                        var type=mod.GetType("Game.ScGunBloom");var bloom=Activator.CreateInstance(type);
                        for(int i=0;i<100;i++) type.GetMethod("Fired").Invoke(bloom,[mode,0d]);
                        float peak=(float)type.GetProperty("Value").GetValue(bloom);
                        if(peak>Number(mode,"BloomMax")+.00001) return false;
                        if(Number(mode,"BloomMax")==0)return peak==0;
                        if((float)type.GetMethod("At").Invoke(bloom,[.1d])!=peak)return false;
                        double end=.12+Number(mode,"BloomRecoverySeconds")+.001;
                        return (float)type.GetMethod("At").Invoke(bloom,[end])==0;
                    });
                }
            }
            Test("mode-selection",()=> {
                bool A(string name,bool scope,bool sil,bool burst,bool alt)=> (bool)Call("ScGunHandling","Alternate",Call("GunSpec","ForAsset",name),scope,sil,burst,alt);
                return A("glock18",false,false,true,false) && A("famas",false,false,true,false)
                    && A("awp",true,false,false,false) && A("m4a1s",false,true,false,false) && A("revolver",false,false,false,true)
                    && !A("ak47",true,true,true,true) && !A("m4a4",false,false,true,true);
            });
            Test("movement-deadzone-and-cap",()=> (float)Call("ScGunHandling","MoveFactor",.5f)==0 && (float)Call("ScGunHandling","MoveFactor",4.5f)==1
                && (float)Call("ScGunHandling","MoveFactor",2.5f)==.5f && (float)Call("ScGunHandling","MoveFactor",15f)==1);
            Test("stance-jump-landing-aim",()=> {
                var t=mod.GetType("Game.ScGunStance");var s=Activator.CreateInstance(t);
                void U(double now,bool ground,bool jump,bool scope)=>t.GetMethod("Update").Invoke(s,[now,ground,jump,scope]);
                U(0,true,false,false);U(.01,false,false,false);
                if((bool)Prop(s,"Airborne"))return false;
                U(.12,false,false,false);if(!(bool)Prop(s,"Airborne"))return false;
                U(.2,true,false,true);if((bool)Prop(s,"Airborne") || Math.Abs(Number(s,"LandingFactor")-.4f)>.001)return false;
                U(.275,true,false,true);if(Math.Abs(Number(s,"AimBlend")-.5)>.001)return false;
                U(.4,true,false,true);return Number(s,"AimBlend")==1 && Number(s,"LandingFactor")==0;
            });
            Test("bloom-frame-invariant",()=> {
                var mode=Call("ScGunHandling","ForMode","ak47",false);var t=mod.GetType("Game.ScGunBloom");
                object A()=>Activator.CreateInstance(t);var a=A();var b=A();
                for(int i=0;i<20;i++){t.GetMethod("Fired").Invoke(a,[mode,0d]);t.GetMethod("Fired").Invoke(b,[mode,0d]);}
                for(int i=1;i<=20;i++) t.GetMethod("At").Invoke(a,[i*.01]);
                t.GetMethod("At").Invoke(b,[.2d]);
                return Math.Abs(Number(a,"Value")-Number(b,"Value"))<.00001;
            });
            Test("approved-key-values",()=>Number(Call("ScGunHandling","ForMode","awp",false),"BaseCone")==3.01f
                && Number(Call("ScGunHandling","ForMode","awp",true),"BaseCone")==.11f
                && Number(Call("ScGunHandling","For","ak47"),"Range")==48
                && Number(Call("ScGunHandling","For","sawedoff"),"Range")==18);
            Test("body-padding-bounded",()=> (float)Call("ScGunHitTest","Tolerance",new BoundingBox(Vector3.Zero,new Vector3(.2f,1,.2f)))<=.02001
                && (float)Call("ScGunHitTest","Tolerance",new BoundingBox(Vector3.Zero,new Vector3(10)))==.08f);
            Test("precise-miss-not-head-or-body",()=> {
                var part=mod.GetType("Game.ScPartBox");var a=Array.CreateInstance(part,1);
                a.SetValue(Activator.CreateInstance(part,[new BoundingBox(new Vector3(-.1f,-.1f,4),new Vector3(.1f,.1f,5)),Matrix.Identity,true]),0);
                var hit=Call("ScGunHitTest","Precise",a,new Vector3(.15f,0,0),Vector3.UnitZ,20f,.08f);
                return (int)hit.GetType().GetField("Item1").GetValue(hit)==0; // head never expands
            });
            Test("logical-body-fallback-and-wall",()=> {
                var bodies=new List<ComponentBody>{new Body{Box=new(new Vector3(-.3f,0,4),new Vector3(.3f,1.8f,5))}};
                var ray=new Vector3(0,.9f,0);
                var accepted=Call("ScGunHitTest","Raycast",bodies,null,ray,Vector3.UnitZ,10f);
                var blocked=Call("ScGunHitTest","Raycast",bodies,null,ray,Vector3.UnitZ,3f);
                var miss=Call("ScGunHitTest","Raycast",bodies,null,new Vector3(.5f,.9f,0),Vector3.UnitZ,10f);
                return accepted is not null && blocked is null && miss is null;
            });
            Test("front-broad-miss-does-not-hide-back-target",()=> {
                var a=new Body{Box=new(new Vector3(-.4f,0,2),new Vector3(.4f,2,3))};
                var b=new Body{Box=new(new Vector3(-.4f,0,5),new Vector3(.4f,2,6))};
                var partType=mod.GetType("Game.ScPartBox");var arrayType=partType.MakeArrayType();
                Array Parts(BoundingBox box) {
                    var parts=Array.CreateInstance(partType,1);
                    parts.SetValue(Activator.CreateInstance(partType,[box,Matrix.Identity,false]),0);return parts;
                }
                var front=Parts(new BoundingBox(new Vector3(.25f,0,2),new Vector3(.4f,2,3)));
                var back=Parts(b.Box);
                var parameter=System.Linq.Expressions.Expression.Parameter(typeof(ComponentBody));
                var condition=System.Linq.Expressions.Expression.Condition(
                    System.Linq.Expressions.Expression.ReferenceEqual(parameter,System.Linq.Expressions.Expression.Constant(a,typeof(ComponentBody))),
                    System.Linq.Expressions.Expression.Constant(front,arrayType),System.Linq.Expressions.Expression.Constant(back,arrayType));
                var field=mod.GetType("Game.ScGunHitTest").GetField("PoseProvider");var saved=field.GetValue(null);
                var provider=System.Linq.Expressions.Expression.Lambda(field.FieldType,condition,parameter).Compile();
                try {
                    field.SetValue(null,provider);
                    var hit=Call("ScGunHitTest","Raycast",new List<ComponentBody>{a,b},null,new Vector3(0,1,0),Vector3.UnitZ,10f);
                    return hit is not null && ReferenceEquals(Prop(hit,"Body"),b) && Math.Abs(Number(hit,"Distance")-5)<.001;
                } finally {field.SetValue(null,saved);}
            });
            Test("scope-does-not-clear-bloom-or-move",()=> {
                var t=mod.GetType("Game.ScGunStance");var state=Activator.CreateInstance(t);
                t.GetMethod("Update").Invoke(state,[0d,true,false,true]);t.GetMethod("Update").Invoke(state,[.2d,true,false,true]);
                var scope=Call("ScGunHandling","ForMode","awp",true);var hip=Call("ScGunHandling","ForMode","awp",false);
                float c=(float)t.GetMethod("Cone").Invoke(state,[scope,hip,true,4.5f,0f,false,.5f]);
                return Math.Abs(c-(Number(scope,"BaseCone")+Number(scope,"MovingExtra")+.5f))<.0001;
            });
            Test("effective-stats-preserve-stored-state",()=> {
                var spec=Call("GunSpec","ForAsset","ak47");
                int data=(int)Call("GunSpec","MakeData",0,7,false);
                int value=Terrain.MakeBlockValue(701,0,data);
                int before=(int)Call("GunSpec","GetDurability",data);
                var stats=Call("EffectiveGunStats","Resolve",spec,value,false);
                if(Number(stats,"Power")!=15 || Number(stats,"Range")!=48 || (int)Prop(stats,"Capacity")!=30)return false;
                return (int)Call("GunSpec","GetRounds",data)==7 && (int)Call("GunSpec","GetDurability",data)==before;
            });
        } finally {enabled.SetValue(null,old);}
        return results;
    }
}
