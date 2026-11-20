using System.Runtime.CompilerServices;
using Engine;
namespace Game;

/// <summary>CS2's fixed spray pattern for survival guns under the "realistic" setting (feedback-1120-160 #3). The model is
/// the deathmatch package's (DmRecoil, measured in CS2 1.41.8.8 on 2026-10-04; see its notes for what was read, published
/// and measured): Valve's seeded table per gun from the vdata recoil fields, a velocity kick per shot decaying back, the
/// view showing a fixed share of the round's offset. The numbers here are that model's, copied, not refitted; the vdata
/// comes from the core's own cs2_weapons.json, so this works without the deathmatch package. The deathmatch keeps its own
/// copy for its matches (its rules fingerprint names its version); keep both in step when the model changes.
///
/// Unlike the match, survival growth scales the whole pattern (<see cref="Scale"/>: ScRecoilRules.KickScale, set by the shot
/// before it fires). State is per player on every process that fires for that player, as in the match.</summary>
public sealed class ScSprayRecoil : IScRecoil {
    // ---- the model (DmRecoilModel "recoil1") ------------------------------------------------------------------------------
    public const float Gain = 1.9525f, ExpDecay = 7.3062f, LinearDecay = 32.2465f, VelocityDecay = 4.7601f;
    public const float ViewPitch = .602f, ViewYaw = .558f;
    public const float Variance = .55f, SuppressionFactor = .75f;
    public const int SuppressionShots = 4, TableSize = 64;
    public const float IndexDecayThreshold = 1.1f;
    public static readonly float IndexDecayRate = MathF.Log(10) * 2f;
    public const float Step = 1f / 1024;

    /// <summary>Valve's CUniformRandomStream (Numerical Recipes' ran1, as in the Source SDK).</summary>
    sealed class Uniform {
        const int IA = 16807, IM = 2147483647, IQ = 127773, IR = 2836, NTAB = 32, NDIV = 1 + (IM - 1) / NTAB;
        const double AM = 1.0 / IM, RNMX = 1.0 - 1.2e-7;
        int m_idum, m_iy; readonly int[] m_iv = new int[NTAB];
        public Uniform(int seed) { m_idum = seed < 0 ? seed : -seed; m_iy = 0; }
        int Next() {
            int j, k;
            if (m_idum <= 0 || m_iy == 0) {
                m_idum = -m_idum < 1 ? 1 : -m_idum;
                for (j = NTAB + 7; j >= 0; j--) {
                    k = m_idum / IQ; m_idum = IA * (m_idum - k * IQ) - IR * k;
                    if (m_idum < 0) m_idum += IM;
                    if (j < NTAB) m_iv[j] = m_idum;
                }
                m_iy = m_iv[0];
            }
            k = m_idum / IQ; m_idum = IA * (m_idum - k * IQ) - IR * k;
            if (m_idum < 0) m_idum += IM;
            j = m_iy / NDIV;
            if (j >= NTAB || j < 0) j = (j % NTAB) & 0x7fffffff;
            m_iy = m_iv[j]; m_iv[j] = m_idum;
            return m_iy;
        }
        public float Float(float low, float high) {
            float fl = (float)(AM * Next());
            if (fl > RNMX) fl = (float)RNMX;
            return fl * (high - low) + low;
        }
    }

    /// <summary>A gun's pattern for one handling state: per shot the kick's angle (degrees, 0 = up, positive right) and magnitude.</summary>
    public static (float Angle, float Magnitude)[] Table(int seed, float angle, float angleVariance, float magnitude, float magnitudeVariance, bool fullAuto) {
        var random = new Uniform(seed); var table = new (float, float)[TableSize];
        float a = 0, m = 0;
        for (int j = 0; j < TableSize; j++) {
            float an = angle + random.Float(-angleVariance, angleVariance), mn = magnitude + random.Float(-magnitudeVariance, magnitudeVariance);
            if (fullAuto && j > 0) { a += (an - a) * Variance; m += (mn - m) * Variance; }
            else { a = an; m = mn; }
            if (fullAuto && j < SuppressionShots) m *= SuppressionFactor + (1 - SuppressionFactor) * j / SuppressionShots;
            table[j] = (a, m);
        }
        return table;
    }
    static readonly Dictionary<(string, bool), (float Angle, float Magnitude)[]> s_tables = [];
    /// <summary>The table of <paramref name="gun"/> from the core's vdata (cached); null when the gun has no recoil data.</summary>
    public static (float Angle, float Magnitude)[] TableOf(string gun, bool alternate) {
        lock (s_tables) {
            if (s_tables.TryGetValue((gun, alternate), out var table)) return table;
            float magnitude = Cs2Weapons.Raw(gun, "m_flRecoilMagnitude", alternate, float.NaN);
            table = float.IsFinite(magnitude)
                ? Table((int)Cs2Weapons.Raw(gun, "m_nRecoilSeed"), Cs2Weapons.Raw(gun, "m_flRecoilAngle", alternate), Cs2Weapons.Raw(gun, "m_flRecoilAngleVariance", alternate),
                    magnitude, Cs2Weapons.Raw(gun, "m_flRecoilMagnitudeVariance", alternate), Cs2Weapons.Raw(gun, "m_bIsFullAuto") != 0)
                : null;
            return s_tables[(gun, alternate)] = table;
        }
    }

