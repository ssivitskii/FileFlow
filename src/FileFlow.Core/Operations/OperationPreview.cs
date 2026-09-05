namespace FileFlow.Core.Operations;

public sealed record OperationPreview(Guid TransactionId, bool IsValid, string Description, string? Error);
