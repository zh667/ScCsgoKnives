using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Small closed, shaded meshes for survival supplies. No weapon/hand resources are replaced.</summary>
public static class ScSurvivalMesh {
    public const string Texture = "Textures/ScCsgoKnives/survival_surface";
    // Atlas cells: brushed steel, dark steel, brass, red polymer, rubber,
    // blue glass, work mat, painted cabinet. Every face has real thickness.
    public static BlockMesh Build(int kind) {
        var mesh = new BlockMesh();
        void Box(float x,float y,float z,float w,float h,float d,int material) =>
            AddBox(mesh,new Vector3(x,y,z),new Vector3(w,h,d),material);
        void Tube(float x,float y,float z,float radius,float height,int material,int sides=16) =>
            Cylinder(mesh,new Vector3(x,y,z),radius,height,material,sides);
        switch (kind) {
            case 0: // Magazine: curved body, recessed ribs, floor plate and exposed top round.
                for (int j=0;j<5;j++) Box(.025f*j*j/5,-.32f+j*.14f,0,.34f,.15f,.20f,1);
                for (int j=0;j<5;j++) for (int side=-1;side<=1;side+=2)
                    Box(.025f*j*j/5,-.32f+j*.14f,side*.108f,.27f,.035f,.014f,0);
                Box(0,-.42f,0,.4f,.075f,.25f,4); Box(.08f,.36f,0,.38f,.06f,.22f,0);
                Cylinder(mesh,new Vector3(.08f,.403f,0),.055f,.27f,2,16,true);
                Box(.08f,.397f,.085f,.32f,.055f,.028f,1);Box(.08f,.397f,-.085f,.32f,.055f,.028f,1); break;
            case 1: // A single twelve-gauge shell, with brass rim, primer and crimped mouth.
                Tube(0,0,0,.16f,.62f,3); Tube(0,-.29f,0,.169f,.14f,2);
                Tube(0,-.37f,0,.185f,.035f,2); Tube(0,-.393f,0,.05f,.015f,0);
                Tube(0,.322f,0,.145f,.025f,1);
                for(int j=0;j<8;j++) { float a=j*MathF.Tau/8;Box(MathF.Cos(a)*.115f,.344f,MathF.Sin(a)*.115f,.045f,.035f,.045f,3); }
                break;
            case 2: // Machined billet with raised rail and bolt holes represented by dark insets.
                Box(0,-.06f,0,.86f,.25f,.42f,0); Box(0,.115f,0,.68f,.10f,.24f,1);
                for(int j=0;j<5;j++) Box(-.27f+j*.135f,.185f,0,.085f,.045f,.32f,0);
                Tube(-.32f,.073f,.13f,.04f,.02f,1); Tube(.32f,.073f,.13f,.04f,.02f,1); break;
            case 3: // Mechanism: receiver plate, gear with teeth, spring and actuator.
                Box(0,-.15f,0,.78f,.12f,.47f,1); Tube(-.19f,-.025f,0,.18f,.13f,0);
                Tube(-.19f,.05f,0,.065f,.04f,2);
                for(int j=0;j<12;j++){float a=j*MathF.Tau/12;Box(-.19f+MathF.Cos(a)*.185f,-.02f,MathF.Sin(a)*.185f,.07f,.09f,.07f,0);}
                Box(.17f,-.005f,0,.18f,.16f,.12f,2);
                for(int j=0;j<8;j++) Box(.03f+j*.043f,.10f,0,.02f,.045f,.18f,0);
                Box(.3f,.02f,.15f,.10f,.18f,.09f,0); break;
            case 4: // Grip panels and tang, ribbed rubber instead of a flat leather icon.
                Box(0,0,0,.32f,.64f,.22f,4); Box(0,.37f,0,.16f,.15f,.12f,0);
                for(int j=0;j<6;j++) Box(0,-.25f+j*.10f,.125f,.34f,.035f,.025f,1);
                Tube(0,-.37f,0,.12f,.09f,0); break;
            case 5: // Optics module: short cylindrical lens stack and mount.
                Tube(0,.04f,0,.235f,.56f,1,24); Tube(0,.34f,0,.25f,.06f,0,24);
                Tube(0,.375f,0,.20f,.018f,5,24); Tube(0,-.27f,0,.25f,.06f,4,24);
                Box(.255f,0,0,.12f,.15f,.15f,0); Box(0,-.13f,.27f,.36f,.18f,.10f,1); break;
            case 6: // Bench, coordinates relative to cell centre.
                Box(0,.28f,0,.98f,.12f,.86f,0); Box(-.06f,.35f,-.03f,.65f,.025f,.60f,6);
                foreach(float x in new[]{-.39f,.39f}) foreach(float z in new[]{-.31f,.31f}) Box(x,-.145f,z,.10f,.71f,.10f,1);
                Box(0,-.32f,0,.86f,.065f,.68f,1); Box(-.25f,.02f,0,.28f,.37f,.63f,7);
                for(int j=0;j<3;j++){Box(-.25f,-.10f+j*.115f,.324f,.265f,.095f,.018f,0);Box(-.25f,-.10f+j*.115f,.342f,.10f,.018f,.02f,1);}
                Box(.30f,.39f,.12f,.24f,.10f,.27f,1); Box(.22f,.46f,.12f,.06f,.09f,.29f,0);
                Box(.38f,.46f,.12f,.06f,.09f,.29f,0); Box(-.13f,.382f,-.08f,.30f,.03f,.045f,0);
                Box(-.28f,.382f,-.08f,.075f,.03f,.12f,0); Box(-.02f,.385f,.16f,.25f,.035f,.045f,4);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return mesh;
    }
    static void Face(BlockMesh mesh,Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material,Vector3 normal,Vector2[] localUv=null) {
        int n=mesh.Vertices.Count;
        float light=.62f+.38f*Math.Max(0,Vector3.Dot(normal,Vector3.Normalize(new Vector3(-1,2,1))));
        Color color=new Color(light,light,light);
        float u=(material%4)*.25f+.008f,v=(material/4)*.5f+.008f;
        Vector2[] uv=[new(u,v+.484f),new(u+.234f,v+.484f),new(u+.234f,v),new(u,v)];
        if(localUv is not null) for(int i=0;i<4;i++) uv[i]=new Vector2(u+localUv[i].X*.234f,v+localUv[i].Y*.484f);
        Vector3[] points=[a,b,c,d];
        for(int i=0;i<4;i++) mesh.Vertices.Add(new BlockMeshVertex {Position=points[i],TextureCoordinates=uv[i],Color=color,Face=(byte)CellFace.Vector3ToFace(normal)});
        // DrawMeshBlock uses clockwise fronts. Include the reverse face as well,
        // keeping the same atlas/light on thin details and terrain viewpoints.
        foreach(int i in new[]{0,1,2,0,2,3,2,1,0,3,2,0}) mesh.Indices.Add((ushort)(n+i));
    }
    static void AddBox(BlockMesh m,Vector3 p,Vector3 size,int mat) {
        Vector3 l=p-size*.5f,h=p+size*.5f;
        Face(m,new(l.X,l.Y,h.Z),new(h.X,l.Y,h.Z),new(h.X,h.Y,h.Z),new(l.X,h.Y,h.Z),mat,Vector3.UnitZ);
        Face(m,new(h.X,l.Y,l.Z),new(l.X,l.Y,l.Z),new(l.X,h.Y,l.Z),new(h.X,h.Y,l.Z),mat,-Vector3.UnitZ);
        Face(m,new(l.X,l.Y,l.Z),new(l.X,l.Y,h.Z),new(l.X,h.Y,h.Z),new(l.X,h.Y,l.Z),mat,-Vector3.UnitX);
        Face(m,new(h.X,l.Y,h.Z),new(h.X,l.Y,l.Z),new(h.X,h.Y,l.Z),new(h.X,h.Y,h.Z),mat,Vector3.UnitX);
        Face(m,new(l.X,h.Y,h.Z),new(h.X,h.Y,h.Z),new(h.X,h.Y,l.Z),new(l.X,h.Y,l.Z),mat,Vector3.UnitY);
        Face(m,new(l.X,l.Y,l.Z),new(h.X,l.Y,l.Z),new(h.X,l.Y,h.Z),new(l.X,l.Y,h.Z),mat,-Vector3.UnitY);
    }
    static void Cylinder(BlockMesh m,Vector3 p,float r,float h,int mat,int sides,bool sideways=false) {
        for(int i=0;i<sides;i++) {
            float a=i*MathF.Tau/sides,b=(i+1)*MathF.Tau/sides;
            Vector3 axis=sideways?Vector3.UnitX:Vector3.UnitY;
            Vector3 x=sideways?new(0,MathF.Cos(a)*r,MathF.Sin(a)*r):new(MathF.Cos(a)*r,0,MathF.Sin(a)*r);
            Vector3 z=sideways?new(0,MathF.Cos(b)*r,MathF.Sin(b)*r):new(MathF.Cos(b)*r,0,MathF.Sin(b)*r),up=axis*h*.5f;
            Vector2 center=new(.5f),ua=new(.5f+.5f*MathF.Cos(a),.5f+.5f*MathF.Sin(a)),ub=new(.5f+.5f*MathF.Cos(b),.5f+.5f*MathF.Sin(b));
            Face(m,p+x-up,p+z-up,p+z+up,p+x+up,mat,Vector3.Normalize(x+z));
            Face(m,p+up,p+x+up,p+z+up,p+up,mat,axis,[center,ua,ub,center]);
            Face(m,p-up,p+z-up,p+x-up,p-up,mat,-axis,[center,ub,ua,center]);
        }
    }
}

// This base has no static Index, so the API still allocates each concrete block dynamically.
public abstract class ScSupplyBlock : ScNoDurabilityBlock {
    protected ScSupplyBlock() {
        FirstPersonScale = .3f; FirstPersonOffset = new(.4f, -.3f, -.55f); FirstPersonRotation = new(0, 25, 0);
        InHandScale = .4f; InHandOffset = new(0, .1f, -.2f);
    }
    readonly Dictionary<int,BlockMesh> m_meshes=[];
    protected abstract int MeshKind(int value);
    public override Texture2D GetDefaultTexture(int value) => ContentManager.Get<Texture2D>(ScSurvivalMesh.Texture);
    public override Vector3 GetIconViewOffset(int value,DrawBlockEnvironmentData env) => new(1.1f,.8f,2);
    public override void DrawBlock(PrimitivesRenderer3D renderer,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env) {
        int kind=MeshKind(value);
        if(!m_meshes.TryGetValue(kind,out var mesh)) m_meshes[kind]=mesh=ScSurvivalMesh.Build(kind);
        BlocksManager.DrawMeshBlock(renderer,mesh,GetDefaultTexture(value),color,size,ref matrix,env);
    }
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int value,int x,int y,int z) { }
}
