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
        var actions=human.Entity.FindComponent<ComponentCsPlayerAppearance>().Pose.Actions;
        var world = weapon?.HasRightGrip == true
            ? actions.RootWorld(weapon,human.AbsoluteBoneTransformsForCamera)*camera.InvertedViewMatrix
            : Matrix.CreateFromYawPitchRoll(MathUtils.DegToRad(block.GetInHandRotation(value).Y), MathUtils.DegToRad(block.GetInHandRotation(value).X), MathUtils.DegToRad(block.GetInHandRotation(value).Z)) * handWorld;
        var pos = world.Translation;
        var env = new DrawBlockEnvironmentData { DrawBlockMode = DrawBlockMode.ThirdPerson, Owner = human.Entity, InWorldMatrix = world,
            SubsystemTerrain = human.m_subsystemTerrain, Light = human.m_subsystemTerrain.Terrain.GetCellLight(Terrain.ToCell(pos.X), Terrain.ToCell(pos.Y), Terrain.ToCell(pos.Z)) };
        var view = world * camera.ViewMatrix;
        var renderer = human.m_subsystemModelsRenderer.PrimitivesRenderer;
        if (weapon == null) { block.DrawBlock(renderer, value, Color.White, block.GetInHandScale(value), ref view, env); return; }
        bool silencerOff = ScGunBlock.SpecOf(value) is { HasSilencer: true } && GunSpec.GetSilencerOff(Terrain.ExtractData(value));
        var action=KnifeAnimationController.ReadAction(human.Entity.FindComponent<ComponentFirstPersonModel>());
        foreach (var group in weapon.Groups) {
            if(!actions.ShowWorldPart(weapon,group,action))continue;
            if (group.Silencer && silencerOff) continue;
            var texture = group.Texture == asset + "_hd" ? gunTexture : ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" +
                (group.Texture == asset + "_cs2" && stance == ScThirdPersonStance.Knife ? ScKnifeSkinCatalog.Texture(asset, skin, ScKnifeBlock.GetVariant(value)) : group.Texture));
            var part=actions.WorldPart(weapon,group,human.AbsoluteBoneTransformsForCamera);
            var partView=part.Transform;
            if (native) ScGunNativeMesh.DrawWorld(renderer, part.Mesh, texture, Color.White, 1, ref partView, env);
            else BlocksManager.DrawMeshBlock(renderer, part.Mesh, texture, Color.White, 1, ref partView, env);
        }
        ScStatTrakRenderer.DrawThirdPerson(value, asset, native, view, renderer, LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light, 0, 15)]);
    }
}
