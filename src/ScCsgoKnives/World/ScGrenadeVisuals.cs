using Engine;
namespace Game;

/// <summary>Bounded, deterministic sprite animation, separate from damage and saved state.</summary>
public static class ScGrenadeVisuals {
    public static readonly string[] Textures = ["grenade_smoke_atlas", "grenade_fire_atlas", "grenade_blast_atlas", "grenade_glow"];
    public const float BlastLifetime = 1.25f, FlashLifetime = .32f;
    public sealed record Sprite(Vector3 Position, float Width, float Height, Color Color, int Texture, int Frame, float Rotation=0, bool Additive=false, bool Upright=false);
    public static int Frame(float phase) => Math.Clamp((int)(phase*16),0,15);
    static float Hash(int i) { float v=MathF.Sin(i*127.1f+311.7f)*43758.5453f; return v-MathF.Floor(v); }
    static Color Tint(int r,int g,int b,float alpha) => new(r,g,b,(int)(255*Math.Clamp(alpha,0,1)));
    public static List<Sprite> Burst(Vector3 origin,float age,bool flash,bool reduced,float distance) {
        List<Sprite> list=[];
        if(age<0 || age> (flash?FlashLifetime:BlastLifetime)) return list;
        if(flash) {
            float fade=1-age/FlashLifetime, strength=reduced?.18f:1;
            list.Add(new(origin,.65f+age*2,.65f+age*2,Tint(255,250,230,fade*strength),3,0,Additive:true));
            list.Add(new(origin,1.8f+age*3,1.8f+age*3,Tint(195,218,255,fade*.45f*strength),3,0,Additive:true));
            return list;
        }
        float t=age/BlastLifetime;
        int clouds=distance<30?12:6;
        for(int i=0;i<clouds;i++) {
            float angle=i*2.399963f, rise=Hash(i+5), speed=.4f+Hash(i+17)*1.5f;
            Vector3 direction=new(MathF.Cos(angle)*.8f,.25f+rise*.65f,MathF.Sin(angle)*.8f);
            Vector3 p=origin+direction*(.1f+speed*(1-MathF.Exp(-age*3)));
            float radius=.22f+age*(.5f+rise*.35f);
            list.Add(new(p,radius,radius,Tint(117,110,101,(1-t)*.75f),2,Frame(t*.9f),angle));
        }
        if(age<.3f) {
            float fade=1-age/.3f;
            list.Add(new(origin+Vector3.UnitY*.15f,.7f+age*2,.7f+age*2,Tint(255,211,142,fade),1,Frame(age/.3f),Additive:true));
            list.Add(new(origin,.9f+age*2,.9f+age*2,Tint(255,169,67,fade),3,0,Additive:true));
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
    public static List<Sprite> Smoke(ScGrenadeState s,float distance) {
        List<Sprite> list=[];
        float radius=ScSmokeVolume.CurrentRadius(s);if(radius<.01f) return list;
        int count=ScSmokeVolume.SpriteCount(distance)*2;
        float fade=Math.Clamp(s.Age/.25f,0,1)*Math.Clamp(s.Remaining/1.5f,0,1);
        for(int i=0;i<count;i++) {
            float y=1-2*(i+.5f)/count,ring=MathF.Sqrt(1-y*y),a=i*2.399963f+s.Age*(i%2==0?.055f:-.04f);
            float shell=i%3==0?.25f:.58f;
            Vector3 offset=new Vector3(MathF.Cos(a)*ring,y,MathF.Sin(a)*ring)*radius*shell;
            float pulse=1+.08f*MathF.Sin(s.Age*1.6f+i),size=radius*.49f*pulse;
            int light=(int)(142+y*20+Hash(i)*10);
            // Ping-pong frame selection avoids a hard last-to-first atlas jump.
            float phase=(s.Age*.16f+Hash(i))%2;phase=phase>1?2-phase:phase;
            list.Add(new(ScSmokeVolume.Center(s)+offset,size,size,Tint(light,light+3,light+5,fade*.76f),0,Frame(phase),a*.3f));
        }
        return list;
    }
    public static List<Sprite> Fire(ScGrenadeState s,IReadOnlyList<Vector3> points,float distance) {
        List<Sprite> list=[];
        float fade=Math.Clamp(s.Age/.20f,0,1)*Math.Clamp(s.Remaining/.65f,0,1);
        int stride=distance>35?3:distance>20?2:1;
        for(int i=0;i<points.Count;i+=stride) {
            Vector3 p=points[i];float phase=(s.Age*1.25f+Hash(i))%1;
            float height=.66f+.24f*MathF.Sin(s.Age*7+i*2.3f);
            list.Add(new(p+Vector3.UnitY*height*.5f,.42f,height*.5f,Tint(255,246,225,fade),1,Frame(phase),Upright:true));
            if(!ScResourcePolicy.Lite && distance<25 && i%2==0) {
                list.Add(new(p+new Vector3(.12f,.20f,-.07f),.25f,.28f,Tint(255,190,100,fade*.7f),1,Frame((phase+.5f)%1),Additive:true,Upright:true));
                float lift=(s.Age*.6f+Hash(i+4))%1;
                list.Add(new(p+new Vector3(MathF.Sin(i+s.Age)*.15f,.2f+lift,0),.018f,.038f,Tint(255,173,72,fade*(1-lift)),3,0,Additive:true));
            }
            if(i%(ScResourcePolicy.Lite?8:4)==0) {
                float lift=(s.Age*.35f+Hash(i+8))%1;
                list.Add(new(p+Vector3.UnitY*(.5f+lift),.28f+lift*.2f,.28f+lift*.2f,Tint(87,84,80,fade*(1-lift)*.22f),2,Frame(lift),i));
            }
        }
        return list;
    }
}
