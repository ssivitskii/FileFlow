using FileFlow.Core.Abstractions;

namespace FileFlow.Core.Tree;

public sealed class TextTreeVisitor : IFileSystemVisitor
{
    private readonly IOutputWriter _output;

    public TextTreeVisitor(IOutputWriter output)
    {
        _output = output;
    }

    public void VisitDirectory(string path, int depth)
    {
        Write(depth, depth == 0 ? "." : Path.GetFileName(path) + "/");
    }

    public void VisitFile(string path, int depth)
    {
        Write(depth, Path.GetFileName(path));
    }

    private void Write(int depth, string displayName)
    {
        _output.WriteLine($"{new string(' ', depth * 2)}{displayName}");
    }
}
