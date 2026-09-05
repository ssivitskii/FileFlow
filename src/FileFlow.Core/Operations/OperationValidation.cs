namespace FileFlow.Core.Operations;

public sealed record OperationValidation(bool IsValid, string? Error, bool IsConflict)
{
    public static OperationValidation Valid { get; } = new(true, null, false);

    public static OperationValidation Conflict(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new OperationValidation(false, error, true);
    }

    public static OperationValidation Invalid(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new OperationValidation(false, error, false);
    }
}