    /// <summary>One player's recoil: the round's offset (X pitch, positive down; Y yaw, positive left; degrees), its velocity,
    /// the recoil index, the last shot and the growth scale of the gun that fired it.</summary>
    public sealed class State {
        public double Time = double.NegativeInfinity, LastShot = double.NegativeInfinity;
        public Vector2 Offset, Velocity;
        public float Index, Cycle = .1f, Scale = 1f;
        public string Gun;
        public void Advance(double now) {
            if (double.IsNegativeInfinity(Time)) { Time = now; return; }
            double dt = now - Time;
            if (!(dt > 0)) return;
            double rest = now - Math.Max(Time, LastShot + IndexDecayThreshold * Cycle);
            if (rest > 0 && Index > 0) Index *= MathF.Exp(-IndexDecayRate * (float)rest);
            if (dt > 3) { Offset = Velocity = Vector2.Zero; Time = now; return; }
            int steps = Math.Max(1, (int)Math.Ceiling(dt / Step)); float h = (float)(dt / steps);
            float keep = MathF.Exp(-ExpDecay * h), velocityKeep = MathF.Exp(-VelocityDecay * h), linear = LinearDecay * h;
            for (int i = 0; i < steps; i++) {
                if (Offset != Vector2.Zero || Velocity != Vector2.Zero) {
                    Offset *= keep; float length = Offset.Length();
                    Offset = length > linear ? Offset * (1 - linear / length) : Vector2.Zero;
                    Offset += Velocity * (h * .5f); Velocity *= velocityKeep; Offset += Velocity * (h * .5f);
                }
            }
            if (Velocity.LengthSquared() < 1e-12f && Offset.LengthSquared() < 1e-12f) Offset = Velocity = Vector2.Zero;
            Time = now;
        }
        public void Fire(string gun, bool alternate, double now) {
            Advance(now);
            if (Gun != gun) { Gun = gun; Index = 0; }
            if (TableOf(gun, alternate) is not { } table) return;
            var (angle, magnitude) = table[(int)Index % TableSize];
            float a = MathUtils.DegToRad(angle), kick = Gain * magnitude;
            Velocity += new Vector2(-MathF.Cos(a) * kick, -MathF.Sin(a) * kick);
            Index += 1; LastShot = now; Cycle = Math.Max(.01f, Cs2Weapons.Raw(gun, "m_flCycleTime", false, .1f));
        }
        /// <summary>Up and left of the shooter's aim, degrees: the round's offset and the view's part of it, both scaled.</summary>
        public (Vector2 Bullet, Vector2 View) Angles() {
            var bullet = new Vector2(-Offset.X, Offset.Y) * Scale;
            return (bullet, new Vector2(bullet.X * ViewPitch, bullet.Y * ViewYaw));
        }
    }

    readonly ConditionalWeakTable<ComponentPlayer, State> m_states = new();
    public State StateOf(ComponentPlayer player) => m_states.GetValue(player, _ => new State());
    /// <summary>The growth scale for this player's next shot (ScRecoilRules.KickScale of the gun about to fire).</summary>
    public void Scale(ComponentPlayer player, float scale) => StateOf(player).Scale = float.IsFinite(scale) ? Math.Clamp(scale, 0, 1) : 1;
    public (Vector2 Bullet, Vector2 View) At(ComponentPlayer player, double now) { var s = StateOf(player); s.Advance(now); return s.Angles(); }
    public void Fired(ComponentPlayer player, GunSpec spec, bool alternate, double now) {
        if (spec is null || Cs2Weapons.LoadError is not null) return;
        StateOf(player).Fire(spec.Name, alternate, now);
    }
}
