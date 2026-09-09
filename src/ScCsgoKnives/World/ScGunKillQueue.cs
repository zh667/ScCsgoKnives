using System.Globalization;
using TemplatesDatabase;
namespace Game;

/// <summary>Kills that are confirmed but not yet written into their gun's record.
///
/// A kill is confirmed the moment the target's health crosses from above zero to zero under this gun's damage,
/// which is inside a body callback - the wrong place to open another gun transaction. The credential is queued
/// here instead, saved with the world, and written through the one gun transaction on a later update. That is
/// what makes a save between the kill and the write neither lose the kill nor count it twice: an entry exists
/// exactly once, under its own event id, until the record has taken it.</summary>
public sealed class ScGunKillQueue {
    public sealed class Entry(long eventId, int recordId, int variant) {
        public long EventId { get; internal set; } = eventId;
        public int RecordId { get; } = recordId;
        public int Variant { get; } = variant;
    }
    /// <summary>Diagnostic threshold, not an eviction limit. Confirmed kills are saved until delivered.</summary>
    public const int MaxPending = 1024;
    readonly List<Entry> m_pending = [];
    long m_next = 1;
    public int Count => m_pending.Count;
    public IReadOnlyList<Entry> Pending => m_pending;
    public long NextEventId => m_next;

    /// <summary>Records one confirmed kill against one gun instance. Returns the event id, or -1 when the
    /// credential is not a usable record reference.</summary>
    public long Enqueue(int recordId, int variant) {
        if (recordId < GunSpec.FirstId || recordId > GunSpec.LastId) return -1;
        if (variant < 0 || variant >= GunSpec.All.Length) return -1;
        if (m_pending.Count == MaxPending)
            KnifeLog.Warning($"gun kill queue has {MaxPending} waiting credits; all are retained until their guns are reachable");
        // Event ids are internal queue keys. Rebase only at exhaustion, retaining every credential.
        if (m_next == long.MaxValue) {
            // Keep entry identity stable if an update is already walking Pending.ToArray().
            for (int i = 0; i < m_pending.Count; i++) m_pending[i].EventId = i + 1L;
            m_next = m_pending.Count + 1L;
        }
        var entry = new Entry(m_next++, recordId, variant);
        m_pending.Add(entry);
        return entry.EventId;
    }
    /// <summary>The credit has been written into the record. Removing it is what stops it being written again.</summary>
    public void Complete(long eventId) => m_pending.RemoveAll(e => e.EventId == eventId);
    /// <summary>Credits whose gun is not in this table can never be delivered.</summary>
    public void DropUnknown(Func<int, bool> known) {
        int before = m_pending.Count;
        m_pending.RemoveAll(e => !known(e.RecordId));
        if (m_pending.Count != before) KnifeLog.Warning($"gun kill credits: {before - m_pending.Count} referenced a record this world does not have and were dropped");
    }

    public ValuesDictionary Save() {
        var d = new ValuesDictionary();
        d.SetValue("Next", m_next.ToString(CultureInfo.InvariantCulture));
        var entries = new ValuesDictionary();
        foreach (var e in m_pending) entries.SetValue(e.EventId.ToString(CultureInfo.InvariantCulture), $"{e.RecordId},{e.Variant}");
        d.SetValue("Entries", entries);
        return d;
    }
    /// <summary>Strict: an unreadable queue is a corrupt save, not something to patch up with defaults.</summary>
    public void LoadInto(ValuesDictionary d) {
        m_pending.Clear(); m_next = 1;
        if (d is null) return;
        string next = d.GetValue<string>("Next", "1");
        if (!long.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out m_next) || m_next < 1)
            throw new InvalidOperationException("PendingKills.Next is not a positive integer");
        var entries = d.GetValue<ValuesDictionary>("Entries", null);
        if (entries is null) return;
        foreach (var pair in entries) {
            string[] parts = (pair.Value as string ?? "").Split(',');
            if (!long.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) || id < 1 || id >= m_next
                || parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int record)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int variant)
                || record < GunSpec.FirstId || record > GunSpec.LastId || variant < 0 || variant >= GunSpec.All.Length)
                throw new InvalidOperationException("PendingKills entry '" + pair.Key + "' is not a valid credential");
            if (m_pending.Any(e => e.EventId == id)) throw new InvalidOperationException("PendingKills has a duplicate event id " + id);
            m_pending.Add(new Entry(id, record, variant));
        }
        m_pending.Sort((a, b) => a.EventId.CompareTo(b.EventId));
    }
}

/// <summary>Who a kill belongs to, captured when the shot is fired rather than read back afterwards.
///
/// The record id and model are frozen at the moment the trigger transaction committed, so switching weapons,
/// dropping the gun or handing it to somebody else between the shot and the death cannot move the kill onto
/// another gun. Both creative and survival shots count; a gun without a counter carries no credential.</summary>
public sealed record ScGunKillCredit(int RecordId, int Variant, long ShotId) {
    public static ScGunKillCredit For(int data, bool creative, long shotId) {
        if (!GunSpec.TryGetSnapshot(data, out var s) || !s.CounterInstalled) return null;
        if (s.Id < GunSpec.FirstId || s.Id > GunSpec.LastId) return null;
        return new ScGunKillCredit(s.Id, s.Variant, shotId);
    }
}

/// <summary>What counts as a valid kill (plan §4.3).
///
/// Survivalcraft has no taming system, so "tamed" here means a mount somebody is actually riding. Players are
/// excluded because the first release deliberately does not let PvP feed weapon growth.
/// Targets explicitly marked as test spawns remain excluded in both game modes.</summary>
public static class ScGunKillRules {
    /// <summary>Set by headless tests to mark a target as a test spawn. Never set by gameplay.</summary>
    public static Func<ComponentBody, bool> TestTarget;

    public static bool Counts(ComponentBody target, ComponentPlayer shooter, bool creative, out string why) {
        why = null;
        if (target?.Entity is null) { why = "no entity"; return false; }
        if (shooter is null) { why = "no shooter"; return false; }
        if (ReferenceEquals(target.Entity, shooter.Entity)) { why = "self"; return false; }
        if (target.Entity.FindComponent<ComponentPlayer>() is not null) { why = "player target"; return false; }
        if (target.Entity.FindComponent<ComponentCreature>() is null) { why = "not a creature"; return false; }
        if (target.Entity.FindComponent<ComponentMount>() is not null && target.ChildBodies.Count > 0) { why = "ridden mount"; return false; }
        if (TestTarget is not null && TestTarget(target)) { why = "test spawn"; return false; }
        return true;
    }
}
