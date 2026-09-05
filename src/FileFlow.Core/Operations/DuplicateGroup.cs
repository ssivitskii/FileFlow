namespace FileFlow.Core.Operations;

public sealed record DuplicateGroup(long Size, string Sha256, IReadOnlyList<string> Files);
