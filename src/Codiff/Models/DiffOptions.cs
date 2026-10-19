namespace Codiff.Models;

/// <summary>Options that control which differences between two texts are reported.</summary>
/// <param name="IgnoreWhiteSpace">
/// Lines that differ only in leading/trailing white space or in the amount of white space
/// between words are considered equal.
/// </param>
/// <param name="IgnoreCase">Lines that differ only in letter case are considered equal.</param>
public sealed record DiffOptions(bool IgnoreWhiteSpace = true, bool IgnoreCase = false)
{
    public static readonly DiffOptions Default = new();
}
