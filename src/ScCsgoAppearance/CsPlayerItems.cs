using Engine;
using Engine.Graphics;
namespace Game;

public static class CsPlayerItems {
    public static void Draw(ComponentHumanModel human, Camera camera, int value) {
        var hand = human.Model.FindBone("hand_R", false);
        if (hand == null) return;
        var handWorld = human.AbsoluteBoneTransformsForCamera[hand.Index] * camera.InvertedViewMatrix;
        var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        string asset = ScThirdPerson.AssetFor(value, out var stance);
        int skin = stance == ScThirdPersonStance.Knife ? ScKnifeBlock.SkinOf(value) : ScGunBlock.SpecOf(value) != null ? ScGunBlock.SkinOf(value) : 0;
        Texture2D gunTexture = null;
        bool native = asset != null && stance != ScThirdPersonStance.Knife && stance != ScThirdPersonStance.Grenade && ScGunNativeMesh.Resolve(asset, skin, out gunTexture, out _) != null;
        var weapon = asset == null ? null : ScThirdPersonWeapon.For(asset, native);
        var world = weapon?.HasRightGrip == true
            ? human.Entity.FindComponent<ComponentCsPlayerAppearance>().Pose.Actions.WeaponWorld(weapon.GripRight, handWorld)
            : Matrix.CreateFromYawPitchRoll(MathUtils.DegToRad(block.GetInHandRotation(value).Y), MathUtils.DegToRad(block.GetInHandRotation(value).X), MathUtils.DegToRad(block.GetInHandRotation(value).Z)) * handWorld;
        var pos = world.Translation;
        var env = new DrawBlockEnvironmentData { DrawBlockMode = DrawBlockMode.ThirdPerson, Owner = human.Entity, InWorldMatrix = world,
            SubsystemTerrain = human.m_subsystemTerrain, Light = human.m_subsystemTerrain.Terrain.GetCellLight(Terrain.ToCell(pos.X), Terrain.ToCell(pos.Y), Terrain.ToCell(pos.Z)) };
        var view = world * camera.ViewMatrix;
        var renderer = human.m_subsystemModelsRenderer.PrimitivesRenderer;
        if (weapon == null) { block.DrawBlock(renderer, value, Color.White, block.GetInHandScale(value), ref view, env); return; }
        bool silencerOff = ScGunBlock.SpecOf(value) is { HasSilencer: true } && GunSpec.GetSilencerOff(Terrain.ExtractData(value));
        var action=KnifeAnimationController.ReadAction(human.Entity.FindComponent<ComponentFirstPersonModel>());
        var weaponPose=weapon.ActionPose(action);
        foreach (var group in weapon.Groups) {
            if(!weapon.ShowPart(group,weaponPose,action))continue;
            if (group.Silencer && silencerOff) continue;
            var texture = group.Texture == asset + "_hd" ? gunTexture : ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" +
                (group.Texture == asset + "_cs2" && stance == ScThirdPersonStance.Knife ? ScKnifeSkinCatalog.Texture(asset, skin, ScKnifeBlock.GetVariant(value)) : group.Texture));
            var partView=weapon.PartTransform(group,weaponPose)*view;
            if (native) ScGunNativeMesh.DrawWorld(renderer, group.Mesh, texture, Color.White, 1, ref partView, env);
            else BlocksManager.DrawMeshBlock(renderer, group.Mesh, texture, Color.White, 1, ref partView, env);
        }
        ScStatTrakRenderer.DrawThirdPerson(value, asset, native, view, renderer, LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light, 0, 15)]);
    }
}
