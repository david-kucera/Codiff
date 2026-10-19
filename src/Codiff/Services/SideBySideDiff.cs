using System.Text;
using Codiff.Models;

namespace Codiff.Services;

/// <summary>
/// Arranges the lines of two texts side by side. Equal lines are found the way git finds them
/// (see <see cref="MyersDiff"/>); inside a block of changed lines, lines that resemble each other
/// are put next to each other with their changed words marked, the remaining lines are paired in
/// order (as in GitHub's split view), and lines without a counterpart face an empty row.
/// </summary>
public static class SideBySideDiff
{
    /// <summary>Changed lines at least this similar (see <see cref="InlineDiff.Similarity"/>) are aligned with each other.</summary>
    private const double MinAlignSimilarity = 0.5;

    /// <summary>Blocks with more line pairs than this are paired in order, without looking for similar lines.</summary>
    private const long MaxAlignCells = 250_000;

    /// <summary>Compares two texts line by line.</summary>
    public static List<DiffLine> Compare(string leftText, string rightText, DiffOptions options)
    {
        var left = SplitLines(leftText);
        var right = SplitLines(rightText);

        var lineIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] idsLeft = LineIds(left, lineIds, options);
        int[] idsRight = LineIds(right, lineIds, options);
        var (changedLeft, changedRight) = MyersDiff.Diff(idsLeft, idsRight, lineIds.Count, Indents(left), Indents(right));

