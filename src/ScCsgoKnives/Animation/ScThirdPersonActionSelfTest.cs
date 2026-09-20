using Engine;
namespace Game;

public static class ScThirdPersonActionSelfTest {
    public static void Run(Action<string,bool,string> check) {
        foreach(var spec in GunSpec.All)foreach(bool native in new[]{false,true}) {
            if(native&&ScGunNativeMesh.Parts(spec.Name).Length==0)continue;
            var w=ScThirdPersonWeapon.For(spec.Name,native);string id=$"third-actions/{spec.Name}/{native}";
            check(id+"/mesh",w!=null,"actual third-person geometry");if(w==null)continue;
            var idle=Cs2Rig.Sample(spec.Name,"idle",0);
            bool anchored=w.Groups.All(g=>Vector3.Distance(Vector3.Transform(g.Mesh.CalculateBoundingBox().Center(),w.PartTransform(g,idle)),g.Mesh.CalculateBoundingBox().Center())<.002f);
            check(id+"/idle-anchor",anchored,"moving part transform is identity at its baked pose");
            foreach(var kind in new[]{ScWeaponActionKind.Draw,ScWeaponActionKind.Reload,ScWeaponActionKind.Inspect}){
                string clip=kind==ScWeaponActionKind.Draw?"deploy":kind==ScWeaponActionKind.Reload?"reload":"inspect";
                if(!Cs2Rig.HasAlias(spec.Name,clip))continue;
                float seconds=Cs2Rig.Duration(spec.Name,clip);
                foreach(float phase in new[]{.1f,.3f,.6f,.9f}){
                    var action=new ScWeaponAction(spec.Name,kind,clip,1,phase*seconds,seconds,phase*seconds);var pose=w.ActionPose(action);
                    Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
                    foreach(var g in w.Groups){if(!w.ShowPart(g,pose,action))continue;var m=w.PartTransform(g,pose);var box=g.Mesh.CalculateBoundingBox();foreach(var p in new[]{box.Min,box.Max,box.Center()}){var q=Vector3.Transform(p,m);lo=Vector3.Min(lo,q);hi=Vector3.Max(hi,q);}}
                    check(id+$"/{kind}/{phase}",float.IsFinite(lo.X+hi.Y+hi.Z)&&(hi-lo).Length()<3.5f&&Vector3.Distance((hi+lo)/2,w.GripRight)<2.5f,$"bounds {lo} .. {hi}");
                }
            }
        }
    }
}
