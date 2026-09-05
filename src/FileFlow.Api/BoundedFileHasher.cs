using System.Buffers;
using System.Security.Cryptography;

namespace FileFlow.Api;

internal static class BoundedFileHasher
{
    private const int BufferSize = 81_920;

    public static async Task<string> HashAsync(
        string path,
        long expectedSize,
        DateTime expectedLastWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        EnsureSnapshot(path, expectedSize, expectedLastWriteTimeUtc, stream: null);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        EnsureSnapshot(path, expectedSize, expectedLastWriteTimeUtc, stream);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            long remaining = expectedSize;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                    throw WorkspaceChanged();
                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            EnsureSnapshot(path, expectedSize, expectedLastWriteTimeUtc, stream);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void EnsureSnapshot(
        string path,
        long expectedSize,
        DateTime expectedLastWriteTimeUtc,
        FileStream? stream)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists
            || file.Length != expectedSize
            || file.LastWriteTimeUtc != expectedLastWriteTimeUtc
            || (stream is not null && stream.Length != expectedSize))
        {
            throw WorkspaceChanged();
        }
    }

    private static ApiProblemException WorkspaceChanged() => new(
        StatusCodes.Status409Conflict,
        "Workspace changed during scan",
        "A file changed during duplicate analysis. Retry the scan.");
}
