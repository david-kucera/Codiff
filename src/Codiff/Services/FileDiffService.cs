using System.Text;
using Codiff.Models;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Codiff.Services;

/// <summary>Text of a file as read for comparison (with its encoding and line endings), or a note that the file is binary.</summary>
public readonly record struct FileContent(string Text, TextFileFormat Format, bool IsBinary)
{
    public static readonly FileContent Empty = new(string.Empty, TextFileFormat.Default, false);
}

/// <summary>
/// Provides functionality to compare the contents of two files or text inputs and generate a detailed diff result.
/// </summary>
/// <remarks>The diff results produced by this service include line-by-line and hunk-level information, making it
/// suitable for applications that need to display or process file differences. This class is not thread-safe; create a
/// separate instance for each concurrent operation if needed.</remarks>
public class FileDiffService
{
    #region Class members
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Like git: a file whose first 8000 bytes contain a NUL byte is treated as binary.
    private const int BinarySniffLength = 8000;

    private readonly SideBySideDiffBuilder _diffBuilder = new(new Differ());
    #endregion // Class members

    #region Public functions
    /// <summary>
    /// Compares two files. An empty path stands for a side without a file and is compared as empty text.
    /// </summary>
    public FileDiffResult DiffFiles(string leftPath, string rightPath, DiffOptions? options = null)
    {
        var left = ReadFile(leftPath);
        var right = ReadFile(rightPath);

        if (left.IsBinary || right.IsBinary)
        {
            bool leftExists = !string.IsNullOrEmpty(leftPath) && File.Exists(leftPath);
            bool rightExists = !string.IsNullOrEmpty(rightPath) && File.Exists(rightPath);
            return new FileDiffResult
            {
                LeftFilePath = leftPath,
                RightFilePath = rightPath,
                LeftFileExists = leftExists,
                RightFileExists = rightExists,
                IsBinary = true,
                BinaryFilesIdentical = leftExists && rightExists && FileContentComparer.AreIdentical(leftPath, rightPath)
            };
        }

        var result = DiffTexts(left.Text, right.Text, leftPath, rightPath, options);
        result.LeftFormat = left.Format;
        result.RightFormat = right.Format;
        return result;
    }

    public FileDiffResult DiffTexts(string leftText, string rightText, string leftPath = "", string rightPath = "",
        DiffOptions? options = null)
    {
        options ??= DiffOptions.Default;
        var model = _diffBuilder.BuildDiffModel(leftText, rightText, options.IgnoreWhiteSpace, options.IgnoreCase);

        int count = Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count);
        var lines = new List<DiffLine>(count);
        var hunkIndices = new List<int>();
        bool prevWasDiff = false;

        for (int i = 0; i < count; i++)
        {
            var oldPiece = i < model.OldText.Lines.Count ? model.OldText.Lines[i] : null;
            var newPiece = i < model.NewText.Lines.Count ? model.NewText.Lines[i] : null;

            var diffLine = BuildDiffLine(oldPiece, newPiece);
            lines.Add(diffLine);

            bool isDiff = diffLine.LineType != LineType.Unchanged;
            if (isDiff && !prevWasDiff)
                hunkIndices.Add(i);

            prevWasDiff = isDiff;
        }

