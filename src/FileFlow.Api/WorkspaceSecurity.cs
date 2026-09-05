#pragma warning disable SA1402, SA1649 // Cohesive path-policy implementation is kept together for auditability.

namespace FileFlow.Api;

public sealed class WorkspaceOptions
{
    public string WorkspaceRoot { get; set; } = string.Empty;

    public string ApplicationDataRoot { get; set; } = string.Empty;
}

public sealed class ApiProblemException(int statusCode, string title, string detail) : Exception(title)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;

    public string Detail { get; } = detail;
}

public sealed class RootedWorkspace
{
    private readonly StringComparison _comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public RootedWorkspace(IConfiguration configuration, IHostEnvironment environment)
    {
        string root = configuration["FileFlow:WorkspaceRoot"] ?? string.Empty;
        string dataRoot = configuration["FileFlow:ApplicationDataRoot"] ?? string.Empty;
        WorkspaceRoot = NormalizeConfiguredPath(root, environment.ContentRootPath, "workspace");
        ApplicationDataRoot = NormalizeConfiguredPath(dataRoot, environment.ContentRootPath, "application-data");
        if (!Directory.Exists(WorkspaceRoot))
            throw new InvalidOperationException("The configured workspace root does not exist.");
        if (IsReparsePoint(WorkspaceRoot))
            throw new InvalidOperationException("The configured workspace root cannot be a symbolic link.");
        if (Directory.Exists(ApplicationDataRoot) && IsReparsePoint(ApplicationDataRoot))
            throw new InvalidOperationException("The configured application-data root cannot be a symbolic link.");
        if (Contains(ApplicationDataRoot, WorkspaceRoot) || Contains(WorkspaceRoot, ApplicationDataRoot))
            throw new InvalidOperationException("Workspace and application-data roots must be separate.");
    }

    public string WorkspaceRoot { get; }

    public string ApplicationDataRoot { get; }

    public string JournalPath => Path.Combine(ApplicationDataRoot, "journal.jsonl");

    public bool Contains(string path, string root)
    {
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.Equals(normalizedRoot, _comparison)
            || normalizedPath.StartsWith(string.Concat(normalizedRoot, Path.DirectorySeparatorChar), _comparison);
    }

    private static string NormalizeConfiguredPath(string value, string contentRoot, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"A {label} root must be configured.");
        string combined = Path.IsPathFullyQualified(value) ? value : Path.Combine(contentRoot, value);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(combined));
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}

public sealed class PathPolicy(RootedWorkspace workspace)
{
    public const int MaxRelativeLength = 512;
    public const int MaxSegments = 32;

    public string Resolve(string? requestedPath, bool allowRoot, bool mustExist)
    {
        string value = requestedPath ?? ".";
        if (value == ".")
        {
            if (!allowRoot)
                throw BadPath();
            EnsureNoLinks(workspace.WorkspaceRoot);
            return workspace.WorkspaceRoot;
        }

        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaxRelativeLength
            || value.Contains('\0', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(value))
        {
            throw BadPath();
        }

        string[] segments = value.Split('/');
        if (segments.Length > MaxSegments
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw BadPath();
        }

        string resolved = Path.GetFullPath(Path.Combine([workspace.WorkspaceRoot, .. segments]));
        if (!workspace.Contains(resolved, workspace.WorkspaceRoot))
            throw BadPath();
        if (mustExist && !File.Exists(resolved) && !Directory.Exists(resolved))
            throw new ApiProblemException(StatusCodes.Status404NotFound, "Not found", "The requested workspace item was not found.");
        EnsureNoLinks(resolved);
        return resolved;
    }

    public string ToRelative(string absolutePath)
    {
        if (!workspace.Contains(absolutePath, workspace.WorkspaceRoot))
            throw BadPath();
        string relative = Path.GetRelativePath(workspace.WorkspaceRoot, absolutePath);
        return relative == "." ? "." : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public void Recheck(string absolutePath)
    {
        if (!workspace.Contains(absolutePath, workspace.WorkspaceRoot))
            throw BadPath();
        EnsureNoLinks(absolutePath);
    }

    private static ApiProblemException BadPath() => new(
        StatusCodes.Status400BadRequest,
        "Invalid workspace path",
        "Use a bounded, root-relative path without traversal or ambiguous separators.");

    private static ApiProblemException LinkProblem() => new(
        StatusCodes.Status400BadRequest,
        "Unsupported workspace item",
        "Symbolic links and reparse points are not supported.");

    private static bool IsExistingLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private void EnsureNoLinks(string path)
    {
        string relative = Path.GetRelativePath(workspace.WorkspaceRoot, path);
        string current = workspace.WorkspaceRoot;
        if (IsExistingLink(current))
            throw LinkProblem();
        if (relative == ".")
            return;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsExistingLink(current))
                throw LinkProblem();
        }
    }
}
