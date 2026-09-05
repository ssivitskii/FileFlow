namespace FileFlow.Core.Abstractions;

public interface ICommand
{
    void Execute(CommandContext context);
}