        return new FileDiffResult
        {
            LeftFilePath = leftPath,
            RightFilePath = rightPath,
            LeftFileExists = !string.IsNullOrEmpty(leftPath) && File.Exists(leftPath),
            RightFileExists = !string.IsNullOrEmpty(rightPath) && File.Exists(rightPath),
            LeftFormat = TextFileFormat.Default with { NewLine = DetectNewLine(leftText) },
            RightFormat = TextFileFormat.Default with { NewLine = DetectNewLine(rightText) },
            Lines = lines,
            DiffHunkIndices = hunkIndices
        };
    }

    /// <summary>
    /// Reads a file like <see cref="File.ReadAllText(string)"/> does (UTF-8 unless a byte-order mark
    /// says otherwise) and also returns the detected encoding, so saving can keep the BOM.
    /// Binary files are recognized and not decoded. An empty path gives empty text.
    /// </summary>
    public static FileContent ReadFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return FileContent.Empty;

        using var stream = File.OpenRead(path);
        if (LooksBinary(stream))
            return new FileContent(string.Empty, TextFileFormat.Default, IsBinary: true);

        using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return new FileContent(text, new TextFileFormat(reader.CurrentEncoding, DetectNewLine(text)), IsBinary: false);
    }
    #endregion // Public functions

    #region Private functions
    /// <summary>
    /// A NUL byte near the start means binary data, unless the file starts with a UTF-16/UTF-32
    /// byte-order mark (those encodings contain NUL bytes in ordinary text). Rewinds the stream.
    /// </summary>
    private static bool LooksBinary(Stream stream)
    {
        var head = new byte[BinarySniffLength];
        int length = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;

        ReadOnlySpan<byte> utf16Le = [0xFF, 0xFE]; // also the start of the UTF-32 LE mark
        ReadOnlySpan<byte> utf16Be = [0xFE, 0xFF];
        ReadOnlySpan<byte> utf32Be = [0x00, 0x00, 0xFE, 0xFF];

        var bytes = head.AsSpan(0, length);
        if (bytes.StartsWith(utf16Le) || bytes.StartsWith(utf16Be) || bytes.StartsWith(utf32Be))
            return false;

        return bytes.IndexOf((byte)0) >= 0;
    }

    /// <summary>Returns the first line terminator used in <paramref name="text"/> (platform default when there is none).</summary>
    private static string DetectNewLine(string text)
    {
        int i = text.AsSpan().IndexOfAny('\r', '\n');
        if (i < 0)
            return Environment.NewLine;
        if (text[i] == '\n')
            return "\n";
        return i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
    }

    private static DiffLine BuildDiffLine(DiffPiece? oldPiece, DiffPiece? newPiece)
    {
        if (oldPiece == null && newPiece == null)
            return new DiffLine { LineType = LineType.Filler };

        // Determine type based on the change types from both sides
        ChangeType oldType = oldPiece?.Type ?? ChangeType.Imaginary;
        ChangeType newType = newPiece?.Type ?? ChangeType.Imaginary;

        LineType lineType;
        if (oldType == ChangeType.Imaginary && newType == ChangeType.Imaginary)
            lineType = LineType.Filler;
        else if (oldType == ChangeType.Imaginary)
            lineType = LineType.Added;
        else if (newType == ChangeType.Imaginary)
            lineType = LineType.Deleted;
        else if (oldType == ChangeType.Unchanged && newType == ChangeType.Unchanged)
            lineType = LineType.Unchanged;
        else if (oldType == ChangeType.Modified || newType == ChangeType.Modified)
            lineType = LineType.Modified;
        else if (oldType == ChangeType.Deleted && newType == ChangeType.Inserted)
            lineType = LineType.Modified;
        else if (oldType == ChangeType.Inserted)
            lineType = LineType.Added;
        else if (oldType == ChangeType.Deleted)
            lineType = LineType.Deleted;
        else
            lineType = LineType.Unchanged;

        var leftSpans = BuildSpans(oldPiece);
        var rightSpans = BuildSpans(newPiece);

        return new DiffLine
        {
            LineType = lineType,
            LeftText = oldPiece?.Text ?? string.Empty,
            RightText = newPiece?.Text ?? string.Empty,
            LeftLineNumber = (oldType != ChangeType.Imaginary) ? oldPiece?.Position : null,
            RightLineNumber = (newType != ChangeType.Imaginary) ? newPiece?.Position : null,
            LeftSpans = leftSpans,
            RightSpans = rightSpans
        };
    }

    private static IReadOnlyList<DiffSpan> BuildSpans(DiffPiece? piece)
    {
        // Only modified lines carry word-level sub-pieces; everything else has no intra-line information.
        if (piece == null || piece.SubPieces.Count == 0)
            return Array.Empty<DiffSpan>();

        var spans = new List<DiffSpan>(piece.SubPieces.Count);
        foreach (var sub in piece.SubPieces)
        {
            if (sub.Text == null) continue;
            spans.Add(new DiffSpan
            {
                Text = sub.Text,
                IsChanged = sub.Type != ChangeType.Unchanged
            });
        }
        return spans;
    }
    #endregion // Private functions
}
