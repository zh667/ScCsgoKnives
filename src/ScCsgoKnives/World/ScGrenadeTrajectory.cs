using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Preview of the local player's own throw while preparing. It runs <see cref="ScGrenadeBallistics.Launch"/>
/// and <see cref="ScGrenadeBallistics.Integrate"/> on a copy with the same 0.02 s sub-steps as the real grenade, so it
/// never plays sounds, consumes items, spawns effects or detonates anything. Terrain and water only: creatures move,
/// so the path after a possible body contact is a prediction, not a guarantee. Bounded: 600 steps (12 s), 3 visible
/// rebounds (impacts of at least <see cref="BounceSpeed"/>; the small hops of a grenade settling on the floor do not count,
/// so smoke/decoy still show their landing point), 96 drawn points; unloaded terrain ends the path early instead of
/// inventing a landing point. The drawing (R4) is separate from the prediction and never changes it.</summary>
public static class ScGrenadeTrajectory {
    public const int MaxSteps=600,MaxBounces=3,MaxPoints=96;
    public const float SubStep=.02f,BounceSpeed=2;
    public enum End { Detonate, Ignite, AirBurst, Settle, Water, Truncated }
    public sealed class Path {
        public readonly List<Vector3> Points=[];
        /// <summary>Indices into <see cref="Points"/> where the path rebounds: kept by the decimation and drawn as corners.</summary>
        public readonly List<int> Corners=[];
        public Vector3 EndPoint;public End Kind;public float Time;public int Bounces;
        /// <summary>The path ends resting on a surface (a landing point), not in the air.</summary>
        public bool Grounded;
    }
    /// <summary>Pure simulation from a launch state; queries are supplied by the caller (no live objects touched).</summary>
    public static Path Predict(int kind,Vector3 position,Vector3 velocity,Func<Vector3,Vector3,TerrainRaycastResult?> solid,Func<Vector3,bool> water,Func<Vector3,bool> loaded) {
        var s=new ScGrenadeState{Kind=kind,Position=position,Velocity=velocity,Remaining=ScGrenadeBallistics.Fuse(kind)};
        var path=new Path();var raw=new List<Vector3>{position};var rebounds=new List<int>();
        for(int i=0;i<MaxSteps;i++){
            s.Age+=SubStep;
            if(!loaded(s.Position)){path.Kind=End.Truncated;break;}
            float impact=s.Velocity.Length();
            var e=ScGrenadeBallistics.Integrate(s,SubStep,solid,water,null,out _);
            raw.Add(s.Position);
            if(e==ScGrenadeBallistics.StepEvent.Surface&&impact>=BounceSpeed){
                if(rebounds.Count<MaxBounces+1)rebounds.Add(raw.Count-1);
                if(++path.Bounces>MaxBounces&&!s.Grounded){path.Kind=End.Truncated;break;}
            }
            if(kind is 3 or 4){
                if(water(s.Position)){path.Kind=End.Water;break;}
                if(s.Grounded){path.Kind=End.Ignite;break;}
            }
            s.Remaining-=SubStep;
            if(s.Remaining<=0){
                if(kind is 3 or 4){path.Kind=End.AirBurst;break;}
                if(kind is 2 or 5){if(ScGrenadeBallistics.Settled(s)){path.Kind=End.Settle;break;}if(s.Age>=ScGrenadeBallistics.SettleTimeout){path.Kind=End.Truncated;break;}continue;}
                path.Kind=End.Detonate;break;
            }
            if(i==MaxSteps-1)path.Kind=End.Truncated;
        }
        path.EndPoint=s.Position;path.Time=s.Age;path.Grounded=s.Grounded;
        // Decimate evenly, always keeping the first point, the last point and every rebound: a drawn segment never
        // cuts a corner through the wall or floor the grenade bounced off.
        int stride=Math.Max(1,(raw.Count+MaxPoints-2)/(MaxPoints-1)),next=0;
        for(int i=0;i<raw.Count;i++){
            bool corner=next<rebounds.Count&&rebounds[next]==i;
            if(!corner&&i%stride!=0&&i!=raw.Count-1)continue;
            if(corner){next++;if(i!=raw.Count-1)path.Corners.Add(path.Points.Count);}
            path.Points.Add(raw[i]);
        }
        return path;
    }

