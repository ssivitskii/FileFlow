namespace FileFlow.Api;

public sealed record HistoryEntryResponse(
    Guid Id,
    DateTimeOffset Time,
    string Kind,
    string Status,
    string Source,
    string? Destination);
