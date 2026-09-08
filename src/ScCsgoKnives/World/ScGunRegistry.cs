using TemplatesDatabase;
namespace Game;

/// <summary>One gun's mutable state. The item value only carries the model and this record's id (GunSpec layout v5),
/// so the state follows the item through slots, chests, drops and pickups without any slot-side bookkeeping.
/// Records are only changed through ScGunMutation; everything else reads ScGunSnapshot copies.</summary>
public sealed class ScGunRecord {
    public int Variant, Rounds, Durability, MaxDurability, Revision;
    /// <summary>CS2 paint ID of the finish, 0 for the factory look. Appearance only; see ScGunSkinCatalog.</summary>
    public int SkinId;
    public bool SilencerOff;
    /// <summary>Zeus: game time (this session's SubsystemTime.GameTime) when the charge is back; below zero = not charging.</summary>
    public double RechargeReadyAt = -1;
    /// <summary>Last holder that changed this record (player:index:slot, block:x,y,z:slot); transient, never saved.</summary>
    public string Holder;
    public ScGunRecord Copy() => (ScGunRecord)MemberwiseClone();
    internal void CopyFrom(ScGunRecord source) {
        Variant = source.Variant; Rounds = source.Rounds; Durability = source.Durability;
        MaxDurability = source.MaxDurability; Revision = source.Revision; SkinId = source.SkinId;
        SilencerOff = source.SilencerOff; RechargeReadyAt = source.RechargeReadyAt; Holder = source.Holder;
    }
    public ScGunSnapshot Snapshot(int id) => new(id, Variant, Rounds, SilencerOff, Durability, MaxDurability, Revision, RechargeReadyAt, SkinId);
}

/// <summary>Read-only view of a record (or of a fresh gun's defaults). Id 0/1023 = fresh, no record.</summary>
public readonly record struct ScGunSnapshot(int Id, int Variant, int Rounds, bool SilencerOff, int Durability, int MaxDurability, int Revision, double RechargeReadyAt, int SkinId = 0) {
    public bool Fresh => Id is GunSpec.FreshFull or GunSpec.FreshEmpty;
    public static ScGunSnapshot ForFresh(int variant, bool full) {
        int magazine = variant >= 0 && variant < GunSpec.All.Length ? GunSpec.All[variant].Magazine : 0, life = ScGunDurability.Full(variant);
        return new(full ? GunSpec.FreshFull : GunSpec.FreshEmpty, variant, full ? magazine : 0, false, life, life, 0, -1, ScGunSkinCatalog.None);
    }
}

