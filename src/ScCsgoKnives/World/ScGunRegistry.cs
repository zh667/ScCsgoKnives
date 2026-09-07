using TemplatesDatabase;
namespace Game;

/// <summary>One gun's mutable state. The item value only carries the model and this record's id (GunSpec layout v5),
/// so the state follows the item through slots, chests, drops and pickups without any slot-side bookkeeping.</summary>
public sealed class ScGunRecord {
    public int Variant, Rounds, Durability;
    public bool SilencerOff;
    public ScGunRecord Copy() => (ScGunRecord)MemberwiseClone();
}

/// <summary>The world's gun state table (community plan C6, 0.35.0). Ids are handed out monotonically and never
/// reused; a full table refuses new records and the caller keeps the item as a stateless fresh gun.</summary>
public sealed class ScGunRegistry {
    /// <summary>The registry of the world being played; set by SubsystemScGunBlockBehavior.Load, cleared on dispose.
    /// Headless tests install their own.</summary>
    public static ScGunRegistry Current;
    readonly Dictionary<int, ScGunRecord> m_records = [];
    bool m_fullLogged;
    public int Next { get; private set; } = GunSpec.FirstId;
    public int Count => m_records.Count;
    public bool IsFull => Next > GunSpec.LastId;
    public ScGunRecord Get(int id) => id >= GunSpec.FirstId && id <= GunSpec.LastId && m_records.TryGetValue(id, out var record) ? record : null;
    public int Allocate(int variant, int rounds, bool silencerOff, int durability) {
        if (IsFull) {
            if (!m_fullLogged) { m_fullLogged = true; KnifeLog.Error($"gun registry full: {GunSpec.LastId} records; new guns stay stateless until a new world"); }
            return -1;
        }
        int id = Next++;
        m_records[id] = new ScGunRecord { Variant = variant, Rounds = rounds, SilencerOff = silencerOff, Durability = durability };
        return id;
    }
    /// <summary>A duplicate item (creative copy, glitch) gets its own record so the two guns wear independently.</summary>
    public int Clone(int id) {
        var source = Get(id);
        if (source is null || IsFull) return -1;
        int copy = Next++;
        m_records[copy] = source.Copy();
        return copy;
    }
    public ValuesDictionary Save() {
        var d = new ValuesDictionary(); d.SetValue("Next", Next);
        var records = new ValuesDictionary();
        foreach (var (id, r) in m_records) records.SetValue(id.ToString(), $"{r.Variant},{r.Rounds},{(r.SilencerOff ? 1 : 0)},{r.Durability}");
        d.SetValue("Records", records); return d;
    }
    public static ScGunRegistry Load(ValuesDictionary d) {
        var registry = new ScGunRegistry();
        if (d is null) return registry;
        int next = d.GetValue<int>("Next", GunSpec.FirstId);
        var records = d.GetValue<ValuesDictionary>("Records", null);
        int highest = GunSpec.FirstId - 1;
        if (records is not null) foreach (var pair in records) {
            string[] f = (pair.Value as string ?? "").Split(',');
            if (!int.TryParse(pair.Key, out int id) || id < GunSpec.FirstId || id > GunSpec.LastId || f.Length != 4) continue;
            if (!int.TryParse(f[0], out int variant) || !int.TryParse(f[1], out int rounds) || !int.TryParse(f[2], out int sil) || !int.TryParse(f[3], out int durability)) continue;
            if (variant < 0 || variant > GunSpec.VariantMask || rounds < 0 || rounds > 255 || durability < 0 || durability > 100000) continue;
            registry.m_records[id] = new ScGunRecord { Variant = variant, Rounds = rounds, SilencerOff = sil != 0, Durability = durability };
            highest = Math.Max(highest, id);
        }
        registry.Next = Math.Clamp(Math.Max(next, highest + 1), GunSpec.FirstId, GunSpec.LastId + 1);
        return registry;
    }
}
