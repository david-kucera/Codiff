using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace Codiff.Services;

/// <summary>Byte-by-byte comparison of two files.</summary>
public static class FileContentComparer
{
    private const int BlockSize = 64 * 1024;

    /// <summary>
    /// Returns <see langword="true"/> when both files have exactly the same bytes. A file that
    /// cannot be read is never reported as identical.
    /// </summary>
    public static bool AreIdentical(string leftPath, string rightPath)
    {
        try
        {
            return AreIdentical(leftPath, rightPath, new FileInfo(leftPath).Length, new FileInfo(rightPath).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Same as <see cref="AreIdentical(string, string)"/> when the file sizes are already known.</summary>
    public static bool AreIdentical(string leftPath, string rightPath, long leftLength, long rightLength)
    {
        if (leftLength != rightLength)
            return false;

        byte[]? buffer1 = null;
        byte[]? buffer2 = null;
        try
        {
            // FileShare.ReadWrite | Delete: files that another program keeps open for writing
            // (logs, databases) can still be compared.
            using var leftHandle = OpenForCompare(leftPath);
            using var rightHandle = OpenForCompare(rightPath);

            buffer1 = ArrayPool<byte>.Shared.Rent(BlockSize);
            buffer2 = ArrayPool<byte>.Shared.Rent(BlockSize);

            long offset = 0;
            while (true)
            {
                int read1 = ReadBlock(leftHandle, buffer1, offset);
                int read2 = ReadBlock(rightHandle, buffer2, offset);

                if (read1 != read2) return false;
                if (read1 == 0) return true;
                if (!buffer1.AsSpan(0, read1).SequenceEqual(buffer2.AsSpan(0, read2)))
                    return false;

                offset += read1;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A file that cannot be read cannot be confirmed identical.
            System.Diagnostics.Debug.WriteLine($"[FileContentComparer] Compare failed for '{leftPath}': {ex.Message}");
            return false;
        }
        finally
        {
            if (buffer1 != null) ArrayPool<byte>.Shared.Return(buffer1);
            if (buffer2 != null) ArrayPool<byte>.Shared.Return(buffer2);
        }
    }

    private static SafeFileHandle OpenForCompare(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);

    /// <summary>Reads until the block is full or the end of the file is reached (a single read may return less).</summary>
    private static int ReadBlock(SafeFileHandle handle, byte[] buffer, long offset)
    {
        int total = 0;
        while (total < BlockSize)
        {
            int read = RandomAccess.Read(handle, buffer.AsSpan(total, BlockSize - total), offset + total);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
