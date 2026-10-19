using System.Diagnostics;
using System.Globalization;
using System.Text;
using Codiff.Models;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Registry;

namespace Codiff.Services;

/// <summary>A language syntax highlighting knows.</summary>
/// <param name="Name">Display name, e.g. "C#".</param>
/// <param name="ScopeName">TextMate scope of its grammar, e.g. "source.cs".</param>
public sealed record SyntaxLanguage(string Name, string ScopeName);

/// <summary>
/// Syntax highlighting with TextMate grammars, the ones VS Code uses (bundled by TextMateSharp,
/// about 60 languages). Tokens are reduced to a few categories (comment, keyword, string, ...)
/// that the view colors with GitHub's palette for the light or dark theme.
/// </summary>
public static class SyntaxGrammars
{
    /// <summary>Longer lines (e.g. minified files) are not highlighted, as in VS Code.</summary>
    public const int MaxLineLength = 20_000;

    /// <summary>Files larger than this (in characters) are not highlighted.</summary>
    public const int MaxTextLength = 10_000_000;

    // Lines that take longer than this to tokenize (pathological regular expressions) stop early.
    internal static readonly TimeSpan LineTimeLimit = TimeSpan.FromMilliseconds(500);

    // Files recognized by their name rather than their extension.
    private static readonly Dictionary<string, string> LanguageIdsByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = "dockerfile",
        ["Containerfile"] = "dockerfile",
        ["Makefile"] = "makefile",
        ["GNUmakefile"] = "makefile",
        [".gitignore"] = "ignore",
        [".gitattributes"] = "properties",
        [".editorconfig"] = "properties",
    };

    /// <summary>
    /// Independent copies of every grammar. A grammar tokenizes one line at a time, so the two sides
    /// of a comparison use different copies to be tokenized in parallel.
    /// </summary>
    public const int GrammarCopies = 2;

    private static readonly object Lock = new();
    private static RegistryOptions? s_options;
    private static readonly Registry?[] Registries = new Registry?[GrammarCopies];
    private static SyntaxCategory[] s_categoriesByColorId = [];
    private static bool s_unavailable;
    private static readonly Dictionary<(string Scope, int Copy), SyntaxGrammar?> Grammars = new();

    /// <summary>
    /// Maps TextMate scopes to <see cref="SyntaxCategory"/> values, following GitHub's color theme
    /// (github/vscode-theme). Each "color" is a category number: #0000NN is category NN.
    /// TextMateSharp ignores selectors with parent scopes ("markup.heading entity.name"), so the
    /// rules name the scopes themselves.
    /// </summary>
    private const string CategoryTheme = """
        {
          "name": "Codiff categories",
          "tokenColors": [
            { "settings": { "foreground": "#000000", "background": "#FFFFFF" } },
            { "scope": ["comment", "punctuation.definition.comment", "string.comment"], "settings": { "foreground": "#000001" } },
            { "scope": ["constant.other.placeholder", "constant.character"], "settings": { "foreground": "#000002" } },
            { "scope": ["constant", "entity.name.constant", "variable.other.constant", "variable.other.enummember", "variable.language", "entity"], "settings": { "foreground": "#000003" } },
            { "scope": ["entity.name", "meta.export.default", "meta.definition.variable"], "settings": { "foreground": "#000005" } },
            { "scope": ["variable.parameter.function", "meta.jsx.children", "meta.block", "meta.tag.attributes", "entity.name.constant", "meta.object.member", "meta.embedded.expression"], "settings": { "foreground": "#000000" } },
            { "scope": "entity.name.function", "settings": { "foreground": "#000004" } },
            { "scope": ["entity.name.tag", "support.class.component"], "settings": { "foreground": "#000006" } },
            { "scope": "keyword", "settings": { "foreground": "#000002" } },
            { "scope": ["storage", "storage.type"], "settings": { "foreground": "#000002" } },
            { "scope": ["storage.modifier.package", "storage.modifier.import", "storage.type.java"], "settings": { "foreground": "#000000" } },
            { "scope": "string", "settings": { "foreground": "#000007" } },
            { "scope": "support", "settings": { "foreground": "#000003" } },
            { "scope": "meta.property-name", "settings": { "foreground": "#000003" } },
            { "scope": "variable", "settings": { "foreground": "#000005" } },
            { "scope": "variable.other", "settings": { "foreground": "#000000" } },
            { "scope": ["invalid.broken", "invalid.deprecated", "invalid.illegal", "invalid.unimplemented", "message.error"], "settings": { "foreground": "#000008" } },
            { "scope": ["source.regexp", "string.regexp", "string.regexp.character-class"], "settings": { "foreground": "#000007" } },
            { "scope": ["support.constant", "support.variable", "meta.module-reference"], "settings": { "foreground": "#000003" } },
            { "scope": "support.type.property-name.json", "settings": { "foreground": "#000006" } },
            { "scope": "punctuation.definition.list.begin.markdown", "settings": { "foreground": "#000005" } },
            { "scope": ["markup.heading", "entity.name.section", "markup.inline.raw"], "settings": { "foreground": "#000003" } },
            { "scope": "markup.quote", "settings": { "foreground": "#000006" } },
            { "scope": ["markup.italic", "markup.bold"], "settings": { "foreground": "#000000" } },
            { "scope": ["markup.deleted", "meta.diff.header.from-file", "punctuation.definition.deleted"], "settings": { "foreground": "#000009" } },
            { "scope": "punctuation.section.embedded", "settings": { "foreground": "#000002" } },
            { "scope": ["markup.inserted", "meta.diff.header.to-file", "punctuation.definition.inserted"], "settings": { "foreground": "#00000A" } },
            { "scope": ["markup.changed", "punctuation.definition.changed"], "settings": { "foreground": "#000005" } },
            { "scope": ["markup.ignored", "markup.untracked"], "settings": { "foreground": "#000001" } },
            { "scope": "meta.diff.range", "settings": { "foreground": "#000004" } },
            { "scope": ["meta.diff.header", "meta.separator", "meta.output"], "settings": { "foreground": "#000003" } },
            { "scope": ["constant.other.reference.link", "string.other.link"], "settings": { "foreground": "#000007" } }
          ]
        }
        """;

    /// <summary>Loads the grammar registry ahead of time (called on a background thread at startup).</summary>
    public static void Preload()
    {
        lock (Lock)
            EnsureRegistry();
    }

    /// <summary>The language of a file, judged by its name; null when it is not known.</summary>
    public static SyntaxLanguage? FindLanguage(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        lock (Lock)
        {
            if (!EnsureRegistry())
                return null;

            try
            {
                var name = Path.GetFileName(path);
                if (!LanguageIdsByFileName.TryGetValue(name, out var id) &&
                    name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase))
                {
                    id = "dockerfile";
                }
                var language = id != null ? s_options!.GetAvailableLanguages().FirstOrDefault(l => l.Id == id) : null;

                var extension = Path.GetExtension(name);
                if (language == null && extension.Length > 1)
                    language = s_options!.GetLanguageByExtension(extension) ?? s_options.GetLanguageByExtension(extension.ToLowerInvariant());
                if (language == null)
                    return null;

                var scope = s_options!.GetScopeByLanguageId(language.Id);
                if (string.IsNullOrEmpty(scope))
                    return null;
                var displayName = language.Aliases is { Count: > 0 } aliases ? aliases[0] : language.Id;
                return new SyntaxLanguage(displayName, scope);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SyntaxGrammars] Language lookup for '{path}' failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// The grammar of a language, loaded on first use; null when it can't be loaded.
    /// <paramref name="copy"/> (0 to <see cref="GrammarCopies"/> - 1) selects an independent copy.
    /// </summary>
    public static SyntaxGrammar? GetGrammar(SyntaxLanguage language, int copy = 0)
    {
        lock (Lock)
        {
            if (Grammars.TryGetValue((language.ScopeName, copy), out var cached))
                return cached;
            if (!EnsureRegistry())
                return null;

            SyntaxGrammar? grammar = null;
            try
            {
                var registry = Registries[copy] ??= CreateRegistry(s_options!);
                if (registry.LoadGrammar(language.ScopeName) is { } loaded)
                    grammar = new SyntaxGrammar(language, loaded);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SyntaxGrammars] Loading the grammar '{language.ScopeName}' failed: {ex.Message}");
            }
            Grammars[(language.ScopeName, copy)] = grammar;
            return grammar;
        }
    }

    /// <summary>Category of a token color id of the category theme.</summary>
    internal static SyntaxCategory CategoryOf(int colorId)
    {
        var categories = s_categoriesByColorId;
        return (uint)colorId < (uint)categories.Length ? categories[colorId] : SyntaxCategory.Text;
    }

    private static Registry CreateRegistry(RegistryOptions options)
    {
        var registry = new Registry(options);
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(CategoryTheme)));
        registry.SetTheme(ThemeReader.ReadThemeSync(reader));
        return registry;
    }

    private static bool EnsureRegistry()
    {
        if (Registries[0] != null)
            return true;
        if (s_unavailable)
            return false;

        try
        {
            var options = new RegistryOptions(ThemeName.LightPlus);
            var registry = CreateRegistry(options);

            // Color ids start at 1; each theme color names a category (the same in every copy,
            // as all registries load the same theme).
            var theme = registry.GetTheme();
            int count = registry.GetColorMap().Count;
            var categories = new SyntaxCategory[count + 1];
            for (int id = 1; id <= count; id++)
            {
                var color = theme.GetColor(id);
                if (color is { Length: 7 } &&
                    int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value) &&
                    value <= (int)SyntaxCategory.Inserted)
                {
                    categories[id] = (SyntaxCategory)value;
                }
            }

            s_categoriesByColorId = categories;
            s_options = options;
            Registries[0] = registry;
            return true;
        }
        catch (Exception ex)
        {
            // E.g. the native regular expression library is missing for this platform:
            // everything still works, just without colors.
            Debug.WriteLine($"[SyntaxGrammars] Not available: {ex}");
            s_unavailable = true;
            return false;
        }
    }
}

