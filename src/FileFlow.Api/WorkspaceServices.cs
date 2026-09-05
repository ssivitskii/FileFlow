#pragma warning disable SA1402, SA1649 // Cohesive bounded readers are kept together for security review.

using FileFlow.Core.Operations;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace FileFlow.Api;

public sealed class WorkspaceReader(PathPolicy paths)
{
    public const int MaxEntries = 1000;
    public const int MaxPreviewBytes = 65_536;

    public WorkspaceResponse List(string? requestedPath)
    {
        string directory = paths.Resolve(requestedPath, allowRoot: true, mustExist: true);
        if (!Directory.Exists(directory))
            throw new ApiProblemException(StatusCodes.Status400BadRequest, "Invalid directory", "The requested item is not a directory.");
        paths.Recheck(directory);
        string[] entries = Directory.EnumerateFileSystemEntries(directory).Take(MaxEntries + 1).ToArray();
        if (entries.Length > MaxEntries)
            throw new ApiProblemException(StatusCodes.Status413PayloadTooLarge, "Directory limit exceeded", "The directory contains more than 1000 entries.");

        WorkspaceEntry[] result = entries.Select(CreateEntry)
            .OrderBy(entry => entry.Kind == "directory" ? 0 : entry.Kind == "file" ? 1 : 2)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        return new WorkspaceResponse(paths.ToRelative(directory), result);
    }

    public async Task<FilePreviewResponse> PreviewAsync(string? requestedPath, CancellationToken cancellationToken)
    {
        string file = paths.Resolve(requestedPath, allowRoot: false, mustExist: true);
        if (!File.Exists(file))
            throw new ApiProblemException(StatusCodes.Status400BadRequest, "Invalid file", "The requested item is not a regular file.");
        paths.Recheck(file);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxPreviewBytes + 1);
        try
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            int total = 0;
            while (total < MaxPreviewBytes + 1)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, MaxPreviewBytes + 1 - total), cancellationToken);
                if (read == 0)
                    break;
                total += read;
            }

            paths.Recheck(file);
            int exposedBytes = Math.Min(total, MaxPreviewBytes);
            string text;
            try
            {
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                Decoder decoder = encoding.GetDecoder();
                char[] characters = ArrayPool<char>.Shared.Rent(encoding.GetMaxCharCount(exposedBytes));
                try
                {
                    decoder.Convert(
                        buffer,
                        0,
                        exposedBytes,
                        characters,
                        0,
                        characters.Length,
                        flush: total <= MaxPreviewBytes,
                        out int bytesUsed,
                        out int charsUsed,
                        out _);
                    exposedBytes = bytesUsed;
                    text = new string(characters, 0, charsUsed);
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(characters);
                }
            }
            catch (DecoderFallbackException)
            {
                throw BinaryProblem();
            }

            if (text.Any(character => character == '\0' || (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')))
                throw BinaryProblem();
            return new FilePreviewResponse(paths.ToRelative(file), text, exposedBytes, total > MaxPreviewBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ApiProblemException BinaryProblem() => new(
        StatusCodes.Status415UnsupportedMediaType,
        "Unsupported file preview",
        "Only strict UTF-8 text files without binary control characters can be previewed.");

    private WorkspaceEntry CreateEntry(string entry)
    {
        FileAttributes attributes = File.GetAttributes(entry);
        bool link = (attributes & FileAttributes.ReparsePoint) != 0;
        bool directory = (attributes & FileAttributes.Directory) != 0;
        string kind = link ? "link" : directory ? "directory" : "file";
        long? size = kind == "file" ? new FileInfo(entry).Length : null;
        return new WorkspaceEntry(Path.GetFileName(entry), paths.ToRelative(entry), kind, size);
    }
}

public sealed class DuplicateScanner(PathPolicy paths) : IDisposable
{
    public const int MaxEntries = MaxDirectories + MaxFiles;

    private const int MaxDirectories = 250;
    private const int MaxFiles = 1000;
    private const int MaxDepth = 12;
    private const long MaxTotalBytes = 536_870_912;
    private const int MaxGroups = 100;
    private readonly SemaphoreSlim _singleScan = new(1, 1);

    public async Task<DuplicateResponse> ScanAsync(string? requestedPath, CancellationToken cancellationToken)
    {
        if (!await _singleScan.WaitAsync(0, cancellationToken))
            throw new ApiProblemException(StatusCodes.Status429TooManyRequests, "Scan already running", "Wait for the active duplicate scan to finish.");
        try
        {
            string root = paths.Resolve(requestedPath, allowRoot: true, mustExist: true);
            if (!Directory.Exists(root))
                throw new ApiProblemException(StatusCodes.Status400BadRequest, "Invalid directory", "The requested item is not a directory.");
            var files = new List<(string Path, long Size, DateTime LastWriteTimeUtc)>();
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));
            int directories = 0;
            int discoveredEntries = 0;
            long totalBytes = 0;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string directory, int depth) = pending.Pop();
                paths.Recheck(directory);
                if (++directories > MaxDirectories || depth > MaxDepth)
                    throw LimitProblem();
                var entries = new List<string>();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++discoveredEntries > MaxEntries)
                        throw LimitProblem();
                    entries.Add(entry);
                }

                entries.Sort(StringComparer.Ordinal);
                foreach (string entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push((entry, depth + 1));
                        continue;
                    }

                    var fileInfo = new FileInfo(entry);
                    long size = fileInfo.Length;
                    totalBytes = checked(totalBytes + size);
                    files.Add((entry, size, fileInfo.LastWriteTimeUtc));
                    if (files.Count > MaxFiles || totalBytes > MaxTotalBytes)
                        throw LimitProblem();
                }
            }

            var result = new List<DuplicateGroupResponse>();
            foreach (IGrouping<long, (string Path, long Size, DateTime LastWriteTimeUtc)> sizeGroup in files.GroupBy(file => file.Size).Where(group => group.Count() > 1))
            {
                var hashes = new Dictionary<string, List<DuplicateFile>>(StringComparer.Ordinal);
                foreach ((string file, long size, DateTime lastWriteTimeUtc) in sizeGroup.OrderBy(item => item.Path, StringComparer.Ordinal))
                {
                    paths.Recheck(file);
                    string hash = await BoundedFileHasher.HashAsync(file, size, lastWriteTimeUtc, cancellationToken);
                    paths.Recheck(file);
                    if (!hashes.TryGetValue(hash, out List<DuplicateFile>? matches))
                    {
                        matches = [];
                        hashes.Add(hash, matches);
                    }

                    matches.Add(new DuplicateFile(paths.ToRelative(file), size));
                }

                result.AddRange(hashes.Where(pair => pair.Value.Count > 1)
                    .Select(pair => new DuplicateGroupResponse(pair.Key, sizeGroup.Key, pair.Value)));
                if (result.Count > MaxGroups)
                    throw LimitProblem();
            }

            return new DuplicateResponse(result.OrderByDescending(group => group.Size)
                .ThenBy(group => group.Sha256, StringComparer.Ordinal).ToArray());
        }
        catch (OverflowException)
        {
            throw LimitProblem();
        }
        finally
        {
            _singleScan.Release();
        }
    }

    public void Dispose()
    {
        _singleScan.Dispose();
    }

    private static ApiProblemException LimitProblem() => new(
        StatusCodes.Status413PayloadTooLarge,
        "Scan limit exceeded",
        "The duplicate scan exceeded its configured safety limits.");
}

