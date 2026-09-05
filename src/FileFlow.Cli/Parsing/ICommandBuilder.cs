using FileFlow.Core.Abstractions;

namespace FileFlow.Cli.Parsing;

public interface ICommandBuilder
{
    void AddPositional(string value);

    void AddFlag(string name, string value);

    ICommand Build();
}
