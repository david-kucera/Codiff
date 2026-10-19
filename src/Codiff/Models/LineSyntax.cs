namespace Codiff.Models;

/// <summary>Kind of a piece of source code; it decides the color (see <c>SyntaxPalette</c>).</summary>
public enum SyntaxCategory : byte
{
    /// <summary>Everything else (identifiers, punctuation): the normal text color.</summary>
    Text,
    Comment,
    /// <summary>Keywords, storage modifiers and operators.</summary>
    Keyword,
    /// <summary>Numbers, constants, built-in names, attribute names.</summary>
    Constant,
    /// <summary>Function and method names.</summary>
    Function,
    /// <summary>Declared names (types, variables) and variables.</summary>
    Variable,
    /// <summary>Markup tags and keys of JSON objects.</summary>
    Tag,
    String,
    Invalid,
    /// <summary>Removed lines of a diff/patch file.</summary>
    Deleted,
    /// <summary>Added lines of a diff/patch file.</summary>
    Inserted
}

/// <summary>Characters [Start, Start + Length) of a line that belong to one category.</summary>
public readonly record struct SyntaxRun(int Start, int Length, SyntaxCategory Category);

/// <summary>
/// Syntax highlighting of one line: the runs of characters that are not plain text, and the
/// text they were found in (once the line is edited they no longer apply).
/// </summary>
public sealed class LineSyntax(string text, SyntaxRun[] runs)
{
    public string Text { get; } = text;

    public IReadOnlyList<SyntaxRun> Runs { get; } = runs;
}