/// <summary>The world's gun state table (community plan C6 / M4). Ids are handed out monotonically and never reused;
/// a full table refuses new records. Saved with a schema number; a schema this version does not know keeps the
/// saved subtree verbatim and disables guns rather than guessing at it.</summary>
public sealed class ScGunRegistry {
    /// <summary>Record schema. 1 was seven fields; 2 appends the finish's CS2 paint ID. A schema this build
    /// does not know is kept verbatim and disables guns - the item layout stamp (GunSpec.DataLayout) is a
    /// separate number and does not change for a finish.</summary>
    public const int Schema = 2;
    public const int SchemaWithoutSkins = 1;
    /// <summary>The registry of the world being played; set by SubsystemScGunBlockBehavior.Load, cleared on dispose.
    /// Headless tests install their own.</summary>
    public static ScGunRegistry Current;
    public ScGunRecovery Recovery { get; private set; } = new();
    // Runtime-only resolver. Persistence contains owner keys, never live inventory references.
    public Func<IInventory, string> RecoveryOwner;
    public enum WorldStatus { New, Compatible, Legacy, Unknown }
    /// <summary>What a world's saved gun data is. An explicit stamp decides first: exactly this layout = compatible, the
    /// legacy stamp (4) = legacy for good (a legacy world saves a registry too, so the registry key must not outrank it), any
    /// other stamp = unknown, rejected by the load/save guard. Only an unstamped
    /// world is inferred from its keys: a v5 registry (0.35.0/0.35.1) = compatible, pre-v5 keys (ZeusRechargeAt, GunWear) =
    /// saved by 0.34 or earlier, nothing at all = new.</summary>
    public static WorldStatus Classify(int stamp, bool hasRegistry, bool hasOldKeys) {
        if (stamp == GunSpec.DataLayout) return WorldStatus.Compatible;
        if (stamp == LegacyStamp) return WorldStatus.Legacy;
        if (stamp != 0) return WorldStatus.Unknown;
        return hasRegistry ? WorldStatus.Compatible : hasOldKeys ? WorldStatus.Legacy : WorldStatus.New;
    }
    public const int LegacyStamp = GunSpec.DataLayout - 1;
    /// <summary>The stamp a world is saved with: a legacy world keeps its legacy stamp on every save.</summary>
    public static int StampFor(bool legacyWorld) => legacyWorld ? LegacyStamp : GunSpec.DataLayout;
    /// <summary>A world last saved by 0.34 or earlier: every gun item in it is left alone and unusable, new ones included,
    /// because its old-layout values cannot be told from v5 values by their bits.</summary>
    public bool LegacyWorld;
    /// <summary>The saved table used a schema this version does not know: kept verbatim, guns disabled.</summary>
    public bool UnknownSchema { get; private set; }
    public bool Disabled => LegacyWorld || UnknownSchema;
    ValuesDictionary m_preserved;
    readonly Dictionary<int, ScGunRecord> m_records = [];
    /// <summary>Records that failed validation on load, kept as their raw text and written back untouched.</summary>
    readonly Dictionary<string, string> m_quarantined = [];
    bool m_fullLogged;
    public int Next { get; private set; } = GunSpec.FirstId;
    public int Count => m_records.Count;
    public int QuarantinedCount => m_quarantined.Count;
    public bool IsFull => Next > GunSpec.LastId;
    internal ScGunRecord Get(int id) => !Disabled && id >= GunSpec.FirstId && id <= GunSpec.LastId && m_records.TryGetValue(id, out var record) ? record : null;
    public bool TryGetSnapshot(int id, out ScGunSnapshot snapshot) {
        var record = Get(id);
        snapshot = record?.Snapshot(id) ?? default;
        return record is not null;
    }
    /// <summary>The id the next Publish will use, or -1 when the table is full. Nothing is reserved: publish on the main thread right after.</summary>
    public int PeekNextId() => IsFull ? -1 : Next;
    internal int Publish(ScGunRecord record) {
        if (Disabled) return -1;
        if (IsFull) {
            if (!m_fullLogged) { m_fullLogged = true; KnifeLog.Error($"gun registry full: {GunSpec.LastId} records; new guns cannot be used until a new world"); }
            return -1;
        }
        int id = Next++;
        m_records[id] = record;
        return id;
    }
    /// <summary>A record straight from values (tests, MakeData with a partial magazine). Revision starts at 0.</summary>
    public int Allocate(int variant, int rounds, bool silencerOff, int durability, int maxDurability = -1, int skinId = ScGunSkinCatalog.None) {
        int max = maxDurability > 0 ? maxDurability : ScGunDurability.Full(variant);
        return Publish(new ScGunRecord { Variant = variant, Rounds = Math.Max(0, rounds), SilencerOff = silencerOff, Durability = Math.Clamp(durability, 0, max), MaxDurability = max, SkinId = skinId });
    }
    /// <summary>A record that was published but could not be tied to its item: kept as text under its id so the watermark stands, never reused.</summary>
    internal void Abandon(int id, string reason) {
        if (!m_records.Remove(id, out var record)) return;
        m_quarantined[id.ToString()] = Format(record, 0) + ",abandoned:" + reason;
        KnifeLog.Error($"gun record {id} abandoned: {reason}");
    }
    /// <summary>A duplicate item (creative copy, glitch) gets its own record so the two guns wear independently.</summary>
    public int Clone(int id) {
        var source = Get(id);
        if (source is null || IsFull) return -1;
        var copy = source.Copy(); copy.Holder = null;
        return Publish(copy);
    }
    static string Format(ScGunRecord r, double now) =>
        $"{r.Variant},{r.Rounds},{(r.SilencerOff ? 1 : 0)},{r.Durability},{r.MaxDurability},{r.Revision},{(r.RechargeReadyAt >= 0 ? Math.Max(0, r.RechargeReadyAt - now).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-1")},{r.SkinId}";
    /// <summary>A snapshot of the table taken on the game thread. Zeus charge is saved as seconds still to go, because
    /// SubsystemTime.GameTime restarts from zero every session.</summary>
    public ValuesDictionary Save(double now) {
        if (ScGunMutation.IsCommitting) throw new InvalidOperationException("Cannot snapshot gun records during an inventory transaction/recovery; retry saving after this update");
        if (UnknownSchema && m_preserved is not null) return m_preserved;
        var d = new ValuesDictionary(); d.SetValue("Schema", Schema); d.SetValue("Next", Next);
        var records = new ValuesDictionary();
        foreach (var (id, r) in m_records) records.SetValue(id.ToString(), Format(r, now));
        foreach (var (key, raw) in m_quarantined) if (!records.ContainsKey(key)) records.SetValue(key, raw);
        d.SetValue("Records", records); d.SetValue("Recovery", Recovery.Save()); return d;
    }
    public static ScGunRegistry Load(ValuesDictionary d, double now) {
        var registry = new ScGunRegistry();
        if (d is null) return registry;
        int schema = d.GetValue<int>("Schema", 0);
        if (schema != Schema && schema != SchemaWithoutSkins) { registry.UnknownSchema = true; registry.m_preserved = d; KnifeLog.Error($"gun registry schema {schema} is not {SchemaWithoutSkins} or {Schema}; kept verbatim, guns disabled"); return registry; }
        // Schema 1 had no finish field; every record it holds converts to the factory look and nothing else moves.
        int fields = schema == SchemaWithoutSkins ? 7 : 8;
        try { registry.Recovery = ScGunRecovery.Load(d.GetValue<ValuesDictionary>("Recovery", null)); }
        catch (Exception e) {
            registry.UnknownSchema = true; registry.m_preserved = d;
            KnifeLog.Error("Gun recovery data is invalid; whole table retained, guns disabled: " + e.Message);
            return registry;
        }
        int next = d.GetValue<int>("Next", GunSpec.FirstId);
        var records = d.GetValue<ValuesDictionary>("Records", null);
        int highest = GunSpec.FirstId - 1;
        if (records is not null) foreach (var pair in records) {
            // Schema 1 is exactly seven fields, every one of them parsed and in range; anything else is kept as text, unusable.
            string raw = pair.Value as string ?? "";
            string[] f = raw.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ints = System.Globalization.NumberStyles.Integer;
            int id = 0, variant = 0, rounds = 0, sil = 0, durability = 0, max = 0, revision = 0; double remaining = -1;
            int skinId = ScGunSkinCatalog.None;
            bool ok = int.TryParse(pair.Key, ints, ci, out id) && id >= GunSpec.FirstId && id <= GunSpec.LastId && f.Length == fields;
            ok = ok && int.TryParse(f[0], ints, ci, out variant) && int.TryParse(f[1], ints, ci, out rounds) && int.TryParse(f[2], ints, ci, out sil) && int.TryParse(f[3], ints, ci, out durability);
            ok = ok && int.TryParse(f[4], ints, ci, out max) && int.TryParse(f[5], ints, ci, out revision) && double.TryParse(f[6], System.Globalization.NumberStyles.Float, ci, out remaining);
            ok = ok && (fields == 7 || int.TryParse(f[7], ints, ci, out skinId));
            ok = ok && variant >= 0 && variant < GunSpec.All.Length && (sil == 0 || sil == 1) && rounds >= 0 && rounds <= GunSpec.All[variant].Magazine
                && max >= 1 && durability >= 0 && durability <= max && revision >= 0 && double.IsFinite(remaining) && (remaining < 0 ? remaining == -1 : remaining <= 1e6)
                // A finish this build does not know, or one that belongs to another gun, is not guessed at.
                && ScGunSkinCatalog.IsKnown(skinId) && (skinId == ScGunSkinCatalog.None || ScGunSkinCatalog.Fits(ScGunSkinCatalog.Find(skinId), variant));
            if (ok) {
                registry.m_records[id] = new ScGunRecord { Variant = variant, Rounds = rounds, SilencerOff = sil == 1, Durability = durability, MaxDurability = max, Revision = revision, RechargeReadyAt = remaining >= 0 ? now + remaining : -1, SkinId = skinId };
                highest = Math.Max(highest, id);
            }
            else { registry.m_quarantined[pair.Key] = raw; if (int.TryParse(pair.Key, out int bad)) highest = Math.Max(highest, bad); }
        }
        if (registry.m_quarantined.Count > 0) KnifeLog.Warning($"gun registry: {registry.m_quarantined.Count} record(s) failed validation and are kept unusable: {string.Join(",", registry.m_quarantined.Keys.Take(8))}");
        registry.Next = Math.Clamp(Math.Max(next, highest + 1), GunSpec.FirstId, GunSpec.LastId + 1);
        return registry;
    }
}
