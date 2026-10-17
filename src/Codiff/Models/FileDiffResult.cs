using System.Text;

namespace Codiff.Models;

/// <summary>
/// Encoding and line-ending style of a file as it was read, so that saving it writes
/// the text back the same way (keeps CRLF/LF and a byte-order mark if there was one).
/// </summary>
public sealed record TextFileFormat(Encoding Encoding, string NewLine)
{
    /// <summary>Used for texts that were not read from a file: UTF-8 without BOM, platform line endings.</summary>
    public static readonly TextFileFormat Default = new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Environment.NewLine);
}

public class FileDiffResult
{
    public string LeftFilePath { get; set; } = string.Empty;
    public string RightFilePath { get; set; } = string.Empty;
    public bool LeftFileExists { get; set; }
    public bool RightFileExists { get; set; }
    public TextFileFormat LeftFormat { get; set; } = TextFileFormat.Default;
    public TextFileFormat RightFormat { get; set; } = TextFileFormat.Default;
    public IReadOnlyList<DiffLine> Lines { get; set; } = Array.Empty<DiffLine>();
    public IReadOnlyList<int> DiffHunkIndices { get; set; } = Array.Empty<int>();
}
