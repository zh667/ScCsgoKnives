using System.Runtime.CompilerServices;
using Engine;
namespace Game;

/// <summary>CS2's fixed spray patterns (deathmatch round 5; the user: "CS2 那套固定的扫射弹道图案没有复现"), measured in CS2
/// 1.41.8.8 on this machine (2026-10-04, docs/tasks/deathmatch-cs2-measure-20261005/): nospread sprays at a wall, every
/// round's impact located frame by frame and turned into the angle it left at.
///
///   AS READ      each gun's m_nRecoilSeed, m_flRecoilAngle, m_flRecoilAngleVariance, m_flRecoilMagnitude,
///                m_flRecoilMagnitudeVariance (pair: 0 = primary, 1 = alternate: scoped, silenced, burst), m_bIsFullAuto,
///                m_flCycleTime - from Data/dm_cs2_profile.json
///   AS PUBLISHED the pattern table: Valve's uniform random stream seeded with m_nRecoilSeed, each shot's angle and
///                magnitude drawn around the vdata values, a full-auto gun's blended 0.55 toward the previous shot and its
///                first four shots' magnitude suppressed from 0.75 (the CS:GO SDK lineage, weapon_recoil_variance /
///                weapon_recoil_suppression_*). Its shot-by-shot left/right sequence matched every measured spray
///   MEASURED     how a shot's kick moves the round (Gain, ExpDecay, LinearDecay, VelocityDecay): CS2 hides those
///                constants (no weapon_recoil_* console variable answers); fitted to the AK-47's 30 rounds (0.034 deg RMS)
///                and then predicting the M4A4, M4A1-S (alternate pair: silencer on), Galil AR, FAMAS, MP9, UMP-45, P90
///                and MAC-10 within 0.03-0.05 deg RMS; how much of it the view shows (ViewPitch, ViewYaw: the picture's
///                own shift in the same frames)
///   NOT MEASURED how the pattern restarts after a pause: the CS:GO SDK's recoil index decay (a little more than one cycle
///                without a shot, then down to a tenth in half a second); a table longer than 64 shots repeats
///
/// The state is per player on every process that fires for that player (the authority and the shooter's own device),
/// advanced from that process's own shot times: both ends compute the same pattern.</summary>
public static class DmRecoilModel {
    // ---------------------------------------------------------------- measured (CS2 1.41.8.8, 2026-10-04)
    /// <summary>Velocity a shot adds per unit of table magnitude, degrees of round offset per second.</summary>
    public const float Gain = 1.9525f;
    /// <summary>The offset's exponential decay (1/s) and linear decay (deg/s), the velocity's decay (1/s).</summary>
    public const float ExpDecay = 7.3062f, LinearDecay = 32.2465f, VelocityDecay = 4.7601f;
    /// <summary>The share of the round's offset the view shows (pitch, yaw).</summary>
    public const float ViewPitch = .602f, ViewYaw = .558f;
    // ---------------------------------------------------------------- the CS:GO SDK lineage (the table reproduced CS2's measured sequence)
    public const float Variance = .55f, SuppressionFactor = .75f;
    public const int SuppressionShots = 4, TableSize = 64;
    // ---------------------------------------------------------------- the CS:GO SDK lineage, not measured in CS2
    public const float IndexDecayThreshold = 1.1f;
    public static readonly float IndexDecayRate = MathF.Log(10) * 2f;
    /// <summary>The integration step: CS2 takes input between ticks, the fit integrated this finely.</summary>
    public const float Step = 1f / 1024;
    /// <summary>Changes whenever a number above changes (part of DmWeapons.Fingerprint).</summary>
    public const string Version = "recoil1";

    /// <summary>Valve's CUniformRandomStream (Numerical Recipes' ran1, as in the Source SDK).</summary>
    public sealed class Uniform {
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

