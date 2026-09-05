namespace FileFlow.Api;

public sealed record WorkspaceResponse(string Path, IReadOnlyList<WorkspaceEntry> Entries);
