using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Bounded, deterministic sprite animation, separate from damage and saved state.</summary>
public static class ScGrenadeVisuals {
    public static readonly string[] Textures = ["grenade_smoke_atlas", "grenade_fire_atlas", "grenade_blast_atlas", "grenade_glow"];
    // CS2's flash has a short white core followed by a visible after-flash; the burst sprite
    // must outlive the core or it looks as if the effect is cut off on the next frame.
    public const float BlastLifetime = 2.2f, FlashLifetime = 1.15f;
    // ContentReader loads straight RGBA. AlphaBlend expects premultiplied RGB and leaves a bright
    // cloud even when only vertex alpha is faded to zero (including HE openings).
    public static BlendState SpriteBlend(bool additive) => additive?BlendState.Additive:BlendState.NonPremultiplied;
    public sealed record Sprite(Vector3 Position, float Width, float Height, Color Color, int Texture, int Frame, float Rotation=0, bool Additive=false, bool Upright=false);
    public static int Frame(float phase) => Math.Clamp((int)(phase*16),0,15);
    static float Hash(int i) { float v=MathF.Sin(i*127.1f+311.7f)*43758.5453f; return v-MathF.Floor(v); }
    static Color Tint(int r,int g,int b,float alpha) => new(r,g,b,(int)(255*Math.Clamp(alpha,0,1)));
    public static List<Sprite> Burst(Vector3 origin,float age,bool flash,bool reduced,float distance) {
        List<Sprite> list=[];
        if(age<0 || age> (flash?FlashLifetime:BlastLifetime)) return list;
        if(flash) {
            float flashT=Math.Clamp(age/FlashLifetime,0,1), strength=reduced?.18f:1;
            float core=Math.Clamp(1-age/.16f,0,1), tail=1-flashT*flashT*(3-2*flashT);
            list.Add(new(origin,.65f+age*2,.65f+age*2,Tint(255,250,230,Math.Max(core,tail*.28f)*strength),3,0,Additive:true));
            list.Add(new(origin,1.8f+age*3,1.8f+age*3,Tint(195,218,255,tail*.52f*strength),3,0,Additive:true));
            if(age>.12f) list.Add(new(origin,2.8f+age*2.2f,2.8f+age*2.2f,Tint(235,240,255,tail*.18f*strength),3,0,Additive:true));
            return list;
        }
        float t=age/BlastLifetime;
        int clouds=distance<30?(ScResourcePolicy.Lite?8:12):6;
        for(int i=0;i<clouds;i++) {
            float angle=i*2.399963f, rise=Hash(i+5), speed=.4f+Hash(i+17)*1.5f;
            Vector3 direction=new(MathF.Cos(angle)*.8f,.25f+rise*.65f,MathF.Sin(angle)*.8f);
            Vector3 p=origin+direction*(.25f+speed*1.5f*(1-MathF.Exp(-age*5)))+Vector3.UnitY*age*.35f;
            float radius=.3f+(1-MathF.Exp(-age*2))*(.65f+rise*.35f);
            int shade=(int)(32+30*t+rise*12);
            list.Add(new(p,radius,radius,Tint(shade,shade,shade,Math.Clamp(age/.06f,0,1)*(1-t)*.92f),2,Frame(t*.9f),angle));
        }
        if(age<.3f) {
            float fade=1-age/.3f;
            list.Add(new(origin+Vector3.UnitY*.15f,1+age*3,1+age*3,Tint(255,190,105,fade),1,Frame(age/.3f),Additive:true));
            list.Add(new(origin,1.4f+age*3,1.4f+age*3,Tint(255,145,42,fade*.8f),3,0,Additive:true));
        }
        if(distance<35 && age<.7f) for(int i=0;i<18;i+=ScResourcePolicy.Lite?2:1) {
            float angle=i*2.399963f, speed=2+Hash(i)*2;
            Vector3 velocity=new(MathF.Cos(angle)*speed,.8f+Hash(i+2)*3,MathF.Sin(angle)*speed);
            Vector3 p=origin+velocity*age-Vector3.UnitY*(age*age*2);
            float fade=1-age/.7f;
            list.Add(new(p,.025f,.055f,Tint(255,167,64,fade),3,0,angle,Additive:true));
        }
        return list;
    }
    /// <summary>F01: the in-smoke screen tint is neutral grey; only alpha follows the depth inside the volume.</summary>
    public static Color SmokeInside(float smoke) => new(106,106,106,(int)(255*Math.Clamp(smoke,0,1)));
    public static List<Sprite> Smoke(ScGrenadeState s,float distance) {
        List<Sprite> list=[];
        float radius=ScSmokeVolume.CurrentRadius(s);if(radius<.01f || ScSmokeVolume.Dissipation(s)<=.001f) return list;
        int count=ScSmokeVolume.SpriteCount(distance)*(ScResourcePolicy.Lite?1:2);
        float fade=Math.Clamp(s.Age/.20f,0,1)*ScSmokeVolume.Dissipation(s);
        for(int i=0;i<count;i++) {
            float y=1-2*(i+.5f)/count,ring=MathF.Sqrt(1-y*y),a=i*2.399963f+s.Age*(i%2==0?.055f:-.04f);
            // Three staggered shells create billows without making the whole cloud breathe in and out.
            float shell=i%4 switch { 0=>.12f, 1=>.30f, 2=>.46f, _=>.60f };
            float pulse=1+.02f*MathF.Sin(s.Age*.9f+i*1.7f);
            float vertical=y*ScSmokeVolume.CurrentHeight(s)*.58f;
            Vector3 offset=new Vector3(MathF.Cos(a)*ring*radius*shell,vertical,MathF.Sin(a)*ring*radius*shell);
            // Width/Height are half-extents. Fit the whole billboard inside the cloud bounds,
            // using overlapping broad cores in both editions instead of sparse outer balls.
            float width=Math.Min(radius*(.57f+.04f*Hash(i))*pulse,radius-new Vector2(offset.X,offset.Z).Length());
            float height=Math.Min(ScSmokeVolume.CurrentHeight(s)*.57f*pulse,ScSmokeVolume.CurrentHeight(s)-Math.Abs(vertical));
            // F01: neutral grey with the shading kept in the value, never in a hue offset (atlas RGB is white).
            int light=(int)(102+y*18+Hash(i)*14);
            // Ping-pong frame selection avoids a hard last-to-first atlas jump.
            float phase=(s.Age*.16f+Hash(i))%2;phase=phase>1?2-phase:phase;
            list.Add(new(ScSmokeVolume.Center(s)+offset,width,height,Tint(light,light,light,fade),0,Frame(phase)));
        }
        return list;
    }
    /// <summary>Camera-facing smoke, including overhead views. All four corners remain inside the requested total bounds.</summary>
    public static (Vector3 Right,Vector3 Up) SmokeAxes(Sprite sprite,ScGrenadeState smoke,Vector3 right,Vector3 up) {
        float radius=ScSmokeVolume.CurrentRadius(smoke),height=ScSmokeVolume.CurrentHeight(smoke);
        float verticalRatio=ScSmokeVolume.HalfHeight/ScSmokeVolume.Radius;
        Vector3 r=right*sprite.Width,u=up*(sprite.Height/MathF.Sqrt(Math.Max(.001f,up.Y*up.Y+verticalRatio*verticalRatio*(up.X*up.X+up.Z*up.Z))));
        Vector3 offset=sprite.Position-ScSmokeVolume.Center(smoke);
        float Fit(float p,float a,float b,float bound)=>Math.Clamp((bound-Math.Abs(p))/Math.Max(.0001f,Math.Abs(a)+Math.Abs(b)),0,1);
        float scale=Math.Min(Fit(offset.X,r.X,u.X,radius),Math.Min(Fit(offset.Y,r.Y,u.Y,height),Fit(offset.Z,r.Z,u.Z,radius)));
        return (r*scale,u*scale);
    }
    public static List<Sprite> Fire(ScGrenadeState s,IReadOnlyList<Vector3> points,float distance) {
        List<Sprite> list=[];
        float fade=Math.Clamp(s.Age/.20f,0,1)*Math.Clamp(s.Remaining/.65f,0,1);
        int stride=distance>35?3:distance>20?2:1;
        for(int i=0;i<points.Count;i+=stride) {
            Vector3 p=points[i];float phase=(s.Age*1.25f+Hash(i))%1;
            float spread=Math.Clamp((s.Age-.12f*Vector3.Distance(p,s.Position))/.4f,0,1);
            float height=.78f+.20f*MathF.Sin(s.Age*5+i*2.3f)+Hash(i)*.22f;
            // Broad low flames overlap into a pool, taller moving tongues are staggered by location.
            list.Add(new(p+Vector3.UnitY*height*.45f,.48f,height*.5f,Tint(255,187,100,fade*spread),1,Frame(phase),Upright:true));
            if(!ScResourcePolicy.Lite && distance<25 && i%2==0) {
                list.Add(new(p+new Vector3(.12f,.17f,-.07f),.43f,.20f,Tint(255,122,28,fade*spread*.45f),1,Frame((phase+.5f)%1),Additive:true,Upright:true));
                float lift=(s.Age*.6f+Hash(i+4))%1;
                list.Add(new(p+new Vector3(MathF.Sin(i+s.Age)*.15f,.2f+lift,0),.018f,.038f,Tint(255,173,72,fade*(1-lift)),3,0,Additive:true));
            }
            if(i%(ScResourcePolicy.Lite?8:4)==0) {
                float lift=(s.Age*.35f+Hash(i+8))%1;
                list.Add(new(p+Vector3.UnitY*(.7f+lift*1.5f),.32f+lift*.25f,.32f+lift*.25f,Tint(48,46,43,fade*spread*(1-lift)*.28f),2,Frame(lift),i));
            }
        }
        return list;
    }
    public static void ApplySmokeOpenings(List<Sprite> sprites,ScGrenadeState smoke,IEnumerable<ScSmokeDisturbance> openings,Vector3 right,Vector3 up) {
        var active=openings.Where(o=>o.Active&&o.Affects(smoke)).ToArray();if(active.Length==0)return;
        for(int i=0;i<sprites.Count;i++) {
            var sp=sprites[i];var axes=SmokeAxes(sp,smoke,right,up);Vector3 r=axes.Right*.7f,u=axes.Up*.7f;
            float cleared=ScSmokeDisturbance.Clearing(active,sp.Position,smoke)
                +ScSmokeDisturbance.Clearing(active,sp.Position+r,smoke)+ScSmokeDisturbance.Clearing(active,sp.Position-r,smoke)
                +ScSmokeDisturbance.Clearing(active,sp.Position+u,smoke)+ScSmokeDisturbance.Clearing(active,sp.Position-u,smoke);
            sprites[i]=sp with {Color=new Color(sp.Color.R,sp.Color.G,sp.Color.B,(int)(sp.Color.A*(1-cleared/5)))};
        }
    }
}
