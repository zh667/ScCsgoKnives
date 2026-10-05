using System.Runtime.CompilerServices;
namespace Game;

/// <summary>Optional transport service: the platform's own inventory replication (its full-inventory packet), for slot
/// writes the engine does not announce itself.</summary>
public interface IScNetInventorySync {
    /// <summary>Server: send this inventory's present slots to every client now. False when the platform cannot address the
    /// inventory (not an entity's component).</summary>
    bool Publish(IInventory inventory);
    /// <summary>Server: send this inventory's present slots to one client only (an authoritative correction).</summary>
    bool Correct(IInventory inventory, ScNetPeer peer);
    /// <summary>Server: tell every client but <paramref name="except"/> which slot of this inventory is the active one
    /// (the platform's own active-slot message).</summary>
    bool AnnounceActiveSlot(IInventory inventory, ScNetPeer except);
}

/// <summary>mp-state-consistency-20261002 (W1/W2): a gun's slot value names its record, so the server has to send a slot
/// it rewrote as well as the record. The engine announces slot changes of ordinary inventories itself
/// (ComponentInventoryBase.OnSlotChange → the platform's inventory packet), but a creative inventory's AddSlotItems
/// replaces the slot without any announcement, and so does a direct slot write. A client then kept the template (30
/// rounds for ever) or the former record id until an unrelated drag happened to resend the whole inventory.
///
/// Every CS transaction that rewrites slots notes the inventory (<see cref="Touched"/>); when the transaction ends
/// (ScInventoryTransaction.Changed, success or rollback alike) the inventory is due. Once a frame, after every subsystem
/// has updated (<see cref="EndOfFrame"/>, called by the adapter), the server sends, in this order on the platform's one
/// ordered channel: the gun records that changed (each client's message ends with its own shot confirmation, which names
/// the item it is for and waits on the client until its slot holds that item), then each due inventory as the
/// platform's own inventory packet. A client therefore reads the record before the slot that names it, and the snapshot is the final
/// state of the frame: a rolled-back transaction publishes what it restored, never an intermediate value, and several
/// transactions on one inventory publish it once. A shot on an existing record changes no slot and publishes nothing
/// here: only its record row goes out. Outside a multiplayer host all of this is a no-op.</summary>
public static class ScNetSlots {
    static readonly ScSlotPublicationQueue s_queue = new();
    static readonly ConditionalWeakTable<object, object> s_unsupportedTold = new();

    /// <summary>Counters read by the offline checks: inventory packets sent, storages the platform cannot address,
    /// corrections sent to one client.</summary>
    public static int Published, Unsupported, Corrections;
    /// <summary>An inventory waits for this frame's publication (the shot confirmations then follow it).</summary>
    public static bool PublicationDue => s_queue.HasDue;

    /// <summary>A CS transaction is rewriting slots of this inventory; due when the transaction ends.</summary>
    public static void Touched(IInventory inventory) {
        if (!ScNet.IsHost || inventory is null) return;
        if (ScInventoryIdentity.Storage(inventory) is { } storage) s_queue.Touch(storage);
    }

    /// <summary>The transaction on this inventory ended (called by ScInventoryTransaction.Changed).</summary>
    internal static void TransactionEnded(IInventory inventory) {
        if (!s_queue.HasTouched || inventory is null) return;
        if (!ScNet.IsHost) { s_queue.ClearTouched(); return; }
        // Publishing never fails the transaction that ended: the server's state stands whatever the network layer does.
        try { if (ScInventoryIdentity.Storage(inventory) is { } storage && s_queue.Untouch(storage)) Changed(inventory); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("scnet-slots-publish", "[ScCsgoNet] server: slot publication failed: " + e); }
    }

    /// <summary>Server: this inventory's slots were rewritten outside the engine's own slot-change announcement.</summary>
    public static void Changed(IInventory inventory) {
        if (!ScNet.IsHost || inventory is null) return;
        object storage = ScInventoryIdentity.Storage(inventory);
        if (storage is null) return;
        s_queue.Untouch(storage);
        if (storage is IInventory real && ScNet.Transport is IScNetInventorySync) {
            s_queue.Enqueue(real); return;
        }
        NotAddressed(storage);
    }

    /// <summary>Server, once a frame after every subsystem has updated (called by the adapter): records (with the shot
    /// confirmations), then the due inventories.</summary>
    public static void EndOfFrame() {
        if (!s_queue.HasDue) return;
        var due = s_queue.TakeDue();
        if (!ScNet.IsHost || ScNet.Transport is not IScNetInventorySync sync) return;
        try {
            ScNetMirror.FlushRows();
            foreach (IInventory inventory in due) { if (sync.Publish(inventory)) Published++; else NotAddressed(inventory); }
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("scnet-slots-publish", "[ScCsgoNet] server: slot publication failed: " + e); }
    }

    /// <summary>A storage the platform's inventory packet cannot address (a provider's own shared storage object): its
    /// provider's network bridge owns the replication. Said once per storage, never guessed around.</summary>
    static void NotAddressed(object storage) {
        Unsupported++;
        if (s_unsupportedTold.TryGetValue(storage, out _)) return;
        s_unsupportedTold.Add(storage, storage);
        KnifeLog.Warning($"[ScCsgoNet] server: slots of {storage.GetType().FullName} changed but the platform's inventory packet cannot address it; its provider's own network bridge has to replicate it");
    }

    /// <summary>Server: one client's view of this inventory is behind (its input named a slot value the server does not
    /// hold); send it the present records and slots.</summary>
    public static bool Correct(IInventory inventory, ScNetPeer peer) {
        if (!ScNet.IsHost || inventory is null || peer is null) return false;
        ScNetMirror.FlushRows();
        if (ScNet.Transport is not IScNetInventorySync sync || ScInventoryIdentity.Storage(inventory) is not IInventory real || !sync.Correct(real, peer)) return false;
        Corrections++; return true;
    }

    /// <summary>Server: it took over the active slot a client holds (ScNetGuns); the other clients see that player's hand
    /// by it.</summary>
    public static bool ActiveSlotAdopted(IInventory inventory, ScNetPeer owner) =>
        ScNet.IsHost && inventory is not null && ScNet.Transport is IScNetInventorySync sync && ScInventoryIdentity.Storage(inventory) is IInventory real && sync.AnnounceActiveSlot(real, owner);

    /// <summary>World closed or session ended: nothing noted for the old world survives.</summary>
    public static void Clear() => s_queue.Clear();
}
