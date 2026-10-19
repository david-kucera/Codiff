using System.Diagnostics;
using Avalonia.Threading;
using Codiff.Models;
using TextMateSharp.Grammars;

namespace Codiff.Services;

/// <summary>
/// Keeps the syntax highlighting of one side of a comparison up to date. Each line starts in the
/// state the previous one ended in, so lines are tokenized in order, on a background thread, and
/// handed to the UI thread in batches. When lines change, only the changed lines and the lines
/// after them up to where the state is the same as before are tokenized again; until then the
/// old highlighting stays visible.
/// </summary>
/// <remarks>All public members must be called on the UI thread.</remarks>
public sealed class SyntaxSideHighlighter
{
    // Results are handed over after this many lines or this much time, whichever comes first.
    private const int BatchSize = 500;
    private static readonly TimeSpan BatchTime = TimeSpan.FromMilliseconds(40);

    private readonly SyntaxGrammar _grammar;
    private readonly Action<SyntaxSideHighlighter, int, int> _linesChanged;

    private string[] _lines = [];
    private LineSyntax?[] _syntax = [];

    // State after each line; known for the lines [0, _validCount), whose syntax is up to date.
    private IStateStack?[] _endStates = [];
    private int _validCount;

    // State after each line as it was before the latest change (null where unknown). When the
    // new tokenization reaches the same state, the rest of the old highlighting is still right.
    private IStateStack?[] _previousEndStates = [];

    private int _generation;
    private CancellationTokenSource? _cancellation;

    /// <param name="grammar">Grammar of the side's language.</param>
    /// <param name="linesChanged">Called with (this, first line, line count) when lines got new highlighting.</param>
    public SyntaxSideHighlighter(SyntaxGrammar grammar, Action<SyntaxSideHighlighter, int, int> linesChanged)
    {
        _grammar = grammar;
        _linesChanged = linesChanged;
    }

    public SyntaxLanguage Language => _grammar.Language;

    /// <summary>Highlighting of a line, or null when it has not been tokenized yet.</summary>
    public LineSyntax? GetLine(int index) => (uint)index < (uint)_syntax.Length ? _syntax[index] : null;