/// <summary>A loaded grammar. Tokenizing is serialized, so one grammar can serve several threads.</summary>
public sealed class SyntaxGrammar
{
    private readonly IGrammar _grammar;
    private readonly object _lock = new();

    internal SyntaxGrammar(SyntaxLanguage language, IGrammar grammar)
    {
        Language = language;
        _grammar = grammar;
    }

    public SyntaxLanguage Language { get; }

    /// <summary>
    /// Tokenizes one line that starts in <paramref name="state"/> (null for the first line of a
    /// text) and returns its syntax and the state the next line starts in.
    /// </summary>
    public (LineSyntax Syntax, IStateStack? EndState) TokenizeLine(string line, IStateStack? state)
    {
        if (line.Length > SyntaxGrammars.MaxLineLength)
            return (new LineSyntax(line, []), state);

        ITokenizeLineResult2 result;
        try
        {
            lock (_lock)
                result = _grammar.TokenizeLine2(new LineText(line), state, SyntaxGrammars.LineTimeLimit);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SyntaxGrammars] Tokenizing failed: {ex.Message}");
            return (new LineSyntax(line, []), state);
        }

        var tokens = result.Tokens;
        var runs = new List<SyntaxRun>(tokens.Length / 2);
        for (int i = 0; i < tokens.Length; i += 2)
        {
            int start = tokens[i];
            int end = i + 2 < tokens.Length ? tokens[i + 2] : line.Length;
            end = Math.Min(end, line.Length);
            if (start >= end)
                continue;

            var category = SyntaxGrammars.CategoryOf(EncodedTokenAttributes.GetForeground(tokens[i + 1]));
            if (category == SyntaxCategory.Text)
                continue;

            if (runs.Count > 0 && runs[^1].Category == category && runs[^1].Start + runs[^1].Length == start)
                runs[^1] = runs[^1] with { Length = end - runs[^1].Start };
            else
                runs.Add(new SyntaxRun(start, end - start, category));
        }
        return (new LineSyntax(line, runs.ToArray()), result.RuleStack);
    }
}
