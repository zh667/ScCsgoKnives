using System.Globalization;
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
    /// <summary>The full cycle length the charge in progress started with, so a level change can keep its
    /// remaining fraction instead of guessing it from the new cycle. 0 when nothing is charging.</summary>
    public float RechargeCycleSeconds;
    /// <summary>Kill counter (schema 3). Installed in place on the original gun; counting starts here, never
    /// back-filled from firing counts, wear or the player's own totals.</summary>
    public bool CounterInstalled;
    public long KillCount;
    /// <summary>The level whose effects this gun is actually carrying, and the one waiting for a safe moment.</summary>
    public int AppliedGrowthLevel;
    public int PendingGrowthLevel = ScGunGrowth.NoPending;
    /// <summary>Which growth parameter set produced AppliedGrowthLevel's numbers. Separate from the mod version.</summary>
    public int GrowthRulesVersion;
    /// <summary>Live rounds this gun holds beyond its current capacity, kept with the gun rather than discarded
    /// when a capacity shrinks. Zero in normal play.</summary>
    public int ReserveOverflowRounds;
    /// <summary>Last holder that changed this record (player:index:slot, block:x,y,z:slot); transient, never saved.</summary>
    public string Holder;
    public ScGunRecord Copy() => (ScGunRecord)MemberwiseClone();
    internal void CopyFrom(ScGunRecord source) {
        Variant = source.Variant; Rounds = source.Rounds; Durability = source.Durability;
        MaxDurability = source.MaxDurability; Revision = source.Revision; SkinId = source.SkinId;
        SilencerOff = source.SilencerOff; RechargeReadyAt = source.RechargeReadyAt; Holder = source.Holder;
        RechargeCycleSeconds = source.RechargeCycleSeconds; CounterInstalled = source.CounterInstalled;
        KillCount = source.KillCount; AppliedGrowthLevel = source.AppliedGrowthLevel;
        PendingGrowthLevel = source.PendingGrowthLevel; GrowthRulesVersion = source.GrowthRulesVersion;
        ReserveOverflowRounds = source.ReserveOverflowRounds;
    }
    public ScGunSnapshot Snapshot(int id) => new(id, Variant, Rounds, SilencerOff, Durability, MaxDurability, Revision, RechargeReadyAt, SkinId,
        CounterInstalled, KillCount, AppliedGrowthLevel, PendingGrowthLevel, GrowthRulesVersion, RechargeCycleSeconds, ReserveOverflowRounds);
}

/// <summary>Read-only view of a record (or of a fresh gun's defaults). Id 0/1023 = fresh, no record.</summary>
public readonly record struct ScGunSnapshot(int Id, int Variant, int Rounds, bool SilencerOff, int Durability, int MaxDurability, int Revision, double RechargeReadyAt, int SkinId = 0,
                                            bool CounterInstalled = false, long KillCount = 0, int AppliedGrowthLevel = 0, int PendingGrowthLevel = ScGunGrowth.NoPending,
                                            int GrowthRulesVersion = 0, float RechargeCycleSeconds = 0, int ReserveOverflowRounds = 0) {
    public bool Fresh => Id is GunSpec.FreshFull or GunSpec.FreshEmpty;
    /// <summary>The level whose numbers are in force right now. A pending level has deliberately not been applied yet.</summary>
    public int Level => CounterInstalled ? ScGunGrowth.Clamp(AppliedGrowthLevel) : 0;
    /// <summary>The level this gun's kills have already earned, whether or not it has been applied.</summary>
    public int EarnedLevel => CounterInstalled ? ScGunGrowth.LevelFor(KillCount) : 0;
    public int Capacity => ScGunGrowth.Capacity(Variant, Level);
    public static ScGunSnapshot ForFresh(int variant, bool full) {
        int magazine = variant >= 0 && variant < GunSpec.All.Length ? GunSpec.All[variant].Magazine : 0, life = ScGunDurability.Full(variant);
        return new(full ? GunSpec.FreshFull : GunSpec.FreshEmpty, variant, full ? magazine : 0, false, life, life, 0, -1, ScGunSkinCatalog.None);
    }
}

