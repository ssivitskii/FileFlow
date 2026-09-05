using FileFlow.Core.Abstractions;
using FileFlow.Core.Commands;
using System.Globalization;

namespace FileFlow.Cli.Parsing;

public sealed class CommandParser
{
    private static readonly string[] NoFlags = [];
    private static readonly string[] ConnectFlags = ["--mode"];
    private static readonly string[] DuplicateFlags = ["--format"];
    private static readonly string[] MutationFlags = ["--dry-run"];
    private static readonly string[] TreeFlags = ["--depth"];
    private static readonly HashSet<string> ValuelessFlags = new(["--dry-run"], StringComparer.Ordinal);
    private readonly CommandHandler _handlerChain;

    public CommandParser()
    {
        var connect = Handler(
            "connect",
            1,
            1,
            ConnectFlags,
            (arguments, flags) => CommandFactory.Connect(arguments[0], flags.GetValueOrDefault("--mode", "local")));
        connect.SetNext(Handler("disconnect", 0, 0, NoFlags, (_, _) => CommandFactory.Disconnect()))
            .SetNext(Handler("pwd", 0, 0, NoFlags, (_, _) => CommandFactory.PrintWorkingDirectory()))
            .SetNext(Handler("ls", 0, 0, NoFlags, (_, _) => CommandFactory.List()))
            .SetNext(Handler("cd", 1, 1, NoFlags, (arguments, _) => CommandFactory.ChangeDirectory(arguments[0])))
            .SetNext(Handler("show", 1, 1, NoFlags, (arguments, _) => CommandFactory.Show(arguments[0])))
            .SetNext(Handler(
                "copy",
                2,
                2,
                MutationFlags,
                (arguments, flags) => CommandFactory.Copy(
                    arguments[0],
                    arguments[1],
                    flags.ContainsKey("--dry-run"))))
            .SetNext(Handler(
                "move",
                2,
                2,
                MutationFlags,
                (arguments, flags) => CommandFactory.Move(
                    arguments[0],
                    arguments[1],
                    flags.ContainsKey("--dry-run"))))
            .SetNext(Handler(
                "rename",
                2,
                2,
                MutationFlags,
                (arguments, flags) => CommandFactory.Rename(
                    arguments[0],
                    arguments[1],
                    flags.ContainsKey("--dry-run"))))
            .SetNext(Handler(
                "delete",
                1,
                1,
                MutationFlags,
                (arguments, flags) => CommandFactory.Delete(arguments[0], flags.ContainsKey("--dry-run"))))
            .SetNext(Handler(
                "tree",
                0,
                0,
                TreeFlags,
                (_, flags) => CommandFactory.Tree(ParseDepth(flags.GetValueOrDefault("--depth", "2")))))
            .SetNext(Handler(
                "duplicates",
                1,
                1,
                DuplicateFlags,
                (arguments, flags) => CommandFactory.Duplicates(
                    arguments[0],
                    flags.GetValueOrDefault("--format", "text"))))
            .SetNext(Handler("history", 0, 0, NoFlags, (_, _) => CommandFactory.History()))
            .SetNext(Handler(
                "undo",
                1,
                1,
                NoFlags,
                (arguments, _) => CommandFactory.Undo(ParseTransactionId(arguments[0]))))
            .SetNext(Handler("help", 0, 0, NoFlags, (_, _) => CommandFactory.Help()))
            .SetNext(Handler("exit", 0, 0, NoFlags, (_, _) => CommandFactory.Exit()));
        _handlerChain = connect;
    }

    public ICommand Parse(string input)
    {
        IReadOnlyList<string> tokens = Tokenizer.Tokenize(input);
        if (tokens.Count == 0)
            throw new ArgumentException("Command is empty.");
        ICommandBuilder builder = _handlerChain.Handle(tokens[0])
            ?? throw new ArgumentException($"Unknown command '{tokens[0]}'.");
        for (int index = 1; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                builder.AddPositional(token);
                continue;
            }

            if (ValuelessFlags.Contains(token))
            {
                builder.AddFlag(token, string.Empty);
                continue;
            }

            if (++index >= tokens.Count)
                throw new ArgumentException($"Flag '{token}' requires a value.");
            builder.AddFlag(token, tokens[index]);
        }

        return builder.Build();
    }

    private static CommandHandler Handler(
        string name,
        int minimumArguments,
        int maximumArguments,
        IEnumerable<string> flags,
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, ICommand> factory)
    {
        return new CommandHandler(name, () => new CommandBuilder(
            minimumArguments,
            maximumArguments,
            flags,
            factory));
    }

    private static int ParseDepth(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int depth) || depth < 0)
            throw new ArgumentException("Tree depth must be a non-negative integer.");
        return depth;
    }

    private static Guid ParseTransactionId(string value)
    {
        if (!Guid.TryParse(value, out Guid transactionId) || transactionId == Guid.Empty)
            throw new ArgumentException("Transaction ID must be a non-empty GUID.");

        return transactionId;
    }
}
