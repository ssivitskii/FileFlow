namespace FileFlow.Core.Abstractions;

public interface IFileContentSource
{
    long GetFileLength(string path);

    Stream OpenRead(string path);
}
