using System.Reflection;
using System.Text.Json;
using Engine;
namespace Game;

public static class ScC4Visuals {
    sealed class Digit { public float At {get;set;} public float Row {get;set;} }
    sealed class Data {
        public Dictionary<string,Dictionary<string,float[]>> WorldPoses {get;set;}
        public Digit[] Digits {get;set;}
    }
    static readonly Data data=JsonSerializer.Deserialize<Data>(Assembly.GetExecutingAssembly().GetManifestResourceStream("Game.AnimationData.c4_visuals.json"));
    public static Vector2 ScreenOffset(string clip,float seconds) {
        if(clip is not ("plant" or "plant_c4"))return Vector2.Zero;
        return new Vector2(0,data.Digits.LastOrDefault(d=>d.At<=seconds)?.Row/16f??0);
    }
    public static Cs2Rig.Pose WorldPose(bool planted) => new() {
        Gun="c4",Clip=planted?"planted":"dropped",Parts=[],
        Bones=data.WorldPoses[planted?"planted":"dropped"].ToDictionary(p=>p.Key,p=>{
            var a=p.Value;return new Matrix(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);
        })
    };
    public static ScThirdPersonWeapon.Group[] WorldGroups(bool planted) {
        var skin=Cs2SkinnedMesh.Weapon("c4");var pose=WorldPose(planted);
        if(!skin.SetPose(pose,Matrix.CreateScale(Cs2Placement.InchesToEngine))||skin.UnresolvedWeight(pose)>0)throw new InvalidOperationException("C4 world pose has unmapped vertices");
        skin.Skin();var vertices=skin.Skinned;
        Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
        foreach(var v in vertices){lo=Vector3.Min(lo,v.Position);hi=Vector3.Max(hi,v.Position);}
        Vector3 center=new((lo.X+hi.X)/2,lo.Y,(lo.Z+hi.Z)/2);
        return skin.Primitives.Select(part=>{
            var mesh=new BlockMesh();var map=new Dictionary<int,ushort>();
            foreach(int i in part.Indices) {
                if(!map.TryGetValue(i,out ushort mapped)) {
                    mapped=checked((ushort)mesh.Vertices.Count);map[i]=mapped;var v=vertices[i];
                    mesh.Vertices.Add(new BlockMeshVertex{Position=v.Position-center,TextureCoordinates=v.TextureCoordinate+(part.Material=="weapon_c4_digits"&&planted?ScreenOffset("plant",3.2f):Vector2.Zero),Color=Color.White});
                }
                mesh.Indices.Add(mapped);
            }
            return new ScThirdPersonWeapon.Group(mesh,part.Material=="weapon_c4_digits"?part.Material:"c4_cs2");
        }).ToArray();
    }
}
