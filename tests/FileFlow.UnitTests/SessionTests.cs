using FileFlow.Core.Abstractions;
using FileFlow.Core.Session;

namespace FileFlow.UnitTests;

public sealed class SessionTests
{
    private static readonly string[] RootAndExistingDirectories = ["/root", "/root/existing"];
    private static readonly string[] RootDirectory = ["/root"];

    [Fact]
    public void MissingDirectoryDoesNotChangeCurrentDirectory()
    {
        var fileSystem = new FakeFileSystem(RootAndExistingDirectories);
        var session = new FileSystemSession();
        session.Connect("/root", fileSystem);
        session.ChangeDirectory("existing");
        string before = session.GetCurrentDirectory();

        Assert.Throws<DirectoryNotFoundException>(() => session.ChangeDirectory("missing"));

        Assert.Equal(before, session.GetCurrentDirectory());
    }

    [Fact]
    public void ParentTraversalCannotEscapeConnectedRoot()
    {
        var session = new FileSystemSession();
        session.Connect("/root", new FakeFileSystem(RootDirectory));

        Assert.Throws<InvalidOperationException>(() => session.ResolvePath("../outside"));
    }

    [Fact]
    public void ConnectNormalizesTrailingDirectorySeparator()
    {
        var session = new FileSystemSession();
        session.Connect("/root/", new FakeFileSystem(RootDirectory));

        Assert.Equal("/root", session.RootPath);
        Assert.Equal("/root", session.GetCurrentDirectory());
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories;

        public FakeFileSystem(IEnumerable<string> directories)
        {
            _directories = directories.ToHashSet(StringComparer.Ordinal);
        }

        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public IEnumerable<string> EnumerateFiles(string path) => [];

        public IEnumerable<string> EnumerateDirectories(string path) => [];

        public string ReadFile(string path) => throw new NotSupportedException();

        public void CopyFile(string source, string destination) => throw new NotSupportedException();

        public void MoveFile(string source, string destination) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();
    }
}
