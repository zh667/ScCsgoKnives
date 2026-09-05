using Engine;

namespace Game;

/// <summary>
/// Eyepiece framing fitted to the user's 1408x1056 AUG / SG 553 recordings in
/// SC_VIDEO (2026-09-05 23-33-30 and 23-33-52). CS2's ironsight clips already
/// align the optical axis to Source +X; viewmodel offsets must not be added.
/// The world FOV remains 45 (2x), from vdata. The model needs a separate narrow
/// projection: moving the eye into the tube exaggerates the rear circular lip
/// and hides the four-bolt housing visible in the recording. Model FOV and the
/// aperture below are fitted presentation parameters, not weapon statistics.
/// </summary>
public static class Cs2Ironsight {
    public const float Fov = 9f;
    public static float Aperture(string asset) => asset == "aug" ? 0.52f : asset == "sg556" ? 0.46f : 0f;
    public static Vector3 Eye(string asset) => asset switch {
        "aug" => new Vector3(0f, -0.00473f, -0.00201f),
        "sg556" => new Vector3(0f, 0.00602f, -0.02432f),
        _ => Vector3.Zero
    };

    /// <summary>Correction after ordinary CS2 placement, shared by gun, arms and muzzle.</summary>
    public static Matrix Correction(string asset) {
        Vector3 eye = Eye(asset);
        Vector3 offset = new(KnifeTuning.Cs2ViewmodelOffsetX, KnifeTuning.Cs2ViewmodelOffsetZ,
            -KnifeTuning.Cs2ViewmodelOffsetY);
        return Matrix.CreateTranslation((new Vector3(eye.Y, -eye.Z, eye.X) - offset) * Cs2Placement.InchesToEngine);
    }

    public static Matrix Projection(float aspect) => Matrix.CreatePerspectiveFieldOfView(
        MathUtils.DegToRad(Cs2Placement.FovYDegrees(Fov)), aspect, 0.005f, 64f);
}
