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
    public enum WorldStatus { New, Compatible, Legacy }
    /// <summary>What a world's saved gun data is. An explicit stamp decides first: exactly this layout = compatible, the
    /// legacy stamp (4) = legacy for good (a legacy world saves a registry too, so the registry key must not outrank it), any
    /// other stamp = written by a version this one does not know, treated as legacy rather than guessed. Only an unstamped
    /// world is inferred from its keys: a v5 registry (0.35.0/0.35.1) = compatible, pre-v5 keys (ZeusRechargeAt, GunWear) =
    /// saved by 0.34 or earlier, nothing at all = new.</summary>
    public static WorldStatus Classify(int stamp, bool hasRegistry, bool hasOldKeys) {
        if (stamp == GunSpec.DataLayout) return WorldStatus.Compatible;
        if (stamp != 0) return WorldStatus.Legacy;
        return hasRegistry ? WorldStatus.Compatible : hasOldKeys ? WorldStatus.Legacy : WorldStatus.New;
    }
    public const int LegacyStamp = GunSpec.DataLayout - 1;
    /// <summary>The stamp a world is saved with: a legacy world keeps its legacy stamp on every save.</summary>
    public static int StampFor(bool legacyWorld) => legacyWorld ? LegacyStamp : GunSpec.DataLayout;
    /// <summary>A world last saved by 0.34 or earlier: every gun item in it is left alone and unusable, new ones included,
    /// because its old-layout values cannot be told from v5 values by their bits.</summary>
    public bool LegacyWorld;
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
