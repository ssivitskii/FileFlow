using FileFlow.Cli;
using FileFlow.Core.Abstractions;
using FileFlow.Core.FileSystems;
using FileFlow.Core.Operations;
using System.Text.Json;

namespace FileFlow.IntegrationTests;

public sealed class OperationWorkflowTests : IDisposable
{
    private readonly string _container = Path.Combine(Path.GetTempPath(), $"fileflow-workflow-{Guid.NewGuid():N}");
    private readonly string _root;
    private readonly string _state;

    public OperationWorkflowTests()
    {
        _root = Path.Combine(_container, "workspace");
        _state = Path.Combine(_container, "state");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void DryRunPreviewsWithoutMutationOrJournalEntry()
    {
        File.WriteAllText(Path.Combine(_root, "source.txt"), "source");
        var output = new CaptureOutput();
        var shell = new FileFlowShell(output, _state);
        shell.Execute($"connect \"{_root}\"");

        shell.Execute("copy source.txt target.txt --dry-run");

        Assert.False(File.Exists(Path.Combine(_root, "target.txt")));
        Assert.Empty(new FileOperationService(_state).GetHistory());
        Assert.Contains(output.Lines, line => line.StartsWith("DRY RUN VALID", StringComparison.Ordinal));
    }

    [Fact]
    public void CopyCanBeUndoneAndHistoryTracksLatestStatus()
    {
        string source = Path.Combine(_root, "source.txt");
        string destination = Path.Combine(_root, "copy.txt");
        File.WriteAllText(source, "copy content");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();

        OperationJournalEntry executed = Execute(
            service,
            fileSystem,
            FileOperationKind.Copy,
            source,
            destination);
        OperationJournalEntry undone = service.Undo(executed.TransactionId, _root, fileSystem);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Equal(FileOperationStatus.Undone, undone.Status);
        Assert.Equal(FileOperationStatus.Undone, Assert.Single(service.GetHistory()).Status);
    }

    [Fact]
    public void MoveAndRenameCanBeUndone()
    {
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();
        string moveSource = Path.Combine(_root, "move-source.txt");
        string moveDestination = Path.Combine(_root, "move-target.txt");
        File.WriteAllText(moveSource, "move content");
        OperationJournalEntry moved = Execute(
            service,
            fileSystem,
            FileOperationKind.Move,
            moveSource,
            moveDestination);
        service.Undo(moved.TransactionId, _root, fileSystem);
        Assert.True(File.Exists(moveSource));
        Assert.False(File.Exists(moveDestination));

        string renameDestination = Path.Combine(_root, "renamed.txt");
        OperationJournalEntry renamed = Execute(
            service,
            fileSystem,
            FileOperationKind.Rename,
            moveSource,
            renameDestination);
        service.Undo(renamed.TransactionId, _root, fileSystem);
        Assert.True(File.Exists(moveSource));
        Assert.False(File.Exists(renameDestination));
    }

    [Fact]
    public void DeleteUsesTransactionTrashAndCanBeRestored()
    {
        string source = Path.Combine(_root, "delete.txt");
        File.WriteAllText(source, "recoverable content");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();

        OperationJournalEntry deleted = Execute(
            service,
            fileSystem,
            FileOperationKind.Delete,
            source,
            null);

        Assert.False(File.Exists(source));
        Assert.NotNull(deleted.TrashPath);
        Assert.True(File.Exists(deleted.TrashPath));
        service.Undo(deleted.TransactionId, _root, fileSystem);
        Assert.Equal("recoverable content", File.ReadAllText(source));
        Assert.False(File.Exists(deleted.TrashPath));
    }

    [Fact]
    public void UndoFailsClosedWhenFingerprintOrSourceOccupancyChanges()
    {
        string source = Path.Combine(_root, "source.txt");
        string copy = Path.Combine(_root, "copy.txt");
        File.WriteAllText(source, "first");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();
        OperationJournalEntry copied = Execute(service, fileSystem, FileOperationKind.Copy, source, copy);
        File.WriteAllText(copy, "other");

        Assert.Throws<InvalidOperationException>(() => service.Undo(copied.TransactionId, _root, fileSystem));
        Assert.True(File.Exists(copy));

        string movedPath = Path.Combine(_root, "moved.txt");
        OperationJournalEntry moved = Execute(service, fileSystem, FileOperationKind.Move, source, movedPath);
        File.WriteAllText(source, "occupied");
        Assert.Throws<InvalidOperationException>(() => service.Undo(moved.TransactionId, _root, fileSystem));
        Assert.True(File.Exists(movedPath));
    }

    [Fact]
    public void ValidationRejectsConflictsSamePathsDangerousRootsAndLinks()
    {
        string source = Path.Combine(_root, "source.txt");
        string existing = Path.Combine(_root, "existing.txt");
        File.WriteAllText(source, "source");
        File.WriteAllText(existing, "existing");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();

        Assert.False(Validate(service, fileSystem, _root, source, source).IsValid);
        Assert.False(Validate(service, fileSystem, _root, source, existing).IsValid);
        string volumeRoot = Path.GetPathRoot(_root) ?? throw new InvalidOperationException("Volume root unavailable.");
        FileOperationPlan dangerous = service.Plan(FileOperationKind.Delete, volumeRoot, source, null, fileSystem);
        Assert.False(service.Validate(dangerous, fileSystem).IsValid);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            FileOperationPlan homePlan = service.Plan(FileOperationKind.Delete, home, source, null, fileSystem);
            Assert.False(service.Validate(homePlan, fileSystem).IsValid);
        }

        Directory.CreateDirectory(_state);
        string stateFile = Path.Combine(_state, "state.txt");
        File.WriteAllText(stateFile, "state");
        FileOperationPlan statePlan = service.Plan(FileOperationKind.Delete, _state, stateFile, null, fileSystem);
        Assert.False(service.Validate(statePlan, fileSystem).IsValid);

        string outside = Path.Combine(_container, "outside.txt");
        string link = Path.Combine(_root, "link.txt");
        File.WriteAllText(outside, "outside");
        File.CreateSymbolicLink(link, outside);
        FileOperationPlan linked = service.Plan(FileOperationKind.Delete, _root, link, null, fileSystem);
        Assert.False(service.Validate(linked, fileSystem).IsValid);
        Assert.Equal("outside", File.ReadAllText(outside));
    }

