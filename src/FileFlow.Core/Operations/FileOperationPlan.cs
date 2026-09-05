namespace FileFlow.Core.Operations;

public sealed record FileOperationPlan(
    Guid TransactionId,
    FileOperationKind Operation,
    string ConnectedRoot,
    string Source,
    string? Destination,
    string? TrashPath);
