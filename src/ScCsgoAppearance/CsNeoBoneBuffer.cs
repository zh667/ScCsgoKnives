using System.Reflection;
using Engine;
using Engine.Graphics;
using Neorxna.Components;
namespace Game;

/// <summary>Synchronize the companion buffer owned by NEO, without changing its code.</summary>
public static class CsNeoBoneBuffer {
    static readonly PropertyInfo property=typeof(ComponentRigidBody).GetProperty("AbsoluteBoneTransforms");
    public static void Ensure(ComponentModel component){
        if(component?.Entity?.FindComponent<ComponentCsPlayerAppearance>() is not {} adapter||
            !ReferenceEquals(adapter.TargetComponent,component)||component.Model is not {} model)return;
        var rigid=component.Entity.FindComponent<ComponentRigidBody>();
        if(rigid==null)return;
        int needed=Math.Max(model.Bones.Count,model.Meshes.Count);
        foreach(var bone in model.Bones)needed=Math.Max(needed,checked(bone.Index+1));
        if(needed<=0||rigid.AbsoluteBoneTransforms?.Length>=needed)return;
        var previous=rigid.AbsoluteBoneTransforms;
        var expanded=new Matrix[needed];
        if(previous!=null)Array.Copy(previous,expanded,previous.Length);
        // The setter is internal in official Neo 1.4; use only this named property.
        // Do not alias a private obfuscated field or replace any third-party method.
        property.SetValue(rigid,expanded);
        KnifeLog.Information($"[CS_NEO] bone-buffer resized old={previous?.Length??0} new={needed} modelBones={model.Bones.Count}");
    }
}
