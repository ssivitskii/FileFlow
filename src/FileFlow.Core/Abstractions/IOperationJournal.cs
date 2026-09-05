using FileFlow.Core.Operations;

namespace FileFlow.Core.Abstractions;

public interface IOperationJournal
{
    string ApplicationDataRoot { get; }

    string TrashRoot { get; }

    void Append(OperationJournalEntry entry);

    IReadOnlyList<OperationJournalEntry> ReadHistory();

    OperationJournalEntry? FindLatest(Guid transactionId);
}
