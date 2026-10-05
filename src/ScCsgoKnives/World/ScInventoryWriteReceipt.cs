namespace Game;

/// <summary>Requested quantity, provider's report (Remove only), and measured change are distinct facts.
/// An exception is propagated by the writer; its finally still records Observed before that propagation.</summary>
internal readonly record struct ScInventoryWriteReceipt(int Requested, int? Reported, int Observed) {
    public bool Exact => Observed == Requested && (!Reported.HasValue || Reported.Value == Requested);
    public static ScInventoryWriteReceipt Removal(int requested, int reported, int before, int after) => new(requested, reported, Math.Max(0, before - after));
    public static ScInventoryWriteReceipt Addition(int requested, int before, int after) => new(requested, null, Math.Max(0, after - before));
}
