using FileFlow.Core.Abstractions;
using System.Security.Cryptography;

namespace FileFlow.Core.Operations;

public sealed class FileOperationService
{
    private readonly IOperationJournal _journal;
    private readonly TimeProvider _timeProvider;

    public FileOperationService(string applicationDataRoot, TimeProvider? timeProvider = null)
        : this(new JsonOperationJournal(applicationDataRoot), timeProvider)
    {
    }

    public FileOperationService(IOperationJournal journal, TimeProvider? timeProvider = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ApplicationDataRoot => _journal.ApplicationDataRoot;

    public FileOperationPlan Plan(
        FileOperationKind operation,
        string connectedRoot,
        string source,
        string? destination,
        IFileSystem fileSystem)
    {
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));

        ArgumentException.ThrowIfNullOrWhiteSpace(connectedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(fileSystem);
        string normalizedRoot = Normalize(connectedRoot);
        string normalizedSource = Normalize(source);
        string? normalizedDestination = destination is null ? null : Normalize(destination);
        if (normalizedDestination is not null && fileSystem.DirectoryExists(normalizedDestination))
            normalizedDestination = Path.Combine(normalizedDestination, Path.GetFileName(normalizedSource));

        var transactionId = Guid.NewGuid();
        string? trashPath = operation == FileOperationKind.Delete
            ? Path.Combine(_journal.TrashRoot, transactionId.ToString("D"), Path.GetFileName(normalizedSource))
            : null;
        return new FileOperationPlan(
            transactionId,
            operation,
            normalizedRoot,
            normalizedSource,
            normalizedDestination,
            trashPath);
    }

    public OperationValidation Validate(FileOperationPlan plan, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fileSystem);
        try
        {
            if (plan.TransactionId == Guid.Empty || !Enum.IsDefined(plan.Operation))
                return OperationValidation.Invalid("The operation plan is malformed.");
            if (ContainsReparsePointFromVolume(ApplicationDataRoot))
                return OperationValidation.Invalid("FileFlow application-data paths cannot contain symbolic links.");

            string? rootError = ValidateConnectedRoot(plan.ConnectedRoot);
            if (rootError is not null)
                return OperationValidation.Invalid(rootError);
            if (!IsWithin(plan.Source, plan.ConnectedRoot))
                return OperationValidation.Invalid("The source is outside the connected root.");
            if (IsWithin(plan.Source, ApplicationDataRoot))
                return OperationValidation.Invalid("FileFlow application data cannot be modified.");
            if (!fileSystem.FileExists(plan.Source))
                return OperationValidation.Invalid("The source must be an existing regular file.");
            if (ContainsReparsePoint(plan.ConnectedRoot, plan.Source))
                return OperationValidation.Invalid("Symbolic links and reparse points are not supported for mutations.");

            if (plan.Operation == FileOperationKind.Delete)
            {
                string expectedTrashPath = Path.Combine(
                    _journal.TrashRoot,
                    plan.TransactionId.ToString("D"),
                    Path.GetFileName(plan.Source));
                return plan.Destination is null
                    && plan.TrashPath is not null
                    && PathsEqual(plan.TrashPath, expectedTrashPath)
                    && IsWithin(plan.TrashPath, _journal.TrashRoot)
                    && !ContainsReparsePointFromVolume(plan.TrashPath)
                    ? OperationValidation.Valid
                    : OperationValidation.Invalid("The delete plan is malformed.");
            }

            if (plan.Destination is null)
                return OperationValidation.Invalid("A destination is required.");
            if (!IsWithin(plan.Destination, plan.ConnectedRoot))
                return OperationValidation.Invalid("The destination is outside the connected root.");
            if (IsWithin(plan.Destination, ApplicationDataRoot))
                return OperationValidation.Invalid("FileFlow application data cannot be modified.");
            if (PathsEqual(plan.Source, plan.Destination))
                return OperationValidation.Invalid("Source and destination resolve to the same path.");
            if (IsWithin(plan.Destination, plan.Source))
                return OperationValidation.Invalid("A path cannot be moved into itself.");
            if (fileSystem.FileExists(plan.Destination) || fileSystem.DirectoryExists(plan.Destination))
                return OperationValidation.Conflict("The destination already exists; overwriting is not supported.");

            string? parent = Path.GetDirectoryName(plan.Destination);
            if (parent is null || !fileSystem.DirectoryExists(parent))
                return OperationValidation.Invalid("The destination directory does not exist.");
            if (ContainsReparsePoint(plan.ConnectedRoot, parent))
                return OperationValidation.Invalid("Symbolic links and reparse points are not supported for mutations.");

            return OperationValidation.Valid;
        }
        catch (ArgumentException exception)
        {
            return OperationValidation.Invalid(exception.Message);
        }
        catch (IOException exception)
        {
            return OperationValidation.Invalid(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return OperationValidation.Invalid(exception.Message);
        }
    }

