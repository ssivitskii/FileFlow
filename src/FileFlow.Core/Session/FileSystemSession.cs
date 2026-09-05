using FileFlow.Core.Abstractions;

namespace FileFlow.Core.Session;

public sealed class FileSystemSession
{
    public IFileSystem? FileSystem { get; private set; }

    public string? RootPath { get; private set; }

    public string? CurrentPath { get; private set; }

    public bool IsConnected => FileSystem is not null;

    public void Connect(string rootPath, IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!fileSystem.DirectoryExists(normalized))
            throw new DirectoryNotFoundException($"Directory '{normalized}' does not exist.");
        FileSystem = fileSystem;
        RootPath = normalized;
        CurrentPath = normalized;
    }

    public void Disconnect()
    {
        FileSystem = null;
        RootPath = null;
        CurrentPath = null;
    }

    public void ChangeDirectory(string path)
    {
        string resolved = ResolvePath(path);
        if (FileSystem?.DirectoryExists(resolved) != true)
            throw new DirectoryNotFoundException($"Directory '{resolved}' does not exist.");
        CurrentPath = resolved;
    }

    public string ResolvePath(string path)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string root = RootPath ?? throw new InvalidOperationException("Not connected.");
        string current = CurrentPath ?? root;
        string candidate = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(current, path));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : string.Concat(root, Path.DirectorySeparatorChar);
        if (!candidate.Equals(root, comparison) && !candidate.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException("The path resolves outside the connected root.");
        return candidate;
    }

    public string GetCurrentDirectory()
    {
        EnsureConnected();
        return CurrentPath ?? throw new InvalidOperationException("Not connected.");
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Connect to a file-system root first.");
    }
}
