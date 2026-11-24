using Engine;
namespace Game;

/// <summary>1.7.0 (feedback-1124-170 D8/D9, P4/P5, Q3): what an enemy squad's C4 does to loot. Pure rules; the bombs
/// subsystem applies them on the server only. Players' own C4 (SubsystemScC4) is not affected.
/// <list type="bullet">
/// <item>Dropped items in the blast radius with a clear line to the charge are destroyed, except the drops of a player
/// who died there recently (within <see cref="DeathProtectRadius"/> blocks of the death and <see cref="DeathProtectSeconds"/>
/// of game time) and CS guns and knives (a gun's permanent record must never be destroyed without trace).</item>
/// <item>An airdrop crate in the radius loses half of every stack (rounded down); a single item is kept or lost by a
/// stable roll seeded with where the charge lies, so repeating the same blast gives the same result. A gun with its own
/// record is never removed from a crate.</item>
/// </list></summary>
public static class TacticalBlastLoot {
    public const float DeathProtectRadius = 4f;
    public const double DeathProtectSeconds = 900;
    public const int MaxDeathMarks = 32;
    public readonly record struct DeathMark(Vector3 Position, double At);
    public static bool Fresh(DeathMark m, double now) => now >= m.At && now - m.At <= DeathProtectSeconds;
    public static bool Protected(Vector3 item, IEnumerable<DeathMark> marks, double now) =>
        marks.Any(m => Fresh(m, now) && Vector3.DistanceSquared(m.Position, item) <= DeathProtectRadius * DeathProtectRadius);
    /// <summary>Items a blast may destroy: anything but a CS gun or knife.</summary>
    public static bool Destructible(int value) {
        int contents = Terrain.ExtractContents(value);
        return contents != BlocksManager.GetBlockIndex<ScGunBlock>(true) && contents != BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
    }
    /// <summary>How many of a crate stack the blast destroys (P4).</summary>
    public static int CrateLoss(int value, int count, string seed, int slot) {
        if (count <= 0) return 0;
        if (Terrain.ExtractContents(value) == BlocksManager.GetBlockIndex<ScGunBlock>(true) && !GunSpec.IsFresh(Terrain.ExtractData(value))) return 0;
        return count >= 2 ? count / 2 : (Roll(seed, slot) & 1) == 0 ? 1 : 0;
    }
    /// <summary>A stable 32-bit FNV-1a roll (string.GetHashCode differs between processes).</summary>
    public static uint Roll(string seed, int slot) {
        uint h = 2166136261;
        foreach (char ch in (seed ?? "") + ":" + slot) { h ^= ch; h *= 16777619; }
        return h;
    }
    public static string SaveMarks(IEnumerable<DeathMark> marks) => string.Join("|", marks.Select(m =>
        string.Join(";", new double[] { m.Position.X, m.Position.Y, m.Position.Z, m.At }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
    /// <summary>Reads saved marks; malformed or non-finite entries and stale ones are dropped (they only protect loot).</summary>
    public static List<DeathMark> LoadMarks(string text, double now) {
        var marks = new List<DeathMark>();
        foreach (string entry in (text ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries)) {
            var parts = entry.Split(';');
            if (parts.Length != 4) continue;
            var v = new double[4]; bool ok = true;
            for (int i = 0; i < 4; i++) ok &= double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]) && double.IsFinite(v[i]);
            if (!ok) continue;
            var m = new DeathMark(new Vector3((float)v[0], (float)v[1], (float)v[2]), v[3]);
            if (Fresh(m, now) && marks.Count < MaxDeathMarks) marks.Add(m);
        }
        return marks;
    }
}