    public OperationPreview Preview(FileOperationPlan plan, OperationValidation validation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(validation);
        string target = plan.Operation == FileOperationKind.Delete
            ? plan.TrashPath ?? "<invalid trash>"
            : plan.Destination ?? "<missing destination>";
        string description = $"{plan.Operation}: '{plan.Source}' -> '{target}'";
        return new OperationPreview(plan.TransactionId, validation.IsValid, description, validation.Error);
    }

    public OperationJournalEntry Execute(FileOperationPlan plan, IFileSystem fileSystem)
    {
        OperationValidation validation = Validate(plan, fileSystem);
        ThrowIfInvalid(validation);
        string fingerprint = ComputeFingerprint(plan.Source);
        var prepared = new OperationJournalEntry(
            plan.TransactionId,
            _timeProvider.GetUtcNow(),
            plan.Operation,
            plan.ConnectedRoot,
            plan.Source,
            plan.Destination,
            plan.TrashPath,
            fingerprint,
            FileOperationStatus.Prepared);
        _journal.Append(prepared);
        ThrowIfInvalid(Validate(plan, fileSystem));

        string resultingPath;
        switch (plan.Operation)
        {
            case FileOperationKind.Copy:
                resultingPath = RequireDestination(plan);
                fileSystem.CopyFile(plan.Source, resultingPath);
                break;
            case FileOperationKind.Move:
            case FileOperationKind.Rename:
                resultingPath = RequireDestination(plan);
                fileSystem.MoveFile(plan.Source, resultingPath);
                break;
            case FileOperationKind.Delete:
                resultingPath = plan.TrashPath ?? throw new InvalidOperationException("The delete trash path is missing.");
                Directory.CreateDirectory(Path.GetDirectoryName(resultingPath)
                    ?? throw new InvalidOperationException("The delete trash directory is invalid."));
                fileSystem.MoveFile(plan.Source, resultingPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(plan));
        }

        RequireFingerprint(prepared, resultingPath);
        OperationJournalEntry entry = prepared with
        {
            Timestamp = _timeProvider.GetUtcNow(),
            Status = FileOperationStatus.Completed,
        };
        _journal.Append(entry);
        return entry;
    }

    public OperationJournalEntry Undo(Guid transactionId, string connectedRoot, IFileSystem fileSystem)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException("A non-empty transaction ID is required.", nameof(transactionId));

        ArgumentException.ThrowIfNullOrWhiteSpace(connectedRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);
        OperationJournalEntry entry = _journal.FindLatest(transactionId)
            ?? throw new InvalidOperationException($"Transaction '{transactionId}' was not found.");
        if (entry.Status == FileOperationStatus.Undone)
            throw new InvalidOperationException("The transaction has already been undone.");
        if (entry.Status != FileOperationStatus.Completed)
            throw new InvalidOperationException("The transaction did not reach a completed state and cannot be undone automatically.");

        string normalizedRoot = Normalize(connectedRoot);
        if (!PathsEqual(entry.ConnectedRoot, normalizedRoot))
            throw new InvalidOperationException("Reconnect to the transaction's original root before undoing it.");
        ValidateUndoPaths(entry, normalizedRoot);
        OperationJournalEntry undoPrepared = entry with
        {
            Timestamp = _timeProvider.GetUtcNow(),
            Status = FileOperationStatus.UndoPrepared,
        };
        _journal.Append(undoPrepared);
        ValidateUndoPaths(entry, normalizedRoot);

        switch (entry.Operation)
        {
            case FileOperationKind.Copy:
                UndoCopy(entry, fileSystem);
                break;
            case FileOperationKind.Move:
            case FileOperationKind.Rename:
                UndoMove(entry, fileSystem);
                break;
            case FileOperationKind.Delete:
                UndoDelete(entry, fileSystem);
                break;
            default:
                throw new InvalidDataException("The journal contains an unsupported operation.");
        }

