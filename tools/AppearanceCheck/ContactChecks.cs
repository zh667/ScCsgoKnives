using System.Text.Json;
using Engine;
using Engine.Graphics;
using Game;

static class ContactChecks {
    public static void Run(string root,string name,Model model,ComponentHumanModel human,ComponentBody body,Action<string,bool> check){
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tools/fixtures/world-prop-contacts.json")));
        int frame=80000;float maxDistance=0,maxAngle=0;
        var pose=new CsPlayerPose(model);
        foreach(var row in fixture.RootElement.EnumerateArray()){
            if(row.GetProperty("model").GetString()!=name)continue;
            string asset=row.GetProperty("asset").GetString(),clip=row.GetProperty("clip").GetString();float phase=row.GetProperty("phase").GetSingle();
            var kind=clip.StartsWith("hold_")?ScWeaponActionKind.Idle:clip.StartsWith("draw_")?ScWeaponActionKind.Draw:ScWeaponActionKind.Reload;
            var action=new ScWeaponAction(asset,kind,clip.StartsWith("reloadEmpty_")?"reloadEmpty":"reload",1,phase*3,3,phase*3);
            pose.Sample(frame++,.1f,1.2f,true,false,0,0,action,asset);
            Array.Copy(pose.Local,human.m_boneTransforms,pose.Local.Length);human.m_boneTransforms[model.RootBone.Index]*=body.Matrix;
            human.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,human.AbsoluteBoneTransformsForCamera);
            foreach(var expected in row.GetProperty("expected").EnumerateArray()){
                string bone=expected.GetProperty("bone").GetString(),hand=expected.GetProperty("hand").GetString();
                var prop=pose.Actions.PropFrame(asset,bone,human.AbsoluteBoneTransformsForCamera);
                prop.Decompose(out _,out var rotation,out var position);
                prop=Matrix.CreateFromQuaternion(rotation)*Matrix.CreateTranslation(position);
                var actual=human.AbsoluteBoneTransformsForCamera[model.FindBone(hand).Index]*Matrix.Invert(prop);
                float[] m=expected.GetProperty("matrix").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                var reference=new Matrix(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
                float distance=Vector3.Distance(actual.Translation,reference.Translation);
                actual.Decompose(out _,out var qa,out _);reference.Decompose(out _,out var qr,out _);
                float dot=Math.Abs(qa.X*qr.X+qa.Y*qr.Y+qa.Z*qr.Z+qa.W*qr.W);float angle=2*MathF.Acos(Math.Clamp(dot,0,1));
                maxDistance=Math.Max(maxDistance,distance);maxAngle=Math.Max(maxAngle,angle);
                check($"source contact {name}/{clip}@{phase}/{hand}/{bone}: {distance:0.0000}m {angle:0.0000}rad actual {actual.Translation} reference {reference.Translation}",distance<.025f&&angle<.18f);
            }
        }
        Console.WriteLine($"{name} world contact max error {maxDistance:0.00000}m / {maxAngle:0.00000}rad");
    }
}
