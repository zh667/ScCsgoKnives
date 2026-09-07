using TemplatesDatabase;
namespace Game;

/// <summary>A measured inverse inventory operation. Positive Count returns removed items; negative Count
/// removes an inserted replacement first. Creative slots use a conditional value replacement.</summary>
public sealed class ScGunUndo {
    public int Slot, Value, Count;
    public bool Creative;
    public int ReplacedValue;
    public ScGunUndo Copy() => (ScGunUndo)MemberwiseClone();
}

/// <summary>Durable, world-owned compensation. Batches run in inverse operation order, stopping at an
/// unresolved step; an original gun is never returned while its failed replacement still exists.
/// Successful partial returns subtract their observed amount immediately, including add-then-throw.</summary>
public sealed class ScGunRecovery {
    public sealed class Batch {
        public long Id;
        public string Owner;
        public readonly List<ScGunUndo> Steps = [];
    }
    readonly List<Batch> m_batches = [];
    long m_next = 1;
    public int Count => m_batches.Count;
    public IEnumerable<Batch> Batches => m_batches;
    public bool HasPending(string owner) => m_batches.Any(b => b.Owner == owner);
    public void Enqueue(string owner, IEnumerable<ScGunUndo> remaining) {
        var batch = new Batch { Id = m_next++, Owner = owner };
        batch.Steps.AddRange(remaining.Where(s => s.Count != 0).Select(s => s.Copy()));
        if (batch.Steps.Count == 0) return;
        m_batches.Add(batch);
        KnifeLog.Error($"gun recovery {batch.Id}: {batch.Steps.Count} pending step(s) for {owner}; saved with this world and retried when the inventory accepts them");
    }
    static int Quantity(IInventory inventory, int slot, int value) => inventory.GetSlotValue(slot) == value ? inventory.GetSlotCount(slot) : 0;

    static void ReturnAt(IInventory inventory, int slot, ScGunUndo step) {
        if (step.Count <= 0 || slot < 0 || slot >= inventory.SlotsCount) return;
        if (inventory.GetSlotCount(slot) > 0 && inventory.GetSlotValue(slot) != step.Value) return;
        int before = Quantity(inventory, slot, step.Value);
        int amount = Math.Min(step.Count, Math.Max(0, inventory.GetSlotCapacity(slot, step.Value) - before));
        if (amount == 0) return;
        try { inventory.AddSlotItems(slot, step.Value, amount); }
        catch (Exception e) { KnifeLog.Warning($"gun recovery Add at {slot}: {e.Message}"); }
        finally {
            int added = Math.Max(0, Quantity(inventory, slot, step.Value) - before);
            step.Count -= Math.Min(step.Count, added);
        }
    }
    public static bool Apply(IInventory inventory, List<ScGunUndo> steps) {
        while (steps.Count > 0) {
            var step = steps[0];
            try {
                if (step.Creative) {
                    if (step.Slot < 0 || step.Slot >= inventory.SlotsCount) return false;
                    int value = inventory.GetSlotValue(step.Slot);
                    if (value == step.Value) step.Count = 0;
                    else if (value == step.ReplacedValue) {
                        try { inventory.AddSlotItems(step.Slot, step.Value, 1); }
                        catch (Exception e) { KnifeLog.Warning("gun recovery creative slot: " + e.Message); }
                        if (inventory.GetSlotValue(step.Slot) == step.Value) step.Count = 0;
                    }
                }
                else if (step.Count < 0) {
                    // Removal debts are restricted to the exact slot. Never remove a similar gun elsewhere.
                    if (step.Slot < 0 || step.Slot >= inventory.SlotsCount) return false;
                    int before = Quantity(inventory, step.Slot, step.Value);
                    if (before > 0) {
                        try { inventory.RemoveSlotItems(step.Slot, Math.Min(before, -step.Count)); }
                        catch (Exception e) { KnifeLog.Warning("gun recovery Remove: " + e.Message); }
                        finally {
                            int removed = Math.Max(0, before - Quantity(inventory, step.Slot, step.Value));
                            step.Count += Math.Min(-step.Count, removed);
                        }
                    }
                }
                else {
                    ReturnAt(inventory, step.Slot, step);
                    for (int slot = 0; slot < inventory.SlotsCount && step.Count > 0; slot++)
                        if (slot != step.Slot) ReturnAt(inventory, slot, step);
                }
            }
            catch (Exception e) { KnifeLog.Warning("gun recovery inventory unavailable: " + e.Message); }
            if (step.Count != 0) return false;
            steps.RemoveAt(0);
        }
        return true;
    }
    public int Retry(Func<string, IInventory> resolve) {
        if (resolve is null || !ScGunMutation.TryEnter()) return 0;
        int completed = 0;
        try {
            foreach (var batch in m_batches.ToArray()) {
                var inventory = resolve(batch.Owner);
                if (inventory is null) continue; // player absent / container unloaded: keep the durable claim
                bool done = Apply(inventory, batch.Steps);
                ScInventoryTransaction.Changed(inventory);
                if (!done) continue;
                m_batches.Remove(batch); completed++;
                KnifeLog.Information($"gun recovery {batch.Id} completed for {batch.Owner}");
            }
        }
        finally { ScGunMutation.Exit(); }
        return completed;
    }
    public ValuesDictionary Save() {
        var result = new ValuesDictionary(); result.SetValue("Schema", 1); result.SetValue("Next", m_next);
        var batches = new ValuesDictionary();
        foreach (var batch in m_batches) {
            var entry = new ValuesDictionary(); entry.SetValue("Owner", batch.Owner);
            var steps = new ValuesDictionary();
            for (int i = 0; i < batch.Steps.Count; i++) {
                var s = batch.Steps[i];
                steps.SetValue(i.ToString(), $"{s.Slot},{s.Value},{s.Count},{(s.Creative ? 1 : 0)},{s.ReplacedValue}");
            }
            entry.SetValue("Steps", steps); batches.SetValue(batch.Id.ToString(), entry);
        }
        result.SetValue("Batches", batches); return result;
    }
    public static ScGunRecovery Load(ValuesDictionary data) {
        var result = new ScGunRecovery();
        if (data is null) return result;
        if (data.GetValue<int>("Schema", 0) != 1) throw new InvalidOperationException("Unknown gun recovery schema");
        result.m_next = data.GetValue<long>("Next", 1);
        var batches = data.GetValue<ValuesDictionary>("Batches", new());
        foreach (var pair in batches) {
            if (!long.TryParse(pair.Key, out long id) || id < 1 || id == long.MaxValue || pair.Value is not ValuesDictionary entry)
                throw new InvalidOperationException("Invalid recovery batch");
            var batch = new Batch { Id = id, Owner = entry.GetValue<string>("Owner") };
            if (string.IsNullOrWhiteSpace(batch.Owner)) throw new InvalidOperationException("Missing recovery owner");
            var ordered = new SortedDictionary<int, ScGunUndo>();
            foreach (var item in entry.GetValue<ValuesDictionary>("Steps")) {
                var parts = (item.Value as string ?? "").Split(',');
                if (!int.TryParse(item.Key, out int index) || index < 0 || parts.Length != 5) throw new InvalidOperationException("Invalid recovery step");
                int[] values = parts.Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (values[2] == 0 || values[2] == int.MinValue || values[3] is not (0 or 1)) throw new InvalidOperationException("Invalid recovery quantity");
                ordered.Add(index, new ScGunUndo { Slot = values[0], Value = values[1], Count = values[2], Creative = values[3] == 1, ReplacedValue = values[4] });
            }
            batch.Steps.AddRange(ordered.Values); result.m_batches.Add(batch); result.m_next = Math.Max(result.m_next, id + 1);
        }
        if (result.m_next < 1 || result.m_next == long.MaxValue) throw new InvalidOperationException("Invalid recovery watermark");
        return result;
    }
}

