using System.Globalization;
namespace Game;

/// <summary>The armoury's ledger (design §6, L4): the gun records the mode owns in this world and who has each one out.
/// A record's id and model never change and no id is ever given back to the registry: the same instance is lent again.
/// The pool grows only when every record of a model is out, and never past <see cref="Limit"/> - one record of a model
/// for each player the design allows, whatever the number of lives, matches or visitors. Pure bookkeeping: the registry
/// and the inventories are the caller's.</summary>
public sealed class DmArmory {
    /// <summary>Who has a record out. The generation rises at every lending, so a message of an earlier one is told apart.</summary>
    public sealed record Lease(string PlayerKey, int MatchId, int LifeId, int Generation);
    readonly SortedDictionary<int, int> m_pool = [];            // record id -> model (variant)
    readonly Dictionary<int, Lease> m_leases = [];
    readonly Dictionary<int, int> m_generations = [];
    /// <summary>Text that could not be read at load: kept and saved back as it was, never guessed at (L4).</summary>
    public string Unreadable { get; private set; }

    /// <summary>The most records the pool may ever hold: models with a record × the design's players.</summary>
    public static int Limit => GunSpec.All.Length * DmFixed.MaxPlayers;
    public static int PerModel => DmFixed.MaxPlayers;
    public int Count => m_pool.Count;
    public IEnumerable<int> Ids => m_pool.Keys;
    public bool Owns(int id) => m_pool.ContainsKey(id);
    public int VariantOf(int id) => m_pool.TryGetValue(id, out int variant) ? variant : -1;
    public Lease LeaseOf(int id) => m_leases.GetValueOrDefault(id);
    public IEnumerable<int> Parked => m_pool.Keys.Where(id => !m_leases.ContainsKey(id));
    public IEnumerable<int> LeasedTo(string playerKey) => m_leases.Where(p => p.Value.PlayerKey == playerKey).Select(p => p.Key).OrderBy(id => id);
    public int CountOf(int variant) => m_pool.Count(p => p.Value == variant);

    /// <summary>Lends a record of this model for a player's life: one the player already has out (kept as it is), else a
    /// parked one, else a new one from <paramref name="allocate"/> (the registry; -1 = refused) while the bounds allow.
    /// False changes nothing.</summary>
    public bool TryLease(int variant, string playerKey, int matchId, int lifeId, Func<int, int> allocate, out int id, out bool reused) {
        id = -1; reused = false;
        if (variant < 0 || variant >= GunSpec.All.Length || string.IsNullOrEmpty(playerKey)) return false;
        foreach (var mine in m_leases) if (mine.Value.PlayerKey == playerKey && m_pool[mine.Key] == variant) {
            id = mine.Key; reused = true;
            m_leases[id] = mine.Value with { MatchId = matchId, LifeId = lifeId };
            return true;
        }
        foreach (var parked in m_pool) if (parked.Value == variant && !m_leases.ContainsKey(parked.Key)) { id = parked.Key; break; }
        if (id < 0) {
            if (m_pool.Count >= Limit || CountOf(variant) >= PerModel) return false;
            int made = allocate(variant);
            if (made < 0 || m_pool.ContainsKey(made)) return false;
            m_pool[made] = variant; id = made;
        }
        int generation = m_generations.GetValueOrDefault(id) + 1;
        m_generations[id] = generation;
        m_leases[id] = new Lease(playerKey, matchId, lifeId, generation);
        return true;
    }
    /// <summary>Whether this is the record's present lending to that player (a late action of an earlier one is not).</summary>
    public bool Current(int id, string playerKey, int generation) => m_leases.TryGetValue(id, out var lease) && lease.PlayerKey == playerKey && lease.Generation == generation;
    public bool Return(int id) => m_leases.Remove(id);
    public List<int> ReturnAll(string playerKey) { var ids = LeasedTo(playerKey).ToList(); foreach (int id in ids) m_leases.Remove(id); return ids; }
    public List<int> ReturnEverything() { var ids = m_leases.Keys.OrderBy(id => id).ToList(); m_leases.Clear(); return ids; }
    /// <summary>A record this ledger lists that the registry no longer has (or has as another model) is not lent or counted
    /// as stock any more; it stays in the text that is saved, so nothing is hidden.</summary>
    public void Quarantine(int id) { m_leases.Remove(id); if (m_pool.Remove(id, out int variant)) m_quarantined.Add((id, variant)); }
    readonly List<(int Id, int Variant)> m_quarantined = [];
    public IReadOnlyList<(int Id, int Variant)> Quarantined => m_quarantined;

    // "1|id:variant:generation,...|id:player:match:life,...|id:variant,..." (pool, leases at save time, quarantined)
    public string Encode() {
        if (Unreadable is not null) return Unreadable;
        var ci = CultureInfo.InvariantCulture;
        string pool = string.Join(",", m_pool.Select(p => string.Create(ci, $"{p.Key}:{p.Value}:{m_generations.GetValueOrDefault(p.Key)}")));
        string leases = string.Join(",", m_leases.OrderBy(p => p.Key).Select(p => string.Create(ci, $"{p.Key}:{Uri.EscapeDataString(p.Value.PlayerKey)}:{p.Value.MatchId}:{p.Value.LifeId}")));
        string held = string.Join(",", m_quarantined.Select(q => string.Create(ci, $"{q.Id}:{q.Variant}")));
        return $"1|{pool}|{leases}|{held}";
    }
    public static DmArmory Decode(string text) {
        var armory = new DmArmory();
        if (string.IsNullOrEmpty(text)) return armory;
        try {
            var ci = CultureInfo.InvariantCulture; string[] parts = text.Split('|');
            if (parts.Length != 4 || parts[0] != "1") throw new FormatException("version");
            foreach (string item in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                string[] f = item.Split(':'); int id = int.Parse(f[0], ci), variant = int.Parse(f[1], ci), generation = int.Parse(f[2], ci);
                if (!ScGunEncoding.IsRecordId(id) || variant < 0 || variant >= GunSpec.All.Length || generation < 0 || !armory.m_pool.TryAdd(id, variant)) throw new FormatException("pool");
                armory.m_generations[id] = generation;
            }
            foreach (string item in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                string[] f = item.Split(':'); int id = int.Parse(f[0], ci);
                if (!armory.m_pool.ContainsKey(id)) throw new FormatException("lease");
                armory.m_leases[id] = new Lease(Uri.UnescapeDataString(f[1]), int.Parse(f[2], ci), int.Parse(f[3], ci), armory.m_generations.GetValueOrDefault(id));
            }
            foreach (string item in parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                string[] f = item.Split(':'); armory.m_quarantined.Add((int.Parse(f[0], ci), int.Parse(f[1], ci)));
            }
            if (armory.m_pool.Count > Limit) throw new FormatException("limit");
            return armory;
        }
        catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or ArgumentException) {
            return new DmArmory { Unreadable = text };
        }
    }
}
