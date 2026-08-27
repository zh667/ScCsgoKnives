using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Draws two independently animated copies of Survivalcraft's own first-person
/// hand model. The player's inner clothing texture is used, matching the proven
/// approach used by classic SC firearm mods without redistributing their models.
/// </summary>
public static class FirstPersonKnifeArmsRenderer {
    static Model s_sourceModel;
    static BlockMesh s_rightArm;
    static BlockMesh s_leftArm;

    public static void Draw(
        ComponentFirstPersonModel model,
        Camera camera,
        Matrix sharedMovement,
        KnifeFramePose pose
    ) {
        EnsureMeshes(model.m_handModel);
        if (s_rightArm is null || s_leftArm is null) return;

        Vector3 eyePosition = model.m_componentPlayer.ComponentCreatureModel.EyePosition;
        Matrix eye = Matrix.CreateFromQuaternion(model.m_componentPlayer.ComponentCreatureModel.EyeRotation);
        eye.Translation = eyePosition;
        Matrix lag = Matrix.CreateFromYawPitchRoll(model.m_lagAngles.X, model.m_lagAngles.Y, 0f);

        // The default SC hand already has the correct UVs and player skin. Two
        // camera-space placements turn it into CS-style forearms, while the
        // imported keyframes move each arm independently.
        Matrix right = Matrix.CreateRotationX(0.82f)
            * Matrix.CreateRotationY(0.36f)
            * pose.RightHand
            * sharedMovement
            * Matrix.CreateTranslation(0.31f, -0.34f, -0.10f)
            * lag * eye * camera.ViewMatrix;
        Matrix left = Matrix.CreateRotationX(0.86f)
            * Matrix.CreateRotationY(-0.46f)
            * pose.LeftHand
            * sharedMovement
            * Matrix.CreateTranslation(-0.25f, -0.37f, -0.12f)
            * lag * eye * camera.ViewMatrix;

        DrawBlockEnvironmentData environment = model.m_drawBlockEnvironmentData;
        environment.DrawBlockMode = DrawBlockMode.FirstPerson;
        environment.SubsystemTerrain = model.m_subsystemTerrain;
        environment.InWorldMatrix = Matrix.Identity;
        environment.ViewProjectionMatrix = null;
        environment.Light = model.m_itemLight;
        environment.Owner = model.Entity;
        Texture2D skin = model.m_componentPlayer.ComponentClothing.InnerClothedTexture
            ?? model.m_componentPlayer.ComponentCreatureModel.TextureOverride;

        BlocksManager.DrawMeshBlock(model.m_primitivesRenderer, s_leftArm, skin, Color.White, 0.0105f, ref left, environment);
        BlocksManager.DrawMeshBlock(model.m_primitivesRenderer, s_rightArm, skin, Color.White, 0.0105f, ref right, environment);
    }

    static void EnsureMeshes(Model model) {
        if (model is null || ReferenceEquals(model, s_sourceModel)) return;
        s_sourceModel = model;
        s_rightArm = new BlockMesh();
        s_leftArm = new BlockMesh();
        foreach (ModelMesh mesh in model.Meshes) {
            Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
            foreach (ModelMeshPart part in mesh.MeshParts) {
                s_rightArm.AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
                s_leftArm.AppendModelMeshPart(part, transform * Matrix.CreateScale(-1f, 1f, 1f), false, true, true, false, Color.White);
            }
        }
    }
}
