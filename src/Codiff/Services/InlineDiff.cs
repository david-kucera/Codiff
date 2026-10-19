using System.Globalization;
using Codiff.Models;

namespace Codiff.Services;

/// <summary>
/// Word-level comparison of two versions of a line: finds the words that changed, so they can be
/// highlighted inside the line (like GitHub and TortoiseMerge do).
/// </summary>
public static class InlineDiff
{
    /// <summary>
    /// Share of equal characters (indentation not counted) two lines need for their changed words
    /// to be highlighted. Below it the lines have little in common and highlighting would mark
    /// nearly everything.
    /// </summary>
    public const double MinHighlightSimilarity = 0.4;

    // Inputs longer than this are compared with xdiff's speed-ups instead of exactly.
    private const int MinimalDiffTokenLimit = 2000;

    private enum TokenKind : byte
    {
        Space,
        Word,
        Other
    }

    private readonly record struct Token(int Start, int Length, TokenKind Kind);

    /// <summary>
    /// Compares two lines word by word. Returns the pieces of both lines with the changed ones
    /// marked, or null when the lines are too different for that to be useful.
    /// </summary>
    public static (IReadOnlyList<DiffSpan> Left, IReadOnlyList<DiffSpan> Right)? Compare(string left, string right,
        DiffOptions options)
    {
        var tokensA = Tokenize(left);
        var tokensB = Tokenize(right);

        // With white space ignored, leading and trailing white space is not compared at all.
        var (a0, a1) = ComparedRange(tokensA, options);
        var (b0, b1) = ComparedRange(tokensB, options);

        var ids = new Dictionary<string, int>(options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var lookup = ids.GetAlternateLookup<ReadOnlySpan<char>>();
        int[] idsA = TokenIds(left, tokensA, a0, a1, lookup, options);
        int[] idsB = TokenIds(right, tokensB, b0, b1, lookup, options);

        var (changedA, changedB) = MyersDiff.Diff(idsA, idsB, ids.Count,
            minimal: idsA.Length + idsB.Length <= MinimalDiffTokenLimit);

        var segments = BuildSegments(changedA, changedB, a0, b0);
        MergeSmallEqualities(segments, left, tokensA, right, tokensB);

        // Dice coefficient of the equal characters; the indentation would make any two lines of a
        // block look alike, so it is not counted.
        int indentA = IndentTokens(tokensA), indentB = IndentTokens(tokensB);
        int equal = 0;
        foreach (var segment in segments)
        {
            if (!segment.Changed)
            {
                equal += CharLength(tokensA, Math.Max(segment.AStart, indentA), segment.AEnd) +
                         CharLength(tokensB, Math.Max(segment.BStart, indentB), segment.BEnd);
            }
        }
        int total = CharLength(tokensA, Math.Max(a0, indentA), a1) + CharLength(tokensB, Math.Max(b0, indentB), b1);
        if (total > 0 && equal < MinHighlightSimilarity * total)
            return null;

        return (BuildSpans(left, tokensA, segments, isLeft: true), BuildSpans(right, tokensB, segments, isLeft: false));
    }

    // ── Similarity of lines ────────────────────────────────────────────────

    /// <summary>The words and symbols of a line (white space left out), for <see cref="Similarity"/>.</summary>
    public sealed class LineProfile
    {
        // Hash and length of every token, sorted.
        internal readonly long[] Keys;
        internal readonly int Length;

        internal LineProfile(long[] keys, int length)
        {
            Keys = keys;
            Length = length;
        }
    }

    public static LineProfile Profile(string line, bool ignoreCase)
    {
        var tokens = Tokenize(line);
        var keys = new List<long>(tokens.Count);
        int length = 0;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Space)
                continue;
            int hash = string.GetHashCode(line.AsSpan(token.Start, token.Length), comparison);
            keys.Add(((long)hash << 32) | (uint)token.Length);
            length += token.Length;
        }
        var array = keys.ToArray();
        Array.Sort(array);
        return new LineProfile(array, length);
    }

    /// <summary>
    /// How much two lines have in common, from 0 to 1: the share of characters in words and
    /// symbols that occur in both lines (in any order). Cheap enough to compare every line of a
    /// changed block with every other one.
    /// </summary>
    public static double Similarity(LineProfile a, LineProfile b)
    {
        int total = a.Length + b.Length;
        if (total == 0)
            return 1;

        int common = 0;
        int i = 0, j = 0;
        var keysA = a.Keys;
        var keysB = b.Keys;
        while (i < keysA.Length && j < keysB.Length)
        {
            long ka = keysA[i], kb = keysB[j];
            if (ka == kb)
            {
                common += (int)(ka & 0xFFFFFFFF);
                i++;
                j++;
            }
            else if (ka < kb)
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        return 2.0 * common / total;
    }

    // ── Tokens ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a line into words (letters, digits, '_'), runs of white space and single other
    /// characters. CJK characters are single words, as these scripts don't separate words.
    /// </summary>
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            char c = text[i];
            TokenKind kind;
            if (char.IsWhiteSpace(c))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                kind = TokenKind.Space;
            }
            else if (IsWordChar(c))
            {
                while (i < text.Length && IsWordChar(text[i]))
                    i++;
                kind = TokenKind.Word;
            }
            else
            {
                i += char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                kind = TokenKind.Other;
            }
            tokens.Add(new Token(start, i - start, kind));
        }
        return tokens;
    }

    private static bool IsWordChar(char c)
    {
        if (c == '_')
            return true;
        if (c < 128)
            return char.IsAsciiLetterOrDigit(c);
        if (IsCjk(c))
            return false;
        return char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    private static bool IsCjk(char c) =>
        c is >= '⺀' and <= '鿿' or >= '가' and <= '힯' or >= '豈' and <= '﫿';

    private static (int Start, int End) ComparedRange(List<Token> tokens, DiffOptions options)
    {
        int start = 0, end = tokens.Count;
        if (options.IgnoreWhiteSpace)
        {
            while (start < end && tokens[start].Kind == TokenKind.Space)
                start++;
            while (end > start && tokens[end - 1].Kind == TokenKind.Space)
                end--;
        }
        return (start, end);
    }

    private static int[] TokenIds(string text, List<Token> tokens, int start, int end,
        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> lookup, DiffOptions options)
    {
        var ids = new int[end - start];
        for (int i = start; i < end; i++)
        {
            var token = tokens[i];
            // With white space ignored, any amount of white space between words is equal.
            var key = options.IgnoreWhiteSpace && token.Kind == TokenKind.Space
                ? " ".AsSpan()
                : text.AsSpan(token.Start, token.Length);
            if (!lookup.TryGetValue(key, out int id))
            {
                id = lookup.Dictionary.Count;
                lookup[key] = id;
            }
            ids[i - start] = id;
        }
        return ids;
    }

    private static int CountNonSpace(string text, List<Token> tokens, int start, int end)
    {
        int count = 0;
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Kind != TokenKind.Space)
                count += tokens[i].Length;
        }
        return count;
    }

    /// <summary>Number of leading white space tokens (0 or 1).</summary>
    private static int IndentTokens(List<Token> tokens) =>
        tokens.Count > 0 && tokens[0].Kind == TokenKind.Space ? 1 : 0;

    private static int CharLength(List<Token> tokens, int start, int end)
    {
        int length = 0;
        for (int i = start; i < end; i++)
            length += tokens[i].Length;
        return length;
    }

    // ── Segments ───────────────────────────────────────────────────────────

    /// <summary>Tokens [AStart, AEnd) of the left line against [BStart, BEnd) of the right line.</summary>
    private sealed class Segment
    {
        public bool Changed;
        public int AStart, AEnd, BStart, BEnd;
    }

    private static List<Segment> BuildSegments(bool[] changedA, bool[] changedB, int offsetA, int offsetB)
    {
        var segments = new List<Segment>();
        int i = 0, j = 0;
        while (i < changedA.Length || j < changedB.Length)
        {
            int si = i, sj = j;
            bool changed = !(i < changedA.Length && j < changedB.Length && !changedA[i] && !changedB[j]);
            if (changed)
            {
                while (i < changedA.Length && changedA[i])
                    i++;
                while (j < changedB.Length && changedB[j])
                    j++;
                if (i == si && j == sj)
                {
                    // Unpaired unchanged tokens can't occur; treat any leftovers as changed.
                    i = changedA.Length;
                    j = changedB.Length;
                }
            }
            else
            {
                while (i < changedA.Length && j < changedB.Length && !changedA[i] && !changedB[j])
                {
                    i++;
                    j++;
                }
            }

            if (segments.Count > 0 && segments[^1].Changed == changed)
            {
                segments[^1].AEnd = i + offsetA;
                segments[^1].BEnd = j + offsetB;
            }
            else
            {
                segments.Add(new Segment
                {
                    Changed = changed,
                    AStart = si + offsetA,
                    AEnd = i + offsetA,
                    BStart = sj + offsetB,
                    BEnd = j + offsetB
                });
            }
        }
        return segments;
    }

    /// <summary>
    /// Joins changes that are separated only by white space or a single symbol (e.g. "a.b" → "x.y"
    /// becomes one change instead of three), as long as the gap is not longer than the changes
    /// around it. This keeps the highlighting from breaking into confetti.
    /// </summary>
    private static void MergeSmallEqualities(List<Segment> segments, string left, List<Token> tokensA,
        string right, List<Token> tokensB)
    {
        int k = 1;
        while (k < segments.Count - 1)
        {
            var equal = segments[k];
            var before = segments[k - 1];
            var after = segments[k + 1];
            int equalLength = Math.Max(CharLength(tokensA, equal.AStart, equal.AEnd), CharLength(tokensB, equal.BStart, equal.BEnd));
            int beforeLength = Math.Max(CharLength(tokensA, before.AStart, before.AEnd), CharLength(tokensB, before.BStart, before.BEnd));
            int afterLength = Math.Max(CharLength(tokensA, after.AStart, after.AEnd), CharLength(tokensB, after.BStart, after.BEnd));

            if (!equal.Changed && CountNonSpace(left, tokensA, equal.AStart, equal.AEnd) <= 1 &&
                equalLength <= beforeLength && equalLength <= afterLength)
            {
                before.AEnd = after.AEnd;
                before.BEnd = after.BEnd;
                segments.RemoveRange(k, 2);
                // The equality before the joined change may qualify now.
                k = Math.Max(1, k - 2);
            }
            else
            {
                k++;
            }
        }
    }

    private static IReadOnlyList<DiffSpan> BuildSpans(string text, List<Token> tokens, List<Segment> segments, bool isLeft)
    {
        var changed = new bool[tokens.Count];
        foreach (var segment in segments)
        {
            if (!segment.Changed)
                continue;
            int start = isLeft ? segment.AStart : segment.BStart;
            int end = isLeft ? segment.AEnd : segment.BEnd;
            for (int i = start; i < end; i++)
                changed[i] = true;
        }

        var spans = new List<DiffSpan>();
        int t = 0;
        while (t < tokens.Count)
        {
            bool isChanged = changed[t];
            int start = tokens[t].Start;
            while (t < tokens.Count && changed[t] == isChanged)
                t++;
            int end = t < tokens.Count ? tokens[t].Start : text.Length;
            spans.Add(new DiffSpan { Text = text[start..end], IsChanged = isChanged });
        }
        return spans;
    }
}
