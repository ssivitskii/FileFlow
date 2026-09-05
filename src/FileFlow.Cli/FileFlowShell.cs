using FileFlow.Cli.Parsing;
using FileFlow.Core;
using FileFlow.Core.Abstractions;
using FileFlow.Core.Operations;
using FileFlow.Core.Session;

namespace FileFlow.Cli;

public sealed class FileFlowShell
{
    private readonly CommandParser _parser;
    private readonly CommandContext _context;

    public FileFlowShell(IOutputWriter output)
        : this(output, FileFlowPaths.GetDefaultApplicationDataRoot())
    {
    }

    public FileFlowShell(IOutputWriter output, string applicationDataRoot)
    {
        _parser = new CommandParser();
        _context = new CommandContext(
            new FileSystemSession(),
            output,
            new FileOperationService(applicationDataRoot));
    }

    public bool ExitRequested => _context.ExitRequested;

    public void Execute(string line)
    {
        ICommand command = _parser.Parse(line);
        command.Execute(_context);
    }
}
