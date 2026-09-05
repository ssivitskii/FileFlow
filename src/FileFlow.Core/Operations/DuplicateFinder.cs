using FileFlow.Core.Abstractions;
using System.Security.Cryptography;

namespace FileFlow.Core.Operations;

public sealed class DuplicateFinder
{
    public IReadOnlyList<DuplicateGroup> Find(string path, IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (fileSystem is not IFileContentSource contentSource)
            throw new InvalidOperationException("The connected file system does not support content hashing.");

        string[] files = CollectFiles(path, fileSystem).ToArray();
        return files
            .Select(file => new FileCandidate(file, contentSource.GetFileLength(file)))
            .GroupBy(candidate => candidate.Size)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(candidate => new HashedFile(
                candidate.Path,
                candidate.Size,
                ComputeHash(candidate.Path, contentSource))))
            .GroupBy(file => new { file.Size, file.Sha256 })
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key.Size)
            .ThenBy(group => group.Key.Sha256, StringComparer.Ordinal)
            .Select(group => new DuplicateGroup(
                group.Key.Size,
                group.Key.Sha256,
                group.Select(file => file.Path)
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(file => file, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static IEnumerable<string> CollectFiles(string path, IFileSystem fileSystem)
    {
        if (fileSystem.FileExists(path))
        {
            yield return path;
            yield break;
        }

        if (!fileSystem.DirectoryExists(path))
            throw new FileNotFoundException("The duplicate-scan path does not exist.", path);

        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string file in fileSystem.EnumerateFiles(directory)
                         .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(file => file, StringComparer.Ordinal))
            {
                if (!IsReparsePoint(file))
                    yield return file;
            }

            string[] directories = fileSystem.EnumerateDirectories(directory)
                .Where(child => !IsReparsePoint(child))
                .OrderByDescending(child => child, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(child => child, StringComparer.Ordinal)
                .ToArray();
            foreach (string child in directories)
                pending.Push(child);
        }
    }

    private static string ComputeHash(string path, IFileContentSource contentSource)
    {
        using Stream stream = contentSource.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private sealed record FileCandidate(string Path, long Size);

    private sealed record HashedFile(string Path, long Size, string Sha256);
}
