namespace Codiff.Services;

/// <summary>
/// Finds the elements that differ between two sequences the way git does. This is a port of
/// git's xdiff (xprepare.c, xdiffi.c): the Myers algorithm with xdiff's speed-ups for long
/// inputs, followed by xdiff's change compaction, which moves every block of changes to the
/// most natural position (with git's "indent heuristic" for lines). Line comparisons therefore
/// show the same changes as git and GitHub.
/// </summary>
/// <remarks>
/// Elements are given as ids: equal ids are equal elements. Ids must be small non-negative
/// numbers (as handed out by an interning dictionary), because they index count arrays.
/// </remarks>
public static class MyersDiff
{
    // Limits and factors of xdiff (git/xdiff/xdiffi.c, xprepare.c).
    private const int MaxCostMin = 256;
    private const int HeuristicMinCost = 256;
    private const int SnakeCount = 20;
    private const int HeuristicFactor = 4;
    private const int MaxEqualLimit = 1024;
    private const int SimilarScanWindow = 100;
    private const int DiscardRunFactor = 4;
    private const int LineMax = int.MaxValue;

    // Indent heuristic (git/xdiff/xdiffi.c).
    private const int MaxIndent = 200;
    private const int MaxBlanks = 20;
    private const int StartOfFilePenalty = 1;
    private const int EndOfFilePenalty = 21;
    private const int TotalBlankWeight = -30;
    private const int PostBlankWeight = 6;
    private const int RelativeIndentPenalty = -4;
    private const int RelativeIndentWithBlankPenalty = 10;
    private const int RelativeOutdentPenalty = 24;
    private const int RelativeOutdentWithBlankPenalty = 17;
    private const int RelativeDedentPenalty = 23;
    private const int RelativeDedentWithBlankPenalty = 17;
    private const int IndentWeight = 60;
    private const int IndentHeuristicMaxSliding = 100;

    // Result of cleanup_records for one element.
    private const byte Discard = 0;
    private const byte Keep = 1;
    private const byte Investigate = 2;

    /// <summary>
    /// Marks the elements of <paramref name="a"/> and <paramref name="b"/> that are not part of
    /// their common subsequence.
    /// </summary>
    /// <param name="a">Ids of the first sequence.</param>
    /// <param name="b">Ids of the second sequence.</param>
    /// <param name="idCount">Upper bound (exclusive) of the ids.</param>
    /// <param name="indentA">
    /// Indentation of each line of <paramref name="a"/> (see <see cref="GetIndent"/>) for the indent
    /// heuristic; null for sequences that are not lines of text.
    /// </param>
    /// <param name="indentB">Indentation of each line of <paramref name="b"/>, or null.</param>
    /// <param name="minimal">Always find a minimal difference, even when that takes long.</param>
    public static (bool[] ChangedA, bool[] ChangedB) Diff(int[] a, int[] b, int idCount,
        int[]? indentA = null, int[]? indentB = null, bool minimal = false)
    {
        var file1 = new Sequence(a, indentA);
        var file2 = new Sequence(b, indentB);

        TrimEnds(file1, file2);
        CleanupRecords(file1, file2, idCount, minimal);

        int diagonals = file1.ReducedCount + file2.ReducedCount + 3;
        var kvd = new int[2 * diagonals + 2];
        int forwardOffset = file2.ReducedCount + 1;
        int backwardOffset = diagonals + file2.ReducedCount + 1;
        int maxCost = Math.Max(BogoSqrt(diagonals), MaxCostMin);
        CompareRecords(file1, file2, kvd, forwardOffset, backwardOffset, minimal, maxCost);

        ChangeCompact(file1, file2);
        ChangeCompact(file2, file1);

        return (file1.ToChangedArray(), file2.ToChangedArray());
    }

    /// <summary>
    /// Indentation of a line for the indent heuristic: tabs count up to the next multiple of 8,
    /// -1 for a blank line (git's get_indent).
    /// </summary>
    public static int GetIndent(string line)
    {
        int indent = 0;
        foreach (char c in line)
        {
            if (!IsSpace(c))
                return indent;
            if (c == ' ')
                indent += 1;
            else if (c == '\t')
                indent += 8 - indent % 8;
            // other white space characters are ignored

            if (indent >= MaxIndent)
                return MaxIndent;
        }
        return -1;
    }

    // C isspace() in the "C" locale.
    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private static int BogoSqrt(int n)
    {
        int i = 1;
        for (; n > 0; n >>= 2)
            i <<= 1;
        return i;
    }

