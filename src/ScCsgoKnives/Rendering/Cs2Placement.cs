using Engine;

namespace Game;

/// <summary>
/// Where CS2 puts the viewmodel, and how it projects it.
///
/// Measured from the clips (tools/cs2_placement.py --measure): root_motion is the
/// eye, the weapon's back end wpnEnd sits at x = 0.4 / -0.02 / 0.7 inches for the
/// AK / M4A1-S / AWP, the muzzle at x = +37.4 / +39.6 / +55.0 with y about -5 and
/// z about -3, and trigger->muzzle points along +x to within 0.05. That is Source
/// view space - x forward, y left, z up, origin at the eye - so the CS2 rig is
/// already posed in the camera's frame and needs no placement solve. All this
/// chain does is change axes, convert inches, apply the player's viewmodel_offset,
/// and build a projection from viewmodel_fov.
///
/// The cvars default to this machine's own CS2 config rather than CS2's defaults
/// (D:\steam\userdata\1415980225, "name" "zh667"): viewmodel_fov 68,
/// offset_x 2.5, offset_y 0, offset_z -1.5. KnifeTuning overrides all four.
///
/// One assumption, marked as such until the CS2 recording settles it: Source reads
/// `fov` as a horizontal angle defined at 4:3 and holds the vertical fixed as the
/// aspect widens, so fovY = 2*atan(tan(fovX/2) / (4/3)). 68 gives 53.668 degrees.
/// </summary>
public static class Cs2Placement {
    public const float InchesToEngine = 0.0254f;

    /// <summary>
    /// Source view space (x forward, y left, z up) to Engine's (x right, y up, z back),
    /// row-vector: x_fwd -> -z, y_left -> -x, z_up -> +y.
    /// </summary>
    static readonly Matrix s_axis = new() {
        M11 = 0f, M12 = 0f, M13 = -1f, M14 = 0f,
        M21 = -1f, M22 = 0f, M23 = 0f, M24 = 0f,
        M31 = 0f, M32 = 1f, M33 = 0f, M34 = 0f,
        M41 = 0f, M42 = 0f, M43 = 0f, M44 = 1f
    };

    /// <summary>Vertical field of view for a CS2 `viewmodel_fov`, by Source's Hor+ rule.</summary>
    public static float FovYDegrees(float viewmodelFov) {
        float half = MathF.Tan(MathUtils.DegToRad(MathUtils.Clamp(viewmodelFov, 10f, 179f)) * 0.5f);
        return MathUtils.RadToDeg(2f * MathF.Atan(half / (4f / 3f)));
    }

    /// <summary>Rig inches (Source view space) to engine view space.</summary>
    public static Matrix Placement() {
        Vector3 offset = new Vector3(KnifeTuning.Cs2ViewmodelOffsetX,
                                     KnifeTuning.Cs2ViewmodelOffsetZ,
                                     -KnifeTuning.Cs2ViewmodelOffsetY) * InchesToEngine;
        return Matrix.CreateScale(InchesToEngine) * s_axis * Matrix.CreateTranslation(offset);
    }

    /// <summary>The viewmodel's own projection: CS2's vertical FOV at the window's aspect.</summary>
    public static Matrix Projection(Camera camera) {
        float aspect = camera.ProjectionMatrix.M22 / camera.ProjectionMatrix.M11;
        if (!float.IsFinite(aspect) || aspect <= 0.01f) aspect = 16f / 9f;
        return Matrix.CreatePerspectiveFieldOfView(
            MathUtils.DegToRad(FovYDegrees(KnifeTuning.Cs2ViewmodelFov)), aspect, 0.02f, 64f);
    }

    /// <summary>
    /// True when the cs2 profile should draw this variant. Guns and knives are
    /// gated separately: they were built at different times, and a regression in
    /// one must not force the other back to CS:MC.
    /// </summary>
    public static bool Active(int variant) {
        bool isGun = CsmcKnifeRig.IsGun(variant);
        float profile = isGun ? KnifeTuning.GunProfile : KnifeTuning.KnifeProfile;
        return profile >= 0.5f && Cs2Rig.Has(CsmcKnifeRig.GetAssetName(variant));
    }
}
