namespace FileFlow.Api;

public sealed record OperationPreviewRequest(string Operation, string Source, string? Destination);
