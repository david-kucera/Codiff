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
