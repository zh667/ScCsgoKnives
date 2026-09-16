using System.Collections;
using System.Reflection;
using Engine;
using Game;

static class DropGeometryRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail="") => results.Add(new("gun-drop/"+name,ok,detail));
        try {
            var type=mod.GetType("Game.ScGunDropRenderer"); var weaponType=mod.GetType("Game.ScThirdPersonWeapon");
            var guns=(Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null);
            var native=mod.GetType("Game.ScGunNativeMesh");
            foreach(var gun in guns) {
                string asset=(string)gun.GetType().GetField("Name").GetValue(gun);
                foreach(bool legacy in new[]{false,true}) {
                    if(legacy && ((Array)native.GetMethod("Parts").Invoke(null,[asset])).Length==0)continue;
                    var weapon=weaponType.GetMethod("For").Invoke(null,[asset,legacy]);
                    Check("assembled/"+asset+"/"+legacy,weapon is not null); if(weapon is null)continue;
                    int Triangles(object w){int n=0;foreach(var g in (IEnumerable)weaponType.GetField("Groups").GetValue(w)){var m=(BlockMesh)g.GetType().GetProperty("Mesh").GetValue(g);n+=m.Indices.Count/3;}return n;}
                    var drop=weaponType.GetMethod("ForDrop").Invoke(null,[asset,legacy]);
                    int heldTriangles=Triangles(weapon),dropTriangles=drop is null?-1:Triangles(drop);
                    Check("drop-decimated/"+asset+"/"+legacy,drop is not null&&dropTriangles>0&&dropTriangles<=heldTriangles,
                        $"held {heldTriangles} triangles -> drop {dropTriangles}");
                    foreach(bool silencerOff in new[]{false,true}) {
                        var center=(Vector3)type.GetMethod("Center").Invoke(null,[drop,silencerOff]);
                        Vector3 min=new(float.MaxValue),max=new(float.MinValue);int triangles=0;
                        foreach(var group in (IEnumerable)weaponType.GetField("Groups").GetValue(drop)) {
                            if(silencerOff&&(bool)group.GetType().GetProperty("Silencer").GetValue(group))continue;
                            var mesh=(BlockMesh)group.GetType().GetProperty("Mesh").GetValue(group);triangles+=mesh.Indices.Count/3;
                            foreach(int index in mesh.Indices){var v=mesh.Vertices[index];min=Vector3.Min(min,v.Position);max=Vector3.Max(max,v.Position);}
                        }
                        Vector3 span=max-min;
                        float scale=(float)type.GetMethod("ScaleFor").Invoke(null,[span]);
                        float longest=Math.Max(span.X,Math.Max(span.Y,span.Z));
                        Check($"readable-drop/{asset}/{legacy}/{silencerOff}",longest*scale>=.439f && (longest>=.44f?scale==1:scale>1),$"visible indexed drop span={span}, scale={scale}");
                        Check($"solid-centred-metres/{asset}/{legacy}/{silencerOff}",triangles>100&&span.X>.002f&&span.Y>.002f&&span.Z>.002f
                            &&Math.Max(span.X,Math.Max(span.Y,span.Z)) is > .1f and < 2f && (center-(min+max)*.5f).Length()<1e-5f,
                            $"dimensions in metres={span}; triangles={triangles}; same unscaled mesh as held third-person gun");
                    }
                }
            }
            var item=new Pickable{Position=new Vector3(2,3,4),CreationTime=0};
            Matrix Frame(double t)=>(Matrix)type.GetMethod("Frame").Invoke(null,[item,t]);
            Matrix a=Frame(1),b=Frame(2);
            Check("rotates-without-camera-billboarding",(a.Right-b.Right).Length()>.5f&&Math.Abs(a.Right.Length()-1)<1e-6f&&a.Translation.X==2&&b.Translation.Z==4);
            item.StuckMatrix=Matrix.CreateTranslation(8,9,10);Check("stuck-transform-kept",Frame(3)==item.StuckMatrix.Value);
            var calls=CombatRegression.Calls(type.GetMethod("Draw")).ToArray();
            Check("draw-uses-drop-geometry-and-counter",calls.Any(c=>c.DeclaringType==weaponType&&c.Name=="ForDrop")
                &&calls.Any(c=>c.DeclaringType.Name=="ScStatTrakRenderer"&&c.Name=="DrawThirdPerson")
                &&!calls.Any(c=>c.Name=="DrawFlatBlock"));
        }catch(Exception e){Check("exception",false,e.ToString());}
        return results;
    }
}
