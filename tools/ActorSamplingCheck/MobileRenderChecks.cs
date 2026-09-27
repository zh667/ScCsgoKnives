using Engine;
using Engine.Graphics;
using Game;
using System.Diagnostics;

static class MobileRenderChecks {
    public static void Run(string role,Model model,Func<bool,ComponentTacticalModel> create,Action<string,bool> check,List<object> rows){
        var pose=create(true);pose.Animate();
        var enemy=pose.Entity.FindComponent<ComponentTacticalEnemy>();
        var view=Matrix.CreateLookAt(new(1,2,3),new(4,1,-5),Vector3.UnitY);
        var expected=new Matrix[model.Bones.Count];
        int minimum=model.Bones.Count,maximum=0;
        for(int variant=0;variant<GunSpec.All.Length;variant++){
            enemy.State.Variant=variant;
            var selected=ScActorRenderBones.For(model,GunSpec.All[variant].Name);
            minimum=Math.Min(minimum,selected.Length);maximum=Math.Max(maximum,selected.Length);
            check(role+" skin closure "+variant,model.Skin.Joints.All(j=>selected.Contains(j)));
            check(role+" reduced prop bones "+variant,selected.Length<model.Bones.Count/2);
            foreach(float scale in new[]{.7f,1f,1.3f}){
                pose.ModelScale=scale;
                pose.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,expected);
                pose.CalculateRenderBones(view);
                check(role+" camera matrices "+variant+"/"+scale,selected.All(b=>pose.AbsoluteBoneTransformsForCamera[b.Index]==expected[b.Index]*view));
            }
        }
        // Warm both routes before measuring; only the camera hierarchy differs.
        pose.ModelScale=1;
        foreach(bool selected in new[]{false,true}){
            void Run(){
                if(selected)pose.CalculateRenderBones(view);
                else{pose.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,pose.AbsoluteBoneTransformsForCamera);for(int j=0;j<expected.Length;j++)pose.AbsoluteBoneTransformsForCamera[j]*=view;}
            }
            for(int i=0;i<100;i++)Run();
            var watch=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<3000;i++)Run();
            rows.Add(new{stage="render-bones",role,selected,calls=3000,minBones=minimum,maxBones=maximum,total=model.Bones.Count,ms=watch.Elapsed.TotalMilliseconds,bytes=GC.GetAllocatedBytesForCurrentThread()-allocated});
        }
        ScActorRenderBones.Clear();
        check(role+" render closure rebuild",ScActorRenderBones.For(model,"ak47").Length<model.Bones.Count/2);
    }
}
