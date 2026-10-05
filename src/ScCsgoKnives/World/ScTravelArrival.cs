using System.Xml.Linq;
using Engine;
using GameEntitySystem;
namespace Game;

// subworld-travel-generic-20261003 (docs/tasks/subworld-travel-generic-20261003.md). Guns carried between a world and its
// sub-worlds by ANY sub-world mod, without that mod calling us.
//
// What every sub-world mod we found does (Ancient World 0.41.15/0.41.16, Ghoul 2.0; the Command Block mod's "world" only
// switches to another, unrelated world): it saves the world it leaves before it loads the next one, and it keeps its
// sub-worlds inside the world's own folder. Two ways of bringing the player along follow:
//   - it copies the player's saved XML into the next world before loading it (Ghoul): the packet ScGunTravel.Capture
//     writes into that XML on every save comes along, and ScGunTravel.BeforeLoad imports it while the world is read;
//   - it carries only the item values and puts them back into the inventory after the next world has loaded (Ancient
//     World). The gun numbers come along, their records do not. This file covers that case: the world just left was
//     saved a moment ago with the same packet in its Project.xml; when the player's inventory here settles holding
//     exactly what that packet says was carried (same slots, same gun data, this world's gun block), the carried records
//     are imported with the one rule of ScItemTravel.Resolve and the slots get their local numbers.
// Never across two different top-level worlds, never by number or model alone, never twice for one departure (the
// transfer id is the departure's own; the receipt is kept in this world).