    private sealed class Sequence
    {
        public readonly int[] Ids;
        public readonly int[]? Indent;

        // changed flag of element i is at index i + 1; the extra first and last entries stay
        // false, so loops over runs of changes need no bounds checks (as in xdiff).
        private readonly bool[] _changed;

        /// <summary>Elements that take part in the Myers search (the others are known to be changed).</summary>
        public int[] ReducedIds = [];
        public int[] ReducedIndex = [];
        public int ReducedCount;

        // First and last element after the common prefix and suffix.
        public int DStart;
        public int DEnd;

        public Sequence(int[] ids, int[]? indent)
        {
            Ids = ids;
            Indent = indent;
            _changed = new bool[ids.Length + 2];
        }

        public int Count => Ids.Length;

        public bool IsChanged(int i) => _changed[i + 1];

        public void SetChanged(int i, bool value) => _changed[i + 1] = value;

        public bool[] ToChangedArray() => _changed[1..^1];
    }

    // ── Preparation (xprepare.c) ───────────────────────────────────────────

    private static void TrimEnds(Sequence file1, Sequence file2)
    {
        int lim = Math.Min(file1.Count, file2.Count);
        int i = 0;
        while (i < lim && file1.Ids[i] == file2.Ids[i])
            i++;
        file1.DStart = file2.DStart = i;

        lim -= i;
        int j = 0;
        while (j < lim && file1.Ids[file1.Count - 1 - j] == file2.Ids[file2.Count - 1 - j])
            j++;
        file1.DEnd = file1.Count - j - 1;
        file2.DEnd = file2.Count - j - 1;
    }

    /// <summary>
    /// Elements without a match in the other sequence can't be part of the common subsequence
    /// and are marked as changed right away. Elements with very many matches are also left out
    /// when they sit among such elements (xdl_cleanup_records).
    /// </summary>
    private static void CleanupRecords(Sequence file1, Sequence file2, int idCount, bool minimal)
    {
        var count1 = new int[idCount];
        var count2 = new int[idCount];
        foreach (int id in file1.Ids)
            count1[id]++;
        foreach (int id in file2.Ids)
            count2[id]++;

        Classify(file1, count2, minimal);
        Classify(file2, count1, minimal);
    }

    private static void Classify(Sequence file, int[] otherCounts, bool minimal)
    {
        int offset = file.DStart;
        int length = Math.Max(0, file.DEnd - offset + 1);
        int limit = minimal ? int.MaxValue : Math.Min(BogoSqrt(file.Count), MaxEqualLimit);

        var action = new byte[length];
        for (int i = 0; i < length; i++)
        {
            int matches = otherCounts[file.Ids[i + offset]];
            action[i] = matches == 0 ? Discard : matches < limit ? Keep : Investigate;
        }

        file.ReducedIds = new int[length];
        file.ReducedIndex = new int[length];
        int count = 0;
        for (int i = 0; i < length; i++)
        {
            byte result = action[i];
            if (result == Investigate)
                result = CleanMultiMatch(action, i, length) ? Discard : Keep;

            if (result == Keep)
            {
                file.ReducedIndex[count] = i + offset;
                file.ReducedIds[count] = file.Ids[i + offset];
                count++;
            }
            else
            {
                file.SetChanged(i + offset, true);
            }
        }
        file.ReducedCount = count;
    }

    /// <summary>
    /// Whether a multi-match element should be left out: only when it sits in a run consisting
    /// mostly of elements without a match (xdl_clean_mmatch).
    /// </summary>
    private static bool CleanMultiMatch(byte[] action, int i, int length)
    {
        int s = 0, e = length - 1;
        if (i - s > SimilarScanWindow)
            s = i - SimilarScanWindow;
        if (e - i > SimilarScanWindow)
            e = i + SimilarScanWindow;

        int discardsBefore = 0, multiBefore = 1;
        for (int r = 1; i - r >= s; r++)
        {
            if (action[i - r] == Discard)
                discardsBefore++;
            else if (action[i - r] == Investigate)
                multiBefore++;
            else
                break;
        }
        if (discardsBefore == 0)
            return false;

        int discardsAfter = 0, multiAfter = 1;
        for (int r = 1; i + r <= e; r++)
        {
            if (action[i + r] == Discard)
                discardsAfter++;
            else if (action[i + r] == Investigate)
                multiAfter++;
            else
                break;
        }
        if (discardsAfter == 0)
            return false;

        int discards = discardsAfter + discardsBefore;
        int multi = multiAfter + multiBefore;
        return multi * DiscardRunFactor < multi + discards;
    }

