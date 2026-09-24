using Engine;
namespace Game;

/// <summary>Authored supply geometry. Atlas cells are defined by build_supply_surface.py.</summary>
internal sealed class ScSupplyGeometry {
    readonly BlockMesh mesh = new();
    const int Steel=0, Dark=1, Brass=2, Red=3, Rubber=4, Glass=5, Mat=6, Cabinet=7,
        Cut=8, Screw=9, Speaker=10, Screen=11, PaintLabel=16, Paint=17, Wood=18, Bristles=19,
        Warning=20, Grip=21, Grooves=22, Black=23;

    public static BlockMesh Build(int kind) {
        var g = new ScSupplyGeometry();
        switch (kind) {
            case 0: g.Magazine(); break;
            case 1: g.Shell(); break;
            case 2: g.Billet(); break;
            case 3: g.Mechanism(); break;
            case 4: g.Handle(); break;
            case 5: g.Optics(); break;
            case 6: g.Bench(); break;
            case 7: g.PaintTin(); break;
            case >= 8 and <= 12: g.Radio(kind); g.mesh.TransformPositions(Matrix.CreateScale(.35f)); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return g.mesh;
    }

    void Magazine() {
        // One continuous curved profile, rather than overlapping stair-step boxes.
        Vector2[] outline = [new(-.20f,-.40f),new(.13f,-.40f),new(.20f,-.18f),new(.22f,.36f),
            new(-.10f,.36f),new(-.12f,-.14f)];
        Profile(outline,.19f,Dark);
        for (int side=-1;side<=1;side+=2) {
            for (int j=0;j<3;j++) {
                float x=-.08f+j*.09f;
                Profile([new(x-.055f,-.31f),new(x-.022f,-.31f),new(x+.05f,.26f),new(x+.018f,.26f)],.012f,Grooves,side*.102f);
            }
            Box(.06f,.30f,side*.104f,.28f,.035f,.015f,Cut);
        }
        Bevel(-.035f,-.415f,0,.40f,.07f,.245f,.012f,Rubber);
        Box(-.035f,-.459f,.055f,.09f,.014f,.09f,Steel);
        Box(.06f,.37f,0,.34f,.045f,.21f,Steel);
        Tube(new(.06f,.412f,0),.048f,.21f,Brass,0,12);
        Tube(new(.19f,.412f,0),.040f,.055f,Cut,0,8);
        Box(.055f,.416f,.085f,.26f,.052f,.024f,Dark);
        Box(.055f,.416f,-.085f,.26f,.052f,.024f,Dark);
    }

    void Shell() {
        Tube(new(0,.01f,0),.157f,.57f,Red,1,16);
        Tube(new(0,-.273f,0),.163f,.13f,Brass,1,16);
        Tube(new(0,-.347f,0),.179f,.025f,Cut,1,16);
        Tube(new(0,-.363f,0),.053f,.012f,Dark,1,12);
        Tube(new(0,-.370f,0),.038f,.006f,Brass,1,12);
        Tube(new(0,.20f,0),.158f,.07f,Warning,1,16);
        Ring(new(0,.302f,0),.158f,.126f,.018f,Red,1,16);
        Tube(new(0,.293f,0),.126f,.012f,Black,1,16);
        for(int i=0;i<6;i++) {
            float a=i*MathF.Tau/6;
            Box(MathF.Cos(a)*.07f,.310f,MathF.Sin(a)*.07f,.048f,.012f,.048f,Red);
        }
    }

    void Billet() {
        Bevel(0,-.045f,0,.85f,.25f,.42f,.035f,Steel);
        Box(0,-.01f,.214f,.55f,.08f,.008f,Grooves);
        Box(0,-.01f,-.214f,.55f,.08f,.008f,Grooves);
        Bevel(0,.103f,0,.66f,.075f,.22f,.018f,Dark);
        for(int i=0;i<5;i++) Bevel(-.256f+i*.128f,.158f,0,.085f,.045f,.28f,.012f,Cut);
        // Recessed sockets with metal walls, rather than dots painted on the top.
        foreach(float x in new[]{-.34f,.34f}) {
            Ring(new(x,.086f,.10f),.042f,.025f,.018f,Cut,1,8);
            Tube(new(x,.079f,.10f),.025f,.004f,Black,1,8);
        }
        Box(.24f,-.10f,.216f,.15f,.035f,.008f,Warning);
    }

    void Mechanism() {
        Bevel(0,-.145f,0,.78f,.11f,.44f,.022f,Dark);
        Bevel(0,-.078f,-.13f,.66f,.025f,.10f,.006f,Steel);
        Tube(new(-.19f,-.018f,0),.15f,.13f,Steel,1,12);
        for(int i=0;i<10;i++) {
            float a=i*MathF.Tau/10;
            Box(-.19f+MathF.Cos(a)*.154f,-.018f,MathF.Sin(a)*.154f,.054f,.09f,.054f,Cut);
        }
        Ring(new(-.19f,.058f,0),.082f,.038f,.018f,Brass,1,12);
        Tube(new(-.19f,.053f,0),.035f,.018f,Screw,1,8);
        Bevel(.14f,-.01f,.0f,.25f,.15f,.15f,.012f,Cut);
        Box(.14f,.071f,.0f,.19f,.012f,.11f,Grooves);
        Tube(new(.14f,.024f,-.13f),.025f,.38f,Brass,0,8);
        for(int i=0;i<7;i++) Ring(new(-.008f+i*.042f,.024f,-.13f),.047f,.027f,.018f,Steel,0,8);
        Bevel(.30f,.015f,.10f,.065f,.21f,.11f,.012f,Dark);
        foreach(float x in new[]{-.33f,.33f}) Tube(new(x,-.081f,.145f),.022f,.012f,Screw,1,8);
    }

    void Handle() {
        Profile([new(-.16f,-.34f),new(.09f,-.34f),new(.20f,.25f),new(.13f,.32f),new(-.10f,.32f),new(-.18f,-.20f)],.23f,Rubber);
        for(int side=-1;side<=1;side+=2) {
            Profile([new(-.125f,-.26f),new(.067f,-.26f),new(.152f,.22f),new(-.068f,.22f)],.017f,Grip,side*.122f);
            foreach(float y in new[]{-.22f,.18f}) Tube(new(.008f+y*.17f,y,side*.138f),.025f,.007f,Screw,2,8);
        }
        for(int i=0;i<3;i++) Bevel(-.12f,-.20f+i*.14f,0,.07f,.035f,.25f,.009f,Dark);
        Bevel(-.035f,-.35f,0,.30f,.055f,.26f,.016f,Dark);
        Bevel(.04f,.348f,0,.26f,.065f,.245f,.014f,Dark);
        Bevel(.045f,.412f,0,.13f,.075f,.11f,.009f,Cut);
    }

    void Optics() {
        // Axis Z faces the inventory camera: a short sight, with a visible mounting foot.
        Tube(new(0,.035f,0),.16f,.53f,Dark,2,16);
        Tube(new(0,.035f,.22f),.211f,.15f,Dark,2,16);
        Ring(new(0,.035f,.307f),.216f,.169f,.042f,Cut,2,16);
        Ring(new(0,.035f,.313f),.190f,.160f,.040f,Rubber,2,16);
        Tube(new(0,.035f,.299f),.159f,.006f,Glass,2,16);
        Ring(new(0,.035f,-.282f),.18f,.13f,.036f,Rubber,2,16);
        Tube(new(0,.035f,-.271f),.129f,.008f,Glass,2,16);
        foreach(float z in new[]{-.14f,.095f}) {
            Ring(new(0,.035f,z),.182f,.162f,.047f,Steel,2,16);
            Box(0,-.153f,z,.26f,.10f,.06f,Dark);
        }
        Bevel(0,-.226f,-.02f,.33f,.055f,.38f,.012f,Dark);
        Box(0,-.261f,-.02f,.29f,.022f,.34f,Grooves);
        Tube(new(0,.223f,-.055f),.076f,.09f,Dark,1,12);
        Tube(new(0,.277f,-.055f),.078f,.016f,Screw,1,12);
        Tube(new(.195f,.035f,-.055f),.067f,.10f,Grooves,0,12);
        Tube(new(.251f,.035f,-.055f),.065f,.014f,Screw,0,12);
    }

    void PaintTin() {
        Tube(new(0,-.10f,0),.265f,.36f,Steel,1,16);
        Tube(new(0,-.11f,0),.268f,.20f,PaintLabel,1,16);
        Ring(new(0,-.29f,0),.279f,.245f,.025f,Cut,1,16);
        Ring(new(0,.09f,0),.281f,.235f,.027f,Cut,1,16);
        Tube(new(0,.091f,0),.234f,.013f,Paint,1,16);
        Ring(new(0,.102f,0),.218f,.202f,.009f,Dark,1,16);
        foreach(float x in new[]{-.28f,.28f}) Tube(new(x,.035f,0),.035f,.025f,Screw,0,8);
        // Handle follows a lifted half-circle, thick enough to survive small icons.
        Vector3 last = new(-.28f,.04f,0);
        for(int i=1;i<=8;i++) {
            float a=MathF.PI-i*MathF.PI/8;
            Vector3 next = new(MathF.Cos(a)*.28f,.04f+MathF.Sin(a)*.24f,0);
            Rod(last,next,.013f,Steel,6); last=next;
        }
        Profile([new(-.20f,.127f),new(-.16f,.098f),new(.17f,.25f),new(.13f,.28f)],.048f,Wood,.22f);
        Bevel(.17f,.278f,.22f,.12f,.085f,.065f,.009f,Steel);
        Bevel(.185f,.348f,.22f,.13f,.075f,.06f,.008f,Bristles);
    }

    void Bench() {
        // Keep the existing tabletop, cabinet, shelf and leg collision dimensions.
        Bevel(0,.28f,0,.98f,.12f,.86f,.018f,Steel);
        Box(0,.215f,0,.88f,.022f,.77f,Dark);
        Bevel(-.06f,.35f,-.03f,.65f,.025f,.60f,.008f,Mat);
        foreach(float x in new[]{-.39f,.39f}) foreach(float z in new[]{-.31f,.31f}) {
            Box(x,-.145f,z,.10f,.71f,.10f,Dark);
            Box(x,-.478f,z,.12f,.04f,.12f,Rubber);
            Box(x,.14f,z,.12f,.12f,.12f,Steel);
        }
        Box(0,-.32f,0,.86f,.065f,.68f,Dark);
        Bevel(-.25f,.02f,0,.28f,.37f,.63f,.012f,Cabinet);
        for(int j=0;j<3;j++) {
            float y=-.10f+j*.115f;
            Bevel(-.25f,y,.324f,.265f,.095f,.018f,.006f,Steel);
            Box(-.25f,y,.339f,.115f,.032f,.01f,Black);
            Box(-.25f,y+.008f,.355f,.12f,.014f,.025f,Cut);
        }
        Bevel(.29f,.39f,.12f,.25f,.09f,.27f,.008f,Dark);
        Box(.20f,.457f,.12f,.055f,.075f,.27f,Cut);
        Box(.38f,.457f,.12f,.055f,.075f,.27f,Cut);
        Box(.232f,.459f,.12f,.009f,.049f,.25f,Grooves);
        Box(.348f,.459f,.12f,.009f,.049f,.25f,Grooves);
        Tube(new(.29f,.414f,.12f),.022f,.31f,Steel,0,8);
        Rod(new(.456f,.378f,.12f),new(.456f,.455f,.12f),.012f,Dark,6);
        // One small wrench and a screwdriver on the mat.
        Box(-.15f,.38f,-.07f,.26f,.022f,.043f,Cut);
        Box(-.29f,.38f,-.11f,.07f,.022f,.035f,Cut);
        Box(-.29f,.38f,-.03f,.07f,.022f,.035f,Cut);
        Box(-.325f,.38f,-.07f,.027f,.022f,.11f,Cut);
        Bevel(-.04f,.385f,.16f,.14f,.035f,.044f,.009f,Rubber);
        Box(-.175f,.385f,.16f,.13f,.017f,.022f,Cut);
        Box(.285f,.26f,.434f,.18f,.045f,.008f,Warning);
        foreach(float x in new[]{-.445f,.445f}) foreach(float z in new[]{-.38f,.38f}) Tube(new(x,.343f,z),.014f,.007f,Screw,1,8);
    }

    void Radio(int kind) {
        Bevel(0,-.09f,0,.54f,.77f,.225f,.048f,Dark);
        Bevel(0,-.34f,0,.56f,.20f,.235f,.025f,Rubber);
        Bevel(0,-.44f,0,.53f,.045f,.23f,.012f,Steel);
        Bevel(0,-.015f,.123f,.448f,.50f,.028f,.024f,Steel);
        Bevel(0,.115f,.144f,.35f,.17f,.022f,.015f,Black);
        Box(0,.115f,.160f,.30f,.126f,.006f,Screen);
        Box(0,-.10f,.146f,.34f,.15f,.009f,Speaker);
        int badge = kind switch {9=>12,10=>13,11=>14,12=>15,_=>24};
        Box(0,-.331f,.126f,.42f,.195f,.012f,badge);
        for(int i=0;i<3;i++) Bevel(-.12f+i*.12f,-.212f,.137f,.075f,.037f,.025f,.008f,Rubber);
        Tube(new(.164f,.313f,0),.048f,.095f,Rubber,1,10);
        Tube(new(.164f,.495f,0),.019f,.28f,Dark,1,8);
        Tube(new(.164f,.636f,0),.022f,.018f,Rubber,1,8);
        Tube(new(-.155f,.299f,0),.061f,.07f,Grooves,1,12);
        Tube(new(-.155f,.340f,0),.052f,.012f,Screw,1,12);
        Bevel(-.276f,.065f,0,.04f,.18f,.125f,.012f,Rubber);
        Box(-.299f,.065f,0,.011f,.12f,.073f,Grooves);
        Bevel(0,-.075f,-.133f,.15f,.39f,.035f,.015f,Steel);
        Box(0,-.25f,-.149f,.19f,.035f,.036f,Dark);
        foreach(float x in new[]{-.20f,.20f}) Tube(new(x,.203f,.15f),.017f,.008f,Screw,2,8);
    }

    void Box(float x,float y,float z,float w,float h,float d,int mat) {
        Vector2[] p=[new(x-w/2,y-h/2),new(x+w/2,y-h/2),new(x+w/2,y+h/2),new(x-w/2,y+h/2)];
        Profile(p,d,mat,z);
    }

    void Bevel(float x,float y,float z,float w,float h,float d,float cut,int mat) {
        float r=Math.Min(cut,Math.Min(w,Math.Min(h,d))*.4f);
        Vector2[] Outline(float inset) {
            float l=x-w/2+inset,rr=x+w/2-inset,b=y-h/2+inset,t=y+h/2-inset,c=Math.Max(r-inset*.5f,r*.3f);
            return [new(l+c,b),new(rr-c,b),new(rr,b+c),new(rr,t-c),new(rr-c,t),new(l+c,t),new(l,t-c),new(l,b+c)];
        }
        Vector2[] outer=Outline(0),inner=Outline(r*.45f);
        Vector3[][] rings = [inner.Select(p=>new Vector3(p.X,p.Y,z-d/2)).ToArray(),outer.Select(p=>new Vector3(p.X,p.Y,z-d/2+r)).ToArray(),
            outer.Select(p=>new Vector3(p.X,p.Y,z+d/2-r)).ToArray(),inner.Select(p=>new Vector3(p.X,p.Y,z+d/2)).ToArray()];
        for(int j=0;j<3;j++) for(int i=0;i<8;i++) { int n=(i+1)%8; Quad(rings[j][i],rings[j][n],rings[j+1][n],rings[j+1][i],mat); }
        Cap(rings[0],mat,false);Cap(rings[3],mat,true);
    }

    // Convex XY outline, counterclockwise, extruded along Z. Caps use one planar UV projection.
    void Profile(Vector2[] p,float depth,int mat,float z=0) {
        Vector3[] a=p.Select(v=>new Vector3(v.X,v.Y,z-depth/2)).ToArray(),b=p.Select(v=>new Vector3(v.X,v.Y,z+depth/2)).ToArray();
        for(int i=0;i<p.Length;i++) {int n=(i+1)%p.Length;Quad(a[i],a[n],b[n],b[i],mat);}
        Cap(a,mat,false);Cap(b,mat,true);
    }

    void Tube(Vector3 p,float r,float length,int mat,int axis=1,int sides=12) => Radial(p,r,0,length,mat,axis,sides);
    void Ring(Vector3 p,float outer,float inner,float length,int mat,int axis=1,int sides=12) => Radial(p,outer,inner,length,mat,axis,sides);
    void Radial(Vector3 p,float r,float inner,float length,int mat,int axis,int sides) {
        Vector3 up=axis==0?Vector3.UnitX:axis==1?Vector3.UnitY:Vector3.UnitZ;
        Vector3 right=axis==0?Vector3.UnitY:Vector3.UnitX,forward=Vector3.Cross(up,right);
        for(int i=0;i<sides;i++) {
            float a=i*MathF.Tau/sides,b=(i+1)*MathF.Tau/sides;
            Vector3 ra=right*MathF.Cos(a)+forward*MathF.Sin(a),rb=right*MathF.Cos(b)+forward*MathF.Sin(b),v=up*length/2;
            Vector3 A=p+ra*r-v,B=p+rb*r-v,C=p+rb*r+v,D=p+ra*r+v;
            // A continuous unwrap: labels and machining lines must not repeat on every facet.
            int segments=mat==PaintLabel?sides/2:sides,segment=i%segments;
            float u0=(float)segment/segments,u1=(float)(segment+1)/segments;
            QuadUv(A,B,C,D,mat,new(u0,1),new(u1,1),new(u1,0),new(u0,0));
            if(inner>0) {
                Vector3 E=p+ra*inner-v,F=p+rb*inner-v,G=p+rb*inner+v,H=p+ra*inner+v;
                Quad(F,E,H,G,mat);Quad(D,C,G,H,mat);Quad(B,A,E,F,mat);
            } else {
                Vector2 ua=new(.5f+.5f*MathF.Cos(a),.5f-.5f*MathF.Sin(a)),ub=new(.5f+.5f*MathF.Cos(b),.5f-.5f*MathF.Sin(b));
                Triangle(p+v,D,C,mat,new(.5f),ua,ub);Triangle(p-v,B,A,mat,new(.5f),ub,ua);
            }
        }
    }

    void Rod(Vector3 a,Vector3 b,float r,int mat,int sides) {
        int start=mesh.Vertices.Count;
        Tube(Vector3.Zero,r,(b-a).Length(),mat,1,sides);
        Vector3 up=Vector3.Normalize(b-a),right=Vector3.Normalize(Vector3.Cross(Math.Abs(up.Z)<.9f?Vector3.UnitZ:Vector3.UnitX,up)),forward=Vector3.Cross(right,up);
        for(int i=start;i<mesh.Vertices.Count;i++) {
            var v=mesh.Vertices[i];var p=v.Position;v.Position=(a+b)/2+right*p.X+up*p.Y+forward*p.Z;mesh.Vertices[i]=v;
        }
        // Rods change orientation after construction; update baked light and terrain face too.
        for(int i=start;i<mesh.Vertices.Count;i+=3) {
            Vector3 normal=Vector3.Normalize(Vector3.Cross(mesh.Vertices[i+1].Position-mesh.Vertices[i].Position,mesh.Vertices[i+2].Position-mesh.Vertices[i].Position));
            float light=.62f+.38f*Math.Max(0,Vector3.Dot(normal,Vector3.Normalize(new Vector3(-1,2,1))));
            for(int j=0;j<3;j++){var v=mesh.Vertices[i+j];v.Color=new Color(light,light,light);v.Face=(byte)CellFace.Vector3ToFace(normal);mesh.Vertices[i+j]=v;}
        }
    }

    void Cap(Vector3[] p,int mat,bool front) {
        float minX=p.Min(v=>v.X),maxX=p.Max(v=>v.X),minY=p.Min(v=>v.Y),maxY=p.Max(v=>v.Y);
        Vector2 UV(Vector3 v)=>new((v.X-minX)/(maxX-minX),1-(v.Y-minY)/(maxY-minY));
        for(int i=1;i<p.Length-1;i++) {
            Vector3 b=p[front?i:i+1],c=p[front?i+1:i];Triangle(p[0],b,c,mat,UV(p[0]),UV(b),UV(c));
        }
    }

    void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int mat) {
        QuadUv(a,b,c,d,mat,new(0,1),new(1,1),new(1,0),new(0,0));
    }
    void QuadUv(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int mat,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud) {
        Triangle(a,b,c,mat,ua,ub,uc);
        Triangle(a,c,d,mat,ua,uc,ud);
    }
    void Triangle(Vector3 a,Vector3 b,Vector3 c,int mat,Vector2 ua,Vector2 ub,Vector2 uc) {
        Vector3 normal=Vector3.Normalize(Vector3.Cross(b-a,c-a));
        float light=.62f+.38f*Math.Max(0,Vector3.Dot(normal,Vector3.Normalize(new Vector3(-1,2,1))));
        Color color=new(light,light,light);
        Vector2 Atlas(Vector2 uv)=>new((mat%8+(3+58*uv.X)/64)/8,(mat/8+(3+58*uv.Y)/64)/4);
        int n=mesh.Vertices.Count;
        foreach(var (point,uv) in new[]{(a,ua),(b,ub),(c,uc)}) mesh.Vertices.Add(new BlockMeshVertex{Position=point,TextureCoordinates=Atlas(uv),Color=color,Face=(byte)CellFace.Vector3ToFace(normal)});
        // Preserve two-sided visibility, including the underside of the placeable bench.
        foreach(int i in new[]{0,1,2,2,1,0}) mesh.Indices.Add((ushort)(n+i));
    }
}