/// <summary>Worlds that belong together: a world and the sub-worlds kept inside its folder.</summary>
public static class ScWorldTree {
    public static string Canonical(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');
    static string Parent(string path) { int i = path.LastIndexOf('/'); return i <= 0 ? null : path[..i]; }
    /// <summary>The top-most folder, from <paramref name="directory"/> up, that is a world (holds a Project.xml): the world
    /// a sub-world belongs to (Ancient World: &lt;world&gt;/Dimensions/Ancient; Ghoul: &lt;world&gt;/&lt;name&gt;). Two
    /// different top-level worlds never share one.</summary>
    public static string Root(string directory, Func<string, bool> isWorld) {
        string root = null;
        for (string d = Canonical(directory); !string.IsNullOrEmpty(d); d = Parent(d)) if (isWorld(d)) root = d;
        return root;
    }
    /// <summary>Two different worlds of one tree (a world and its sub-world, or two sub-worlds of one world).</summary>
    public static bool Related(string a, string b, Func<string, bool> isWorld) {
        a = Canonical(a); b = Canonical(b);
        if (a.Length == 0 || b.Length == 0 || string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        string root = Root(a, isWorld);
        return root is not null && string.Equals(root, Root(b, isWorld), StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Every world of the tree under <paramref name="root"/>, the root included, <paramref name="depth"/> folder
    /// levels down.</summary>
    public static List<string> Worlds(string root, Func<string, IEnumerable<string>> children, Func<string, bool> isWorld, int depth = 3) {
        var worlds = new List<string>();
        void Walk(string d, int left) {
            if (isWorld(d)) worlds.Add(Canonical(d));
            if (left > 0) foreach (string c in children(d)) Walk(Canonical(c), left - 1);
        }
        if (!string.IsNullOrEmpty(root)) Walk(Canonical(root), depth);
        return worlds;
    }
    public static bool IsWorld(string directory) { try { return Storage.FileExists(Storage.CombinePaths(directory, "Project.xml")); } catch (Exception) { return false; } }
    public static IEnumerable<string> Children(string directory) {
        try { return Storage.ListDirectoryNames(directory).Select(n => Storage.CombinePaths(directory, n)).ToList(); } catch (Exception) { return []; }
    }
    public static XElement ReadProject(string directory) {
        try {
            using var stream = Storage.OpenFile(Storage.CombinePaths(directory, "Project.xml"), OpenFileMode.Read);
            return XElement.Load(stream);
        }
        catch (Exception) { return null; }
    }
}

/// <summary>The check a world makes after it has loaded: did the player arrive from a world of the same tree carrying CS
/// guns whose records stayed behind? One per loaded world; it ends when the carried guns are taken in, when the guns
/// cannot be taken in (the reason is shown), or when nothing matching arrives within <see cref="Window"/> seconds.</summary>
public sealed class ScTravelArrival {
    public sealed record Candidate(string Source, string Inventory, ScTravelEnvelope Envelope, string Text, string Error);
    /// <summary>Seconds the inventory must stay unchanged before it is read as "what arrived" (a mod restores it when the
    /// player becomes available; reading earlier could take the world's own saved inventory for the carried one).</summary>
    public const double Quiet = 1.0;
    /// <summary>Seconds after the first look at the player during which an arrival is still expected, and after taking the
    /// guns in, during which a late second restore by the mod is still mapped the same way.</summary>
    public const double Window = 60;
    /// <summary>The world this process left last (set when a world closes). Null after a restart: then every other world
    /// of the tree is asked, and only one that matches alone is taken.</summary>
    public static string LastLeft;

    readonly List<Candidate> m_candidates;
    double m_start = double.NaN, m_quietSince, m_until;
    string m_signature;
    Candidate m_taken;
    ScTravelLedger Ledger => ScGunRegistry.Current?.Travel;
    ScTravelLedger.Receipt Receipt => m_taken is null ? null : Ledger?.Find(m_taken.Envelope.Transfer);
    public bool Finished { get; private set; }
    bool m_settled;   // the first look at a settled inventory found nothing to take: nothing is held back any more
    public IReadOnlyList<Candidate> Candidates => m_candidates;
    ScTravelArrival(List<Candidate> candidates) { m_candidates = candidates; }

    /// <summary>The check for the world in <paramref name="here"/>, or null when there is nothing to check: the saved-XML
    /// path already took the guns in, the world left is not of this tree (another world opened from the list, the Command
    /// Block mod's "world"), the same world was reopened, or no world of the tree left a packet.</summary>
    public static ScTravelArrival Prepare(string here, string left, bool importedOnLoad, ScTravelLedger ledger,
        Func<string, bool> isWorld, Func<string, IEnumerable<string>> children, Func<string, XElement> readProject) {
        if (ledger is null) return null;
        // The destination's own receipt survives source-world removal, restarts and mixed item numbers.
        var pending = ledger.Receipts.Where(r => !r.Completed && r.Arrival is not null).Select(r => {
            var envelope = ScTravelEnvelope.Decode(r.Arrival.Envelope, out _, out _);
            return new Candidate(r.World, r.Arrival.Owner, envelope, r.Arrival.Envelope, null);
        }).ToList();
        if (pending.Count > 0) return new ScTravelArrival(pending);
        if (importedOnLoad || string.IsNullOrEmpty(here)) return null;
        here = ScWorldTree.Canonical(here);
        List<string> sources;
        if (!string.IsNullOrEmpty(left)) {
            if (!ScWorldTree.Related(here, left, isWorld)) return null;
            sources = [ScWorldTree.Canonical(left)];
        }
        else {
            string root = ScWorldTree.Root(here, isWorld);
            sources = ScWorldTree.Worlds(root, children, isWorld).Where(w => !string.Equals(w, here, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var candidates = new List<Candidate>();
        foreach (string source in sources) {
            if (readProject(source) is not { } project) continue;
            List<(string Inventory, ScTravelEnvelope Envelope, string Error)> departures;
            try { departures = ScGunTravel.Departures(project); } catch (Exception e) { KnifeLog.Warning($"[GUN_TRAVEL] the saved packet of {source} could not be read: {e.Message}"); continue; }
            foreach (var (inventory, envelope, error) in departures) {
                if (envelope is not null && ledger.Find(envelope.Transfer) is { Completed: true }) continue;   // taken in here already
                candidates.Add(new(source, inventory, envelope, envelope?.Encode(), error));
            }
        }
        if (candidates.Count == 0) return null;
        KnifeLog.Information($"[GUN_TRAVEL] arrival check in {here}: {candidates.Count} departure(s) from {string.Join(", ", candidates.Select(c => c.Source).Distinct())}{(string.IsNullOrEmpty(left) ? " (no world left in this session: the tree was asked)" : "")}");
        return new ScTravelArrival(candidates);
    }

    /// <summary>Whether the gun in <paramref name="slot"/> may be one that arrived and has not got its local number yet. Such a
    /// gun is not used, and not reported as damaged, until the check has looked at a settled inventory once: its number may
    /// still name nothing here, or another gun of this world whose rounds and wear it would then spend.</summary>
    public bool Pending(int slot, int value) {
        if (Finished || m_settled) return false;
        int data = Terrain.ExtractData(value);
        if (m_taken is not null) return Receipt is { Completed: false } receipt && m_taken.Envelope.Slots.Any(s => s.Identity.Length > 0
            && !receipt.Arrival.Applied.ContainsKey(s.Identity) && Terrain.ExtractData(s.Value) == data);
        return m_candidates.Any(c => c.Envelope is not null && c.Envelope.Slots.Any(s => s.Slot == slot && s.Identity.Length > 0 && Terrain.ExtractData(s.Value) == data));
    }

    /// <summary>Whether <paramref name="inventory"/> holds what <paramref name="envelope"/> says was carried: every carried
    /// slot a gun of this world's gun block with the very same data bits (a mod translates the block index, nothing
    /// else).</summary>
    public static bool Holds(IInventory inventory, int block, ScTravelEnvelope envelope) {
        if (inventory is null || envelope is null || envelope.Slots.Count == 0) return false;
        foreach (var slot in envelope.Slots) {
            if (slot.Slot < 0 || slot.Slot >= inventory.SlotsCount || inventory.GetSlotCount(slot.Slot) <= 0) return false;
            int value = inventory.GetSlotValue(slot.Slot);
            if (Terrain.ExtractContents(value) != block || Terrain.ExtractData(value) != Terrain.ExtractData(slot.Value)) return false;
        }
        return true;
    }
    static string Signature(IInventory inventory) {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < inventory.SlotsCount; i++) text.Append(inventory.GetSlotCount(i) > 0 ? inventory.GetSlotValue(i) : 0).Append(',');
        return text.ToString();
    }

    /// <summary>One look at the traveller's inventory (game thread, the world's authority). Returns a message for the
    /// player, or null.</summary>
    public string Step(Project project, IInventory inventory, int block, double now) {
        if (Finished || inventory is null || block < 0) return null;
        if (ScInventoryIdentity.Inventory(inventory) is null) return null;
        inventory = ScInventoryIdentity.Inventory(inventory);
        var activeLedger = Ledger;
        string owner = ScItemTravel.Owner(inventory);
        if (m_taken is null) {
            var resumed = m_candidates.Where(c => c.Envelope is not null && Ledger?.Find(c.Envelope.Transfer) is { Completed: false } r
                && r.Digest == c.Envelope.Digest && (r.Arrival?.Owner == owner || r.Arrival is null && HoldsCommitted(inventory, block, c.Envelope, r))).ToList();
            if (resumed.Count == 1 && owner is not null) {
                m_taken = resumed[0]; m_until = now + Window; m_settled = false;
                if (Receipt.Arrival is null) Bind(inventory, block, owner, alreadyMapped: true);
            }
        }
        if (double.IsNaN(m_start)) { m_start = now; m_quietSince = now; m_signature = Signature(inventory); }
        string signature = Signature(inventory);
        if (signature != m_signature) { m_signature = signature; m_quietSince = now; }
        if (m_taken is not null) {   // taken in: a late second restore by the mod gets the same numbers
            if (owner is null || Receipt is not { Arrival: { } arrivalState } || arrivalState.Owner != owner) return null;
            if (now > m_until && Receipt.Completed) { Finished = true; return null; }
            if (now - m_quietSince >= Quiet) {
                // Late provider restores are recognized as a whole departure, never by a loose id match.
                if (Receipt.Completed) {
                    if (!Holds(inventory, block, m_taken.Envelope)) return null;
                    Bind(inventory, block, owner, alreadyMapped: false);
                }
                int remapped = Rewrite(inventory, block);
                if (!ReferenceEquals(Ledger, activeLedger) || ScItemTravel.Owner(inventory) != owner) return null;
                var again = ScItemTravel.Complete(project, inventory, m_taken.Text);
                bool firstCompletion = again.Ok;
                if (remapped > 0 || firstCompletion)
                    KnifeLog.Information($"[GUN_TRAVEL] transfer {m_taken.Envelope.Transfer}: mapped {remapped} slot(s) ({(again.Ok ? "complete" : again.Message)})");
                if (firstCompletion) return $"CS 枪械已随行迁移（{m_taken.Envelope.Guns.Count} 把，弹药与耐久保持）";
            }
            return null;
        }
        if (now - m_quietSince < Quiet) return null;
        var matching = m_candidates.Where(c => c.Envelope is not null ? Holds(inventory, block, c.Envelope) : false).ToList();
        if (matching.Count == 0) {
            m_settled = true;
            if (now - m_start > Window) {
                Finished = true;
                var refused = m_candidates.FirstOrDefault(c => c.Error is not null);
                KnifeLog.Information($"[GUN_TRAVEL] arrival check ended: the inventory never held what a departure carried{(refused is not null ? "; a departure from " + refused.Source + " was refused when saved: " + refused.Error : "")}");
            }
            return null;
        }
        if (matching.Select(c => c.Envelope.Transfer).Distinct().Count() > 1) {
            Finished = true;
            KnifeLog.Warning($"[GUN_TRAVEL] arrival check: {matching.Count} departures match this inventory ({string.Join(", ", matching.Select(c => c.Source))}); none taken (not guessed)");
            return "CS 枪械：无法确定这些枪来自哪个世界，未迁移（不会按编号猜测）";
        }
        var taken = matching[0];
        if (string.IsNullOrWhiteSpace(owner)) return "CS 枪械随行迁移尚未完成：无法确认库存所有者";
        var import = ScItemTravel.Import(project, inventory, taken.Text);
        if (!import.Ok) {
            Finished = true;
            KnifeLog.Warning($"[GUN_TRAVEL] arrival from {taken.Source}: refused ({import.Code}) {import.Message}");
            return "CS 枪械未能随行迁移：" + import.Message;
        }
        m_taken = taken; m_until = now + Window; m_settled = false;
        // Same-world receipts need no import or rewrite.
        if (import.Code == ScTravelCode.SameWorld) { Finished = true; return null; }
        Bind(inventory, block, owner, alreadyMapped: false);
        int rewritten = Rewrite(inventory, block);
        if (!ReferenceEquals(Ledger, activeLedger) || ScItemTravel.Owner(inventory) != owner) return null;
        var done = ScItemTravel.Complete(project, inventory, taken.Text);
        KnifeLog.Information($"[GUN_TRAVEL] arrival from {taken.Source} ({taken.Inventory}): transfer {taken.Envelope.Transfer}, {taken.Envelope.Guns.Count} carried gun(s), {rewritten} slot(s) given local numbers; {(done.Ok ? "complete" : "not complete: " + done.Message)}");
        if (!done.Ok) return "CS 枪械随行迁移尚未完成，物品恢复待处理：" + done.Message;
        return taken.Envelope.Guns.Count > 0 ? $"CS 枪械已随行迁移（{taken.Envelope.Guns.Count} 把，弹药与耐久保持）" : null;
    }

    static int Target(ScTravelEnvelope envelope, ScTravelLedger.Receipt receipt, ScTravelSlot slot, int block) {
        var gun = envelope.Guns.Single(g => g.Identity == slot.Identity);
        return Terrain.MakeBlockValue(block, 0, GunSpec.WithId(gun.Variant, receipt.Map.Single(m => m.Identity == slot.Identity).Id));
    }
    static bool HoldsCommitted(IInventory inventory, int block, ScTravelEnvelope envelope, ScTravelLedger.Receipt receipt) =>
        envelope.Slots.All(s => s.Slot >= 0 && s.Slot < inventory.SlotsCount && inventory.GetSlotCount(s.Slot) > 0
            && Terrain.ExtractContents(inventory.GetSlotValue(s.Slot)) == block
            && (Terrain.ExtractData(inventory.GetSlotValue(s.Slot)) == Terrain.ExtractData(s.Value)
                || s.Identity.Length > 0 && inventory.GetSlotValue(s.Slot) == Target(envelope, receipt, s, block)));

    void Bind(IInventory inventory, int block, string owner, bool alreadyMapped) {
        var receipt = Receipt;
        var applied = alreadyMapped ? m_taken.Envelope.Slots.Where(s => s.Identity.Length > 0
            && inventory.GetSlotValue(s.Slot) == Target(m_taken.Envelope, receipt, s, block)).ToDictionary(s => s.Identity, s => s.Slot) : [];
        Ledger.Commit(receipt with { Completed = false, Arrival = new(owner, m_taken.Text, applied) });
    }

    /// <summary>Only the receipt's remaining identities in its original inventory can be rewritten. Rollback can move a
    /// gun to another slot; require a unique remaining value and never scan other owners for a coincidental number.</summary>
    int Rewrite(IInventory inventory, int block) {
        int count = 0;
        var ledger = Ledger;
        var receipt = Receipt;
        // Mapped items stay in custody until all guns complete. Exclude them when source and target ids overlap.
        var reserved = new HashSet<int>();
        foreach (var mapped in m_taken.Envelope.Slots.Where(s => receipt.Arrival.Applied.ContainsKey(s.Identity))) {
            int target = Target(m_taken.Envelope, receipt, mapped, block);
            int held = receipt.Arrival.Applied[mapped.Identity];
            if (held >= inventory.SlotsCount || inventory.GetSlotCount(held) <= 0 || inventory.GetSlotValue(held) != target) return 0;
            reserved.Add(held);
        }
        var remaining = m_taken.Envelope.Slots.Where(s => s.Identity.Length > 0 && !receipt.Arrival.Applied.ContainsKey(s.Identity)).ToArray();
        // Resolve all original locations before changing any item: a new local number can equal another source number.
        var locations = remaining.ToDictionary(s => s.Identity, s => Enumerable.Range(0, inventory.SlotsCount)
            .Where(i => !reserved.Contains(i) && inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(inventory.GetSlotValue(i)) == block
                && Terrain.ExtractData(inventory.GetSlotValue(i)) == Terrain.ExtractData(s.Value)).ToArray());
        foreach (var slot in remaining) {
            if (!ReferenceEquals(Ledger, ledger) || ScItemTravel.Owner(inventory) != receipt.Arrival.Owner) break;
            if (locations[slot.Identity] is not { Length: 1 } found) continue;
            int index = found[0], current = inventory.GetSlotValue(index), target = Target(m_taken.Envelope, Receipt, slot, block);
            if (Terrain.ExtractData(current) != Terrain.ExtractData(slot.Value)) continue;
            if (ScInventoryTransaction.ReplaceWithCost(inventory, index, current, target, 0, 0)) {
                var progress = Receipt;
                var applied = new Dictionary<string, int>(progress.Arrival.Applied) { [slot.Identity] = index };
                Ledger.Commit(progress with { Arrival = progress.Arrival with { Applied = applied } });
                count++;
            }
            else KnifeLog.Warning($"[GUN_TRAVEL] slot {index + 1}: the local number could not be written");
        }
        return count;
    }
}
