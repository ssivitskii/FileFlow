namespace FileFlow.Api;

public sealed record DuplicateResponse(IReadOnlyList<DuplicateGroupResponse> Groups);
