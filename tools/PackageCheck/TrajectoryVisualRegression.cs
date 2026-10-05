using System.Collections;
using System.Reflection;
using Engine;
using Game;

// video-feedback-20260929 R4: the throw preview's drawing (orange ribbon, shaded end ball) from the packaged DLL.
// The prediction itself is covered by the step-identity regression; here it must come out of drawing untouched.
static class TrajectoryVisualRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod){
        List<Result> results=[];
        void Test(string name,Func<string> test){try{results.Add(new("trajectory-visual/"+name,true,test()));}catch(Exception e){results.Add(new("trajectory-visual/"+name,false,e.ToString()));}}
        void Require(bool ok,string why){if(!ok)throw new Exception(why);}
        var type=mod.GetType("Game.ScGrenadeTrajectory",true);var predict=type.GetMethod("Predict");var build=type.GetMethod("Build");
        // Floor at y=60 (up face), a wall at x=12 (face toward -X); no water.
        TerrainRaycastResult? Solid(Vector3 a,Vector3 b){
            TerrainRaycastResult? best=null;var d=b-a;float len=d.Length();if(len<1e-6f)return null;var dir=d/len;
            void Try(float t,int face){if(t>=0&&t<=1&&(!best.HasValue||t*len<best.Value.Distance))best=new TerrainRaycastResult{Ray=new Ray3(a,dir),Distance=t*len,CellFace=new CellFace(0,0,0,face)};}
            if(a.Y>=60&&b.Y<60)Try((a.Y-60)/(a.Y-b.Y),4);
            if(a.X<=12&&b.X>12)Try((12-a.X)/(b.X-a.X),3);
            return best;}
        object Path(int kind,Vector3 position,Vector3 velocity,Func<Vector3,bool> loaded=null)=>predict.Invoke(null,[kind,position,velocity,(Func<Vector3,Vector3,TerrainRaycastResult?>)Solid,(Func<Vector3,bool>)(_=>false),loaded??(_=>true)]);
        T Get<T>(object o,string n)=>(T)(o.GetType().GetField(n)?.GetValue(o)??o.GetType().GetProperty(n)?.GetValue(o));
        List<Vector3> Points(object path)=>Get<List<Vector3>>(path,"Points");
        (List<(Vector3 A,Vector3 B,Vector3 C,Color Ca,Color Cb,Color Cc)> Triangles,Vector3 Centre,float Radius,bool Hollow,float Width,float Outline) Geometry(object path,Vector3 eye,Vector3 forward,float height,float fov=1.1f){
            var g=build.Invoke(null,[path,eye,Vector3.Normalize(forward),Vector3.UnitY,1/MathF.Tan(fov/2),height]);
            var list=((IEnumerable)Get<object>(g,"Triangles")).Cast<object>().Select(t=>(Get<Vector3>(t,"A"),Get<Vector3>(t,"B"),Get<Vector3>(t,"C"),Get<Color>(t,"ColorA"),Get<Color>(t,"ColorB"),Get<Color>(t,"ColorC"))).ToList();
            var outline=g.GetType().GetField("OutlineWidthPixels");
            return(list,Get<Vector3>(g,"BallCentre"),Get<float>(g,"BallRadius"),Get<bool>(g,"Hollow"),Get<float>(g,"WidthPixels"),outline==null?float.NaN:(float)outline.GetValue(g));
        }
        // The orange core's centre line in drawing order: each segment is two triangles (a-s, a+s, b+s) and (a-s, b+s, b-s).
        List<Vector3> CoreLine(object path,Vector3 eye,Vector3 forward,float height){
            var core=Geometry(path,eye,forward,height).Triangles.Where(t=>t.Ca.A==235&&t.Ca.G>90&&t.Ca.R>200).ToList();var line=new List<Vector3>();
            for(int i=0;i+1<core.Count;i+=2){var a=(core[i].A+core[i].B)*.5f;var b=(core[i+1].B+core[i+1].C)*.5f;
                if(Vector3.Distance(core[i].A,core[i+1].A)>1e-5f)break; // past the ribbon: caps and the end marker
                if(line.Count==0||Vector3.Distance(line[^1],a)>1e-4f)line.Add(a);line.Add(b);}
            return line;
        }
        // Largest change of direction, in degrees, between consecutive screen-space pieces of a polyline seen from eye.
        (float Turn,int At) ScreenTurn(List<Vector3> line,Vector3 eye,Vector3 forward,float height,float fov,Func<int,bool> skip){
            forward=Vector3.Normalize(forward);var right=Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitY));var up=Vector3.Cross(right,forward);float scale=(1/MathF.Tan(fov/2))*height*.5f;
            var screen=line.Select(p=>{float d=Vector3.Dot(p-eye,forward);return new Vector2(Vector3.Dot(p-eye,right),Vector3.Dot(p-eye,up))*(scale/d);}).ToList();
            float worst=0;int at=-1;Vector2? previous=null;int previousEnd=-1;
            for(int i=0;i+1<screen.Count;i++){
                var d=screen[i+1]-screen[i];if(d.Length()<.75f)continue; // shorter than a pixel: no direction to speak of
                d=Vector2.Normalize(d);
                if(previous is {} p&&!skip(i)){float turn=MathF.Acos(Math.Clamp(Vector2.Dot(p,d),-1,1))*180/MathF.PI;if(turn>worst){worst=turn;at=i;}}
                previous=d;previousEnd=i+1;
            }
            return(worst,at);
        }
        float PixelsAt(Vector3 p,Vector3 eye,Vector3 forward,float height,float fov=1.1f)=>2*Vector3.Dot(p-eye,Vector3.Normalize(forward))/((1/MathF.Tan(fov/2))*height);
        var eye=new Vector3(0,61.6f,0);
        Test("drawing-never-changes-the-prediction",()=>{
            var parts=new List<string>();
            foreach(int kind in new[]{0,1,2,3,4,5})foreach(var velocity in new[]{new Vector3(9,4,1),new Vector3(16,6,0),new Vector3(3,-2,0)}){
                var path=Path(kind,new Vector3(0,61.5f,0),velocity);var before=Points(path).ToArray();var end=Get<Vector3>(path,"EndPoint");string kindName=Get<object>(path,"Kind").ToString();float time=Get<float>(path,"Time");
                foreach(float height in new[]{480f,1080,2160})Geometry(path,eye,new Vector3(1,-.1f,0),height);
                Require(before.SequenceEqual(Points(path))&&end==Get<Vector3>(path,"EndPoint")&&kindName==Get<object>(path,"Kind").ToString()&&time==Get<float>(path,"Time"),"drawing changed the predicted path");
                Require(before.Length<=97&&before[0]==new Vector3(0,61.5f,0)&&before[^1]==end,"path does not run from the launch point to the predicted end within 96 segments");
                parts.Add($"kind {kind} v {velocity}: {kindName} at {end} after {time:0.00}s, {before.Length} points");
            }
            return string.Join(" | ",parts);
        });
        Test("rebounds-are-kept-as-corners",()=>{
            var path=Path(0,new Vector3(0,61.5f,0),new Vector3(16,5,0));var points=Points(path);var corners=Get<List<int>>(path,"Corners");
            Require(Get<int>(path,"Bounces")>=1&&corners.Count>=1,"the wall rebound is not recorded");
            Require(corners.All(i=>i>0&&i<points.Count-1&&(Math.Abs(points[i].X-(12-.06f))<.02f||Math.Abs(points[i].Y-60.06f)<.02f)),"a corner is not the rebound point itself: "+string.Join(",",corners.Select(i=>points[i])));
            // No drawn segment passes beyond the wall plane or below the floor.
            Require(points.All(p=>p.X<=12.001f&&p.Y>=59.999f),"a drawn point lies inside the wall or the floor");
            return $"{corners.Count} corners of {points.Count} points";
        });
        Test("ribbon-width-constant-on-screen-and-scaled-with-the-view",()=>{
            // An arc that never touches anything (it bursts in the air): ribbon quads only, no corner caps.
            var path=Path(0,new Vector3(0,61.5f,0),new Vector3(6,12,1));var parts=new List<string>();var forward=new Vector3(1,.6f,.1f);
            Require(Get<List<int>>(path,"Corners").Count==0,"fixture: the arc rebounds");
            foreach(float height in new[]{360f,480,720,1080,1440,2160}){
                // r2-c4-completion-20260929: 2.5 px body at 1080p (1.25 px floor, 5 px ceiling), a separate outline of a fifth of it per side.
                float expected=Math.Clamp(2.5f*height/1080,1.25f,5),edge=Math.Max(.25f,.2f*expected);var g=Geometry(path,eye+new Vector3(-3,0,2),forward,height);
                Require(Math.Abs(g.Width-expected)<1e-3f,$"width {g.Width} at height {height}, expected {expected}");
                Require(Math.Abs(g.Outline-edge)<1e-3f,$"outline {g.Outline} px per side at height {height}, expected {edge}");
                var dark=g.Triangles.Where(t=>t.Ca.R==40&&t.Ca.G==14&&t.Ca.A==210&&Vector3.Distance(t.A,g.Centre)>g.Radius*2.5f).ToList();Require(dark.Count>=20,"outline not found");
                float outer=dark.Where((_,i)=>i%2==0).Take(60).Select(t=>Vector3.Distance(t.A,t.B)/PixelsAt((t.A+t.B)*.5f,eye+new Vector3(-3,0,2),forward,height)).Where(px=>px>.5f).Max();
                Require(Math.Abs(outer-(expected+2*edge))<.05f*(expected+2*edge),$"outline band {outer:0.00} px wide at height {height}, expected {expected+2*edge:0.00} (body plus two edges)");
                // The second half of the triangles is the orange core; its quads span the core width at both ends.
                var core=g.Triangles.Where(t=>t.Ca.G>90&&t.Ca.R>200&&t.Ca.B<80&&t.Ca.A==235&&Vector3.Distance(t.A,g.Centre)>g.Radius*2.5f).ToList();Require(core.Count>=20,"orange core not found");
                float smallest=float.MaxValue,largest=0;
                foreach(var t in core.Where((_,i)=>i%2==0).Take(60)){float px=Vector3.Distance(t.A,t.B)/PixelsAt((t.A+t.B)*.5f,eye+new Vector3(-3,0,2),forward,height);if(px>.5f){smallest=Math.Min(smallest,px);largest=Math.Max(largest,px);}}
                Require(smallest>=expected*.8f&&largest<=expected*1.25f,$"core width on screen {smallest:0.00}..{largest:0.00} px at height {height}, expected {expected}");
                parts.Add($"{height}: body {smallest:0.00}-{largest:0.00} px, with outline {outer:0.00} px");
            }
            return string.Join(", ",parts);
        });
        Test("end-marker-ball-ring-and-burst",()=>{
            var forward=new Vector3(1,-.1f,0);
            var landing=Path(3,new Vector3(0,61.5f,0),new Vector3(6,2,0));Require(Get<object>(landing,"Kind").ToString()=="Ignite"&&Get<bool>(landing,"Grounded"),"fixture: molotov did not land");
            var g=Geometry(landing,eye,forward,1080);var end=Get<Vector3>(landing,"EndPoint");
            Require(!g.Hollow&&g.Radius>=.15f&&g.Radius<=.45f,"landing ball radius "+g.Radius);
            Require(Math.Abs(g.Centre.X-end.X)<1e-5f&&Math.Abs(g.Centre.Z-end.Z)<1e-5f&&Math.Abs(g.Centre.Y-(end.Y+g.Radius-.06f))<1e-4f,"ball not sitting on the landing point: "+g.Centre+" / "+end);
            var lit=g.Triangles.Where(t=>Vector3.Distance(t.A,g.Centre)<=g.Radius*1.01f&&Vector3.Distance(t.B,g.Centre)<=g.Radius*1.01f&&t.Ca.A==245).ToList();
            Require(lit.Count>=60&&lit.Max(t=>(int)t.Ca.R+t.Ca.G)-lit.Min(t=>(int)t.Ca.R+t.Ca.G)>=90,"the ball has no light-to-dark shading");
            // Far away the ball keeps a minimum size on screen.
            var far=Geometry(landing,end+new Vector3(-70,6,0),new Vector3(1,-.08f,0),1080);
            Require(far.Radius/PixelsAt(far.Centre,end+new Vector3(-70,6,0),new Vector3(1,-.08f,0),1080)>=6.9f||far.Radius>=.449f,"distant ball smaller than seven pixels although below its largest size");
            var air=Path(0,new Vector3(0,61.5f,0),new Vector3(4,14,0));Require(Get<object>(air,"Kind").ToString()=="Detonate"&&!Get<bool>(air,"Grounded"),"fixture: HE did not burst in the air");
            var burst=Geometry(air,eye,new Vector3(.3f,1,0),1080);
            Require(!burst.Hollow&&burst.Centre==Get<Vector3>(air,"EndPoint"),"air burst marker not centred on the burst point");
            int ground=g.Triangles.Count(t=>Vector3.Distance((t.A+t.B+t.C)/3,g.Centre)>g.Radius*1.3f&&Vector3.Distance((t.A+t.B+t.C)/3,g.Centre)<g.Radius*2.3f&&t.Ca.R>200&&t.Ca.G>90);
            int rays=burst.Triangles.Count(t=>Vector3.Distance((t.A+t.B+t.C)/3,burst.Centre)>burst.Radius*1.3f&&Vector3.Distance((t.A+t.B+t.C)/3,burst.Centre)<burst.Radius*2.3f&&t.Ca.R>200&&t.Ca.G>90);
            Require(rays>=16&&rays>ground,"air burst not told apart from a landing point");
            var cut=Path(0,new Vector3(0,61.5f,0),new Vector3(6,5,0),q=>q.X<3);Require(Get<object>(cut,"Kind").ToString()=="Truncated","fixture: path not cut at unloaded terrain");
            var unknown=Geometry(cut,eye,forward,1080);
            Require(unknown.Hollow&&!unknown.Triangles.Any(t=>t.Ca.A==245),"a cut-short path shows a solid landing ball");
            Require(unknown.Triangles.Where(t=>t.Ca.R>150).Min(t=>t.Cb.A)<80,"the tail of a cut-short path does not fade");
            foreach(var set in new[]{g,far,burst,unknown})Require(set.Triangles.Count<=1200&&set.Triangles.All(t=>float.IsFinite(t.A.X+t.A.Y+t.A.Z+t.B.X+t.B.Y+t.B.Z+t.C.X+t.C.Y+t.C.Z)),"geometry unbounded or not finite: "+set.Triangles.Count);
            return $"landing {g.Triangles.Count} triangles, far {far.Triangles.Count}, air burst {burst.Triangles.Count}, cut {unknown.Triangles.Count}";
        });
        Test("own-eye-ribbon-leaves-from-the-hand-and-joins-the-true-path",()=>{
            var launch=new Vector3(0,61.5f,0);var path=Path(0,launch,new Vector3(9,4,1));var points=Points(path);var forward=Vector3.Normalize(new Vector3(9,4,1));
            var own=build.Invoke(null,[path,launch,forward,Vector3.UnitY,1/MathF.Tan(.55f),1080f]);var other=build.Invoke(null,[path,launch+new Vector3(-4,1,3),forward,Vector3.UnitY,1/MathF.Tan(.55f),1080f]);
            Require(Get<bool>(own,"FromHand")&&!Get<bool>(other,"FromHand"),"hand start not limited to the thrower's own eye");
            Require(Get<Vector3>(own,"BallCentre")==Get<Vector3>(other,"BallCentre")&&Points(path).SequenceEqual(points),"the end marker or the prediction moved with the hand start");
            // Beyond the join distance every ribbon vertex pair is centred on a true path point; near the start it is to the right and below.
            var ribbon=((IEnumerable)Get<object>(own,"Triangles")).Cast<object>().Select(t=>(A:Get<Vector3>(t,"A"),B:Get<Vector3>(t,"B"),Color:Get<Color>(t,"ColorA"))).Where(t=>t.Color.A==235&&t.Color.G>90).ToList();
            var right=Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitY));
            var centres=ribbon.Select(t=>(t.A+t.B)*.5f).ToList();
            var near=centres.Where(c=>Vector3.Distance(c,launch)<1.2f).ToList();Require(near.Count>0&&near.All(c=>Vector3.Dot(c-launch,right)>.1f),"the ribbon does not leave from the right hand side");
            float total=0;for(int i=1;i<points.Count;i++)total+=Vector3.Distance(points[i-1],points[i]);float join=Math.Clamp(.6f*total,3,16);
            float length=0;var distant=new List<Vector3>();for(int i=1;i<points.Count-1;i++){length+=Vector3.Distance(points[i-1],points[i]);if(length>join+.2f&&Vector3.Distance(points[i],points[^1])>1.2f)distant.Add(points[i]);}
            Require(distant.Count>5&&distant.All(p=>centres.Any(c=>Vector3.Distance(c,p)<.02f)),$"the ribbon does not run on the true path beyond the join distance {join:0.0}");
            return $"{near.Count} near vertices from the hand, join {join:0.0} of {total:0.0} blocks, {distant.Count} true points checked";
        });
        Test("own-eye-hand-join-has-no-false-corner-and-rebounds-stay-sharp",()=>{
            // Seen from the thrower's eye the true path starts at the launch direction's vanishing point and then falls. The
            // hand-side start must flow into it: the r2-06 ribbon joined within 3 blocks and turned by about 135 degrees there.
            var parts=new List<string>();const float fov=1.1f;
            foreach(var (velocity,look) in new[]{(new Vector3(18,7,0),new Vector3(1,.35f,0)),(new Vector3(9,4,1),new Vector3(9,4,1)),(new Vector3(8,1.5f,0),new Vector3(1,0,0)),(new Vector3(4,14,0),new Vector3(.3f,1,0))}){
                var launch=new Vector3(0,61.5f,0);var path=Path(0,launch,velocity);var corners=Get<List<int>>(path,"Corners");var points=Points(path);
                var line=CoreLine(path,launch,look,1080);Require(line.Count>=10,"fixture: no ribbon");
                // Real rebounds keep their corner; only their neighbourhood is excused from the smoothness check.
                var bounce=corners.Select(i=>points[i]).ToList();
                bool NearBounce(int i)=>bounce.Any(b=>Vector3.Distance(line[Math.Min(i,line.Count-1)],b)<.6f||Vector3.Distance(line[Math.Min(i+1,line.Count-1)],b)<.6f);
                // The hand offset has faded by the first rebound, so from there on the ribbon is the true path (checked
                // above). Seen edge-on from the eye, a path bouncing back toward the thrower folds over itself on screen:
                // that fold is the real path, not a join corner (c02 found it 1.3 blocks from the x=12 wall).
                int stop=bounce.Count==0?line.Count-1:line.FindIndex(p=>Vector3.Distance(p,bounce[0])<1e-3f);
                Require(stop>0,$"launch {velocity}: the first rebound {(bounce.Count>0?bounce[0]:default)} is not on the drawn ribbon; the hand offset has not faded there");
                // The ribbon ends at the predicted end point; beyond it the extractor would read the end marker (an
                // airburst's starburst is drawn in the core colour, c03: a 138-degree "turn" two pieces from the end).
                var endPoint=Get<Vector3>(path,"EndPoint");int end=line.FindIndex(p=>Vector3.Distance(p,endPoint)<1e-3f);
                if(end>0)stop=Math.Min(stop,end);
                var (turn,at)=ScreenTurn(line.Take(stop+1).ToList(),launch,look,1080,fov,NearBounce);
                Require(turn<=30,$"launch {velocity}: the drawn ribbon turns by {turn:0} degrees on screen at piece {at} of {stop} before the first rebound (near {line[Math.Max(0,at)]})");
                parts.Add($"v {velocity}: largest turn {turn:0.0} deg over the {stop} pieces before the first rebound, {corners.Count} rebounds");
            }
            // A rebound seen from the side still shows as a corner at the true rebound point.
            var wall=Path(0,new Vector3(0,61.5f,0),new Vector3(16,5,0));var wallPoints=Points(wall);var wallCorner=wallPoints[Get<List<int>>(wall,"Corners")[0]];
            var side=new Vector3(6,62,-14);var sideLine=CoreLine(wall,side,new Vector3(0,0,1),1080);
            int k=sideLine.FindIndex(p=>Vector3.Distance(p,wallCorner)<1e-3f);Require(k>0&&k+1<sideLine.Count,"the rebound point is not on the drawn ribbon");
            var before=Vector3.Normalize(sideLine[k]-sideLine[k-1]);var after=Vector3.Normalize(sideLine[k+1]-sideLine[k]);float bend=MathF.Acos(Math.Clamp(Vector3.Dot(before,after),-1,1))*180/MathF.PI;
            Require(bend>=60,$"the wall rebound is drawn with only {bend:0} degrees of turn");
            parts.Add($"wall rebound drawn as a {bend:0}-degree corner");
            return string.Join("; ",parts);
        });
        Test("behind-and-through-the-camera",()=>{
            var path=Path(0,new Vector3(0,61.5f,0),new Vector3(9,4,1));
            var behind=Geometry(path,new Vector3(40,62,0),new Vector3(1,0,0),1080);Require(behind.Triangles.Count==0,"a path entirely behind the camera is drawn");
            var through=Geometry(path,new Vector3(4,62.5f,.4f),new Vector3(1,-.1f,0),1080);
            Require(through.Triangles.All(t=>new[]{t.A,t.B,t.C}.All(p=>Vector3.Dot(p-new Vector3(4,62.5f,.4f),Vector3.Normalize(new Vector3(1,-.1f,0)))>.05f)),"geometry crosses the near plane");
            return $"{through.Triangles.Count} triangles in front of the camera";
        });
        return results;
    }
}
