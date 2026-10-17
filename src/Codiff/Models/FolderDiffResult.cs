namespace Codiff.Models;

public enum FileStatus
{
    Identical,
    Modified,
    OnlyInLeft,
    OnlyInRight
}

public class FolderDiffEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public FileStatus Status { get; set; }
    public string? LeftFullPath { get; set; }
    public string? RightFullPath { get; set; }

    /// <summary>File size captured during the scan (<see langword="null"/> when not present on this side).</summary>
    public long? LeftSize { get; set; }

    /// <summary>File size captured during the scan (<see langword="null"/> when not present on this side).</summary>
    public long? RightSize { get; set; }

    /// <summary>Local last-write time captured during the scan (<see langword="null"/> when not present on this side).</summary>
    public DateTime? LeftLastWriteTime { get; set; }

    /// <summary>Local last-write time captured during the scan (<see langword="null"/> when not present on this side).</summary>
    public DateTime? RightLastWriteTime { get; set; }

    public string StatusIcon => Status switch
    {
        FileStatus.Identical => "=",
        FileStatus.Modified => "~",
        FileStatus.OnlyInLeft => "-",
        FileStatus.OnlyInRight => "+",
        _ => "?"
    };
}

public class FolderDiffResult
{
    public string LeftFolderPath { get; set; } = string.Empty;
    public string RightFolderPath { get; set; } = string.Empty;
    public IReadOnlyList<FolderDiffEntry> Entries { get; set; } = Array.Empty<FolderDiffEntry>();
}
