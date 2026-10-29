using Engine;
using Engine.Animation;
using Engine.Graphics;
using Engine.Media;
using Game;
using ZipArchive = System.IO.Compression.ZipArchive;

static class CounterChecks {
    public static void Run(ZipArchive package,Action<string,bool> check){
        float maxError=0,maxOldError=0;int oldFailures=0,samples=0;
        foreach(string role in new[]{"ct","t"}){
            using var input=package.GetEntry("Assets/Models/ScCsgoTactical/"+role+".glb").Open();
            using var buffer=new MemoryStream();input.CopyTo(buffer);buffer.Position=0;
            using var model=Model.Load(GltfLoader.Load(buffer),true);ScActorAnimations.Ensure(model);
            var actions=new ScAgentActions(model);var local=new Matrix?[model.Bones.Count];var absolute=new Matrix[model.Bones.Count];
            var basePlayer=new AnimationPlayer();
            foreach(var spec in GunSpec.All)foreach(bool legacy in new[]{false,true}){
                var entry=package.GetEntry("Assets/"+ScNpcWeaponGeometry.PathFor(spec.Name,legacy));
                if(entry==null){check($"counter/{role}/{spec.Name}/legacy-cache-not-required",legacy&&ScGunNativeMesh.Parts(spec.Name).Length==0);continue;}
                using var stream=entry.Open();var weapon=ScNpcWeaponGeometry.Read(stream,spec.Name,legacy);
                var placement=ScGunStatTrak.For(spec.Name,legacy);var idle=Cs2Rig.Sample(spec.Name,"idle",0);
                check($"counter/{role}/{spec.Name}/{legacy}/parent-present",actions.HasProp(spec.Name,placement.Bone));
                var group=weapon.Groups.FirstOrDefault(g=>g.WorldBone==placement.Bone);
                check($"counter/{role}/{spec.Name}/{legacy}/parent-matches-gun",group!=null);
                ScStatTrakRenderer.AttachmentWorld(spec.Name,legacy,idle,out var idleAttachment);
                Matrix baked=idleAttachment*ScThirdPersonWeapon.LocalPlacement(idle);
                foreach(string posture in new[]{"aim","run","crouch","jump"}){
                    var gait=model.Animations.FirstOrDefault(a=>a.Name==posture)??model.Animations.FirstOrDefault(a=>a.Name.Contains(posture));
                    check($"counter/{role}/{posture}/gait-present",gait!=null);basePlayer.SetAnimation(model,gait);basePlayer.Time=gait.Duration*.4f;
                    foreach(var kind in new[]{ScWeaponActionKind.Idle,ScWeaponActionKind.Draw,ScWeaponActionKind.Reload})foreach(float phase in new[]{.15f,.5f,.85f}){
                        Array.Clear(local);ScActorSampler.Sample(basePlayer,local);
                        string clip=kind==ScWeaponActionKind.Draw?"deploy":"reload";
                        var action=new ScWeaponAction(spec.Name,kind,clip,1,phase*3,3,phase*3);
                        actions.ApplyHeld(local,action,spec.Name);
                        Matrix Absolute(ModelBone bone)=>(local[bone.Index]??bone.Transform)*(bone.ParentBone==null
                            ?Matrix.CreateRotationY(.6f)*Matrix.CreateTranslation(13,7,-9):Absolute(bone.ParentBone));
                        foreach(var bone in model.Bones)absolute[bone.Index]=Absolute(bone);
                        foreach(var camera in new[]{Matrix.Identity,Matrix.CreateLookAt(new(15,9,-6),new(13,8,-9),Vector3.UnitY)}){
                            var inView=absolute.Select(m=>m*camera).ToArray();
                            var actual=ScStatTrakRenderer.ActorAttachment(placement,actions.PropFrame(spec.Name,placement.Bone,inView));
                            var expected=baked*actions.WorldPartFor(spec.Name,group,inView).Transform;
                            float error=Error(actual,expected);maxError=Math.Max(maxError,error);
                            check($"counter/{role}/{spec.Name}/{legacy}/{posture}/{kind}/{phase}/camera{(camera==Matrix.Identity?0:1)}",error<.0001f);
                            var old=baked*weapon.WorldRootInverse*actions.PropFrame(spec.Name,"weapon",inView);
                            float oldError=Vector3.Distance(old.Translation,expected.Translation);maxOldError=Math.Max(maxOldError,oldError);if(oldError>.01f)oldFailures++;samples++;
                        }
                    }
                }
            }
        }
        check("counter/old-root-only-path-reproduces-offset",oldFailures>0);
        Console.WriteLine($"Counter: {samples} samples, max matrix-point error {maxError:F7}m, old path >1cm in {oldFailures}, max old displacement {maxOldError:F4}m");
        foreach(string asset in new[]{"ak47","m4a1s","awp","elite","revolver"}){
            var weapon=ScThirdPersonWeapon.For(asset);check("counter/player/"+asset+"/geometry",weapon!=null);
            var idle=Cs2Rig.Sample(asset,"idle",0);var placement=ScGunStatTrak.For(asset,false);
            var group=weapon.Groups.First(g=>g.WorldBone==placement.Bone);
            ScStatTrakRenderer.AttachmentWorld(asset,false,idle,out var bind);
            var baked=bind*ScThirdPersonWeapon.LocalPlacement(idle);
            foreach(string clip in new[]{"idle","deploy","reload"})foreach(float phase in new[]{0f,.3f,.7f}){
                var pose=Cs2Rig.Sample(asset,clip,Cs2Rig.Duration(asset,clip)*phase);
                ScStatTrakRenderer.AttachmentWorld(asset,false,pose,out var attachment);
                check($"counter/player/{asset}/{clip}/{phase}",Error(attachment*weapon.RigPlacement(pose),baked*weapon.PartTransform(group,pose))<.0001f);
            }
            check("counter/drop/"+asset+"/idle-unchanged",Error(bind*weapon.RigPlacement(idle),baked)<.0001f);
        }
    }
    static float Error(Matrix a,Matrix b)=>new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ}.Max(p=>Vector3.Distance(Vector3.Transform(p,a),Vector3.Transform(p,b)));
}