    [Fact]
    public void MalformedPlanAndJournalAreRejectedWithoutMutation()
    {
        string source = Path.Combine(_root, "source.txt");
        File.WriteAllText(source, "source");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();
        var malformed = new FileOperationPlan(
            Guid.Empty,
            FileOperationKind.Copy,
            _root,
            source,
            Path.Combine(_root, "target.txt"),
            null);

        Assert.False(service.Validate(malformed, fileSystem).IsValid);
        string arbitraryTrash = Path.Combine(_container, "outside-trash", "source.txt");
        var unsafeDelete = new FileOperationPlan(
            Guid.NewGuid(),
            FileOperationKind.Delete,
            _root,
            source,
            null,
            arbitraryTrash);
        Assert.False(service.Validate(unsafeDelete, fileSystem).IsValid);
        Directory.CreateDirectory(_state);
        File.WriteAllText(Path.Combine(_state, "journal.jsonl"), "{not-json}");
        Assert.Throws<InvalidDataException>(() => service.GetHistory());
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(_root, "target.txt")));
    }

    [Fact]
    public void UndoRejectsPostOperationSymlinkReplacement()
    {
        string subdirectory = Path.Combine(_root, "sub");
        string outside = Path.Combine(_container, "outside");
        Directory.CreateDirectory(subdirectory);
        Directory.CreateDirectory(outside);
        string source = Path.Combine(subdirectory, "source.txt");
        string destination = Path.Combine(_root, "moved.txt");
        File.WriteAllText(source, "content");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();
        FileOperationPlan plan = service.Plan(
            FileOperationKind.Move,
            _root,
            source,
            destination,
            fileSystem);
        OperationJournalEntry moved = service.Execute(plan, fileSystem);
        Directory.Delete(subdirectory);
        Directory.CreateSymbolicLink(subdirectory, outside);

        Assert.Throws<InvalidOperationException>(() => service.Undo(moved.TransactionId, _root, fileSystem));
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(Path.Combine(outside, "source.txt")));
    }

    [Fact]
    public void DeleteRejectsSymlinkedTrashPath()
    {
        string source = Path.Combine(_root, "source.txt");
        string outside = Path.Combine(_container, "outside-trash");
        Directory.CreateDirectory(_state);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(_state, "trash"), outside);
        File.WriteAllText(source, "content");
        var service = new FileOperationService(_state);
        var fileSystem = new LocalFileSystem();
        FileOperationPlan plan = service.Plan(FileOperationKind.Delete, _root, source, null, fileSystem);

        Assert.False(service.Validate(plan, fileSystem).IsValid);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(outside));
    }

    [Fact]
    public void DuplicateScanGroupsBySizeThenHashWithDeterministicPaths()
    {
        string upper = Path.Combine(_root, "A.txt");
        string lower = Path.Combine(_root, "a.txt");
        string different = Path.Combine(_root, "different.txt");
        File.WriteAllText(upper, "same");
        File.WriteAllText(lower, "same");
        File.WriteAllText(different, "size");
        var output = new CaptureOutput();
        var shell = new FileFlowShell(output, _state);
        shell.Execute($"connect \"{_root}\"");
        output.Lines.Clear();

        shell.Execute("duplicates . --format json");

        using var document = JsonDocument.Parse(Assert.Single(output.Lines));
        JsonElement group = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(4, group.GetProperty("size").GetInt64());
        string[] files = group.GetProperty("files").EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal([upper, lower], files);
    }

    [Fact]
    public void JournalContainsRequiredJsonFieldsAndHistoryCommandReadsIt()
    {
        File.WriteAllText(Path.Combine(_root, "source.txt"), "journal");
        var output = new CaptureOutput();
        var shell = new FileFlowShell(output, _state);
        shell.Execute($"connect \"{_root}\"");
        shell.Execute("copy source.txt target.txt");
        shell.Execute("history");

        string journalPath = Path.Combine(_state, "journal.jsonl");
        string[] journalLines = File.ReadLines(journalPath).ToArray();
        Assert.Equal(2, journalLines.Length);
        using var document = JsonDocument.Parse(journalLines[1]);
        JsonElement entry = document.RootElement;
        Assert.True(entry.TryGetProperty("transactionId", out _));
        Assert.True(entry.TryGetProperty("timestamp", out _));
        Assert.True(entry.TryGetProperty("operation", out _));
        Assert.True(entry.TryGetProperty("source", out _));
        Assert.True(entry.TryGetProperty("destination", out _));
        Assert.True(entry.TryGetProperty("trashPath", out _));
        Assert.True(entry.TryGetProperty("fingerprint", out _));
        Assert.True(entry.TryGetProperty("status", out _));
        Assert.Equal("completed", entry.GetProperty("status").GetString());
        Assert.Contains(output.Lines, line => line.Contains("Completed", StringComparison.Ordinal));
    }

    [Fact]
    public void JournalPreparationAndCompletionFailuresPreserveRecoveryBoundary()
    {
        string source = Path.Combine(_root, "source.txt");
        string destination = Path.Combine(_root, "copy.txt");
        File.WriteAllText(source, "content");
        var fileSystem = new LocalFileSystem();
        var preparationJournal = new FailingJournal(_state, 1);
        var preparationService = new FileOperationService(preparationJournal);
        FileOperationPlan preparationPlan = preparationService.Plan(
            FileOperationKind.Copy,
            _root,
            source,
            destination,
            fileSystem);

        Assert.Throws<IOException>(() => preparationService.Execute(preparationPlan, fileSystem));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Empty(preparationJournal.Entries);

        var completionJournal = new FailingJournal(_state, 2);
        var completionService = new FileOperationService(completionJournal);
        FileOperationPlan completionPlan = completionService.Plan(
            FileOperationKind.Copy,
            _root,
            source,
            destination,
            fileSystem);
        Assert.Throws<IOException>(() => completionService.Execute(completionPlan, fileSystem));
        Assert.True(File.Exists(destination));
        OperationJournalEntry prepared = Assert.Single(completionJournal.Entries);
        Assert.Equal(FileOperationStatus.Prepared, prepared.Status);
    }

    [Theory]
    [InlineData(3, FileOperationStatus.Completed, true)]
    [InlineData(4, FileOperationStatus.UndoPrepared, false)]
    public void UndoJournalFailuresExposeTheLastRecoveryBoundary(
        int failureCall,
        FileOperationStatus expectedStatus,
        bool destinationExists)
    {
        string source = Path.Combine(_root, "source.txt");
        string destination = Path.Combine(_root, "copy.txt");
        File.WriteAllText(source, "content");
        var fileSystem = new LocalFileSystem();
        var journal = new FailingJournal(_state, failureCall);
        var service = new FileOperationService(journal);
        OperationJournalEntry executed = Execute(
            service,
            fileSystem,
            FileOperationKind.Copy,
            source,
            destination);

        Assert.Throws<IOException>(() => service.Undo(executed.TransactionId, _root, fileSystem));

        Assert.Equal(destinationExists, File.Exists(destination));
        Assert.Equal(expectedStatus, journal.FindLatest(executed.TransactionId)!.Status);
    }

    [Fact]
    public void UndoRejectsTamperedDeleteTrashPath()
    {
        var transactionId = Guid.NewGuid();
        string source = Path.Combine(_root, "source.txt");
        string tamperedTrash = Path.Combine(_state, "trash", transactionId.ToString("D"), "other.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(tamperedTrash)!);
        File.WriteAllText(tamperedTrash, "content");
        var journal = new FailingJournal(_state, int.MaxValue);
        journal.Entries.Add(new OperationJournalEntry(
            transactionId,
            DateTimeOffset.UtcNow,
            FileOperationKind.Delete,
            _root,
            source,
            null,
            tamperedTrash,
            "SHA256:unused",
            FileOperationStatus.Completed));
        var service = new FileOperationService(journal);

        Assert.Throws<InvalidDataException>(() => service.Undo(transactionId, _root, new LocalFileSystem()));

        Assert.True(File.Exists(tamperedTrash));
        Assert.False(File.Exists(source));
        Assert.Equal(FileOperationStatus.Completed, journal.FindLatest(transactionId)!.Status);
    }

    [Fact]
    public async Task CliReportsMalformedJournalAndContinuesInteractiveSession()
    {
        Directory.CreateDirectory(_state);
        File.WriteAllText(Path.Combine(_state, "journal.jsonl"), "{not-json}");
        using var input = new StringReader($"connect \"{_root}\"{Environment.NewLine}history{Environment.NewLine}pwd{Environment.NewLine}exit");
        using var output = new StringWriter();

        int exitCode = await new FileFlowApplication(input, output, _state).RunAsync([], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Error: The operation journal is malformed.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(_root, output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_container, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static OperationJournalEntry Execute(
        FileOperationService service,
        IFileSystem fileSystem,
        FileOperationKind operation,
        string source,
        string? destination)
    {
        FileOperationPlan plan = service.Plan(operation, Path.GetDirectoryName(source)!, source, destination, fileSystem);
        OperationValidation validation = service.Validate(plan, fileSystem);
        Assert.True(validation.IsValid, validation.Error);
        Assert.True(service.Preview(plan, validation).IsValid);
        return service.Execute(plan, fileSystem);
    }

    private static OperationValidation Validate(
        FileOperationService service,
        IFileSystem fileSystem,
        string root,
        string source,
        string destination)
    {
        FileOperationPlan plan = service.Plan(FileOperationKind.Copy, root, source, destination, fileSystem);
        return service.Validate(plan, fileSystem);
    }

    private sealed class CaptureOutput : IOutputWriter
    {
        public List<string> Lines { get; } = [];

        public void WriteLine(string value)
        {
            Lines.Add(value);
        }
    }

    private sealed class FailingJournal : IOperationJournal
    {
        private readonly int _failureCall;
        private int _appendCalls;

        public FailingJournal(string applicationDataRoot, int failureCall)
        {
            ApplicationDataRoot = applicationDataRoot;
            TrashRoot = Path.Combine(applicationDataRoot, "trash");
            _failureCall = failureCall;
        }

        public string ApplicationDataRoot { get; }

        public string TrashRoot { get; }

        public List<OperationJournalEntry> Entries { get; } = [];

        public void Append(OperationJournalEntry entry)
        {
            _appendCalls++;
            if (_appendCalls == _failureCall)
                throw new IOException("Injected journal failure.");

            Entries.Add(entry);
        }

        public IReadOnlyList<OperationJournalEntry> ReadHistory()
        {
            return Entries;
        }

        public OperationJournalEntry? FindLatest(Guid transactionId)
        {
            return Entries.LastOrDefault(entry => entry.TransactionId == transactionId);
        }
    }
}
