namespace FileFlow.Api;

public sealed record WorkspaceEntry(string Name, string Path, string Kind, long? Size);