/// <summary>Receipts are based on observed inventory deltas in finally, even when a container mutates then
/// throws. All material, ammo and gun operations use this one journal; no nested opaque inventory helper.</summary>
internal sealed class ScGunInventoryJournal(IInventory inventory) {
    readonly List<ScGunUndo> m_undo = [];
    static int Quantity(IInventory inv, int slot, int value) => inv.GetSlotValue(slot) == value ? inv.GetSlotCount(slot) : 0;
    public void RemoveExact(int slot, int value, int amount) {
        int before = Quantity(inventory, slot, value), reported = 0, removed = 0;
        if (amount <= 0 || before < amount) throw new InvalidOperationException("Inventory deduction no longer available");
        try { reported = inventory.RemoveSlotItems(slot, amount); }
        finally {
            removed = Math.Max(0, before - Quantity(inventory, slot, value));
            if (removed > 0) m_undo.Add(new ScGunUndo { Slot = slot, Value = value, Count = removed });
        }
        if (reported != amount || removed != amount) throw new InvalidOperationException("Inventory deducted an unexpected amount");
    }
    public void AddExact(int slot, int value, int amount) {
        int before = Quantity(inventory, slot, value), added = 0;
        if (inventory.GetSlotCount(slot) > 0 && inventory.GetSlotValue(slot) != value) throw new InvalidOperationException("Replacement slot occupied");
        try { inventory.AddSlotItems(slot, value, amount); }
        finally {
            added = Math.Max(0, Quantity(inventory, slot, value) - before);
            if (added > 0) m_undo.Add(new ScGunUndo { Slot = slot, Value = value, Count = -added });
        }
        if (added != amount) throw new InvalidOperationException("Inventory accepted an unexpected amount");
    }
    public void ReplaceCreative(int slot, int expected, int replacement) {
        try { inventory.AddSlotItems(slot, replacement, 1); }
        finally {
            if (inventory.GetSlotValue(slot) == replacement && replacement != expected)
                m_undo.Add(new ScGunUndo { Slot = slot, Value = expected, Count = 1, Creative = true, ReplacedValue = replacement });
        }
        if (inventory.GetSlotValue(slot) != replacement) throw new InvalidOperationException("Creative slot rejected replacement");
    }
    public void Rollback(ScGunRecovery recovery, string owner) {
        m_undo.Reverse();
        if (!ScGunRecovery.Apply(inventory, m_undo)) recovery.Enqueue(owner, m_undo);
        m_undo.Clear();
        ScInventoryTransaction.Changed(inventory);
    }
}
