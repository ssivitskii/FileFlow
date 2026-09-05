namespace FileFlow.Core.Abstractions;

public interface IFileSystemVisitor
{
    void VisitDirectory(string path, int depth);

    void VisitFile(string path, int depth);
}