        OperationJournalEntry undone = undoPrepared with
        {
            Timestamp = _timeProvider.GetUtcNow(),
            Status = FileOperationStatus.Undone,
        };
        _journal.Append(undone);
        return undone;
    }

    public IReadOnlyList<OperationJournalEntry> GetHistory()
    {
        return _journal.ReadHistory();
    }

    private static string ComputeFingerprint(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return string.Concat("SHA256:", Convert.ToHexString(SHA256.HashData(stream)));
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            return true;

        string relative = Path.GetRelativePath(root, path);
        string current = root;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current))
                && ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsReparsePointFromVolume(string path)
    {
        string normalized = Normalize(path);
        string volumeRoot = Path.GetPathRoot(normalized)
            ?? throw new InvalidOperationException("The path has no file-system root.");
        return ContainsReparsePoint(volumeRoot, normalized);
    }

    private static bool IsWithin(string path, string root)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalizedPath = Normalize(path);
        string normalizedRoot = Normalize(root);
        string prefix = string.Concat(normalizedRoot, Path.DirectorySeparatorChar);
        return normalizedPath.Equals(normalizedRoot, comparison) || normalizedPath.StartsWith(prefix, comparison);
    }

    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool PathsEqual(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Normalize(first).Equals(Normalize(second), comparison);
    }

    private static string RequireDestination(FileOperationPlan plan)
    {
        return plan.Destination ?? throw new InvalidOperationException("The operation destination is missing.");
    }

    private static void ThrowIfInvalid(OperationValidation validation)
    {
        if (validation.IsValid)
            return;
        if (validation.IsConflict)
            throw new IOException(validation.Error);

        throw new InvalidOperationException(validation.Error);
    }

    private static void RequireFingerprint(OperationJournalEntry entry, string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("Undo target is missing.");
        if (!string.Equals(ComputeFingerprint(path), entry.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Undo refused because the file content no longer matches its fingerprint.");
    }

    private static void UndoCopy(OperationJournalEntry entry, IFileSystem fileSystem)
    {
        string destination = entry.Destination
            ?? throw new InvalidDataException("The copy journal entry has no destination.");
        RequireFingerprint(entry, destination);
        fileSystem.DeleteFile(destination);
    }

    private static void UndoDelete(OperationJournalEntry entry, IFileSystem fileSystem)
    {
        string trashPath = entry.TrashPath
            ?? throw new InvalidDataException("The delete journal entry has no trash path.");
        if (fileSystem.FileExists(entry.Source) || fileSystem.DirectoryExists(entry.Source))
            throw new InvalidOperationException("Undo refused because the original source path is occupied.");
        RequireFingerprint(entry, trashPath);
        fileSystem.MoveFile(trashPath, entry.Source);
    }

    private static void UndoMove(OperationJournalEntry entry, IFileSystem fileSystem)
    {
        string destination = entry.Destination
            ?? throw new InvalidDataException("The move journal entry has no destination.");
        if (fileSystem.FileExists(entry.Source) || fileSystem.DirectoryExists(entry.Source))
            throw new InvalidOperationException("Undo refused because the original source path is occupied.");
        RequireFingerprint(entry, destination);
        fileSystem.MoveFile(destination, entry.Source);
    }

    private string? ValidateConnectedRoot(string root)
    {
        string normalizedRoot = Normalize(root);
        string? volumeRoot = Path.GetPathRoot(normalizedRoot);
        if (volumeRoot is not null && PathsEqual(normalizedRoot, volumeRoot))
            return "A file-system root is too broad for mutation commands.";

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && PathsEqual(normalizedRoot, home))
            return "The user-profile root is too broad for mutation commands.";

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localData)
            && (IsWithin(normalizedRoot, localData) || IsWithin(localData, normalizedRoot)))
        {
            return "Application-data directories cannot be mutation roots.";
        }

        if (IsWithin(normalizedRoot, ApplicationDataRoot) || IsWithin(ApplicationDataRoot, normalizedRoot))
            return "The configured FileFlow application-data directory cannot overlap the mutation root.";

        return null;
    }

    private void ValidateUndoPaths(OperationJournalEntry entry, string connectedRoot)
    {
        if (!IsWithin(entry.Source, connectedRoot)
            || (entry.Destination is not null && !IsWithin(entry.Destination, connectedRoot))
            || (entry.TrashPath is not null && !IsWithin(entry.TrashPath, ApplicationDataRoot)))
        {
            throw new InvalidDataException("The journal contains paths outside the original connected root.");
        }

        if (entry.Operation == FileOperationKind.Delete)
        {
            string expectedTrashPath = Path.Combine(
                _journal.TrashRoot,
                entry.TransactionId.ToString("D"),
                Path.GetFileName(entry.Source));
            if (entry.TrashPath is null || !PathsEqual(entry.TrashPath, expectedTrashPath))
                throw new InvalidDataException("The delete journal entry contains a noncanonical trash path.");
        }

        string sourceParent = Path.GetDirectoryName(entry.Source)
            ?? throw new InvalidDataException("The journal source path has no parent directory.");
        if (ContainsReparsePoint(connectedRoot, sourceParent))
            throw new InvalidOperationException("Undo refused because the source path contains a symbolic link.");
        if (entry.Destination is not null && ContainsReparsePoint(connectedRoot, entry.Destination))
            throw new InvalidOperationException("Undo refused because the destination path contains a symbolic link.");
        if (entry.TrashPath is not null && ContainsReparsePointFromVolume(entry.TrashPath))
            throw new InvalidOperationException("Undo refused because the trash path contains a symbolic link.");
    }
}
