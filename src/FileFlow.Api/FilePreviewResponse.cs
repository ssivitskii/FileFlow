namespace FileFlow.Api;

public sealed record FilePreviewResponse(string Path, string Text, int BytesRead, bool Truncated);