/// <summary>The world's gun state table (community plan C6 / M4). Ids are handed out monotonically and never reused;
/// a full table refuses new records. Saved with a schema number; a schema this version does not know keeps the
/// saved subtree verbatim and disables guns rather than guessing at it.</summary>
public sealed class ScGunRegistry {
    /// <summary>Record schema. 1 was seven positional fields; 2 appended the finish's CS2 paint ID; 3 replaces the
    /// positional row with named fields and adds the kill counter and growth state. A schema this build does not
    /// know is kept verbatim and disables guns - the item layout stamp (GunSpec.DataLayout) is a separate number
    /// and does not change for a record field.</summary>
    public const int Schema = 3;
    public const int SchemaWithoutSkins = 1;
    public const int SchemaWithoutGrowth = 2;
    /// <summary>Every schema this build reads. Anything else is a format from another version.</summary>
    public static bool IsKnownSchema(int schema) => schema is Schema or SchemaWithoutGrowth or SchemaWithoutSkins;
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
    /// <summary>The schema the loaded table was written with, for the upgrade report. Schema of a new world is this build's.</summary>
    public int LoadedSchema { get; private set; } = Schema;
    /// <summary>Whether this world does counting only or counting plus growth. Owned by the subsystem's save data;
    /// held here so every read of a record's effective numbers can see it.</summary>
    public ScGunGrowthMode GrowthMode = ScGunGrowthMode.Unset;
    /// <summary>Kills confirmed but not yet written into their gun's record. Saved with the world so a save between
    /// the kill and the write neither loses nor repeats it.</summary>
    public readonly ScGunKillQueue Kills = new();
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
    /// <summary>Ids currently in the table, for sweeps that must reach guns nobody is holding.</summary>
    public IEnumerable<int> Ids => m_records.Keys;
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
        m_quarantined[id.ToString()] = Format(record, 0) + ";abandoned:" + reason;
        KnifeLog.Error($"gun record {id} abandoned: {reason}");
    }
    /// <summary>A duplicate item (creative copy, glitch) gets its own record so the two guns wear independently.
    /// A creative copy is a new item, so it carries no survival kill count or level (plan §4.3).</summary>
    public int Clone(int id) {
        var source = Get(id);
        if (source is null || IsFull) return -1;
        var copy = source.Copy(); copy.Holder = null;
        ScGunGrowth.StripGrowth(copy);
        return Publish(copy);
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    static string Number(double value) => value.ToString("0.###", Ci);
    /// <summary>Schema 3 rows are named fields, so a later addition is a new name rather than another comma
    /// position nobody can check.</summary>
    static string Format(ScGunRecord r, double now) => string.Join(",",
        $"v={r.Variant}", $"r={r.Rounds}", $"s={(r.SilencerOff ? 1 : 0)}", $"d={r.Durability}", $"m={r.MaxDurability}",
        $"n={r.Revision}", $"c={(r.RechargeReadyAt >= 0 ? Number(Math.Max(0, r.RechargeReadyAt - now)) : "-1")}",
        $"p={r.SkinId}", $"ct={(r.CounterInstalled ? 1 : 0)}", $"k={r.KillCount.ToString(Ci)}",
        $"gl={r.AppliedGrowthLevel}", $"gp={r.PendingGrowthLevel}", $"gv={r.GrowthRulesVersion}",
        $"rc={Number(r.RechargeCycleSeconds)}", $"ov={r.ReserveOverflowRounds}");

    /// <summary>The schema 3 field names, all required exactly once and nothing else accepted.</summary>
    static readonly string[] Fields3 = ["v", "r", "s", "d", "m", "n", "c", "p", "ct", "k", "gl", "gp", "gv", "rc", "ov"];

    /// <summary>A snapshot of the table taken on the game thread. Zeus charge is saved as seconds still to go, because
    /// SubsystemTime.GameTime restarts from zero every session.</summary>
    public ValuesDictionary Save(double now) {
        if (ScGunMutation.IsCommitting) throw new InvalidOperationException("Cannot snapshot gun records during an inventory transaction/recovery; retry saving after this update");
        if (UnknownSchema && m_preserved is not null) return m_preserved;
        var d = new ValuesDictionary(); d.SetValue("Schema", Schema); d.SetValue("Next", Next);
        var records = new ValuesDictionary();
        foreach (var (id, r) in m_records) records.SetValue(id.ToString(), Format(r, now));
        foreach (var (key, raw) in m_quarantined) if (!records.ContainsKey(key)) records.SetValue(key, raw);
        d.SetValue("Records", records); d.SetValue("Recovery", Recovery.Save());
        d.SetValue("GrowthMode", GrowthMode.ToString());
        d.SetValue("PendingKills", Kills.Save());
        return d;
    }

    /// <summary>Parses one saved row. Schema 1 and 2 are the historical positional rows and convert here: the
    /// fields they never had take their documented defaults (no finish, no counter, level 0) and every field they
    /// did have is carried over untouched. Anything that does not parse or falls outside its range is refused,
    /// never patched up with a default.</summary>
    static bool TryParseRecord(int schema, string raw, double now, out ScGunRecord record) {
        record = null;
        var ints = NumberStyles.Integer;
        int variant = 0, rounds = 0, sil = 0, durability = 0, max = 0, revision = 0, skinId = ScGunSkinCatalog.None;
        double remaining = -1;
        bool counter = false; long kills = 0; int applied = 0, pending = ScGunGrowth.NoPending, rules = 0, overflow = 0;
        double cycle = 0;
        if (schema == Schema) {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in raw.Split(',')) {
                int split = field.IndexOf('=');
                if (split <= 0 || split == field.Length - 1) return false;
                if (!seen.TryAdd(field[..split], field[(split + 1)..])) return false;
            }
            if (seen.Count != Fields3.Length) return false;
            foreach (string name in Fields3) if (!seen.ContainsKey(name)) return false;
            int flag = 0;
            if (!int.TryParse(seen["v"], ints, Ci, out variant) || !int.TryParse(seen["r"], ints, Ci, out rounds)
                || !int.TryParse(seen["s"], ints, Ci, out sil) || !int.TryParse(seen["d"], ints, Ci, out durability)
                || !int.TryParse(seen["m"], ints, Ci, out max) || !int.TryParse(seen["n"], ints, Ci, out revision)
                || !double.TryParse(seen["c"], NumberStyles.Float, Ci, out remaining) || !int.TryParse(seen["p"], ints, Ci, out skinId)
                || !int.TryParse(seen["ct"], ints, Ci, out flag) || !long.TryParse(seen["k"], ints, Ci, out kills)
                || !int.TryParse(seen["gl"], ints, Ci, out applied) || !int.TryParse(seen["gp"], ints, Ci, out pending)
                || !int.TryParse(seen["gv"], ints, Ci, out rules) || !double.TryParse(seen["rc"], NumberStyles.Float, Ci, out cycle)
                || !int.TryParse(seen["ov"], ints, Ci, out overflow)) return false;
            if (flag is not (0 or 1)) return false;
            counter = flag == 1;
        }
        else {
            // Schema 1 is exactly seven positional fields, schema 2 exactly eight; every one is parsed and range-checked.
            int fields = schema == SchemaWithoutSkins ? 7 : 8;
            string[] f = raw.Split(',');
            if (f.Length != fields) return false;
            if (!int.TryParse(f[0], ints, Ci, out variant) || !int.TryParse(f[1], ints, Ci, out rounds)
                || !int.TryParse(f[2], ints, Ci, out sil) || !int.TryParse(f[3], ints, Ci, out durability)
                || !int.TryParse(f[4], ints, Ci, out max) || !int.TryParse(f[5], ints, Ci, out revision)
                || !double.TryParse(f[6], NumberStyles.Float, Ci, out remaining)) return false;
            if (fields == 8 && !int.TryParse(f[7], ints, Ci, out skinId)) return false;
        }
        if (variant < 0 || variant >= GunSpec.All.Length) return false;
        if (sil is not (0 or 1)) return false;
        if (applied < 0 || applied > ScGunGrowth.MaxLevel) return false;
        if (pending != ScGunGrowth.NoPending && (pending < 0 || pending > ScGunGrowth.MaxLevel)) return false;
        if (kills < 0 || rules < 0 || overflow < 0 || overflow > 1_000_000) return false;
        if (!counter && (kills != 0 || applied != 0 || rules != 0 || pending != ScGunGrowth.NoPending)) return false;
        if (!double.IsFinite(cycle) || cycle < 0 || cycle > 1e6) return false;
        // Rounds are bounded by this gun's own capacity at the level it is actually carrying, not by the base magazine.
        if (rounds < 0 || rounds > ScGunGrowth.Capacity(variant, applied)) return false;
        if (max < 1 || durability < 0 || durability > max || revision < 0) return false;
        if (!double.IsFinite(remaining) || (remaining < 0 ? remaining != -1 : remaining > 1e6)) return false;
        // A finish this build does not know, or one that belongs to another gun, is not guessed at.
        if (!ScGunSkinCatalog.IsKnown(skinId) || (skinId != ScGunSkinCatalog.None && !ScGunSkinCatalog.Fits(ScGunSkinCatalog.Find(skinId), variant))) return false;
        record = new ScGunRecord {
            Variant = variant, Rounds = rounds, SilencerOff = sil == 1, Durability = durability, MaxDurability = max,
            Revision = revision, RechargeReadyAt = remaining >= 0 ? now + remaining : -1, SkinId = skinId,
            CounterInstalled = counter, KillCount = kills, AppliedGrowthLevel = applied, PendingGrowthLevel = pending,
            GrowthRulesVersion = rules, RechargeCycleSeconds = (float)cycle, ReserveOverflowRounds = overflow,
        };
        return true;
    }

    public static ScGunRegistry Load(ValuesDictionary d, double now) {
        var registry = new ScGunRegistry();
        if (d is null) return registry;
        int schema = d.GetValue<int>("Schema", 0);
        if (!IsKnownSchema(schema)) {
            registry.UnknownSchema = true; registry.m_preserved = d;
            KnifeLog.Error($"gun registry schema {schema} is not 1, 2 or {Schema}; kept verbatim, guns disabled");
            return registry;
        }
        registry.LoadedSchema = schema;
        try { registry.Recovery = ScGunRecovery.Load(d.GetValue<ValuesDictionary>("Recovery", null)); }
        catch (Exception e) {
            registry.UnknownSchema = true; registry.m_preserved = d;
            KnifeLog.Error("Gun recovery data is invalid; whole table retained, guns disabled: " + e.Message);
            return registry;
        }
        // Schema 1 and 2 never carried a growth mode or a pending kill; both start from their documented defaults.
        string mode = d.GetValue<string>("GrowthMode", null);
        if (mode is not null && !Enum.TryParse(mode, out registry.GrowthMode)) {
            registry.UnknownSchema = true; registry.m_preserved = d;
            KnifeLog.Error($"gun growth mode '{mode}' is not one of {string.Join('/', Enum.GetNames<ScGunGrowthMode>())}; table retained, guns disabled");
            return registry;
        }
        try { registry.Kills.LoadInto(d.GetValue<ValuesDictionary>("PendingKills", null)); }
        catch (Exception e) {
            registry.UnknownSchema = true; registry.m_preserved = d;
            KnifeLog.Error("Pending gun kill data is invalid; whole table retained, guns disabled: " + e.Message);
            return registry;
        }
        int next = d.GetValue<int>("Next", GunSpec.FirstId);
        var records = d.GetValue<ValuesDictionary>("Records", null);
        int highest = GunSpec.FirstId - 1;
        if (records is not null) foreach (var pair in records) {
            string raw = pair.Value as string ?? "";
            ScGunRecord record = null;
            bool ok = int.TryParse(pair.Key, NumberStyles.Integer, Ci, out int id) && id >= GunSpec.FirstId && id <= GunSpec.LastId
                && TryParseRecord(schema, raw, now, out record);
            if (ok) { registry.m_records[id] = record; highest = Math.Max(highest, id); }
            else { registry.m_quarantined[pair.Key] = raw; if (int.TryParse(pair.Key, out int bad)) highest = Math.Max(highest, bad); }
        }
        if (registry.m_quarantined.Count > 0) KnifeLog.Warning($"gun registry: {registry.m_quarantined.Count} record(s) failed validation and are kept unusable: {string.Join(",", registry.m_quarantined.Keys.Take(8))}");
        registry.Next = Math.Clamp(Math.Max(next, highest + 1), GunSpec.FirstId, GunSpec.LastId + 1);
        // Corrupt/quarantined records may later be recovered. Keep their credentials exactly
        // as saved; a missing record is not permission to erase its pending kill history.
        int heldCredits = registry.Kills.Pending.Count(e => !registry.m_records.ContainsKey(e.RecordId));
        if (heldCredits > 0) KnifeLog.Warning($"gun kill queue: {heldCredits} credits retained for missing/quarantined records; not applied until their original identity is recovered");
        if (schema != Schema) KnifeLog.Information($"gun registry: converted {registry.m_records.Count} record(s) from schema {schema} to {Schema}; finishes, ammunition, durability, silencer and charge preserved, counter and growth start unset");
        return registry;
    }
}
