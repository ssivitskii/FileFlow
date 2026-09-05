namespace FileFlow.Cli.Parsing;

public interface ICommandHandler
{
    ICommandHandler SetNext(ICommandHandler nextHandler);

    ICommandBuilder? Handle(string commandName);
}
