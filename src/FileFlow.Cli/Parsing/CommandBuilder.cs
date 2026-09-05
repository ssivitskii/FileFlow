using FileFlow.Core.Abstractions;

namespace FileFlow.Cli.Parsing;

public sealed class CommandBuilder : ICommandBuilder
{
    private readonly int _minimumArguments;
    private readonly int _maximumArguments;
    private readonly HashSet<string> _allowedFlags;
    private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, ICommand> _factory;
    private readonly List<string> _arguments = [];
    private readonly Dictionary<string, string> _flags = new(StringComparer.Ordinal);

    public CommandBuilder(
        int minimumArguments,
        int maximumArguments,
        IEnumerable<string> allowedFlags,
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>, ICommand> factory)
    {
        _minimumArguments = minimumArguments;
        _maximumArguments = maximumArguments;
        _allowedFlags = allowedFlags.ToHashSet(StringComparer.Ordinal);
        _factory = factory;
    }

    public void AddPositional(string value)
    {
        if (_arguments.Count >= _maximumArguments)
            throw new ArgumentException("Too many positional arguments.");
        _arguments.Add(value);
    }

    public void AddFlag(string name, string value)
    {
        if (!_allowedFlags.Contains(name))
            throw new ArgumentException($"Unknown flag '{name}'.");
        if (!_flags.TryAdd(name, value))
            throw new ArgumentException($"Flag '{name}' was specified more than once.");
    }

    public ICommand Build()
    {
        if (_arguments.Count < _minimumArguments)
            throw new ArgumentException("Required positional arguments are missing.");
        return _factory(_arguments, _flags);
    }
}