    // ── Myers algorithm (xdiffi.c) ─────────────────────────────────────────

    private struct SplitPoint
    {
        public int I1, I2;
        public bool MinLow, MinHigh;
    }

    /// <summary>
    /// Divide and conquer: splits the box (off1, lim1) × (off2, lim2) at its middle snake until a
    /// side is empty, and marks the elements of the other side as changed (xdl_recs_cmp). Uses an
    /// explicit stack instead of recursion.
    /// </summary>
    private static void CompareRecords(Sequence file1, Sequence file2, int[] kvd, int forwardOffset,
        int backwardOffset, bool minimal, int maxCost)
    {
        var ha1 = file1.ReducedIds;
        var ha2 = file2.ReducedIds;
        var boxes = new Stack<(int Off1, int Lim1, int Off2, int Lim2, bool NeedMin)>();
        boxes.Push((0, file1.ReducedCount, 0, file2.ReducedCount, minimal));

        while (boxes.Count > 0)
        {
            var (off1, lim1, off2, lim2, needMin) = boxes.Pop();

            // Shrink the box by walking through each diagonal snake (SW and NE).
            while (off1 < lim1 && off2 < lim2 && ha1[off1] == ha2[off2])
            {
                off1++;
                off2++;
            }
            while (off1 < lim1 && off2 < lim2 && ha1[lim1 - 1] == ha2[lim2 - 1])
            {
                lim1--;
                lim2--;
            }

            // If one dimension is empty, all elements of the other one are changed.
            if (off1 == lim1)
            {
                for (; off2 < lim2; off2++)
                    file2.SetChanged(file2.ReducedIndex[off2], true);
            }
            else if (off2 == lim2)
            {
                for (; off1 < lim1; off1++)
                    file1.SetChanged(file1.ReducedIndex[off1], true);
            }
            else
            {
                var split = Split(ha1, off1, lim1, ha2, off2, lim2, kvd, forwardOffset, backwardOffset,
                    needMin, maxCost);
                boxes.Push((split.I1, lim1, split.I2, lim2, split.MinHigh));
                boxes.Push((off1, split.I1, off2, split.I2, split.MinLow));
            }
        }
    }

