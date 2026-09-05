using Engine;
namespace Game;

public static class ScPolishSelfTest {
    public static void Run(Action<string,bool,string> check) {
        void Test(string name,Func<bool> test) {try {check("polish/"+name,test(),name);}catch(Exception e){check("polish/"+name,false,e.ToString());}}
        foreach(bool shell in new[]{false,true}) Test("creative-cost/"+shell,()=>
            ScReloadTransaction.CostMessage(true,shell,0)==ScReloadTransaction.CostMessage(true,shell,5)
            && ScReloadTransaction.CostMessage(true,shell,0).Contains("无需消耗")
            && ScReloadTransaction.CostMessage(false,shell,5).EndsWith("×5"));
        for(int k=0;k<7;k++) {
            int kind=k;
            Test("closed-volume/"+kind,()=>{
                var mesh=ScSurvivalMesh.Build(kind);Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
                foreach(var v in mesh.Vertices) {
                    if(!ScGrenadeState.Finite(v.Position) || v.TextureCoordinates.X is <0 or >1 || v.TextureCoordinates.Y is <0 or >1) return false;
                    lo=Vector3.Min(lo,v.Position);hi=Vector3.Max(hi,v.Position);
                }
                Vector3 extent=hi-lo;
                return extent.X>.1f && extent.Y>.1f && extent.Z>.1f && mesh.Vertices.Count<6000
                    && mesh.Vertices.Select(v=>v.Face).Distinct().Count()==6
                    && mesh.Indices.Count%3==0 && mesh.Indices.All(i=>i<mesh.Vertices.Count);
            });
        }
        bool Valid(List<ScGrenadeVisuals.Sprite> sprites,int limit) => sprites.Count<=limit && sprites.All(p=>
            ScGrenadeState.Finite(p.Position) && float.IsFinite(p.Width) && float.IsFinite(p.Height)
            && p.Width>0 && p.Height>0 && p.Texture is >=0 and <4 && p.Frame is >=0 and <16);
        foreach(float distance in new[]{0f,22f,45f}) {
            Test("effect-budget/"+distance,()=> {
                var fire=new ScGrenadeState {Kind=4,Effect=true,Position=Vector3.Zero};
                var smoke=new ScGrenadeState {Kind=2,Effect=true,Position=Vector3.Zero};
                var points=new List<Vector3>();
                for(float x=-2.6f;x<3;x+=.8f)for(float z=-2.6f;z<3;z+=.8f)if(x*x+z*z<=9)points.Add(new(x,0,z));
                for(float t=0;t<=15;t+=1f/30) {
                    smoke.Age=t;smoke.Remaining=Math.Max(0,15-t);
                    fire.Age=t;fire.Remaining=Math.Max(0,7-t);
                    if(!Valid(ScGrenadeVisuals.Burst(Vector3.Zero,t,false,false,distance),32)
                        || !Valid(ScGrenadeVisuals.Burst(Vector3.Zero,t,true,false,distance),2)
                        || !Valid(ScGrenadeVisuals.Smoke(smoke,distance),48)
                        || !Valid(ScGrenadeVisuals.Fire(fire,points,distance),120))return false;
                }
                return true;
            });
        }
        Test("flash-accessibility",()=>ScGrenadeVisuals.Burst(Vector3.Zero,.1f,true,true,0).Sum(p=>p.Color.A)
            <ScGrenadeVisuals.Burst(Vector3.Zero,.1f,true,false,0).Sum(p=>p.Color.A)*.2f);
        Test("blast-expires",()=>ScGrenadeVisuals.Burst(Vector3.Zero,1.26f,false,false,0).Count==0);
        Test("smoke-boundary-fade",()=>ScGrenadeVisuals.Smoke(new(){Kind=2,Effect=true,Age=0,Remaining=15},0).Count==0
            && ScGrenadeVisuals.Smoke(new(){Kind=2,Effect=true,Age=15,Remaining=0},0).Count==0);
        Test("smoke-distance-budget",()=>ScGrenadeVisuals.Smoke(new(){Kind=2,Effect=true,Age=2,Remaining=13},50).Count==12);
    }
}
