namespace Codiff.Models;

public class FileDiffResult
{
    public string LeftFilePath { get; set; } = string.Empty;
    public string RightFilePath { get; set; } = string.Empty;
    public IReadOnlyList<DiffLine> Lines { get; set; } = Array.Empty<DiffLine>();
    public IReadOnlyList<int> DiffHunkIndices { get; set; } = Array.Empty<int>();
}