    /// <summary>
    /// Replaces all lines (a new or recompared text). Lines at the start and the end that are the
    /// same as before keep their highlighting. Up to <paramref name="synchronousBudget"/> is spent
    /// tokenizing right away, so the first lines show colors immediately; the rest follows in the
    /// background.
    /// </summary>
    public void SetLines(string[] lines, TimeSpan synchronousBudget)
    {
        int oldCount = _lines.Length, newCount = lines.Length;
        int common = Math.Min(oldCount, newCount);
        int prefix = 0;
        while (prefix < common && string.Equals(_lines[prefix], lines[prefix], StringComparison.Ordinal))
            prefix++;
        int suffix = 0;
        while (suffix < common - prefix &&
               string.Equals(_lines[oldCount - 1 - suffix], lines[newCount - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var syntax = new LineSyntax?[newCount];
        var endStates = new IStateStack?[newCount];
        var previousEndStates = new IStateStack?[newCount];
        Array.Copy(_syntax, syntax, prefix);
        Array.Copy(_endStates, endStates, Math.Min(prefix, _validCount));
        for (int k = 0; k < suffix; k++)
        {
            int oldIndex = oldCount - 1 - k, newIndex = newCount - 1 - k;
            syntax[newIndex] = _syntax[oldIndex];
            previousEndStates[newIndex] = oldIndex < _validCount ? _endStates[oldIndex] : null;
        }

        _lines = lines;
        _syntax = syntax;
        _endStates = endStates;
        _previousEndStates = previousEndStates;
        _validCount = Math.Min(_validCount, prefix);
        Continue(synchronousBudget);
    }

    /// <summary>A line was edited: it gets its new highlighting right away, the following lines if needed in the background.</summary>
    public void UpdateLine(int index, string text)
    {
        if ((uint)index >= (uint)_lines.Length)
            return;

        _lines[index] = text;
        // The old state after this line says nothing about the new text.
        _previousEndStates[index] = null;
        if (index >= _validCount)
        {
            // The running tokenization has not got this far, but it still has the old text.
            Continue(TimeSpan.Zero);
            return;
        }

        var startState = index == 0 ? null : _endStates[index - 1];
        var (syntax, endState) = _grammar.TokenizeLine(text, startState);
        _syntax[index] = syntax;
        var oldEndState = _endStates[index];
        _endStates[index] = endState;
        _linesChanged(this, index, 1);

        if (Equals(endState, oldEndState))
            return;

        // The next lines start in another state now (e.g. a comment was opened). They keep their
        // highlighting until it has been redone, as far as the state differs from before.
        var previous = new IStateStack?[_lines.Length];
        Array.Copy(_endStates, index + 1, previous, index + 1, _validCount - index - 1);
        _previousEndStates = previous;
        _validCount = index + 1;
        Continue(TimeSpan.Zero);
    }

    /// <summary>Stops the background work (the comparison is closed or highlighting turned off).</summary>
    public void Stop()
    {
        _generation++;
        _cancellation?.Cancel();
        _cancellation = null;
    }

    /// <summary>Tokenizes the lines from <see cref="_validCount"/> on: some right away, the rest in the background.</summary>
    private void Continue(TimeSpan synchronousBudget)
    {
        Stop();
        int generation = _generation;
        int start = _validCount;
        var state = start == 0 ? null : _endStates[start - 1];

        if (synchronousBudget > TimeSpan.Zero && start < _lines.Length)
        {
            var stopwatch = Stopwatch.StartNew();
            var syntax = new List<LineSyntax>();
            var endStates = new List<IStateStack?>();
            bool converged = false;
            for (int i = start; i < _lines.Length && stopwatch.Elapsed < synchronousBudget; i++)
            {
                var (lineSyntax, endState) = _grammar.TokenizeLine(_lines[i], state);
                syntax.Add(lineSyntax);
                endStates.Add(endState);
                state = endState;
                if (HasConverged(_previousEndStates, i, endState))
                {
                    converged = true;
                    break;
                }
            }
            Apply(generation, start, syntax, endStates, converged);
            if (converged || _validCount >= _lines.Length)
                return;
            start = _validCount;
            state = start == 0 ? null : _endStates[start - 1];
        }

        if (start >= _lines.Length)
            return;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var lines = _lines[start..];
        var previousEndStates = _previousEndStates[start..];
        Task.Run(() => TokenizeInBackground(generation, start, state, lines, previousEndStates, cancellation.Token));
    }

    private static bool HasConverged(IStateStack?[] previousEndStates, int index, IStateStack? endState) =>
        previousEndStates[index] is { } previous && Equals(previous, endState);

    private void TokenizeInBackground(int generation, int start, IStateStack? state, string[] lines,
        IStateStack?[] previousEndStates, CancellationToken cancellation)
    {
        var syntax = new List<LineSyntax>(BatchSize);
        var endStates = new List<IStateStack?>(BatchSize);
        int batchStart = start;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < lines.Length; i++)
        {
            if (cancellation.IsCancellationRequested)
                return;

            var (lineSyntax, endState) = _grammar.TokenizeLine(lines[i], state);
            syntax.Add(lineSyntax);
            endStates.Add(endState);
            state = endState;

            bool converged = HasConverged(previousEndStates, i, endState);
            if (converged || syntax.Count >= BatchSize || stopwatch.Elapsed >= BatchTime || i == lines.Length - 1)
            {
                var batchSyntax = syntax;
                var batchEndStates = endStates;
                int first = batchStart;
                Dispatcher.UIThread.Post(() => Apply(generation, first, batchSyntax, batchEndStates, converged));
                if (converged)
                    return;

                syntax = new List<LineSyntax>(BatchSize);
                endStates = new List<IStateStack?>(BatchSize);
                batchStart = start + i + 1;
                stopwatch.Restart();
            }
        }
    }

    private void Apply(int generation, int start, List<LineSyntax> syntax, List<IStateStack?> endStates, bool converged)
    {
        if (generation != _generation || start != _validCount)
            return;

        for (int k = 0; k < syntax.Count; k++)
        {
            _syntax[start + k] = syntax[k];
            _endStates[start + k] = endStates[k];
        }
        _validCount = start + syntax.Count;
        if (syntax.Count > 0)
            _linesChanged(this, start, syntax.Count);

        if (converged)
        {
            // The rest is the same as before the change: take over the old states.
            int i = _validCount;
            while (i < _lines.Length && _previousEndStates[i] is { } previous)
            {
                _endStates[i] = previous;
                i++;
            }
            _validCount = i;
            _previousEndStates = new IStateStack?[_lines.Length];
            if (_validCount < _lines.Length && generation == _generation)
                Continue(TimeSpan.Zero);
        }
    }
}
