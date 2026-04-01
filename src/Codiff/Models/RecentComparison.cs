namespace Codiff.Models;

/// <summary>
/// Represents a single recent file or folder comparison entry persisted in the config.
/// </summary>
public class RecentComparison
{
    public bool IsFolder { get; set; }
    public string LeftPath { get; set; } = string.Empty;
    public string RightPath { get; set; } = string.Empty;
}