public sealed class HistoryReader(RootedWorkspace workspace, PathPolicy paths)
{
    public const long MaxJournalBytes = 1_048_576;

    public IReadOnlyList<HistoryEntryResponse> Read()
    {
        if (!File.Exists(workspace.JournalPath))
            return [];
        if (new FileInfo(workspace.JournalPath).Length > MaxJournalBytes)
            throw Malformed();
        try
        {
            var journal = new JsonOperationJournal(workspace.ApplicationDataRoot);
            return journal.ReadHistory()
                .Where(entry => PathsEqual(entry.ConnectedRoot, workspace.WorkspaceRoot))
                .Where(entry => IsSafe(entry.Source) && (entry.Destination is null || IsSafe(entry.Destination)))
                .Take(100)
                .Select(entry => new HistoryEntryResponse(
                    entry.TransactionId,
                    entry.Timestamp,
                    entry.Operation.ToString().ToLowerInvariant(),
                    entry.Status.ToString().ToLowerInvariant(),
                    paths.ToRelative(entry.Source),
                    entry.Destination is null ? null : paths.ToRelative(entry.Destination)))
                .ToArray();
        }
        catch (InvalidDataException)
        {
            throw Malformed();
        }
        catch (JsonException)
        {
            throw Malformed();
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(first))
            .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), comparison);
    }

    private static ApiProblemException Malformed() => new(
        StatusCodes.Status500InternalServerError,
        "History unavailable",
        "The local operation history could not be read safely.");

    private bool IsSafe(string absolutePath)
    {
        return Path.IsPathFullyQualified(absolutePath) && workspace.Contains(absolutePath, workspace.WorkspaceRoot);
    }
}

public interface IOperationPreviewer
{
    OperationPreviewResponse Preview(OperationPreviewRequest request);
}

public sealed class OperationPreviewer(PathPolicy paths) : IOperationPreviewer
{
    public OperationPreviewResponse Preview(OperationPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string operation = request.Operation?.Trim().ToLowerInvariant() ?? string.Empty;
        if (operation is not ("copy" or "move" or "rename" or "delete"))
            throw new ApiProblemException(StatusCodes.Status400BadRequest, "Invalid operation", "Operation must be copy, move, rename, or delete.");
        string source = paths.Resolve(request.Source, allowRoot: false, mustExist: true);
        if (!File.Exists(source))
            return Result(operation, source, null, false, false, "The source must be an existing regular file.");
        paths.Recheck(source);
        if (operation == "delete")
        {
            if (!string.IsNullOrWhiteSpace(request.Destination))
                throw new ApiProblemException(StatusCodes.Status400BadRequest, "Invalid operation", "Delete does not accept a destination.");
            return Result(operation, source, null, true, false, "Would move the file to FileFlow's private recovery area.");
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
            throw new ApiProblemException(StatusCodes.Status400BadRequest, "Invalid operation", "A destination is required.");
        string destination = paths.Resolve(request.Destination, allowRoot: false, mustExist: false);
        string? parent = Path.GetDirectoryName(destination);
        if (parent is null || !Directory.Exists(parent))
            return Result(operation, source, destination, false, false, "The destination directory does not exist.");
        paths.Recheck(parent);
        bool conflict = File.Exists(destination) || Directory.Exists(destination);
        if (conflict)
            return Result(operation, source, destination, false, true, "The destination already exists; overwrite is not supported.");
        if (Path.GetFullPath(source) == Path.GetFullPath(destination))
            return Result(operation, source, destination, false, true, "Source and destination are the same item.");
        return Result(operation, source, destination, true, false, $"Would {operation} the file without executing the operation.");
    }

    private OperationPreviewResponse Result(string operation, string source, string? destination, bool valid, bool conflict, string summary)
    {
        return new OperationPreviewResponse(
            operation,
            paths.ToRelative(source),
            destination is null ? null : paths.ToRelative(destination),
            valid,
            conflict,
            summary,
            valid ? null : summary);
    }
}
