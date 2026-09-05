using FileFlow.Core.Abstractions;

namespace FileFlow.Core.Tree;

public sealed class FileTreeWalker
{
    public void Walk(string root, int maximumDepth, IFileSystem fileSystem, IFileSystemVisitor visitor)
    {
        if (maximumDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDepth), "Depth cannot be negative.");
        WalkDirectory(root, 0, maximumDepth, fileSystem, visitor);
    }

    private static void WalkDirectory(
        string directory,
        int depth,
        int maximumDepth,
        IFileSystem fileSystem,
        IFileSystemVisitor visitor)
    {
        visitor.VisitDirectory(directory, depth);
        if (depth >= maximumDepth)
            return;
        IEnumerable<string> children = fileSystem.EnumerateDirectories(directory)
            .Concat(fileSystem.EnumerateFiles(directory))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal);
        foreach (string child in children)
        {
            if (fileSystem.DirectoryExists(child))
                WalkDirectory(child, depth + 1, maximumDepth, fileSystem, visitor);
            else
                visitor.VisitFile(child, depth + 1);
        }
    }
}
