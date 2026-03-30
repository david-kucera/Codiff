using Codiff.Models;

namespace Codiff.Services;

/// <summary>
/// Provides functionality to compare the contents of two folders and identify differences between their files.
/// </summary>
/// <remarks>This service determines which files are identical, modified, or present only in one of the specified
/// folders. It is intended for scenarios where a detailed comparison of folder contents is required, such as
/// synchronization or backup validation.</remarks>
public class FolderDiffService
{
    #region Public functions
    public FolderDiffResult DiffFolders(string leftFolder, string rightFolder)
    {
        var entries = new List<FolderDiffEntry>();

        var leftFiles = GetRelativePaths(leftFolder);
        var rightFiles = GetRelativePaths(rightFolder);

        var allPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(leftFiles.Keys);
        allPaths.UnionWith(rightFiles.Keys);

        foreach (var relativePath in allPaths)
        {
            bool inLeft = leftFiles.TryGetValue(relativePath, out var leftFull);
            bool inRight = rightFiles.TryGetValue(relativePath, out var rightFull);

            FileStatus status;
            if (inLeft && inRight)
            {
                status = AreFilesIdentical(leftFull!, rightFull!)
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

            entries.Add(new FolderDiffEntry
            {
                RelativePath = relativePath,
                Status = status,
                LeftFullPath = inLeft ? leftFull : null,
                RightFullPath = inRight ? rightFull : null
            });
        }

        return new FolderDiffResult
        {
            LeftFolderPath = leftFolder,
            RightFolderPath = rightFolder,
            Entries = entries
        };
    }
    #endregion // Public functions

    #region Private functions
    private static Dictionary<string, string> GetRelativePaths(string folder)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file);
            result[relative] = file;
        }
        return result;
    }

    private static bool AreFilesIdentical(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);

        if (leftInfo.Length != rightInfo.Length)
            return false;

        using var leftStream = File.OpenRead(leftPath);
        using var rightStream = File.OpenRead(rightPath);

        var buffer1 = new byte[4096];
        var buffer2 = new byte[4096];

        while (true)
        {
            int read1 = leftStream.Read(buffer1, 0, buffer1.Length);
            int read2 = rightStream.Read(buffer2, 0, buffer2.Length);

            if (read1 != read2) return false;
            if (read1 == 0) return true;
            if (!buffer1.AsSpan(0, read1).SequenceEqual(buffer2.AsSpan(0, read2)))
                return false;
        }
    }
    #endregion // Private functions
}
