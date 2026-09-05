using FileFlow.Core.Abstractions;
using FileFlow.Core.Operations;
using FileFlow.Core.Session;

namespace FileFlow.Core;

public sealed class CommandContext
{
    public CommandContext(FileSystemSession session, IOutputWriter output, FileOperationService operations)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public FileSystemSession Session { get; }

    public IOutputWriter Output { get; }

    public FileOperationService Operations { get; }

    public bool ExitRequested { get; set; }
}
