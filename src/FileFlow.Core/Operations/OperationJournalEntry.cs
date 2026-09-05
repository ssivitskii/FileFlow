namespace FileFlow.Core.Operations;

public sealed record OperationJournalEntry(
    Guid TransactionId,
    DateTimeOffset Timestamp,
    FileOperationKind Operation,
    string ConnectedRoot,
    string Source,
    string? Destination,
    string? TrashPath,
    string Fingerprint,
    FileOperationStatus Status);
