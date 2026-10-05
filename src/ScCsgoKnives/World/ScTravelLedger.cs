using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TemplatesDatabase;
namespace Game;

/// <summary>What one world remembers about transfers: its own identity, the identity of every gun record that has one
/// (records that came from another world keep theirs), and the transfers it has committed.</summary>
public sealed class ScTravelLedger {
    public const string ReceiptsKey = "GunTravelReceipts";
    public const int ReceiptsKept = 64;
    public string WorldIdentity = Guid.NewGuid().ToString("N");
    /// <summary>Local record number -> identity (the saved GunTravelIdentities table, unchanged in form).</summary>
    public readonly Dictionary<int, string> Identities = [];
    readonly Dictionary<string, object> m_otherIdentityKeys = new(StringComparer.Ordinal);   // anything in that table this build does not read: written back as it was
    public sealed record ArrivalState(string Owner, string Envelope, IReadOnlyDictionary<string, int> Applied) {
        public int Version { get; init; } = 1;
    }
    public sealed record Receipt(string Transfer, string Digest, string World, bool Completed, IReadOnlyList<(string Identity, int Id)> Map) {
        public ArrivalState Arrival { get; init; }
    }
    readonly List<Receipt> m_receipts = [];
    public IReadOnlyList<Receipt> Receipts => m_receipts;

    public static string Token(string seed, int id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + id)));
    /// <summary>The identity of a local record: the one it came with, or (first time) one derived from this world's own
    /// identity and the number, the same value ScGunTravel.Capture writes on save.</summary>
    public string IdentityOf(int id) {
        if (!Identities.TryGetValue(id, out string identity)) Identities[id] = identity = Token(Guid.TryParse(WorldIdentity, out var uuid) ? uuid.ToString("N") : WorldIdentity, id);
        return identity;
    }
    /// <summary>Identity -> local number for every record of this world: the ones the table lists, and for a record it
    /// does not list (a world last saved by a build that keeps no such table, or never saved since the record was made)
    /// the identity that record gets here, as the saved-XML path gives every row one when the world is saved. A gun that
    /// was made in this world therefore finds its number again on return whether or not the table kept it.</summary>
    public Dictionary<string, int> Known(IEnumerable<int> records) {
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, token) in Identities) known.TryAdd(token, id);
        string seed = Guid.TryParse(WorldIdentity, out var uuid) ? uuid.ToString("N") : WorldIdentity;
        foreach (int id in records) if (!Identities.ContainsKey(id)) known.TryAdd(Token(seed, id), id);
        return known;
    }
    public Receipt Find(string transfer) => m_receipts.LastOrDefault(r => r.Transfer == transfer);
    public bool HasPending => m_receipts.Any(r => !r.Completed);
    public bool BlocksOwner(string owner) => owner is not null && m_receipts.Any(r => !r.Completed && r.Arrival?.Owner == owner);
    internal void Commit(Receipt receipt) {
        m_receipts.RemoveAll(r => r.Transfer == receipt.Transfer); m_receipts.Add(receipt);
        // An unresolved obligation must never be evicted by later successful trips.
        while (m_receipts.Count(r => r.Completed) > ReceiptsKept) m_receipts.RemoveAt(m_receipts.FindIndex(r => r.Completed));
    }

    public void LoadIdentities(ValuesDictionary saved) {
        Identities.Clear(); m_otherIdentityKeys.Clear();
        if (saved is null) return;
        foreach (var pair in saved) {
            if (int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && pair.Value is string token) Identities[id] = token;
            else m_otherIdentityKeys[pair.Key] = pair.Value;
        }
    }
    public ValuesDictionary SaveIdentities() {
        if (Identities.Count == 0 && m_otherIdentityKeys.Count == 0) return null;
        var d = new ValuesDictionary();
        foreach (var (id, token) in Identities) d.SetValue(id.ToString(CultureInfo.InvariantCulture), token);
        foreach (var (key, value) in m_otherIdentityKeys) d.SetValue(key, value);
        return d;
    }
    /// <summary>Committed mappings and arrival custody, newest last. Older five-field receipts still read unchanged.
    /// Arrival metadata is a versioned child group; record schema and item layout are untouched. An older binary that
    /// drops this group cannot safely resume an unfinished arrival; cross-version delivery needs its own gates.</summary>
    public ValuesDictionary SaveReceipts() {
        if (m_receipts.Count == 0) return null;
        var d = new ValuesDictionary();
        var arrivals = new ValuesDictionary();
        for (int i = 0; i < m_receipts.Count; i++) {
            var r = m_receipts[i];
            d.SetValue(i.ToString("000", CultureInfo.InvariantCulture), string.Join("|", r.Transfer.Replace("|", ""), r.Digest, r.World.Replace("|", ""), r.Completed ? "1" : "0",
                string.Join(",", r.Map.Select(m => m.Identity + ":" + m.Id.ToString(CultureInfo.InvariantCulture)))));
            if (r.Arrival is { } arrival) arrivals.SetValue(r.Transfer, JsonSerializer.Serialize(arrival));
        }
        if (arrivals.Count > 0) d.SetValue("Arrivals", arrivals);
        return d;
    }
    public void LoadReceipts(ValuesDictionary saved) {
        m_receipts.Clear();
        if (saved is null) return;
        var arrivals = saved.GetValue<ValuesDictionary>("Arrivals", new());
        foreach (var pair in saved.Where(p => p.Key != "Arrivals").OrderBy(p => p.Key, StringComparer.Ordinal)) {
            string[] parts = (pair.Value as string ?? "").Split('|');
            if (parts.Length != 5 || parts[0].Length == 0) continue;   // unreadable: not a receipt (the transfer would be planned afresh, never guessed)
            var map = new List<(string, int)>(); bool ok = true;
            foreach (string item in parts[4].Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                string[] kv = item.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) map.Add((kv[0], id)); else ok = false;
            }
            if (ok) {
                ArrivalState arrival = null;
                if (arrivals.ContainsKey(parts[0])) {
                    arrival = JsonSerializer.Deserialize<ArrivalState>(arrivals.GetValue<string>(parts[0]));
                    var envelope = ScTravelEnvelope.Decode(arrival?.Envelope, out _, out _);
                    if (arrival?.Version != 1 || string.IsNullOrWhiteSpace(arrival.Owner) || arrival.Applied is null || envelope is null
                        || envelope.Transfer != parts[0] || envelope.Digest != parts[1] || envelope.World != parts[2]
                        || map.Select(m => m.Item1).Distinct().Count() != map.Count || map.Select(m => m.Item2).Distinct().Count() != map.Count
                        || envelope.Guns.Count != map.Count || envelope.Guns.Any(g => !map.Any(m => m.Item1 == g.Identity && m.Item2 > 0))
                        || arrival.Applied.Values.Distinct().Count() != arrival.Applied.Count
                        || arrival.Applied.Any(p => p.Value < 0 || !map.Any(m => m.Item1 == p.Key)))
                        throw new InvalidOperationException("Invalid pending gun arrival receipt; data retained, load refused");
                }
                m_receipts.Add(new Receipt(parts[0], parts[1], parts[2], parts[3] == "1", map) { Arrival = arrival });
            }
        }
        if (arrivals.Any(p => m_receipts.All(r => r.Transfer != p.Key || r.Arrival is null)))
            throw new InvalidOperationException("Pending gun arrival has no readable receipt; data retained, load refused");
    }
}
