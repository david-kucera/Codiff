using System.Text;
using Codiff.Models;

namespace Codiff.Services;

/// <summary>One row of a side-by-side comparison, as needed to write a unified diff.</summary>
/// <param name="Type">Kind of the row.</param>
/// <param name="LeftText">Text in the left file, or <see langword="null"/> when the row has no left line.</param>
/// <param name="RightText">Text in the right file, or <see langword="null"/> when the row has no right line.</param>
public readonly record struct UnifiedDiffRow(LineType Type, string? LeftText, string? RightText);

/// <summary>Writes a side-by-side comparison in the unified diff format used by diff -u, git and patch.</summary>
public sealed class UnifiedDiffWriter
{
    private readonly IReadOnlyList<UnifiedDiffRow> _rows;
    // Lines of CRLF files keep their '\r' (as in diff -u and git), so the patch applies to them.
    private readonly string _leftLineEnd;
    private readonly string _rightLineEnd;
    private readonly int _lastLeft;
    private readonly int _lastRight;
    private readonly bool _leftEndsWithNewLine;
    private readonly bool _rightEndsWithNewLine;

    private UnifiedDiffWriter(IReadOnlyList<UnifiedDiffRow> rows, string leftNewLine, string rightNewLine)
    {
        _rows = rows;
        _leftLineEnd = leftNewLine == "\r\n" ? "\r\n" : "\n";
        _rightLineEnd = rightNewLine == "\r\n" ? "\r\n" : "\n";
        // The last row of each side holds what follows its last line break: empty when the text
        // ends with a line break (it is then not a line of its own), else a line without one.
        _lastLeft = LastIndex(rows, left: true);
        _lastRight = LastIndex(rows, left: false);
        _leftEndsWithNewLine = _lastLeft >= 0 && rows[_lastLeft].LeftText!.Length == 0;
        _rightEndsWithNewLine = _lastRight >= 0 && rows[_lastRight].RightText!.Length == 0;
    }

    /// <summary>
    /// Creates a unified diff of <paramref name="rows"/> with <paramref name="context"/> unchanged
    /// lines around each change. Returns an empty string when there are no differences.
    /// <paramref name="leftNewLine"/> and <paramref name="rightNewLine"/> are the files' line endings.
    /// </summary>
    public static string Write(IReadOnlyList<UnifiedDiffRow> rows, string leftLabel, string rightLabel,
        string leftNewLine = "\n", string rightNewLine = "\n", int context = 3)
    {
        var writer = new UnifiedDiffWriter(rows, leftNewLine, rightNewLine);
        var output = new StringBuilder();
        int index = 0;
        while (index < rows.Count)
        {
            if (!writer.IsChange(index))
            {
                index++;
                continue;
            }

            // Grow the hunk while the next change is close enough to share context lines.
            int start = Math.Max(0, index - context);
            int end = index;
            while (true)
            {
                while (end < rows.Count && writer.IsChange(end))
                    end++;
                int next = end;
                while (next < rows.Count && !writer.IsChange(next) && next - end < 2 * context)
                    next++;
                if (next < rows.Count && writer.IsChange(next))
                    end = next;
                else
                    break;
            }
            int stop = Math.Min(rows.Count, end + context);

            if (output.Length == 0)
                output.Append("--- ").Append(leftLabel).Append('\n').Append("+++ ").Append(rightLabel).Append('\n');
            writer.WriteHunk(output, start, stop);
            index = stop;
        }
        return output.ToString();
    }

    private bool IsRealLeft(int i) => _rows[i].LeftText != null && !(_leftEndsWithNewLine && i == _lastLeft);

    private bool IsRealRight(int i) => _rows[i].RightText != null && !(_rightEndsWithNewLine && i == _lastRight);

    private bool LeftLacksNewLine(int i) => i == _lastLeft && !_leftEndsWithNewLine;

    private bool RightLacksNewLine(int i) => i == _lastRight && !_rightEndsWithNewLine;

    /// <summary>
    /// A row is a change when it differs, when an empty line exists on one side only (the other
    /// side's "line" is just the text's final line break), or when equal lines differ in whether
    /// a line break follows them (the last line of only one file lacks it).
    /// </summary>
    private bool IsChange(int i)
    {
        bool left = IsRealLeft(i), right = IsRealRight(i);
        if (_rows[i].Type != LineType.Unchanged)
            return left || right;
        return left != right || (left && LeftLacksNewLine(i) != RightLacksNewLine(i));
    }

    private void WriteHunk(StringBuilder output, int start, int stop)
    {
        int leftBefore = 0, rightBefore = 0, leftCount = 0, rightCount = 0;
        for (int i = 0; i < stop; i++)
        {
            if (i < start)
            {
                if (IsRealLeft(i)) leftBefore++;
                if (IsRealRight(i)) rightBefore++;
            }
            else
            {
                if (IsRealLeft(i)) leftCount++;
                if (IsRealRight(i)) rightCount++;
            }
        }

        output.Append("@@ -").Append(Range(leftBefore, leftCount))
              .Append(" +").Append(Range(rightBefore, rightCount)).Append(" @@\n");

        int row = start;
        while (row < stop)
        {
            if (!IsChange(row))
            {
                if (IsRealLeft(row))
                    AppendLine(output, ' ', _rows[row].LeftText!, _leftLineEnd, LeftLacksNewLine(row));
                row++;
                continue;
            }

            // A block of changed rows: all removed lines first, then all added ones.
            int blockEnd = row;
            while (blockEnd < stop && IsChange(blockEnd))
                blockEnd++;
            for (int i = row; i < blockEnd; i++)
            {
                if (IsRealLeft(i))
                    AppendLine(output, '-', _rows[i].LeftText!, _leftLineEnd, LeftLacksNewLine(i));
            }
            for (int i = row; i < blockEnd; i++)
            {
                if (IsRealRight(i))
                    AppendLine(output, '+', _rows[i].RightText!, _rightLineEnd, RightLacksNewLine(i));
            }
            row = blockEnd;
        }
    }

    private static void AppendLine(StringBuilder output, char prefix, string text, string lineEnd, bool lacksNewLine)
    {
        output.Append(prefix).Append(text);
        if (lacksNewLine)
            output.Append("\n\\ No newline at end of file\n");
        else
            output.Append(lineEnd);
    }

    /// <summary>"start,count" of a hunk side; an empty range names the line before it.</summary>
    private static string Range(int linesBefore, int count) => count switch
    {
        0 => $"{linesBefore},0",
        1 => $"{linesBefore + 1}",
        _ => $"{linesBefore + 1},{count}"
    };

    private static int LastIndex(IReadOnlyList<UnifiedDiffRow> rows, bool left)
    {
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if ((left ? rows[i].LeftText : rows[i].RightText) != null)
                return i;
        }
        return -1;
    }
}
