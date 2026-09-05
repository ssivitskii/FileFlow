using FileFlow.Cli;
using FileFlow.Core.Abstractions;

namespace FileFlow.IntegrationTests;

public sealed class LocalFileSystemTests : IDisposable
{
    private static readonly string[] ExpectedDepthOneTree = [".", "  a.txt", "  b.txt", "  z-directory/"];
    private static readonly string[] ExpectedDepthZeroTree = ["."];
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fileflow-{Guid.NewGuid():N}");
    private readonly string _state = Path.Combine(Path.GetTempPath(), $"fileflow-state-{Guid.NewGuid():N}");

    public LocalFileSystemTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ShellHandlesQuotedPathsMoveRenameShowAndDelete()
    {
        string source = Path.Combine(_root, "my file.txt");
        File.WriteAllText(source, "portfolio content");
        var output = new CaptureOutput();
        var shell = new FileFlowShell(output, _state);
        shell.Execute($"connect \"{_root}\"");

        shell.Execute("move \"my file.txt\" moved.txt");
        shell.Execute("rename moved.txt final.txt");
        shell.Execute("show final.txt");
        shell.Execute("delete final.txt");

        Assert.Contains("portfolio content", output.Lines);
        Assert.False(File.Exists(Path.Combine(_root, "final.txt")));
    }

    [Fact]
    public void CopyAndMoveRefuseToOverwriteExistingFiles()
    {
        File.WriteAllText(Path.Combine(_root, "source.txt"), "source");
        File.WriteAllText(Path.Combine(_root, "existing.txt"), "existing");
        var shell = new FileFlowShell(new CaptureOutput(), _state);
        shell.Execute($"connect \"{_root}\"");

        Assert.Throws<IOException>(() => shell.Execute("copy source.txt existing.txt"));
        Assert.Throws<IOException>(() => shell.Execute("move source.txt existing.txt"));
        Assert.True(File.Exists(Path.Combine(_root, "source.txt")));
    }

    [Fact]
    public void TreeUsesDeterministicOrderAndDepthZeroShowsOnlyRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "z-directory"));
        File.WriteAllText(Path.Combine(_root, "b.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "a.txt"), string.Empty);
        var output = new CaptureOutput();
        var shell = new FileFlowShell(output, _state);
        shell.Execute($"connect \"{_root}\"");
        output.Lines.Clear();

        shell.Execute("tree --depth 1");

        Assert.Equal(ExpectedDepthOneTree, output.Lines);
        output.Lines.Clear();
        shell.Execute("tree --depth 0");
        Assert.Equal(ExpectedDepthZeroTree, output.Lines);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        if (Directory.Exists(_state))
            Directory.Delete(_state, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class CaptureOutput : IOutputWriter
    {
        public List<string> Lines { get; } = [];

        public void WriteLine(string value)
        {
            Lines.Add(value);
        }
    }
}
