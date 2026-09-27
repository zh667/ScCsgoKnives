using System.Runtime.CompilerServices;
using Engine.Graphics;
namespace Game;

/// <summary>Render closure: all body bones, all skin/mesh joints, held prop and parents.
/// Sampling and public full hierarchy traversal keep every bone and original index.</summary>
public static class ScActorRenderBones {
    static readonly ConditionalWeakTable<Model,Dictionary<string,ModelBone[]>> cache=new();
    public static void Clear()=>cache.Clear();
    public static ModelBone[] For(Model model,string asset) {
        asset=ScAgentActions.WorldAsset(asset)??"";
        var entries=cache.GetValue(model,static _=>new());
        if(entries.TryGetValue(asset,out var result))return result;
        var needed=new bool[model.Bones.Count];
        void Add(ModelBone bone){while(bone!=null){needed[bone.Index]=true;bone=bone.ParentBone;}}
        string prefix="cswp_"+asset+"/";
        foreach(var bone in model.Bones)
            if(!bone.Name.StartsWith("cswp_",StringComparison.Ordinal)||bone.Name.StartsWith(prefix,StringComparison.Ordinal))Add(bone);
        if(model.Skin!=null)foreach(var joint in model.Skin.Joints)Add(joint);
        foreach(var mesh in model.Meshes)Add(mesh.ParentBone);
        var list=new List<ModelBone>();
        void Visit(ModelBone bone){if(needed[bone.Index])list.Add(bone);foreach(var child in bone.ChildBones)Visit(child);}
        Visit(model.RootBone);result=list.ToArray();entries.Add(asset,result);return result;
    }
}
