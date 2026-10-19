using System.IO.Enumeration;
using Codiff.Models;

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

    private static bool AreFilesIdentical(ScannedFile left, ScannedFile right) =>
        FileContentComparer.AreIdentical(left.FullPath, right.FullPath, left.Length, right.Length);
    #endregion // Private functions
}