    // ---- presentation (video-feedback-20260929 R4): an orange ribbon and a small shaded ball ----
    /// <summary>Width in pixels of the orange body on a 1080-pixel-high view, scaled with the view and kept between the
    /// limits (r2-c4-completion-20260929: 2.5 px, 1.25 px at 360p, 5 px at 2160p; the first 4 px read as too thick).
    /// The dark outline is separate: OutlineShare of the body width on each side, never under MinOutline, so a thinner
    /// body never keeps a thick edge.</summary>
    public const float WidthAt1080=2.5f,MinWidth=1.25f,MaxWidth=5,OutlineShare=.2f,MinOutline=.25f;
    public static float BodyPixels(float viewportHeight)=>Math.Clamp(WidthAt1080*viewportHeight/1080,MinWidth,MaxWidth);
    public static float OutlinePixels(float viewportHeight)=>Math.Max(MinOutline,OutlineShare*BodyPixels(viewportHeight));
    /// <summary>End marker: world radius, never smaller on screen than MinBallPixels (7 at 1080, at least 5), never larger than MaxBallRadius.</summary>
    public const float BallRadius=.15f,MaxBallRadius=.45f,MinBallPixels=5,BallPixelsAt1080=7;
    /// <summary>Seen from the thrower's own eye the true path starts in the eye and would cover itself as one vertical
    /// line. The drawn ribbon therefore leaves from the throwing hand (right of and below the eye) and joins the true
    /// path gradually: the offset fades over HandJoinShare of the path (between MinHandJoin and MaxHandJoin blocks).
    /// Seen from the eye the offset already shrinks with depth; fading it within the first 3 blocks (r2-06) pulled the
    /// ribbon through the launch direction's vanishing point and drew a sharp false corner where the path starts to
    /// fall. Presentation only: the predicted points, rebounds (still sharp) and end point are untouched, and any other
    /// camera (third person, another viewer) gets the true path from its first point.</summary>
    public const float HandRight=.24f,HandDown=.2f,HandJoinShare=.6f,MinHandJoin=3,MaxHandJoin=16,OwnEyeDistance=.5f;
    /// <summary>The hand offset as drawn (presentation only, current-direction-20260929 §5 A/B): right/down in blocks and the
    /// length over which it fades (share of the path, clamped). <see cref="Current"/> is the delivered look; the offline
    /// renders compare it with no offset and with a short join, from the same predicted path. Never changes the path.</summary>
    public readonly record struct HandPresentation(float Right,float Down,float JoinShare,float MinJoin,float MaxJoin) {
        public static readonly HandPresentation Current=new(HandRight,HandDown,HandJoinShare,MinHandJoin,MaxHandJoin);
        public static readonly HandPresentation None=new(0,0,HandJoinShare,MinHandJoin,MaxHandJoin);
    }
    public static HandPresentation Presentation=HandPresentation.Current;
    const float NearDepth=.2f;
    static readonly Color Core=new(255,122,32,235),Edge=new(40,14,0,210),Uncertain=new(205,205,205,150),UncertainEdge=new(30,30,30,150),FeatherColor=new(40,14,0,120);
    /// <summary>Width in pixels of the transparent feather outside the dark edge (anti-aliasing without multisampling).</summary>
    public const float Feather=1;
    public readonly record struct Triangle(Vector3 A,Vector3 B,Vector3 C,Color ColorA,Color ColorB,Color ColorC);
    public sealed class Geometry {
        public readonly List<Triangle> Triangles=[];
        /// <summary>Drawn centre and radius of the end marker; the predicted <see cref="Path.EndPoint"/> is never changed.</summary>
        public Vector3 BallCentre;public float BallRadius;public bool Hollow;public float WidthPixels,OutlineWidthPixels;
        /// <summary>The ribbon leaves from the throwing hand (the viewer is the thrower, in first person).</summary>
        public bool FromHand;
    }
    /// <summary>Camera-facing geometry of a cached path: a dark-edged orange ribbon whose width is constant on
    /// screen, square-ended at rebounds (no smoothing across a wall), and an end marker - a shaded ball where the
    /// grenade lands or bursts (with a starburst when that happens in the air), a grey hollow ring and a fading tail
    /// when the prediction was cut short and the real end is unknown. No queries, bounded by the 96 path points.</summary>
    public static Geometry Build(Path path,Vector3 eye,Vector3 forward,Vector3 up,float projectionScale,float viewportHeight) {
        var g=new Geometry();
        if(path is null||path.Points.Count<2||!(projectionScale>0)||!(viewportHeight>0))return g;
        float pixels=BodyPixels(viewportHeight),outline=OutlinePixels(viewportHeight);g.WidthPixels=pixels;g.OutlineWidthPixels=outline;
        var drawn=path.Points;
        var shown=Presentation;
        if(Vector3.DistanceSquared(eye,path.Points[0])<OwnEyeDistance*OwnEyeDistance&&(shown.Right!=0||shown.Down!=0)){
            var right=Vector3.Cross(forward,up);right=right.LengthSquared()>1e-8f?Vector3.Normalize(right):Vector3.UnitX;var down=Vector3.Cross(forward,right);
            var hand=right*shown.Right+down*shown.Down;float total=0;var lengths=new float[path.Points.Count];
            for(int i=1;i<lengths.Length;i++)lengths[i]=total+=Vector3.Distance(path.Points[i-1],path.Points[i]);
            drawn=new List<Vector3>(path.Points.Count);float join=Math.Clamp(shown.JoinShare*total,shown.MinJoin,shown.MaxJoin);
            // The offset may only fade before the first rebound: past a wall or the floor the ribbon is the true path.
            if(path.Corners.Count>0)join=Math.Min(join,Math.Max(.5f,lengths[path.Corners[0]]));
            for(int i=0;i<lengths.Length;i++){
                float leave=1-MathUtils.SmoothStep(0,join,lengths[i]),arrive=MathUtils.SmoothStep(0,1,total-lengths[i]);
                drawn.Add(path.Points[i]+hand*(leave*arrive));
            }
            g.FromHand=true;
        }
        // World size of one pixel at a view depth.
        float Pixel(Vector3 p)=>2*Math.Max(NearDepth,Vector3.Dot(p-eye,forward))/(projectionScale*viewportHeight);
        bool cut=path.Kind==End.Truncated;var corners=new HashSet<int>(path.Corners);
        int count=drawn.Count;
        Vector3 Side(Vector3 tangent,Vector3 at){var side=Vector3.Cross(tangent,eye-at);return side.LengthSquared()>1e-10f?Vector3.Normalize(side):Vector3.Zero;}
        for(int pass=0;pass<2;pass++){
            float half=(pass==0?pixels+2*outline:pixels)*.5f;
            for(int i=0;i+1<count;i++){
                Vector3 a=drawn[i],b=drawn[i+1];
                float da=Vector3.Dot(a-eye,forward),db=Vector3.Dot(b-eye,forward);
                if(da<NearDepth&&db<NearDepth)continue;
                // Clip at the near plane instead of stretching a segment through the camera.
                if(da<NearDepth)a=Vector3.Lerp(a,b,(NearDepth-da)/(db-da));else if(db<NearDepth)b=Vector3.Lerp(b,a,(NearDepth-db)/(da-db));
                var segment=b-a;if(segment.LengthSquared()<1e-10f)continue;segment=Vector3.Normalize(segment);
                // Smooth joins inside an arc (shared tangent of the neighbours); a rebound keeps each side's own direction.
                Vector3 ta=i>0&&!corners.Contains(i)&&da>=NearDepth?Vector3.Normalize(drawn[i+1]-drawn[i-1]):segment;
                Vector3 tb=i+2<count&&!corners.Contains(i+1)&&db>=NearDepth?Vector3.Normalize(drawn[i+2]-drawn[i]):segment;
                Vector3 sa=Side(ta,a)*(half*Pixel(a)),sb=Side(tb,b)*(half*Pixel(b));
                float fade=cut?Math.Clamp((count-1-i)/6f,.15f,1):1; // an unknown end fades out instead of pointing at a landing spot
                float fadeNext=cut?Math.Clamp((count-2-i)/6f,.15f,1):1;
                Color ca=Alpha(pass==0?Edge:cut?Color.Lerp(Core,Uncertain,1-fade):Core,fade),cb=Alpha(pass==0?Edge:cut?Color.Lerp(Core,Uncertain,1-fadeNext):Core,fadeNext);
                g.Triangles.Add(new(a-sa,a+sa,b+sb,ca,ca,cb));g.Triangles.Add(new(a-sa,b+sb,b-sb,ca,cb,cb));
                // current-direction-20260929 §5: a one-pixel feather outside the dark edge, fading to transparent, so the
                // ribbon's silhouette is smoothed without multisampling (its own alpha: never counted as the edge or core).
                if(pass==0){
                    Vector3 fa=Side(ta,a)*((half+Feather)*Pixel(a)),fb=Side(tb,b)*((half+Feather)*Pixel(b));
                    Color ia=Alpha(FeatherColor,fade),ib=Alpha(FeatherColor,fadeNext),oa=Alpha(FeatherColor,0),ob=oa;
                    g.Triangles.Add(new(a+sa,a+fa,b+fb,ia,oa,ob));g.Triangles.Add(new(a+sa,b+fb,b+sb,ia,ob,ib));
                    g.Triangles.Add(new(a-sa,a-fa,b-fb,ia,oa,ob));g.Triangles.Add(new(a-sa,b-fb,b-sb,ia,ob,ib));
                }
                // A round cap at each rebound closes the gap between the two directions.
                if(corners.Contains(i+1)&&db>=NearDepth)Disc(g,b,half*Pixel(b),eye,up,pass==0?Edge:Core,8);
            }
        }
        var end=path.EndPoint;
        if(Vector3.Dot(end-eye,forward)<NearDepth)return g;
        float radius=Math.Clamp(Math.Max(BallRadius,Math.Max(MinBallPixels,BallPixelsAt1080*viewportHeight/1080)*Pixel(end)),BallRadius,MaxBallRadius);
        // Resting on a surface: lifted so the ball sits on it instead of being half buried. In the air: centred on the point.
        var centre=path.Grounded&&!cut?end+Vector3.UnitY*Math.Max(0,radius-.06f):end;
        g.BallCentre=centre;g.BallRadius=radius;g.Hollow=cut;
        if(cut){Ring(g,centre,radius*.8f,radius*.8f+Math.Max(1.5f,pixels*.6f)*Pixel(centre),eye,up,UncertainEdge,Uncertain,20);return g;}
        Ball(g,centre,radius,eye,up,outline*Pixel(centre));
        if(!path.Grounded&&path.Kind is End.Detonate or End.AirBurst)Burst(g,centre,radius,eye,up,pixels*Pixel(centre));
        return g;
    }
    // Straight (not premultiplied) colours throughout; drawn with BlendState.NonPremultiplied.
    static Color Alpha(Color c,float factor)=>new(c.R,c.G,c.B,(byte)Math.Clamp(c.A*factor,0,255));
    static (Vector3 Right,Vector3 Up,Vector3 Toward) Facing(Vector3 centre,Vector3 eye,Vector3 up){
        var toward=eye-centre;toward=toward.LengthSquared()>1e-10f?Vector3.Normalize(toward):Vector3.UnitZ;
        var right=Vector3.Cross(up,toward);right=right.LengthSquared()>1e-8f?Vector3.Normalize(right):Vector3.Normalize(Vector3.Cross(Vector3.UnitX,toward));
        return(right,Vector3.Cross(toward,right),toward);
    }
    static void Disc(Geometry g,Vector3 centre,float radius,Vector3 eye,Vector3 up,Color color,int segments){
        var f=Facing(centre,eye,up);
        for(int i=0;i<segments;i++){float a0=MathF.PI*2*i/segments,a1=MathF.PI*2*(i+1)/segments;
            g.Triangles.Add(new(centre,centre+(f.Right*MathF.Cos(a0)+f.Up*MathF.Sin(a0))*radius,centre+(f.Right*MathF.Cos(a1)+f.Up*MathF.Sin(a1))*radius,color,color,color));}
    }
    static void Ring(Geometry g,Vector3 centre,float inner,float outer,Vector3 eye,Vector3 up,Color edge,Color fill,int segments){
        var f=Facing(centre,eye,up);float border=(outer-inner)*.35f;
        foreach(var (r0,r1,color) in new[]{(inner-border,outer+border,edge),(inner,outer,fill)})
            for(int i=0;i<segments;i++){float a0=MathF.PI*2*i/segments,a1=MathF.PI*2*(i+1)/segments;
                Vector3 d0=f.Right*MathF.Cos(a0)+f.Up*MathF.Sin(a0),d1=f.Right*MathF.Cos(a1)+f.Up*MathF.Sin(a1);
                g.Triangles.Add(new(centre+d0*r0,centre+d0*r1,centre+d1*r1,color,color,color));g.Triangles.Add(new(centre+d0*r0,centre+d1*r1,centre+d1*r0,color,color,color));}
    }
    /// <summary>The visible half of a sphere, lit from the upper left of the view and darkened toward its rim, on a
    /// dark disc one outline wider: it reads as a solid ball from the side, from above and against bright ground.</summary>
    static void Ball(Geometry g,Vector3 centre,float radius,Vector3 eye,Vector3 up,float outline){
        var f=Facing(centre,eye,up);Disc(g,centre-f.Toward*(radius*.02f),radius+outline,eye,up,Edge,20);
        var light=Vector3.Normalize(-f.Right*.55f+f.Up*.65f+f.Toward*.52f);
        const int rings=5,segments=16;
        Vector3 Normal(int ring,int segment){float polar=MathF.PI*.5f*ring/rings,around=MathF.PI*2*segment/segments;
            return f.Toward*MathF.Cos(polar)+(f.Right*MathF.Cos(around)+f.Up*MathF.Sin(around))*MathF.Sin(polar);}
        Color Shade(Vector3 normal){float diffuse=Math.Max(0,Vector3.Dot(normal,light)),rim=MathF.Pow(1-Math.Max(0,Vector3.Dot(normal,f.Toward)),2.2f);
            float level=Math.Clamp(.38f+.72f*diffuse-.3f*rim,.18f,1.08f);float glint=MathF.Pow(diffuse,14)*.5f;
            return new Color((byte)Math.Clamp(255*level+255*glint,0,255),(byte)Math.Clamp(118*level+230*glint,0,255),(byte)Math.Clamp(30*level+200*glint,0,255),(byte)245);}
        for(int ring=0;ring<rings;ring++)for(int segment=0;segment<segments;segment++){
            Vector3 n00=Normal(ring,segment),n01=Normal(ring,segment+1),n10=Normal(ring+1,segment),n11=Normal(ring+1,segment+1);
            if(ring>0)g.Triangles.Add(new(centre+n00*radius,centre+n10*radius,centre+n01*radius,Shade(n00),Shade(n10),Shade(n01)));
            g.Triangles.Add(new(centre+n01*radius,centre+n10*radius,centre+n11*radius,Shade(n01),Shade(n10),Shade(n11)));
        }
    }
    /// <summary>Eight short rays around the ball: the grenade bursts here in the air, it does not land here.</summary>
    static void Burst(Geometry g,Vector3 centre,float radius,Vector3 eye,Vector3 up,float width){
        var f=Facing(centre,eye,up);
        for(int i=0;i<8;i++){float a=MathF.PI*2*i/8;Vector3 d=f.Right*MathF.Cos(a)+f.Up*MathF.Sin(a),s=Vector3.Cross(d,f.Toward)*(width*.5f);
            Vector3 p0=centre+d*(radius*1.45f),p1=centre+d*(radius*2.1f);
            g.Triangles.Add(new(p0-s,p0+s,p1+s,Core,Core,Core));g.Triangles.Add(new(p0-s,p1+s,p1-s,Core,Core,Core));}
    }
    /// <summary>Draws the cached path. Depth tested against the world (no see-through), no depth written.</summary>
    public static void Draw(PrimitivesRenderer3D renderer,Camera camera,Path path) {
        if(path is null||path.Points.Count<2)return;
        var geometry=Build(path,camera.ViewPosition,camera.ViewDirection,camera.ViewUp,camera.ProjectionMatrix.M22,camera.ViewportSize.Y);
        if(geometry.Triangles.Count==0)return;
        var batch=renderer.FlatBatch(0,DepthStencilState.DepthRead,RasterizerState.CullNone,BlendState.NonPremultiplied);
        foreach(var t in geometry.Triangles)batch.QueueTriangle(t.A,t.B,t.C,t.ColorA,t.ColorB,t.ColorC);
        batch.Flush(camera.ViewProjectionMatrix);
    }
}
