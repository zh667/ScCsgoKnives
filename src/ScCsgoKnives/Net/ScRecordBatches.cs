namespace Game;

/// <summary>Immutable confirmation carried with one peer's record batch. It is a snapshot of that player's counted
/// input, not another owner of the counters or of client prediction.</summary>
internal readonly record struct ScShotAcknowledgement(int Selection, int Value, int Fired, int Skipped) {
    public bool ValidFor(int selection) => Selection == selection && Fired >= 0 && Skipped >= 0;
    public int Resolved => Fired + Skipped;
}

/// <summary>Record/confirmation enqueue contract for one peer. All batches are attempted on false; exceptions still
/// escape. Only full success acknowledges the snapshot. Enqueued means queued locally, not applied remotely.</summary>
internal sealed class ScRecordBatches(List<(int Id, string Row)> rows, int rowsPerMessage) {
    readonly int m_batches = Math.Max(1, (rows.Count + rowsPerMessage - 1) / rowsPerMessage);
    public bool Send(ScShotAcknowledgement? acknowledgement,
        Func<List<(int Id, string Row)>, ScShotAcknowledgement?, bool> enqueue, Action acknowledged) {
        if (rows.Count == 0 && !acknowledgement.HasValue) return true;
        bool sent = true;
        for (int b = 0; b < m_batches; b++) {
            var batch = rows.Skip(b * rowsPerMessage).Take(rowsPerMessage).ToList();
            var ack = b == m_batches - 1 ? acknowledgement : null;
            sent &= enqueue(batch, ack);
        }
        if (sent && acknowledgement.HasValue) acknowledged();
        return sent;
    }
}