        var block = new Block(left, right, idsLeft, idsRight, options, new List<DiffLine>(Math.Max(left.Length, right.Length)));
        int i = 0, j = 0;
        while (i < left.Length || j < right.Length)
        {
            if (i < left.Length && j < right.Length && !changedLeft[i] && !changedRight[j])
            {
                block.Rows.Add(block.Unchanged(i, j));
                i++;
                j++;
                continue;
            }

            int i0 = i, j0 = j;
            while (i < left.Length && changedLeft[i])
                i++;
            while (j < right.Length && changedRight[j])
                j++;
            if (i == i0 && j == j0)
            {
                // Unchanged lines always come in pairs; should they not, show the rest as changed.
                i = left.Length;
                j = right.Length;
            }
            AddChangeBlock(block, i0, i, j0, j);
        }
        return block.Rows;
    }

    /// <summary>
    /// Splits a text into lines at "\r\n", "\r" and "\n". A text ending with a line break has an
    /// empty last line; an empty text has no lines.
    /// </summary>
    public static string[] SplitLines(string text)
    {
        if (text.Length == 0)
            return [];

        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r')
                continue;

            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }
        lines.Add(text.Substring(start));
        return lines.ToArray();
    }

    private static int[] LineIds(string[] lines, Dictionary<string, int> ids, DiffOptions options)
    {
        var result = new int[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var key = Normalize(lines[i], options);
            if (!ids.TryGetValue(key, out int id))
            {
                id = ids.Count;
                ids.Add(key, id);
            }
            result[i] = id;
        }
        return result;
    }

    /// <summary>The form in which lines are compared: with white space ignored, leading and
    /// trailing white space is dropped and every run of it becomes one space.</summary>
    private static string Normalize(string line, DiffOptions options)
    {
        if (options.IgnoreWhiteSpace)
            line = CollapseWhiteSpace(line);
        if (options.IgnoreCase)
            line = line.ToUpperInvariant();
        return line;
    }

    private static string CollapseWhiteSpace(string line)
    {
        var trimmed = line.AsSpan().Trim();
        bool needsWork = false;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (char.IsWhiteSpace(trimmed[i]) && (trimmed[i] != ' ' || char.IsWhiteSpace(trimmed[i + 1])))
            {
                needsWork = true;
                break;
            }
        }
        if (!needsWork)
            return trimmed.Length == line.Length ? line : trimmed.ToString();

        var result = new StringBuilder(trimmed.Length);
        bool inSpace = false;
        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inSpace)
                    result.Append(' ');
                inSpace = true;
            }
            else
            {
                result.Append(c);
                inSpace = false;
            }
        }
        return result.ToString();
    }

    private static int[] Indents(string[] lines)
    {
        var indents = new int[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            indents[i] = MyersDiff.GetIndent(lines[i]);
        return indents;
    }

    // ── Changed blocks ─────────────────────────────────────────────────────

    private sealed record Block(string[] Left, string[] Right, int[] IdsLeft, int[] IdsRight, DiffOptions Options,
        List<DiffLine> Rows)
    {
        public DiffLine Unchanged(int i, int j) => new()
        {
            LineType = LineType.Unchanged,
            LeftText = Left[i],
            RightText = Right[j],
            LeftLineNumber = i + 1,
            RightLineNumber = j + 1
        };

        /// <summary>Two different lines in one row, with their changed words marked when they are alike.</summary>
        public DiffLine Paired(int i, int j)
        {
            if (IdsLeft[i] == IdsRight[j])
                return Unchanged(i, j);

            var spans = InlineDiff.Compare(Left[i], Right[j], Options);
            return new DiffLine
            {
                LineType = LineType.Modified,
                LeftText = Left[i],
                RightText = Right[j],
                LeftLineNumber = i + 1,
                RightLineNumber = j + 1,
                LeftSpans = spans?.Left ?? Array.Empty<DiffSpan>(),
                RightSpans = spans?.Right ?? Array.Empty<DiffSpan>()
            };
        }

        public DiffLine Deleted(int i) => new()
        {
            LineType = LineType.Deleted,
            LeftText = Left[i],
            LeftLineNumber = i + 1
        };

        public DiffLine Added(int j) => new()
        {
            LineType = LineType.Added,
            RightText = Right[j],
            RightLineNumber = j + 1
        };
    }

    /// <summary>Adds the rows for left lines [l0, l1) that were replaced by right lines [r0, r1).</summary>
    private static void AddChangeBlock(Block block, int l0, int l1, int r0, int r1)
    {
        var anchors = l1 > l0 && r1 > r0 && (long)(l1 - l0) * (r1 - r0) <= MaxAlignCells
            ? AlignSimilarLines(block, l0, l1, r0, r1)
            : [];

        int i = l0, j = r0;
        foreach (var (left, right) in anchors)
        {
            AddInOrder(block, i, left, j, right);
            block.Rows.Add(block.Paired(left, right));
            i = left + 1;
            j = right + 1;
        }
        AddInOrder(block, i, l1, j, r1);
    }

    /// <summary>
    /// Pairs the lines in order; the surplus lines of the longer side get rows of their own
    /// (deleted lines first, as in a unified diff).
    /// </summary>
    private static void AddInOrder(Block block, int l0, int l1, int r0, int r1)
    {
        int pairs = Math.Min(l1 - l0, r1 - r0);
        for (int k = 0; k < pairs; k++)
            block.Rows.Add(block.Paired(l0 + k, r0 + k));
        for (int i = l0 + pairs; i < l1; i++)
            block.Rows.Add(block.Deleted(i));
        for (int j = r0 + pairs; j < r1; j++)
            block.Rows.Add(block.Added(j));
    }

    /// <summary>
    /// Chooses which left and right lines of a changed block belong together: the order-keeping
    /// pairing of similar lines with the highest total similarity (dynamic programming over all
    /// line pairs). Lines that are equal apart from ignored differences pair first.
    /// </summary>
    private static List<(int Left, int Right)> AlignSimilarLines(Block block, int l0, int l1, int r0, int r1)
    {
        int n = l1 - l0, m = r1 - r0;
        bool ignoreCase = block.Options.IgnoreCase;
        var profilesLeft = new InlineDiff.LineProfile[n];
        var profilesRight = new InlineDiff.LineProfile[m];
        for (int i = 0; i < n; i++)
            profilesLeft[i] = InlineDiff.Profile(block.Left[l0 + i], ignoreCase);
        for (int j = 0; j < m; j++)
            profilesRight[j] = InlineDiff.Profile(block.Right[r0 + j], ignoreCase);

        const byte SkipLeft = 0, SkipRight = 1, Pair = 2;
        int width = m + 1;
        var score = new double[(n + 1) * width];
        var move = new byte[(n + 1) * width];
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double best = score[(i - 1) * width + j];
                byte step = SkipLeft;
                if (score[i * width + j - 1] > best)
                {
                    best = score[i * width + j - 1];
                    step = SkipRight;
                }

                double similarity = block.IdsLeft[l0 + i - 1] == block.IdsRight[r0 + j - 1]
                    ? 2
                    : InlineDiff.Similarity(profilesLeft[i - 1], profilesRight[j - 1]);
                if (similarity >= MinAlignSimilarity && score[(i - 1) * width + j - 1] + similarity > best)
                {
                    best = score[(i - 1) * width + j - 1] + similarity;
                    step = Pair;
                }

                score[i * width + j] = best;
                move[i * width + j] = step;
            }
        }

        var anchors = new List<(int, int)>();
        int a = n, b = m;
        while (a > 0 && b > 0)
        {
            switch (move[a * width + b])
            {
                case Pair:
                    anchors.Add((l0 + a - 1, r0 + b - 1));
                    a--;
                    b--;
                    break;
                case SkipLeft:
                    a--;
                    break;
                default:
                    b--;
                    break;
            }
        }
        anchors.Reverse();
        return anchors;
    }
}