    /// <summary>
    /// Finds the middle snake of the box, searching forward from (off1, off2) and backward from
    /// (lim1, lim2) at the same time. Long searches are cut short with xdiff's heuristics,
    /// which return a good (not necessarily optimal) split point (xdl_split).
    /// </summary>
    private static SplitPoint Split(int[] ha1, int off1, int lim1, int[] ha2, int off2, int lim2,
        int[] kvd, int fo, int bo, bool needMin, int maxCost)
    {
        int dmin = off1 - lim2, dmax = lim1 - off2;
        int fmid = off1 - off2, bmid = lim1 - lim2;
        bool odd = ((fmid - bmid) & 1) != 0;
        int fmin = fmid, fmax = fmid;
        int bmin = bmid, bmax = bmid;
        SplitPoint split = default;

        kvd[fo + fmid] = off1;
        kvd[bo + bmid] = lim1;

        for (int ec = 1; ; ec++)
        {
            bool gotSnake = false;

            // Extend the forward diagonal domain by one (or shrink it at the box boundary).
            if (fmin > dmin)
                kvd[fo + --fmin - 1] = -1;
            else
                ++fmin;
            if (fmax < dmax)
                kvd[fo + ++fmax + 1] = -1;
            else
                --fmax;

            for (int d = fmax; d >= fmin; d -= 2)
            {
                int i1 = kvd[fo + d - 1] >= kvd[fo + d + 1] ? kvd[fo + d - 1] + 1 : kvd[fo + d + 1];
                int prev1 = i1;
                int i2 = i1 - d;
                while (i1 < lim1 && i2 < lim2 && ha1[i1] == ha2[i2])
                {
                    i1++;
                    i2++;
                }
                if (i1 - prev1 > SnakeCount)
                    gotSnake = true;
                kvd[fo + d] = i1;
                if (odd && bmin <= d && d <= bmax && kvd[bo + d] <= i1)
                {
                    split.I1 = i1;
                    split.I2 = i2;
                    split.MinLow = split.MinHigh = true;
                    return split;
                }
            }

            // The same for the backward diagonal domain.
            if (bmin > dmin)
                kvd[bo + --bmin - 1] = LineMax;
            else
                ++bmin;
            if (bmax < dmax)
                kvd[bo + ++bmax + 1] = LineMax;
            else
                --bmax;

            for (int d = bmax; d >= bmin; d -= 2)
            {
                int i1 = kvd[bo + d - 1] < kvd[bo + d + 1] ? kvd[bo + d - 1] : kvd[bo + d + 1] - 1;
                int prev1 = i1;
                int i2 = i1 - d;
                while (i1 > off1 && i2 > off2 && ha1[i1 - 1] == ha2[i2 - 1])
                {
                    i1--;
                    i2--;
                }
                if (prev1 - i1 > SnakeCount)
                    gotSnake = true;
                kvd[bo + d] = i1;
                if (!odd && fmin <= d && d <= fmax && i1 <= kvd[fo + d])
                {
                    split.I1 = i1;
                    split.I2 = i2;
                    split.MinLow = split.MinHigh = true;
                    return split;
                }
            }

            if (needMin)
                continue;

            // Above the heuristic trigger, after a good snake: take a diagonal that has come far
            // enough (distance from the corner, penalized by the distance from the middle diagonal).
            if (gotSnake && ec > HeuristicMinCost)
            {
                int best = 0;
                for (int d = fmax; d >= fmin; d -= 2)
                {
                    int dd = d > fmid ? d - fmid : fmid - d;
                    int i1 = kvd[fo + d];
                    int i2 = i1 - d;
                    int v = (i1 - off1) + (i2 - off2) - dd;

                    if (v > HeuristicFactor * ec && v > best &&
                        off1 + SnakeCount <= i1 && i1 < lim1 &&
                        off2 + SnakeCount <= i2 && i2 < lim2)
                    {
                        for (int k = 1; ha1[i1 - k] == ha2[i2 - k]; k++)
                        {
                            if (k == SnakeCount)
                            {
                                best = v;
                                split.I1 = i1;
                                split.I2 = i2;
                                break;
                            }
                        }
                    }
                }
                if (best > 0)
                {
                    split.MinLow = true;
                    split.MinHigh = false;
                    return split;
                }

                best = 0;
                for (int d = bmax; d >= bmin; d -= 2)
                {
                    int dd = d > bmid ? d - bmid : bmid - d;
                    int i1 = kvd[bo + d];
                    int i2 = i1 - d;
                    int v = (lim1 - i1) + (lim2 - i2) - dd;

                    if (v > HeuristicFactor * ec && v > best &&
                        off1 < i1 && i1 <= lim1 - SnakeCount &&
                        off2 < i2 && i2 <= lim2 - SnakeCount)
                    {
                        for (int k = 0; ha1[i1 + k] == ha2[i2 + k]; k++)
                        {
                            if (k == SnakeCount - 1)
                            {
                                best = v;
                                split.I1 = i1;
                                split.I2 = i2;
                                break;
                            }
                        }
                    }
                }
                if (best > 0)
                {
                    split.MinLow = false;
                    split.MinHigh = true;
                    return split;
                }
            }

            // Enough is enough: take the furthest reaching path (by i1 + i2).
            if (ec >= maxCost)
            {
                int fbest = -1, fbest1 = -1;
                for (int d = fmax; d >= fmin; d -= 2)
                {
                    int i1 = Math.Min(kvd[fo + d], lim1);
                    int i2 = i1 - d;
                    if (lim2 < i2)
                    {
                        i1 = lim2 + d;
                        i2 = lim2;
                    }
                    if (fbest < i1 + i2)
                    {
                        fbest = i1 + i2;
                        fbest1 = i1;
                    }
                }

                long bbest = LineMax;
                int bbest1 = LineMax;
                for (int d = bmax; d >= bmin; d -= 2)
                {
                    int i1 = Math.Max(off1, kvd[bo + d]);
                    int i2 = i1 - d;
                    if (i2 < off2)
                    {
                        i1 = off2 + d;
                        i2 = off2;
                    }
                    if (i1 + i2 < bbest)
                    {
                        bbest = i1 + i2;
                        bbest1 = i1;
                    }
                }

                if ((lim1 + lim2) - bbest < fbest - (off1 + off2))
                {
                    split.I1 = fbest1;
                    split.I2 = fbest - fbest1;
                    split.MinLow = true;
                    split.MinHigh = false;
                }
                else
                {
                    split.I1 = bbest1;
                    split.I2 = (int)bbest - bbest1;
                    split.MinLow = false;
                    split.MinHigh = true;
                }
                return split;
            }
        }
    }

