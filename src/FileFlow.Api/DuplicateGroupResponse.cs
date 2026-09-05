namespace FileFlow.Api;

public sealed record DuplicateGroupResponse(string Sha256, long Size, IReadOnlyList<DuplicateFile> Files);
