using System.Reflection;
using Engine;
using Game;
using Neorxna.Components;
using Neorxna.NeoModel;

static class NeoBufferChecks {
    public static void Run(ComponentHumanModel human,ComponentCsPlayerAppearance adapter,Action<string,bool> check){
        var rigid=new ComponentRigidBody();rigid.m_entity=human.Entity;human.Entity.m_components.Add(rigid);
        var property=typeof(ComponentRigidBody).GetProperty("AbsoluteBoneTransforms");
        var neo=new Neorxna.NeorxnaModLoader();var own=new AppearanceModLoader();
        foreach(string key in new[]{"fixture.default","zh667.cs.t","zh667.cs.ct","fixture.default","zh667.cs.ct"}){
            adapter.SetResModel(key);
            // Reproduce the precise crash: a nonempty old buffer remains after selecting
            // a larger model through the upstream nonvirtual entry point.
            property.SetValue(rigid,new Matrix[1]);
            bool reproduced=false;
            try{neo.OnModelCalculateBones(human,null,out _);}catch(IndexOutOfRangeException){reproduced=true;}
            check("actual Neo stale array reproduces "+key,reproduced);
            own.OnModelCalculateBones(human,null,out bool skip);
            check("own hook does not suppress Neo "+key,!skip);
            neo.OnModelCalculateBones(human,null,out _);
            check("actual Neo accepts repaired array "+key,rigid.AbsoluteBoneTransforms.Length>=human.Model.Bones.Count);
            var expected=new Matrix[human.Model.Bones.Count];
            human.ProcessBoneHierarchy(human.Model.RootBone,Matrix.Identity,expected);
            check("rigid world matrices exact "+key,expected.SequenceEqual(rigid.AbsoluteBoneTransforms.Take(expected.Length)));
            var retained=rigid.AbsoluteBoneTransforms;own.OnModelCalculateBones(human,null,out _);
            check("same model no allocation "+key,ReferenceEquals(retained,rigid.AbsoluteBoneTransforms));
            ((INeoModel)adapter).Animate();
        }
        human.Entity.m_components.Remove(rigid);
        // Verify the registered priority, not just direct invocation order.
        var previous=ModsManager.ModHooks.GetValueOrDefault("OnModelCalculateBones");
        ModsManager.ModHooks.Remove("OnModelCalculateBones");
        ModsManager.RegisterHook("OnModelCalculateBones",neo);
        own.__ModInitialize();ModsManager.DealWithTempModHooks();
        human.Entity.m_components.Add(rigid);property.SetValue(rigid,new Matrix[1]);
        int seen=0;
        ModsManager.HookAction("OnModelCalculateBones",loader=>{loader.OnModelCalculateBones(human,null,out _);seen++;return false;});
        check("registered own hook before actual Neo",seen>=2&&rigid.AbsoluteBoneTransforms.Length>=human.Model.Bones.Count);
        human.Entity.m_components.Remove(rigid);
        ModsManager.ModHooks.Remove("OnModelCalculateBones");
        if(previous!=null)ModsManager.ModHooks["OnModelCalculateBones"]=previous;
    }
}