    // ── Change compaction (xdiffi.c) ───────────────────────────────────────

    /// <summary>
    /// A run of changed elements: [Start, End). Between any two unchanged elements (and at both
    /// ends) there is a group, usually an empty one.
    /// </summary>
    private struct Group
    {
        public int Start;
        public int End;
    }

    private static void GroupInit(Sequence file, ref Group g)
    {
        g.Start = g.End = 0;
        while (file.IsChanged(g.End))
            g.End++;
    }

    private static bool GroupNext(Sequence file, ref Group g)
    {
        if (g.End == file.Count)
            return false;
        g.Start = g.End + 1;
        for (g.End = g.Start; file.IsChanged(g.End); g.End++)
        {
        }
        return true;
    }

    private static bool GroupPrevious(Sequence file, ref Group g)
    {
        if (g.Start == 0)
            return false;
        g.End = g.Start - 1;
        for (g.Start = g.End; file.IsChanged(g.Start - 1); g.Start--)
        {
        }
        return true;
    }

    /// <summary>Moves the group one element down when that is possible; it then may merge with the next group.</summary>
    private static bool GroupSlideDown(Sequence file, ref Group g)
    {
        if (g.End < file.Count && file.Ids[g.Start] == file.Ids[g.End])
        {
            file.SetChanged(g.Start++, false);
            file.SetChanged(g.End++, true);
            while (file.IsChanged(g.End))
                g.End++;
            return true;
        }
        return false;
    }

