namespace FileFlow.Core.Abstractions;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    IEnumerable<string> EnumerateFiles(string path);

    IEnumerable<string> EnumerateDirectories(string path);

    string ReadFile(string path);

    void CopyFile(string source, string destination);

    void MoveFile(string source, string destination);

    void DeleteFile(string path);
}
