using System.Buffers;
using System.IO.Enumeration;
using Codiff.Models;
using Microsoft.Win32.SafeHandles;

namespace Codiff.Services;

/// <summary>
/// Progress snapshot reported while a folder diff is running.
/// <see cref="Total"/> is 0 during the initial scanning phase (count unknown).
/// </summary>
public readonly record struct FolderDiffProgress(string Phase, int Done, int Total);

/// <summary>
/// Provides functionality to compare the contents of two folders and identify differences between their files.
/// </summary>
/// <remarks>This service determines which files are identical, modified, or present only in one of the specified
/// folders. It is intended for scenarios where a detailed comparison of folder contents is required, such as
/// synchronization or backup validation.</remarks>
public class FolderDiffService
{
    #region Class members
    private const int CompareBufferSize = 64 * 1024;

    // File contents are compared on a few threads at once: it hides I/O latency (SSDs, network
    // shares) without flooding a single spinning disk with concurrent reads.
    private const int MaxParallelComparisons = 4;

    // Same matching as Directory.GetFiles(folder, "*", SearchOption.AllDirectories) (hidden and system
    // files included), except that unreadable subdirectories are skipped instead of failing the scan.
    private static readonly EnumerationOptions ScanOptions = new()
    {
        MatchType = MatchType.Win32,
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true
    };
    #endregion // Class members

    #region Public functions
    public FolderDiffResult DiffFolders(string leftFolder, string rightFolder,
        IProgress<FolderDiffProgress>? progress = null)
    {
        progress?.Report(new FolderDiffProgress("Scanning folders…", 0, 0));
        var leftFiles = GetFiles(leftFolder);
        var rightFiles = GetFiles(rightFolder);

        var allPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(leftFiles.Keys);
        allPaths.UnionWith(rightFiles.Keys);
        var paths = allPaths.ToArray();

        int total = paths.Length;
        int done = 0;
        int lastReported = 0;
        var progressLock = new object();
        progress?.Report(new FolderDiffProgress("Comparing files…", 0, total));

        var entries = new FolderDiffEntry[total];
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelComparisons };
        Parallel.For(0, total, parallelOptions, i =>
        {
            var relativePath = paths[i];
            bool inLeft = leftFiles.TryGetValue(relativePath, out var left);
            bool inRight = rightFiles.TryGetValue(relativePath, out var right);

            FileStatus status;
            if (inLeft && inRight)
            {
                status = AreFilesIdentical(left, right)
                    ? FileStatus.Identical
                    : FileStatus.Modified;
            }
            else if (inLeft)
            {
                status = FileStatus.OnlyInLeft;
            }
            else
            {
                status = FileStatus.OnlyInRight;
            }

            entries[i] = new FolderDiffEntry
            {
                RelativePath = relativePath,
                Status = status,
                LeftFullPath = inLeft ? left.FullPath : null,
                RightFullPath = inRight ? right.FullPath : null,
                LeftSize = inLeft ? left.Length : null,
                RightSize = inRight ? right.Length : null,
                LeftLastWriteTime = inLeft ? left.LastWriteTime : null,
                RightLastWriteTime = inRight ? right.LastWriteTime : null
            };

            int n = Interlocked.Increment(ref done);
            // Throttle reports so the UI thread isn't flooded on huge folders, and keep them in
            // increasing order even though items finish on several threads.
            if (progress != null && (n % 50 == 0 || n == total))
            {
                lock (progressLock)
                {
                    if (n > lastReported)
                    {
                        lastReported = n;
                        progress.Report(new FolderDiffProgress("Comparing files…", n, total));
                    }
                }
            }
        });

        return new FolderDiffResult
        {
            LeftFolderPath = leftFolder,
            RightFolderPath = rightFolder,
            Entries = entries
        };
    }
    #endregion // Public functions

    #region Private functions
    /// <summary>A file found by the scan, with the metadata the enumeration provides for free.</summary>
    private readonly record struct ScannedFile(string FullPath, long Length, DateTime LastWriteTime);

    private static Dictionary<string, ScannedFile> GetFiles(string folder)
    {
        var result = new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;

        // Size and timestamp come with the directory entry itself (Windows) or from a single
        // stat call (Unix), so nothing has to touch the file again just to display them.
        var files = new FileSystemEnumerable<ScannedFile>(
            folder,
            (ref FileSystemEntry entry) => new ScannedFile(
                entry.ToSpecifiedFullPath(),
                entry.Length,
                entry.LastWriteTimeUtc.LocalDateTime),
            ScanOptions)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory
        };

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file.FullPath);
            result[relative] = file;
        }
        return result;
    }

    private static bool AreFilesIdentical(ScannedFile left, ScannedFile right)
    {
        if (left.Length != right.Length)
            return false;

        byte[]? buffer1 = null;
        byte[]? buffer2 = null;
        try
        {
            // FileShare.ReadWrite | Delete: files that another program keeps open for writing
            // (logs, databases) can still be compared.
            using var leftHandle = OpenForCompare(left.FullPath);
            using var rightHandle = OpenForCompare(right.FullPath);

            buffer1 = ArrayPool<byte>.Shared.Rent(CompareBufferSize);
            buffer2 = ArrayPool<byte>.Shared.Rent(CompareBufferSize);

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
            // A file that cannot be read cannot be confirmed identical; show it as modified
            // instead of failing the whole folder comparison.
            System.Diagnostics.Debug.WriteLine($"[FolderDiffService] Compare failed for '{left.FullPath}': {ex.Message}");
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
        while (total < CompareBufferSize)
        {
            int read = RandomAccess.Read(handle, buffer.AsSpan(total, CompareBufferSize - total), offset + total);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
    #endregion // Private functions
}