    /// <summary>Moves the group one element up when that is possible; it then may merge with the previous group.</summary>
    private static bool GroupSlideUp(Sequence file, ref Group g)
    {
        if (g.Start > 0 && file.Ids[g.Start - 1] == file.Ids[g.End - 1])
        {
            file.SetChanged(--g.Start, true);
            file.SetChanged(--g.End, false);
            while (file.IsChanged(g.Start - 1))
                g.Start--;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Slides every group of changes of <paramref name="file"/> up and down as far as possible,
    /// merging groups that touch, and then settles it where it lines up with a change in the
    /// other sequence, or else at the position the indent heuristic likes best (xdl_change_compact).
    /// </summary>
    private static void ChangeCompact(Sequence file, Sequence other)
    {
        Group g = default, go = default;
        GroupInit(file, ref g);
        GroupInit(other, ref go);

        while (true)
        {
            if (g.End != g.Start)
            {
                int groupSize, earliestEnd, endMatchingOther;
                do
                {
                    groupSize = g.End - g.Start;

                    // The last end index at which this group lines up with a group of changes in
                    // the other sequence; -1 while there is none.
                    endMatchingOther = -1;

                    // Shift the group up as far as possible...
                    while (GroupSlideUp(file, ref g))
                    {
                        if (!GroupPrevious(other, ref go))
                            throw new InvalidOperationException("Group sync broken sliding up.");
                    }

                    earliestEnd = g.End;
                    if (go.End > go.Start)
                        endMatchingOther = g.End;

                    // ...and then down as far as possible.
                    while (GroupSlideDown(file, ref g))
                    {
                        if (!GroupNext(other, ref go))
                            throw new InvalidOperationException("Group sync broken sliding down.");
                        if (go.End > go.Start)
                            endMatchingOther = g.End;
                    }
                }
                while (groupSize != g.End - g.Start);

                // The group is at its lowest position now; only upward shifts remain to decide.
                if (g.End == earliestEnd)
                {
                    // It can't be shifted.
                }
                else if (endMatchingOther != -1)
                {
                    // Line it up with the last group of changes of the other sequence it can align with.
                    while (go.End == go.Start)
                    {
                        if (!GroupSlideUp(file, ref g))
                            throw new InvalidOperationException("Match disappeared.");
                        if (!GroupPrevious(other, ref go))
                            throw new InvalidOperationException("Group sync broken sliding to match.");
                    }
                }
                else if (file.Indent != null)
                {
                    // Indent heuristic: score the two splits the group makes at every position
                    // it can be shifted to, and take the best one.
                    int shift = earliestEnd;
                    if (g.End - groupSize - 1 > shift)
                        shift = g.End - groupSize - 1;
                    if (g.End - IndentHeuristicMaxSliding > shift)
                        shift = g.End - IndentHeuristicMaxSliding;

                    int bestShift = -1;
                    SplitScore bestScore = default;
                    for (; shift <= g.End; shift++)
                    {
                        var score = default(SplitScore);
                        ScoreAddSplit(MeasureSplit(file, shift), ref score);
                        ScoreAddSplit(MeasureSplit(file, shift - groupSize), ref score);
                        if (bestShift == -1 || ScoreCompare(score, bestScore) <= 0)
                        {
                            bestScore = score;
                            bestShift = shift;
                        }
                    }

                    while (g.End > bestShift)
                    {
                        if (!GroupSlideUp(file, ref g))
                            throw new InvalidOperationException("Best shift unreached.");
                        if (!GroupPrevious(other, ref go))
                            throw new InvalidOperationException("Group sync broken sliding to blank line.");
                    }
                }
            }

            // Move past the just-processed group.
            if (!GroupNext(file, ref g))
                break;
            if (!GroupNext(other, ref go))
                throw new InvalidOperationException("Group sync broken moving to next group.");
        }
    }

    private struct SplitMeasurement
    {
        public bool EndOfFile;
        public int Indent;
        public int PreBlank;
        public int PreIndent;
        public int PostBlank;
        public int PostIndent;
    }

    private struct SplitScore
    {
        public int EffectiveIndent;
        public int Penalty;
    }

    /// <summary>Characteristics of a hypothetical split of the lines above line <paramref name="split"/>.</summary>
    private static SplitMeasurement MeasureSplit(Sequence file, int split)
    {
        var indent = file.Indent!;
        var m = new SplitMeasurement();

        if (split >= file.Count)
        {
            m.EndOfFile = true;
            m.Indent = -1;
        }
        else
        {
            m.Indent = indent[split];
        }

        m.PreIndent = -1;
        for (int i = split - 1; i >= 0; i--)
        {
            m.PreIndent = indent[i];
            if (m.PreIndent != -1)
                break;
            m.PreBlank++;
            if (m.PreBlank == MaxBlanks)
            {
                m.PreIndent = 0;
                break;
            }
        }

        m.PostIndent = -1;
        for (int i = split + 1; i < file.Count; i++)
        {
            m.PostIndent = indent[i];
            if (m.PostIndent != -1)
                break;
            m.PostBlank++;
            if (m.PostBlank == MaxBlanks)
            {
                m.PostIndent = 0;
                break;
            }
        }

        return m;
    }

    /// <summary>Adds the badness of a split to <paramref name="s"/> (git's empirically tuned weights).</summary>
    private static void ScoreAddSplit(in SplitMeasurement m, ref SplitScore s)
    {
        if (m.PreIndent == -1 && m.PreBlank == 0)
            s.Penalty += StartOfFilePenalty;

        if (m.EndOfFile)
            s.Penalty += EndOfFilePenalty;

        // Blank lines following the split, including the line right after it.
        int postBlank = m.Indent == -1 ? 1 + m.PostBlank : 0;
        int totalBlank = m.PreBlank + postBlank;

        s.Penalty += TotalBlankWeight * totalBlank;
        s.Penalty += PostBlankWeight * postBlank;

        int indent = m.Indent != -1 ? m.Indent : m.PostIndent;
        bool anyBlanks = totalBlank != 0;

        // The effective indent is -1 at the end of the file.
        s.EffectiveIndent += indent;

        if (indent == -1 || m.PreIndent == -1)
        {
            // No additional adjustments needed.
        }
        else if (indent > m.PreIndent)
        {
            // Indented more than its predecessor.
            s.Penalty += anyBlanks ? RelativeIndentWithBlankPenalty : RelativeIndentPenalty;
        }
        else if (indent == m.PreIndent)
        {
            // Same indentation as its predecessor.
        }
        else if (m.PostIndent != -1 && m.PostIndent > indent)
        {
            // Indented less than its predecessor and the next line is indented more: likely
            // the start of a block.
            s.Penalty += anyBlanks ? RelativeOutdentWithBlankPenalty : RelativeOutdentPenalty;
        }
        else
        {
            // Probably the end of a block.
            s.Penalty += anyBlanks ? RelativeDedentWithBlankPenalty : RelativeDedentPenalty;
        }
    }

    private static int ScoreCompare(in SplitScore s1, in SplitScore s2)
    {
        int compareIndents = s1.EffectiveIndent.CompareTo(s2.EffectiveIndent);
        return IndentWeight * compareIndents + (s1.Penalty - s2.Penalty);
    }
}
