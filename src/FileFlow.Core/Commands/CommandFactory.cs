using FileFlow.Core.Abstractions;
using FileFlow.Core.FileSystems;
using FileFlow.Core.Operations;
using FileFlow.Core.Tree;
using System.Text.Json;

namespace FileFlow.Core.Commands;

public static class CommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static ICommand Connect(string root, string mode)
    {
        return new ConnectCommand(root, mode);
    }

    public static ICommand Disconnect()
    {
        return new DisconnectCommand();
    }

    public static ICommand PrintWorkingDirectory()
    {
        return new PrintWorkingDirectoryCommand();
    }

    public static ICommand List()
    {
        return new ListCommand();
    }

    public static ICommand ChangeDirectory(string path)
    {
        return new ChangeDirectoryCommand(path);
    }

    public static ICommand Show(string path)
    {
        return new ShowCommand(path);
    }

    public static ICommand Copy(string source, string destination, bool dryRun)
    {
        return new CopyCommand(source, destination, dryRun);
    }

    public static ICommand Move(string source, string destination, bool dryRun)
    {
        return new MoveCommand(source, destination, dryRun);
    }

    public static ICommand Rename(string path, string name, bool dryRun)
    {
        return new RenameCommand(path, name, dryRun);
    }

    public static ICommand Delete(string path, bool dryRun)
    {
        return new DeleteCommand(path, dryRun);
    }

    public static ICommand Tree(int depth)
    {
        return new TreeCommand(depth);
    }

    public static ICommand History()
    {
        return new HistoryCommand();
    }

    public static ICommand Undo(Guid transactionId)
    {
        return new UndoCommand(transactionId);
    }

    public static ICommand Duplicates(string path, string format)
    {
        return new DuplicatesCommand(path, format);
    }

    public static ICommand Help()
    {
        return new HelpCommand();
    }

    public static ICommand Exit()
    {
        return new ExitCommand();
    }

    private static IFileSystem RequireFileSystem(CommandContext context)
    {
        return context.Session.FileSystem ?? throw new InvalidOperationException("Connect first.");
    }

    private static void ExecutePlannedOperation(
        CommandContext context,
        FileOperationKind operation,
        string source,
        string? destination,
        bool dryRun)
    {
        IFileSystem fileSystem = RequireFileSystem(context);
        string root = context.Session.RootPath ?? throw new InvalidOperationException("Connect first.");
        FileOperationPlan plan = context.Operations.Plan(operation, root, source, destination, fileSystem);
        OperationValidation validation = context.Operations.Validate(plan, fileSystem);
        OperationPreview preview = context.Operations.Preview(plan, validation);
        if (dryRun)
        {
            string label = preview.IsValid ? "DRY RUN VALID" : "DRY RUN INVALID";
            context.Output.WriteLine($"{label}: {preview.Description}");
            if (!preview.IsValid)
                ThrowValidation(validation);

            return;
        }

        if (!preview.IsValid)
            ThrowValidation(validation);

        OperationJournalEntry entry = context.Operations.Execute(plan, fileSystem);
        context.Output.WriteLine($"{entry.Operation} completed. Transaction: {entry.TransactionId}");
    }

    private static void ThrowValidation(OperationValidation validation)
    {
        if (validation.IsConflict)
            throw new IOException(validation.Error);

        throw new InvalidOperationException(validation.Error);
    }

    private sealed class ConnectCommand : ICommand
    {
        private readonly string _root;
        private readonly string _mode;

        public ConnectCommand(string root, string mode)
        {
            _root = root;
            _mode = mode;
        }

        public void Execute(CommandContext context)
        {
            if (!string.Equals(_mode, "local", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only local mode is supported.");
            context.Session.Connect(_root, new LocalFileSystem());
            context.Output.WriteLine($"Connected to {context.Session.GetCurrentDirectory()}");
        }
    }

    private sealed class DisconnectCommand : ICommand
    {
        public void Execute(CommandContext context)
        {
            context.Session.Disconnect();
            context.Output.WriteLine("Disconnected.");
        }
    }

    private sealed class PrintWorkingDirectoryCommand : ICommand
    {
        public void Execute(CommandContext context)
        {
            context.Output.WriteLine(context.Session.GetCurrentDirectory());
        }
    }

    private sealed class ListCommand : ICommand
    {
        public void Execute(CommandContext context)
        {
            IFileSystem fileSystem = RequireFileSystem(context);
            string current = context.Session.GetCurrentDirectory();
            IEnumerable<string> entries = fileSystem.EnumerateDirectories(current)
                .Select(path => (Path.GetFileName(path) ?? path) + "/")
                .Concat(fileSystem.EnumerateFiles(current).Select(path => Path.GetFileName(path) ?? path))
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry, StringComparer.Ordinal);
            foreach (string entry in entries)
                context.Output.WriteLine(entry);
        }
    }

    private sealed class ChangeDirectoryCommand : ICommand
    {
        private readonly string _path;

        public ChangeDirectoryCommand(string path)
        {
            _path = path;
        }

        public void Execute(CommandContext context)
        {
            context.Session.ChangeDirectory(_path);
            context.Output.WriteLine(context.Session.GetCurrentDirectory());
        }
    }

    private sealed class ShowCommand : ICommand
    {
        private readonly string _path;

        public ShowCommand(string path)
        {
            _path = path;
        }

        public void Execute(CommandContext context)
        {
            IFileSystem fileSystem = RequireFileSystem(context);
            context.Output.WriteLine(fileSystem.ReadFile(context.Session.ResolvePath(_path)));
        }
    }

    private sealed class CopyCommand : ICommand
    {
        private readonly string _source;
        private readonly string _destination;
        private readonly bool _dryRun;

        public CopyCommand(string source, string destination, bool dryRun)
        {
            _source = source;
            _destination = destination;
            _dryRun = dryRun;
        }

        public void Execute(CommandContext context)
        {
            ExecutePlannedOperation(
                context,
                FileOperationKind.Copy,
                context.Session.ResolvePath(_source),
                context.Session.ResolvePath(_destination),
                _dryRun);
        }
    }

    private sealed class MoveCommand : ICommand
    {
        private readonly string _source;
        private readonly string _destination;
        private readonly bool _dryRun;

        public MoveCommand(string source, string destination, bool dryRun)
        {
            _source = source;
            _destination = destination;
            _dryRun = dryRun;
        }

        public void Execute(CommandContext context)
        {
            ExecutePlannedOperation(
                context,
                FileOperationKind.Move,
                context.Session.ResolvePath(_source),
                context.Session.ResolvePath(_destination),
                _dryRun);
        }
    }

    private sealed class RenameCommand : ICommand
    {
        private readonly string _path;
        private readonly string _name;
        private readonly bool _dryRun;

        public RenameCommand(string path, string name, bool dryRun)
        {
            _path = path;
            _name = name;
            _dryRun = dryRun;
        }

        public void Execute(CommandContext context)
        {
            if (string.IsNullOrWhiteSpace(_name)
                || Path.IsPathRooted(_name)
                || _name is "." or ".."
                || _name.Contains("/", StringComparison.Ordinal)
                || _name.Contains("\\", StringComparison.Ordinal)
                || _name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("The new name must be a valid file name without directory separators.");
            }

            string source = context.Session.ResolvePath(_path);
            string directory = Path.GetDirectoryName(source) ?? throw new InvalidOperationException("Invalid source path.");
            ExecutePlannedOperation(
                context,
                FileOperationKind.Rename,
                source,
                context.Session.ResolvePath(Path.Combine(directory, _name)),
                _dryRun);
        }
    }

    private sealed class DeleteCommand : ICommand
    {
        private readonly string _path;
        private readonly bool _dryRun;

        public DeleteCommand(string path, bool dryRun)
        {
            _path = path;
            _dryRun = dryRun;
        }

        public void Execute(CommandContext context)
        {
            ExecutePlannedOperation(
                context,
                FileOperationKind.Delete,
                context.Session.ResolvePath(_path),
                null,
                _dryRun);
        }
    }

    private sealed class TreeCommand : ICommand
    {
        private readonly int _depth;

        public TreeCommand(int depth)
        {
            _depth = depth;
        }

        public void Execute(CommandContext context)
        {
            IFileSystem fileSystem = RequireFileSystem(context);
            new FileTreeWalker().Walk(
                context.Session.GetCurrentDirectory(),
                _depth,
                fileSystem,
                new TextTreeVisitor(context.Output));
        }
    }

    private sealed class HistoryCommand : ICommand
    {
        public void Execute(CommandContext context)
        {
            IReadOnlyList<OperationJournalEntry> history = context.Operations.GetHistory();
            if (history.Count == 0)
            {
                context.Output.WriteLine("No recorded operations.");
                return;
            }

            foreach (OperationJournalEntry entry in history)
            {
                context.Output.WriteLine(
                    $"{entry.TransactionId} | {entry.Timestamp:O} | {entry.Operation} | {entry.Status} | {entry.Source}");
            }
        }
    }

    private sealed class UndoCommand : ICommand
    {
        private readonly Guid _transactionId;

        public UndoCommand(Guid transactionId)
        {
            _transactionId = transactionId;
        }

        public void Execute(CommandContext context)
        {
            IFileSystem fileSystem = RequireFileSystem(context);
            string root = context.Session.RootPath ?? throw new InvalidOperationException("Connect first.");
            OperationJournalEntry entry = context.Operations.Undo(_transactionId, root, fileSystem);
            context.Output.WriteLine($"Transaction {entry.TransactionId} undone.");
        }
    }

    private sealed class DuplicatesCommand : ICommand
    {
        private readonly string _path;
        private readonly string _format;

        public DuplicatesCommand(string path, string format)
        {
            _path = path;
            _format = format;
        }

        public void Execute(CommandContext context)
        {
            IFileSystem fileSystem = RequireFileSystem(context);
            IReadOnlyList<DuplicateGroup> groups = new DuplicateFinder().Find(
                context.Session.ResolvePath(_path),
                fileSystem);
            if (string.Equals(_format, "json", StringComparison.OrdinalIgnoreCase))
            {
                context.Output.WriteLine(JsonSerializer.Serialize(groups, JsonOptions));
                return;
            }

            if (!string.Equals(_format, "text", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Duplicate output format must be 'text' or 'json'.");
            if (groups.Count == 0)
            {
                context.Output.WriteLine("No duplicate files found.");
                return;
            }

            foreach (DuplicateGroup group in groups)
            {
                context.Output.WriteLine($"{group.Size} bytes | SHA256:{group.Sha256}");
                foreach (string file in group.Files)
                    context.Output.WriteLine($"  {file}");
            }
        }
    }

    private sealed class HelpCommand : ICommand
    {
        private static readonly string[] HelpLines =
        [
            "connect <root> [--mode local]", "disconnect", "pwd", "ls", "cd <path>",
            "show <file>", "copy <source> <destination> [--dry-run]",
            "move <source> <destination> [--dry-run]", "rename <file> <new-name> [--dry-run]",
            "delete <file> [--dry-run]", "tree [--depth N]", "duplicates <path> [--format text|json]",
            "history", "undo <transaction-id>", "help", "exit",
        ];

        public void Execute(CommandContext context)
        {
            foreach (string line in HelpLines)
                context.Output.WriteLine(line);
        }
    }

    private sealed class ExitCommand : ICommand
    {
        public void Execute(CommandContext context)
        {
            context.ExitRequested = true;
        }
    }
}
