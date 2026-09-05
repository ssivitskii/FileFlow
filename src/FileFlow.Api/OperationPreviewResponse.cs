namespace FileFlow.Api;

public sealed record OperationPreviewResponse(
    string Operation,
    string Source,
    string? Destination,
    bool IsValid,
    bool IsConflict,
    string Summary,
    string? Error);