    /// <summary>A gun's pattern table for one handling state: per shot the kick's angle (degrees, 0 = straight up, positive
    /// to the right) and magnitude.</summary>
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
    /// <summary>The table of a gun of the CS2 profile in one handling state (cached).</summary>
    public static (float Angle, float Magnitude)[] TableOf(string gun, bool alternate) {
        lock (s_tables) {
            if (s_tables.TryGetValue((gun, alternate), out var table)) return table;
            table = Table((int)DmWeapons.Raw(gun, "m_nRecoilSeed"), DmWeapons.Raw(gun, "m_flRecoilAngle", alternate), DmWeapons.Raw(gun, "m_flRecoilAngleVariance", alternate),
                DmWeapons.Raw(gun, "m_flRecoilMagnitude", alternate), DmWeapons.Raw(gun, "m_flRecoilMagnitudeVariance", alternate), DmWeapons.Raw(gun, "m_bIsFullAuto") != 0);
            return s_tables[(gun, alternate)] = table;
        }
    }
}

/// <summary>One player's recoil in CS2's terms: the round's offset (X pitch, positive down; Y yaw, positive left; degrees)
/// and its velocity, the gun's recoil index and when it last fired.</summary>
public sealed class DmRecoilState {
    public double Time = double.NegativeInfinity, LastShot = double.NegativeInfinity;
    public Vector2 Offset, Velocity;
    public float Index, Cycle = .1f;
    public string Gun;
    /// <summary>Brought to <paramref name="now"/>: the offset and velocity decay, the index decays once the trigger rested
    /// a little more than a cycle. Time never runs backwards here.</summary>
    public void Advance(double now) {
        if (double.IsNegativeInfinity(Time)) { Time = now; return; }
        double dt = now - Time;
        if (!(dt > 0)) return;
        double rest = now - Math.Max(Time, LastShot + DmRecoilModel.IndexDecayThreshold * Cycle);
        if (rest > 0 && Index > 0) Index *= MathF.Exp(-DmRecoilModel.IndexDecayRate * (float)rest);
        if (dt > 3) { Offset = Velocity = Vector2.Zero; Time = now; return; }   // long settled: both are far below a pixel
        int steps = Math.Max(1, (int)Math.Ceiling(dt / DmRecoilModel.Step)); float h = (float)(dt / steps);
        float keep = MathF.Exp(-DmRecoilModel.ExpDecay * h), velocityKeep = MathF.Exp(-DmRecoilModel.VelocityDecay * h), linear = DmRecoilModel.LinearDecay * h;
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
    /// <summary>One round of <paramref name="gun"/> left at <paramref name="now"/> (Advance first): its table entry kicks
    /// the velocity, the index moves on. Another gun starts its own pattern from the first shot.</summary>
    public void Fire(string gun, bool alternate, double now) {
        Advance(now);
        if (Gun != gun) { Gun = gun; Index = 0; }
        var table = DmRecoilModel.TableOf(gun, alternate);
        var (angle, magnitude) = table[(int)Index % DmRecoilModel.TableSize];
        float a = MathUtils.DegToRad(angle), kick = DmRecoilModel.Gain * magnitude;
        Velocity += new Vector2(-MathF.Cos(a) * kick, -MathF.Sin(a) * kick);
        Index += 1; LastShot = now; Cycle = Math.Max(.01f, DmWeapons.Raw(gun, "m_flCycleTime", false, .1f));
    }
    /// <summary>Up and left of the shooter's aim, degrees: the round's offset and the view's part of it.</summary>
    public (Vector2 Bullet, Vector2 View) Angles() {
        var bullet = new Vector2(-Offset.X, Offset.Y);
        return (bullet, new Vector2(bullet.X * DmRecoilModel.ViewPitch, bullet.Y * DmRecoilModel.ViewYaw));
    }
}

/// <summary>The deathmatch's recoil for the core (ScMode.Recoil): one state per player of this world.</summary>
public sealed class DmRecoil : IScRecoil {
    readonly ConditionalWeakTable<ComponentPlayer, DmRecoilState> m_states = new();
    public DmRecoilState StateOf(ComponentPlayer player) => m_states.GetValue(player, _ => new DmRecoilState());
    public (Vector2 Bullet, Vector2 View) At(ComponentPlayer player, double now) {
        var state = StateOf(player); state.Advance(now); return state.Angles();
    }
    public void Fired(ComponentPlayer player, GunSpec spec, bool alternate, double now) {
        if (spec is null || !DmWeapons.Ready) return;
        StateOf(player).Fire(spec.Name, alternate, now);
    }
}
