using FileFlow.Core.Abstractions;
using FileFlow.Core.Tree;

namespace FileFlow.UnitTests;

public sealed class TreeOrderingTests
{
    private static readonly string[] ExpectedOrder = ["A.txt", "a.txt", "b.txt"];

    [Fact]
    public void TreeUsesOrdinalTieBreakForCaseInsensitiveMatches()
    {
        var visitor = new RecordingVisitor();

        new FileTreeWalker().Walk("/root", 1, new UnorderedFileSystem(), visitor);

        Assert.Equal(ExpectedOrder, visitor.Files);
    }

    private sealed class UnorderedFileSystem : IFileSystem
    {
        public bool FileExists(string path) => true;

        public bool DirectoryExists(string path) => string.Equals(path, "/root", StringComparison.Ordinal);

        public IEnumerable<string> EnumerateFiles(string path) => ["/root/b.txt", "/root/a.txt", "/root/A.txt"];

        public IEnumerable<string> EnumerateDirectories(string path) => [];

        public string ReadFile(string path) => throw new NotSupportedException();

        public void CopyFile(string source, string destination) => throw new NotSupportedException();

        public void MoveFile(string source, string destination) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();
    }

    private sealed class RecordingVisitor : IFileSystemVisitor
    {
        public List<string> Files { get; } = [];

        public void VisitDirectory(string path, int depth)
        {
        }

        public void VisitFile(string path, int depth)
        {
            Files.Add(Path.GetFileName(path) ?? path);
        }
    }
}
