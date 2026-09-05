namespace FileFlow.Cli.Parsing;

public sealed class CommandHandler : ICommandHandler
{
    private readonly string _commandName;
    private readonly Func<ICommandBuilder> _builderFactory;
    private ICommandHandler? _next;

    public CommandHandler(string commandName, Func<ICommandBuilder> builderFactory)
    {
        _commandName = commandName;
        _builderFactory = builderFactory;
    }

    public ICommandHandler SetNext(ICommandHandler nextHandler)
    {
        _next = nextHandler;
        return nextHandler;
    }

    public ICommandBuilder? Handle(string commandName)
    {
        return string.Equals(commandName, _commandName, StringComparison.OrdinalIgnoreCase)
            ? _builderFactory()
            : _next?.Handle(commandName);
    }
}
