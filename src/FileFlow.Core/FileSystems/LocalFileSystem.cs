using FileFlow.Core.Abstractions;

namespace FileFlow.Core.FileSystems;

public sealed class LocalFileSystem : IFileContentSource, IFileSystem
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        return Directory.EnumerateFiles(path);
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        return Directory.EnumerateDirectories(path);
    }

    public string ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    public void CopyFile(string source, string destination)
    {
        File.Copy(source, destination, overwrite: false);
    }

    public void MoveFile(string source, string destination)
    {
        File.Move(source, destination, overwrite: false);
    }

    public void DeleteFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("File not found.", path);
        File.Delete(path);
    }

    public long GetFileLength(string path)
    {
        return new FileInfo(path).Length;
    }

    public Stream OpenRead(string path)
    {
        return File.OpenRead(path);
    }
}
