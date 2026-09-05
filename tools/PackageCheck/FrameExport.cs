using System.Reflection;
using System.Text.Json;
using Engine;

/// <summary>Exports the packaged runtime's actual skinned vertices for offline visual checks.</summary>
static class FrameExport {
    internal static void Write(Assembly mod,string directory) {
        Directory.CreateDirectory(directory);
        var rig=mod.GetType("Game.Cs2Rig");var type=mod.GetType("Game.Cs2SkinnedMesh");
        object placement=mod.GetType("Game.Cs2Placement").GetMethod("Placement").Invoke(null,null);
        foreach (string asset in new[] {"grenade_hegrenade","grenade_flashbang","grenade_smokegrenade","grenade_molotov","grenade_incendiary","grenade_decoy"})
            foreach (string alias in new[] {"idle","pullpin"}) {
                float time=alias=="idle"?0:.6f;
                object pose=rig.GetMethod("Sample").Invoke(null,[asset,alias,time]);var meshes=new List<object>();
                foreach (bool arms in new[] {false,true}) {
                    var mesh=arms?type.GetProperty("Arms").GetValue(null):type.GetMethod("Weapon").Invoke(null,[asset]);
                    type.GetMethod("SetPose").Invoke(mesh,[pose,placement]);type.GetMethod("Skin").Invoke(mesh,null);
                    var vertices=new List<float[]>();
                    foreach (var v in (Array)type.GetProperty("Skinned").GetValue(mesh)) {
                        var vt=v.GetType();var p=(Vector3)vt.GetField("Position").GetValue(v);var n=(Vector3)vt.GetField("Normal").GetValue(v);var uv=(Vector2)vt.GetField("TextureCoordinate").GetValue(v);
                        vertices.Add([p.X,p.Y,p.Z,n.X,n.Y,n.Z,uv.X,uv.Y]);
                    }
                    var parts=new List<object>();
                    foreach (var part in (Array)type.GetField("Primitives").GetValue(mesh)) {
                        var pt=part.GetType();parts.Add(new {material=(string)pt.GetField("Material").GetValue(part),indices=(int[])pt.GetField("Indices").GetValue(part)});
                    }
                    meshes.Add(new {arms,vertices,parts});
                }
                File.WriteAllText(Path.Combine(directory,asset+"_"+alias+".json"),JsonSerializer.Serialize(new {asset,alias,time,meshes}));
            }
    }
}
