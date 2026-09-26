using System.IO;
using Engine;
using Engine.Graphics;
namespace Game;

// Lossless, offline-derived rigid world geometry. Bounded by the published gun catalogue,
// not by visible actors: a crowd never evicts the next actor's weapon.
public sealed class ScNpcWeaponGeometry {
    const int Magic=0x314D574E;
    static readonly ScResourceCache<string,ScNpcWeaponGeometry> cache=new("npc-weapons",80);
    public string Asset {get;private set;}
    public bool HasRightGrip {get;private set;}
    public Matrix WorldRootInverse {get;private set;}
    public ScThirdPersonWeapon.Group[] Groups {get;private set;}
    public static void Clear()=>cache.Clear();
    public static string PathFor(string asset,bool legacy)=>"Models/ScCsgoTactical/Weapons/"+asset+(legacy?"-legacy":"")+".scmesh";
    public static ScNpcWeaponGeometry Wrap(ScThirdPersonWeapon source)=>source==null?null:new(){Asset=source.Asset,HasRightGrip=source.HasRightGrip,WorldRootInverse=source.WorldRootInverse,Groups=source.Groups};
    public static ScNpcWeaponGeometry For(string asset,bool legacy=false){
        string key=asset+(legacy?"-legacy":"");
        if(cache.TryGetValue(key,out var hit))return hit;
        try { // ContentManager owns this stream, including its rewind/lifetime.
            hit=Read(ContentManager.GetStream(PathFor(asset,legacy)),asset,legacy);
        }catch(Exception e){
            KnifeDiagnostics.WarnOnce("npc-mesh-"+key,$"NPC mesh cache {key}: {e.Message}; using original geometry.");
            if(legacy&&ScThirdPersonWeapon.ObjProvider==null){
                try{foreach(var part in ScGunNativeMesh.Parts(asset))part.Model=ContentManager.Get<ObjModel>(ScGunNativeMesh.ModelPath(asset,part));}
                catch(Exception load){KnifeDiagnostics.WarnOnce("npc-native-"+key,load.Message);return null;}
            }
            hit=Wrap(ScThirdPersonWeapon.For(asset,legacy));
        }
        if(hit!=null)cache[key]=hit;
        return hit;
    }
    static void MatrixWrite(BinaryWriter w,Matrix m){foreach(float v in System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix,float>(new[]{m}.AsSpan()))w.Write(v);}
    static Matrix MatrixRead(BinaryReader r){Span<float> values=stackalloc float[16];for(int i=0;i<16;i++)values[i]=Finite(r);return System.Runtime.InteropServices.MemoryMarshal.Cast<float,Matrix>(values)[0];}
    static float Finite(BinaryReader r){float n=r.ReadSingle();if(!float.IsFinite(n))throw new InvalidDataException("Nonfinite mesh value");return n;}
    static string Text(BinaryReader r){int n=r.ReadInt32();if(n is <0 or >256)throw new InvalidDataException("Mesh name length");var b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return System.Text.Encoding.UTF8.GetString(b);}
    static void Text(BinaryWriter w,string s){var b=System.Text.Encoding.UTF8.GetBytes(s??"");w.Write(b.Length);w.Write(b);}
    public static void Write(Stream stream,ScThirdPersonWeapon source,bool legacy){
        using var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true);
        w.Write(Magic);Text(w,source.Asset);w.Write(legacy);w.Write(source.HasRightGrip);MatrixWrite(w,source.WorldRootInverse);w.Write(source.Groups.Length);
        foreach(var g in source.Groups){
            if(g.VertexBones!=null)throw new InvalidDataException("Nonrigid gun group");
            Text(w,g.Texture);w.Write(g.Silencer);Text(w,g.Bone);MatrixWrite(w,g.BindInverse);Text(w,g.WorldBone);MatrixWrite(w,g.WorldInverse);
            w.Write(g.Mesh.Vertices.Count);w.Write(g.Mesh.Indices.Count);
            foreach(var v in g.Mesh.Vertices){w.Write(v.Position.X);w.Write(v.Position.Y);w.Write(v.Position.Z);w.Write(v.TextureCoordinates.X);w.Write(v.TextureCoordinates.Y);w.Write(v.Color.PackedValue);w.Write(v.Face);w.Write(v.IsEmissive);}
            foreach(int i in g.Mesh.Indices)w.Write(i);
        }
    }
    public static ScNpcWeaponGeometry Read(Stream stream,string asset,bool legacy){
        using var r=new BinaryReader(stream,System.Text.Encoding.UTF8,true);
        if(r.ReadInt32()!=Magic||Text(r)!=asset||r.ReadBoolean()!=legacy)throw new InvalidDataException("Mesh cache identity");
        var result=new ScNpcWeaponGeometry{Asset=asset,HasRightGrip=r.ReadBoolean(),WorldRootInverse=MatrixRead(r)};
        int count=r.ReadInt32();if(count is <1 or >256)throw new InvalidDataException("Mesh group count");
        result.Groups=new ScThirdPersonWeapon.Group[count];int vertices=0,indices=0;
        for(int g=0;g<count;g++){
            string texture=Text(r);bool silencer=r.ReadBoolean();string bone=Text(r);var bind=MatrixRead(r);string worldBone=Text(r);var inverse=MatrixRead(r);
            int nv=r.ReadInt32(),ni=r.ReadInt32();
            if(nv is <0 or >500000||ni is <0 or >3000000||ni%3!=0||(vertices+=nv)>500000||(indices+=ni)>3000000)throw new InvalidDataException("Mesh allocation limit");
            var mesh=new BlockMesh();mesh.Vertices.Count=nv;mesh.Indices.Count=ni;
            for(int v=0;v<nv;v++)mesh.Vertices.Array[v]=new BlockMeshVertex{Position=new(Finite(r),Finite(r),Finite(r)),TextureCoordinates=new(Finite(r),Finite(r)),Color=new Color{PackedValue=r.ReadUInt32()},Face=r.ReadByte(),IsEmissive=r.ReadBoolean()};
            for(int i=0;i<ni;i++){int index=r.ReadInt32();if(index<0||index>=nv)throw new InvalidDataException("Mesh index range");mesh.Indices.Array[i]=index;}
            result.Groups[g]=new(mesh,texture,silencer,bone.Length==0?null:bone,bind){WorldBone=worldBone.Length==0?null:worldBone,WorldInverse=inverse};
        }
        if(r.BaseStream.ReadByte()!=-1)throw new InvalidDataException("Trailing mesh bytes");
        return result;
    }
}
