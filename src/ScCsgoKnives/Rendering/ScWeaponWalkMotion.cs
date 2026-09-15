using Engine;
namespace Game;

/// <summary>Time-based viewmodel motion, shared by gun and arms. Gameplay speed
/// changes the blend, but cannot drive the animation faster than 1.55 Hz.</summary>
public sealed class ScWeaponWalkMotion {
    double last = double.NaN, phase;
    float movement, aim;
    Matrix result = Matrix.Identity;
    public Matrix Sample(double now, float speed, bool grounded, bool aiming) {
        if (!double.IsFinite(now)) return result;
        if (now == last) return result;
        double elapsed = now - last;
        last = now;
        if (!double.IsFinite(elapsed) || elapsed < 0 || elapsed > .25) {
            movement = 0; aim = aiming ? 1 : 0; phase = 0;
            return result = Matrix.Identity;
        }
        float dt = (float)elapsed;
        float target = grounded && float.IsFinite(speed) ? Math.Clamp((speed - .15f) / 4.35f, 0, 1) : 0;
        float decay = MathF.Exp(-10 * dt);
        // Integrate the smoothed blend analytically so 30/60/120 FPS have the
        // same cadence. Stop/start and scope changes fade rather than snap.
        float integral = target * dt + (movement - target) * (1 - decay) / 10;
        movement = target + (movement - target) * decay;
        aim = (aiming ? 1 : 0) + (aim - (aiming ? 1 : 0)) * decay;
        phase = (phase + Math.PI * 2 * (.9 * dt + .65 * integral)) % (Math.PI * 2);
        float a = movement * (1 - .88f * aim), wave = (float)Math.Sin(phase);
        var offset = new Vector3(.008f * wave, .006f * (float)Math.Sin(phase * 2), .024f * wave) * a;
        return result = Matrix.CreateRotationZ(.006f * wave * a) * Matrix.CreateTranslation(offset);
    }
}
