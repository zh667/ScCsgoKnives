using System.Runtime.CompilerServices;
namespace Game;

/// <summary>The process's inventory epochs and synchronous publication boundary, separate from the write journal.
/// Caller still owns its lock, record commit and compensation decision. No record revision or sent cache lives here.</summary>
internal static class ScInventoryChanges {
    sealed class Epoch { public long Value; }
    static readonly ConditionalWeakTable<object, Epoch> Epochs = new();
    public static long Revision(IInventory inventory) => inventory is null ? -1 : Epochs.GetOrCreateValue(ScInventoryIdentity.Storage(inventory)).Value;
    public static void Finished(IInventory inventory) {
        if (inventory is null) return;
        Epochs.GetOrCreateValue(ScInventoryIdentity.Storage(inventory)).Value++;
        ScNetSlots.TransactionEnded(inventory);
    }
}
