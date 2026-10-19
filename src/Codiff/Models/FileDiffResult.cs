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

    /// <summary>Short description for the status bar, e.g. "UTF-8 BOM · CRLF".</summary>
    public string Description
    {
        get
        {
            string encoding = Encoding switch
            {
                UTF8Encoding utf8 => utf8.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8",
                UnicodeEncoding { CodePage: 1201 } => "UTF-16 BE",
                UnicodeEncoding => "UTF-16 LE",
                UTF32Encoding { CodePage: 12001 } => "UTF-32 BE",
                UTF32Encoding => "UTF-32 LE",
                _ => Encoding.WebName.ToUpperInvariant()
            };
            string newLine = NewLine switch
            {
                "\r\n" => "CRLF",
                "\n" => "LF",
                "\r" => "CR",
                _ => "?"
            };
            return $"{encoding} · {newLine}";
        }
    }
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

    /// <summary>True when at least one file is binary; no lines are produced for such a comparison.</summary>
    public bool IsBinary { get; set; }

    /// <summary>For a binary comparison: whether both files contain exactly the same bytes.</summary>
    public bool BinaryFilesIdentical { get; set; }
}
