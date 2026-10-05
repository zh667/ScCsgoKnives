using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Bounded, deterministic sprite animation, separate from damage and saved state.</summary>
public static class ScGrenadeVisuals {
    public static readonly string[] Textures = ["grenade_smoke_atlas", "grenade_fire_atlas", "grenade_blast_atlas", "grenade_glow","grenade_fireburst_atlas"];
    public const float FireBurstLifetime=.85f;
    public static List<Sprite> FireBurst(Vector3 position,float age,float distance){
        List<Sprite> sprites=[];if(age<0||age>=FireBurstLifetime)return sprites;
        float t=age/FireBurstLifetime,fade=Math.Clamp(age/.035f,0,1)*(1-t);
        int count=distance>30||ScResourcePolicy.Lite?4:7;
        for(int i=0;i<count;i++){
            float angle=i*2.399963f;
            var p=position+new Vector3(MathF.Cos(angle),Hash(i+71)*.8f,MathF.Sin(angle))*(.15f+t*.8f);
            sprites.Add(new(p,.5f+t*1.5f,.6f+t*1.6f,Tint(255,225,180,fade),4,Frame(t),angle,Additive:true));
        }
        return sprites;
    }
    // CS2's flash has a short white core followed by a visible after-flash; the burst sprite
    // must outlive the core or it looks as if the effect is cut off on the next frame.
    public const float BlastLifetime = 2.2f, FlashLifetime = 1.15f;
    // ContentReader loads straight RGBA. AlphaBlend expects premultiplied RGB and leaves a bright
    // cloud even when only vertex alpha is faded to zero (including HE openings).
    public static BlendState SpriteBlend(bool additive) => additive?BlendState.Additive:BlendState.NonPremultiplied;
    public sealed record Sprite(Vector3 Position, float Width, float Height, Color Color, int Texture, int Frame, float Rotation=0, bool Additive=false, bool Upright=false);
    public static int Frame(float phase) => Math.Clamp((int)(phase*16),0,15);
    /// <summary>The generated smooth puff's alpha at (u, v) in [0, 1] of variant <paramref name="variant"/> (0-3): fully dense
    /// within 0.40 of the half-size, a smooth radial fall to 0 at the rim, and a gentle low-frequency variation that fades out
    /// before the rim, so the outline stays round. PackageCheck samples this same function.</summary>
    public static float SmoothPuffAlpha(int variant,float u,float v) {
        float x=u*2-1,y=v*2-1,r=MathF.Sqrt(x*x+y*y); if(r>=1) return 0;
        float edge=1-SmoothStep((r-.40f)/.60f);
        float variation=0;
        for(int k=0;k<3;k++) {
            float angle=Hash(variant*5+k+101)*MathF.Tau,frequency=1.6f+k*.9f,phase=Hash(variant*7+k+211)*MathF.Tau;
            variation+=MathF.Sin((x*MathF.Cos(angle)+y*MathF.Sin(angle))*frequency*MathF.PI+phase);
        }
        float inner=1-SmoothStep((r-.15f)/.55f);
        return Math.Clamp(edge*(1+.10f*variation/3*inner)*.96f,0,1);
    }
    static float SmoothStep(float t) { t=Math.Clamp(t,0,1); return t*t*(3-2*t); }
    /// <summary>RGBA8 pixels of the 2x2 smooth puff atlas (<see cref="SmoothPuffCell"/> pixels a variant): white, alpha from
    /// <see cref="SmoothPuffAlpha"/>; the smoke's tint gives it its grey.</summary>
    public static byte[] SmoothPuffPixels() {
        int size=SmoothPuffCell*2; var data=new byte[size*size*4];
        for(int py=0;py<size;py++) for(int px=0;px<size;px++) {
            int variant=(py/SmoothPuffCell)*2+px/SmoothPuffCell,o=(py*size+px)*4;
            float a=SmoothPuffAlpha(variant,(px%SmoothPuffCell+.5f)/SmoothPuffCell,(py%SmoothPuffCell+.5f)/SmoothPuffCell);
            data[o]=data[o+1]=data[o+2]=255; data[o+3]=(byte)Math.Clamp((int)MathF.Round(a*255),0,255);
        }
        return data;
    }
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
    /// <summary>Puffs of one smoke: the same number at every distance (post-mp-bugs-20260930 item 2: the former 18/40 m
    /// bands changed the count, and the layout with it, as the viewer crossed them). <see cref="SmokeCoreCount"/> of them
    /// are the centred cores that make the cloud solid; the rest fill the dome.</summary>
    public static int SmokePuffCount => ScResourcePolicy.Lite ? 64 : 128;
    public static int SmokeCoreCount => ScResourcePolicy.Lite ? 4 : 8;
    /// <summary>A filling puff's half-size as a fraction of the cloud radius (<see cref="SmokePuffMin"/> to
    /// <see cref="SmokePuffMin"/> + <see cref="SmokePuffSpan"/>): smaller and more numerous than the old eight-metre
    /// billboards, so no single quad's turning toward the viewer is readable; the cloud's shape is the world-fixed set
    /// of their places.</summary>
    public const float SmokePuffMin=.30f, SmokePuffSpan=.14f;
    /// <summary>The cloud's surface (2026-10-01 user request: "边缘、表面看起来要平滑", the textured puffs left the outline bitten
    /// here and there): <see cref="SmokeShellCount"/> of the puffs are smooth round ones (<see cref="SmoothPuffKey"/>) on an
    /// even Fibonacci lattice over the dome, <see cref="SmokeShellRadius"/> of the cloud radius out and
    /// <see cref="SmokeShellHeight"/> of its height up, each <see cref="SmokeShellSize"/> radii in half-size: so many overlap
    /// that their union is a smooth dome. The cores and the remaining textured puffs stay inside it.</summary>
    public static int SmokeShellCount => ScResourcePolicy.Lite ? 36 : 72;
    public const float SmokeShellRadius=.72f, SmokeShellHeight=.62f, SmokeShellSize=.40f;
    /// <summary>The generated smooth puff texture (2x2 variants), drawn with the smoke's own quads; not a package asset.</summary>
    public const int SmoothPuffKey=5, SmoothPuffCell=128;
    /// <summary>The cloud's puffs, laid out in world space (post-mp-bugs-20260930 item 2). Every place, size and texture
    /// turn is a function of the puff's index and the smoke's age alone - never of the viewer's distance or direction -
    /// and every puff is sized and placed so that <see cref="SmokeAxes"/>' cloud-bounds fit is never engaged from any
    /// direction (its horizontal bound less the quad's reach, the floor and the dome's top): the drawn quad keeps the same
    /// size wherever it is seen from. The cores sit on the centre (a centred disc facing the viewer has the same outline
    /// from every side); the filling puffs are spread over the dome's area, its bottom half wide as the CS2 smoke
    /// billows out at ground level, its top narrowing. The cloud still grows, pulses and drifts slowly with age.
    /// <paramref name="distance"/> is accepted for callers but does not change the result.</summary>
    public static List<Sprite> Smoke(ScGrenadeState s,float distance) {
        List<Sprite> list=[];
        float radius=ScSmokeVolume.CurrentRadius(s);if(radius<.01f || ScSmokeVolume.Dissipation(s)<=.001f) return list;
        int count=SmokePuffCount,cores=SmokeCoreCount,shell=SmokeShellCount;
        float fade=Math.Clamp(s.Age/.20f,0,1)*ScSmokeVolume.Dissipation(s);
        float height=ScSmokeVolume.CurrentHeight(s),top=height/.80f,growth=ScSmokeVolume.Growth(s);   // top: SmokeAxes' vertical bound above the centre
        Vector3 ground=ScSmokeVolume.Center(s)-Vector3.UnitY*ScSmokeVolume.GroundCenter;
        int seed=s.Id*7;
        for(int i=0;i<count;i++) {
            float u=Hash(i+seed),v=Hash(i*7+11+seed),w=Hash(i*13+29+seed),h=Hash(i+41+seed);
            float width,tall,y; Vector3 offset;
            // The vertical band a square puff of half-size `size` fits in without the floor (-0.8 under the ground) or
            // the dome's top reshaping it from any direction; a young cloud (or a big puff) squeezes the band to a line,
            // then the size follows. Square: SmokeAxes swaps a puff's height for its width when seen from above.
            (float Size,float Low,float High) Band(float size) {
                // The reach includes the 2% pulse, so a puff at the band's edge never crosses the fit even at full swell.
                float reach=size*1.02f,low=reach-.78f,high=top+ScSmokeVolume.GroundCenter-reach-.02f;
                if(high<low){size=Math.Max(.05f,(top+ScSmokeVolume.GroundCenter+.78f-.02f)/2/1.02f);reach=size*1.02f;low=reach-.78f;high=top+ScSmokeVolume.GroundCenter-reach-.02f;}
                return (size,low,Math.Max(low,high));
            }
            int texture=0,frame=-1;
            if(i<cores) {
                // Centred cores: the largest square the band allows, at the cloud's heart.
                var band=Band(radius*.58f); width=tall=band.Size;
                y=Math.Clamp(ScSmokeVolume.GroundCenter+.75f,band.Low,band.High); offset=new Vector3(0,y,0);
            } else if(i<cores+shell) {
                // The smooth surface: an even (golden-angle) lattice over the upper half of an ellipsoid, uniform in area, so
                // neighbouring round puffs overlap by far more than their spacing and the outline has no notches.
                int k=i-cores; var band=Band(radius*SmokeShellSize); float size=band.Size;
                float up=(k+.5f)/shell,across=MathF.Sqrt(Math.Max(0,1-up*up));
                float azimuth=k*2.3999632f+Hash(seed+3)*MathF.Tau+s.Age*.02f;
                y=Math.Clamp(ScSmokeVolume.GroundCenter+up*height*SmokeShellHeight,band.Low,band.High);
                offset=new Vector3(MathF.Cos(azimuth)*across*SmokeShellRadius*radius,y,MathF.Sin(azimuth)*across*SmokeShellRadius*radius);
                width=tall=size; texture=SmoothPuffKey; frame=k&3;
            } else {
                var band=Band(radius*(SmokePuffMin+SmokePuffSpan*h)); float size=band.Size;
                // Bottom-heavy (w^1.5): the CS2 smoke billows along the ground, and the ankle-level rim is where the fewest
                // puffs would otherwise sit (coverage at 0.1 m and grazing low lines of sight both depend on them).
                y=MathUtils.Lerp(band.Low,band.High,MathF.Pow(w,1.5f));
                // Horizontal reach: 1.3 radii less the quad's reach (at most its half-size in any world axis), so a puff
                // never covers a line of sight that passes a metre outside the density's soft edge; never past the
                // density radius; above the centre the dome narrows.
                // Inside the smooth shell (2026-10-01): a textured puff's reach stays under 0.95 radii, so its ragged edge never
                // becomes the cloud's outline.
                float ringMax=Math.Max(0f,Math.Min(1f,.95f-size/radius));
                float aboveCentre=Math.Max(0,y-ScSmokeVolume.GroundCenter);
                if(aboveCentre>0&&height>0) ringMax*=MathF.Sqrt(Math.Max(0,1-aboveCentre*aboveCentre/(height*height)));
                float ring=MathF.Sqrt(u)*ringMax;   // uniform over the disc: the centre is as covered as the rim
                float azimuth=v*MathF.Tau+s.Age*(i%2==0?.03f:-.02f);
                offset=new Vector3(MathF.Cos(azimuth)*ring*radius,y,MathF.Sin(azimuth)*ring*radius);
                width=tall=size;
            }
            float pulse=1+.02f*MathF.Sin(s.Age*.9f+i*1.7f);
            // F01: neutral grey with the shading kept in the value, never in a hue offset (atlas RGB is white).
            // The smooth shell is shaded by height only (no per-puff speckle), so its surface reads as one continuous volume.
            int light=texture==SmoothPuffKey?(int)(100+Math.Clamp(y/Math.Max(.1f,height),0,1)*22):(int)(102+Math.Clamp(y/Math.Max(.1f,height),0,1)*18+Hash(i+5)*14);
            // Ping-pong frame selection avoids a hard last-to-first atlas jump.
            float phase=(s.Age*.16f+Hash(i+3+seed))%2;phase=phase>1?2-phase:phase;
            float rotation=i<cores?i*MathF.Tau/cores:Hash(i+71+seed)*MathF.Tau;
            float alpha=fade*(i<cores?1f:.92f+.08f*Hash(i+9+seed));
            list.Add(new(ground+offset,width*pulse,tall*pulse,Tint(light,light,light,alpha),texture,frame>=0?frame:Frame(phase),rotation));
        }
        return list;
    }
    /// <summary>The plane a smoke puff faces: <paramref name="anchor"/> is the viewing player's character (its eye), never the
    /// camera (round 4, 2026-10-01: the user's requirement). World up is kept upright (a spherical billboard toward the
    /// character), so moving, orbiting or raising a camera while the character stands still never turns a puff; the
    /// character moving does, as with a real volume. In first person the camera sits on the eye, so nothing changes there.
    /// Straight above or below a puff, where world up gives no right axis, the camera's right is used for the in-plane
    /// turn only (the camera is still allowed for projection, depth order and clipping).</summary>
    public static (Vector3 Right,Vector3 Up) FacingAxes(Vector3 anchor,Vector3 position,Vector3 cameraRight) {
        Vector3 toEye=anchor-position;float distance=toEye.Length();
        Vector3 normal=distance>1e-4f?toEye/distance:Vector3.UnitZ;
        Vector3 right=Vector3.Cross(Vector3.UnitY,normal);
        if(right.LengthSquared()<1e-4f) right=cameraRight-normal*Vector3.Dot(cameraRight,normal);
        if(right.LengthSquared()<1e-8f) right=Vector3.UnitX;
        right=Vector3.Normalize(right);
        return (right,Vector3.Cross(normal,right));
    }
    /// <summary>The drawn half-axes of a smoke sprite's facing quad for the character at <paramref name="anchor"/> (facing plane,
    /// then the cloud fit).</summary>
    public static (Vector3 Right,Vector3 Up) SmokeQuad(Sprite sprite,ScGrenadeState smoke,Vector3 anchor,Vector3 cameraRight) {
        var facing=FacingAxes(anchor,sprite.Position,cameraRight);
        return SmokeAxes(sprite,smoke,facing.Right,facing.Up);
    }
    /// <summary>A puff's three quads: the one facing the character (<see cref="SmokeQuad"/>), a vertical quad through the same
    /// place at right angles to it, and a horizontal one. They give the puff thickness for a camera that is not at the
    /// character (third person, orbit, debug views): a facing quad alone is seen edge-on from the side or from above and
    /// the cloud would open. None of them reads the camera's position; the character moving turns the first two together.
    /// With the character straight above or below, the two extra quads are the two vertical planes through the puff.</summary>
    public static (Vector3 Right,Vector3 Up)[] SmokeQuads(Sprite sprite,ScGrenadeState smoke,Vector3 anchor,Vector3 cameraRight) {
        var planes=SmokePlanes(sprite.Position,anchor,cameraRight);
        return [SmokeAxes(sprite,smoke,planes[0].Right,planes[0].Up),SmokeAxes(sprite,smoke,planes[1].Right,planes[1].Up),SmokeAxes(sprite,smoke,planes[2].Right,planes[2].Up)];
    }
    /// <summary>The three unfitted planes of a puff (facing the character, across it, horizontal) as unit right/up axes.</summary>
    static (Vector3 Right,Vector3 Up)[] SmokePlanes(Vector3 position,Vector3 anchor,Vector3 cameraRight) {
        var facing=FacingAxes(anchor,position,cameraRight);
        Vector3 normal=Vector3.Cross(facing.Right,facing.Up);
        Vector3 flat=new(normal.X,0,normal.Z);
        Vector3 across=Vector3.Cross(Vector3.UnitY,facing.Right); across=across.LengthSquared()>1e-8f?Vector3.Normalize(across):Vector3.UnitZ;
        (Vector3 Right,Vector3 Up) side,top;
        if(flat.LengthSquared()>1e-4f) { side=(Vector3.Normalize(flat),Vector3.UnitY); top=(facing.Right,across); }
        else { side=(facing.Right,Vector3.UnitY); top=(across,Vector3.UnitY); }
        return [facing,side,top];
    }
    /// <summary>How much of each of a puff's three quads (<see cref="SmokeQuads"/>) is drawn for a viewer at
    /// <paramref name="viewPosition"/>: the direction to the viewer is split over an orthonormal frame built on the direction
    /// to the character (towards it, across it, and the rest), the components' fourth powers are the three quads' weights (sum 1),
    /// and a weight under <see cref="SmokeQuadMinWeight"/> is not drawn at all. The quads keep
    /// their character-anchored orientation; the viewer only decides how visible each one is, so a plane seen edge-on (a thin
    /// streak) fades out instead of cutting a cross through the puff. With the camera at the character (first person) only
    /// the facing quad is drawn: the single, fully filled puff of r3k (2026-10-01 user feedback: the round-4 cross of three
    /// full-strength quads changed the smoke's shape and fill).</summary>
    public static float[] SmokeQuadWeights(Sprite sprite,ScGrenadeState smoke,Vector3 anchor,Vector3 cameraRight,Vector3 viewPosition) {
        float[] w=new float[3];
        Vector3 v=viewPosition-sprite.Position,a=anchor-sprite.Position; float length=v.Length(),al=a.Length();
        if(!(length>1e-4f) || !(al>1e-4f)) { w[0]=1; return w; }
        v/=length; a/=al;
        // An orthonormal frame on the direction to the character: e1 towards it (the facing quad), e2 horizontal across it
        // (the vertical side quad), e3 the rest (the horizontal quad). A viewer at the character is exactly e1: one quad.
        Vector3 e2=Vector3.Cross(a,Vector3.UnitY); e2=e2.LengthSquared()>1e-8f?Vector3.Normalize(e2):Vector3.UnitX;
        Vector3 e3=Vector3.Cross(e2,a);
        w[0]=Vector3.Dot(v,a); w[1]=Vector3.Dot(v,e2); w[2]=Vector3.Dot(v,e3);
        // The fourth power favours the quad that faces the viewer most squarely: coverage from an elevated camera away from the
        // character stays at the first-person level (deep rays >= .86 in SmokeViewRegression; squares let one ray drop to .83).
        float sum=0; for(int k=0;k<3;k++) { float c=w[k]*w[k]; w[k]=c*c; sum+=w[k]; }
        if(!(sum>1e-6f)) { Array.Clear(w); w[0]=1; return w; }
        for(int k=0;k<w.Length;k++) w[k]=w[k]/sum<SmokeQuadMinWeight?0:w[k];
        sum=0; foreach(float x in w) sum+=x;
        for(int k=0;k<w.Length;k++) w[k]/=sum;
        return w;
    }
    public const float SmokeQuadMinWeight=.02f;
    /// <summary>Smoke in a given plane (right/up). Allow the transparent atlas fringe past the density boundary,
    /// and below the floor, where normal terrain depth clips it instead of squeezing the whole puff.</summary>
    public static (Vector3 Right,Vector3 Up) SmokeAxes(Sprite sprite,ScGrenadeState smoke,Vector3 right,Vector3 up) {
        float radius=ScSmokeVolume.CurrentRadius(smoke)/.70f,height=ScSmokeVolume.CurrentHeight(smoke)/.80f;
        // Looking down sees the horizontal footprint, not the shorter side-view height.
        float projectedHeight=MathF.Sqrt(sprite.Height*sprite.Height*up.Y*up.Y+sprite.Width*sprite.Width*(up.X*up.X+up.Z*up.Z));
        Vector3 r=right*sprite.Width,u=up*projectedHeight;
        float c=MathF.Cos(sprite.Rotation),n=MathF.Sin(sprite.Rotation);
        (r,u)=(r*c+u*n,u*c-r*n);
        Vector3 offset=sprite.Position-ScSmokeVolume.Center(smoke);
        // Fit the circular puff support, not its transparent square corners. L1 corner fitting
        // shrank a 45-degree puff by sqrt(2), reopening gaps whenever the texture was rotated.
        float Fit(float p,float a,float b,float bound)=>Math.Clamp((bound-Math.Abs(p))/Math.Max(.0001f,MathF.Sqrt(a*a+b*b)),0,1);
        // Fit axes independently: a low vertical bound must not shrink the horizontal footprint.
        float horizontal=Math.Min(Fit(offset.X,r.X,u.X,radius),Fit(offset.Z,r.Z,u.Z,radius));
        r*=horizontal;u*=horizontal;
        float above=height-offset.Y,below=sprite.Position.Y-smoke.Position.Y+.8f;
        float vertical=Math.Clamp(Math.Min(above,below)/Math.Max(.0001f,MathF.Sqrt(r.Y*r.Y+u.Y*u.Y)),0,1);
        return (new Vector3(r.X,r.Y*vertical,r.Z),new Vector3(u.X,u.Y*vertical,u.Z));
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
    public static void ApplySmokeOpenings(List<Sprite> sprites,ScGrenadeState smoke,IEnumerable<ScSmokeDisturbance> openings,Vector3 anchor,Vector3 cameraRight) {
        var active=openings.Where(o=>o.Active&&o.Affects(smoke)).ToArray();if(active.Length==0)return;
        for(int i=0;i<sprites.Count;i++) {
            var sp=sprites[i];var axes=SmokeQuad(sp,smoke,anchor,cameraRight);Vector3 r=axes.Right*.7f,u=axes.Up*.7f;
            float cleared=ScSmokeDisturbance.Clearing(active,sp.Position,smoke)
                +ScSmokeDisturbance.Clearing(active,sp.Position+r,smoke)+ScSmokeDisturbance.Clearing(active,sp.Position-r,smoke)
                +ScSmokeDisturbance.Clearing(active,sp.Position+u,smoke)+ScSmokeDisturbance.Clearing(active,sp.Position-u,smoke);
            sprites[i]=sp with {Color=new Color(sp.Color.R,sp.Color.G,sp.Color.B,(int)(sp.Color.A*(1-cleared/5)))};
        }
    }
}
