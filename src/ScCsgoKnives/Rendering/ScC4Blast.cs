using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ScC4Blast(Vector3 position,float radius,double started) {
    public readonly Vector3 Position=position;
    public readonly float Radius=radius;
    public readonly double Started=started;
    public const float Lifetime=2.5f, WaveSeconds=1.2f;
    public static float WaveRadius(float age,float radius)=>radius*Math.Clamp(age/WaveSeconds,0,1);
    public static List<ScGrenadeVisuals.Sprite> Sprites(Vector3 center,float age,float distance) =>
        ScGrenadeVisuals.Burst(center,age*.5f,false,false,distance).Select(s=>s with {
            Position=center+(s.Position-center)*4,Width=s.Width*4,Height=s.Height*4
        }).ToList();
    public void Draw(PrimitivesRenderer3D renderer,Camera camera,double now) {
        float age=(float)(now-Started);if(age<0||age>Lifetime)return;
        foreach(var s in Sprites(Position,age,Vector3.Distance(camera.ViewPosition,Position)).OrderByDescending(s=>Vector3.DistanceSquared(camera.ViewPosition,s.Position))) {
            var texture=ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+ScGrenadeVisuals.Textures[s.Texture]);
            var batch=renderer.TexturedBatch(texture,false,s.Additive?2:1,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,s.Additive?BlendState.Additive:BlendState.AlphaBlend,SamplerState.LinearClamp);
            Vector3 r=camera.ViewRight*s.Width,u=camera.ViewUp*s.Height,p=s.Position;
            float x=s.Texture==3?0:(s.Frame%4)*.25f+.004f,y=s.Texture==3?0:(s.Frame/4)*.25f+.004f,span=s.Texture==3?1:.242f;
            batch.QueueQuad(p-r-u,p+r-u,p+r+u,p-r+u,new Vector2(x,y+span),new Vector2(x+span,y+span),new Vector2(x+span,y),new Vector2(x,y),s.Color);
        }
        if(age>1.5f)return;
        float radius=WaveRadius(age,Radius),width=.2f+radius*.018f;
        var ring=renderer.FlatBatch(2,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.Additive);
        Color color=new(205,215,225,(int)(150*Math.Clamp(1-age/1.5f,0,1)));
        // Ground-front plus a camera-facing pressure rim make the travel visible
        // from both above and ground level. Geometry is bounded at 384 triangles.
        foreach(bool ground in new[]{true,false}) {
            Vector3 right=ground?Vector3.UnitX:camera.ViewRight,up=ground?Vector3.UnitZ:camera.ViewUp;
            Vector3 center=Position+Vector3.UnitY*.15f;
            for(int i=0;i<96;i++) {
                float a=i*MathF.Tau/96,b=(i+1)*MathF.Tau/96;
                Vector3 d1=right*MathF.Cos(a)+up*MathF.Sin(a),d2=right*MathF.Cos(b)+up*MathF.Sin(b);
                ring.QueueQuad(center+d1*Math.Max(0,radius-width),center+d1*(radius+width),center+d2*(radius+width),center+d2*Math.Max(0,radius-width),color);
            }
        }
    }
}
