namespace Codiff.Models;

public enum LineType
{
    Unchanged,
    Modified,
    Added,
    Deleted,
    Filler
}

public class DiffSpan
{
    public string Text { get; set; } = string.Empty;
    public bool IsChanged { get; set; }
}

public class DiffLine
{
    public LineType LineType { get; set; }
    public string LeftText { get; set; } = string.Empty;
    public string RightText { get; set; } = string.Empty;
    public int? LeftLineNumber { get; set; }
    public int? RightLineNumber { get; set; }

    /// <summary>Word-level pieces of a modified line; empty when there is no intra-line information.</summary>
    public IReadOnlyList<DiffSpan> LeftSpans { get; set; } = Array.Empty<DiffSpan>();

    /// <summary>Word-level pieces of a modified line; empty when there is no intra-line information.</summary>
    public IReadOnlyList<DiffSpan> RightSpans { get; set; } = Array.Empty<DiffSpan>();
}
