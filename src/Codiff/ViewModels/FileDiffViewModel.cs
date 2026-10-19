using System.Diagnostics;
using System.Text;
using Codiff.Collections;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class FileDiffViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Line separator of the texts kept in memory and compared; files are written back with
    /// their own line endings (see <see cref="TextFileFormat"/>).
    /// </summary>
    private const string InternalNewLine = "\n";

    private const string ModifiedStatus = "● Modified";
    private const string SavedStatus = "✔ Saved";

    // Time spent tokenizing synchronously, so the first lines are colored when they appear; the
    // rest is done in the background.
    private static readonly TimeSpan SyntaxBudgetOnLoad = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan SyntaxBudgetOnRediff = TimeSpan.FromMilliseconds(25);

    private readonly FileDiffService _diffService = new();

    // Created once and shared by all lines, so subscribing to every line doesn't allocate a delegate per line.
    private readonly Action<DiffLineViewModel> _onLeftTextEdited;
    private readonly Action<DiffLineViewModel> _onRightTextEdited;
    private readonly Action<SyntaxSideHighlighter, int, int> _onSyntaxLinesChanged;

    public FileDiffViewModel()
    {
        _onLeftTextEdited = OnLeftTextEdited;
        _onRightTextEdited = OnRightTextEdited;
        _onSyntaxLinesChanged = OnSyntaxLinesChanged;

        // Start with the last-used preferences (popup windows inherit them too).
        var config = ConfigService.Instance.Config;
        _isHorizontalSplit = config.HorizontalSplit;
        _ignoreWhiteSpace = config.IgnoreWhiteSpace;
        _ignoreCase = config.IgnoreCase;
        _syntaxHighlighting = config.SyntaxHighlighting;
    }

    /// <summary>
    /// The diff lines. The instance never changes: loading a diff replaces the content with a
    /// single reset notification instead of one notification per line.
    /// </summary>
    public BulkObservableCollection<DiffLineViewModel> Lines { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecompareCommand), nameof(SwapCommand), nameof(CopyToLeftCommand))]
    private string _leftFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecompareCommand), nameof(SwapCommand), nameof(CopyToRightCommand))]
    private string _rightFilePath = string.Empty;

    [ObservableProperty]
    private int _currentDiffIndex = -1;

    [ObservableProperty]
    private int _totalDiffs;

    [ObservableProperty]
    private string _leftStatusText = string.Empty;

    [ObservableProperty]
    private string _rightStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyAsTextCommand))]
    [NotifyPropertyChangedFor(nameof(LeftFormatText))]
    private bool _leftFileExists;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyAsTextCommand))]
    [NotifyPropertyChangedFor(nameof(RightFormatText))]
    private bool _rightFileExists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SplitterOrientationMenuHeader))]
    [NotifyPropertyChangedFor(nameof(IsVerticalSplit))]
    private bool _isHorizontalSplit;

    /// <summary>Lines that differ only in white space are considered equal (persisted preference).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _ignoreWhiteSpace;

    /// <summary>Lines that differ only in letter case are considered equal (persisted preference).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _ignoreCase;

    /// <summary>Color the text by the syntax of the files' programming language (persisted preference).</summary>
    [ObservableProperty]
    private bool _syntaxHighlighting;

    /// <summary>True when at least one of the compared files is binary; there are no lines then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(BinaryMessage))]
    [NotifyCanExecuteChangedFor(nameof(CopyToLeftCommand), nameof(CopyToRightCommand))]
    private bool _isBinary;

    /// <summary>Number of lines that exist on both sides but differ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineStatsText))]
    private int _changedLineCount;

    /// <summary>Number of lines that exist only in the right file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineStatsText))]
    private int _addedLineCount;

    /// <summary>Number of lines that exist only in the left file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineStatsText))]
    private int _deletedLineCount;

    public string SplitterOrientationMenuHeader =>
        IsHorizontalSplit ? "Toggle to Vertical Split" : "Toggle to Horizontal Split";

    /// <summary>True when the split is vertical (side-by-side). Inverse of <see cref="IsHorizontalSplit"/>.</summary>
    public bool IsVerticalSplit => !IsHorizontalSplit;

    /// <summary>Raised when the view should copy the given text to the clipboard.</summary>
    public event Action<string>? CopyAsTextRequested;

    /// <summary>Raised when the view should scroll the line with the given index to the top.</summary>
    public event Action<int>? ScrollToLineRequested;

    private bool _isLeftModified;
    private bool _isRightModified;
    private bool _suppressTextEdited;
    private bool _binaryFilesIdentical;

    /// <summary>No differences and every line is exactly equal (not just equal when ignoring white space/case).</summary>
    private bool _textsIdentical;

    private TextFileFormat _leftFormat = TextFileFormat.Default;
    private TextFileFormat _rightFormat = TextFileFormat.Default;

    // Text of each side as last loaded or saved (lines joined with InternalNewLine); a side whose
    // current text differs from it has unsaved changes.
    private string _leftBaseline = string.Empty;
    private string _rightBaseline = string.Empty;

    /// <summary>Files selected with <see cref="SetPendingFiles"/> that have not been compared yet.</summary>
    private (string Left, string Right)? _pendingFiles;

    private IReadOnlyList<int> _hunkIndices = Array.Empty<int>();

    /// <summary>Line range currently flagged with <see cref="DiffLineViewModel.IsInCurrentHunk"/>.</summary>
    private (int Start, int End) _markedHunk = (-1, -1);

    /// <summary>Texts before each Copy to Right/Left, newest on top.</summary>
    private readonly Stack<UndoState> _undoStack = new();

    private readonly record struct UndoState(string LeftText, string RightText, int AnchorLine);

    public string DiffCounterText => TotalDiffs > 0
        ? $"{(CurrentDiffIndex >= 0 ? CurrentDiffIndex + 1 : 0)} / {TotalDiffs}"
        : "No diffs";

    public bool HasDiffs => TotalDiffs > 0;

    /// <summary>Index of the first line of the current difference, or -1 when there is none.</summary>
    public int CurrentHunkLineIndex =>
        CurrentDiffIndex >= 0 && CurrentDiffIndex < _hunkIndices.Count ? _hunkIndices[CurrentDiffIndex] : -1;

    /// <summary>Main status bar message: position among the differences, or the overall result.</summary>
    public string StatusText
    {
        get
        {
            if (_pendingFiles != null)
                return string.Empty;
            if (IsBinary)
                return _binaryFilesIdentical ? "Binary files are identical" : "Binary files differ";
            if (Lines.Count == 0 && !LeftFileExists && !RightFileExists)
                return string.Empty;
            if (TotalDiffs == 0)
                return _textsIdentical ? "Files are identical" : "Files differ only in ignored white space or case";
            return CurrentDiffIndex >= 0
                ? $"Difference {CurrentDiffIndex + 1} of {TotalDiffs}"
                : $"{TotalDiffs} differences";
        }
    }

    /// <summary>Line counts for the status bar, e.g. "3 changed · 1 added · 0 deleted lines".</summary>
    public string LineStatsText => TotalDiffs == 0 || IsBinary
        ? string.Empty
        : $"{ChangedLineCount} changed · {AddedLineCount} added · {DeletedLineCount} deleted lines";

    /// <summary>Encoding, line endings and highlighted language of the left file, e.g. "UTF-8 · CRLF · C#".</summary>
    public string LeftFormatText => FormatText(LeftFileExists, _leftFormat, _leftHighlighter);

    /// <summary>Encoding, line endings and highlighted language of the right file.</summary>
    public string RightFormatText => FormatText(RightFileExists, _rightFormat, _rightHighlighter);

    private string FormatText(bool fileExists, TextFileFormat format, SyntaxSideHighlighter? highlighter)
    {
        if (!fileExists || IsBinary)
            return string.Empty;
        return highlighter == null ? format.Description : $"{format.Description} · {highlighter.Language.Name}";
    }

    /// <summary>Message shown instead of the lines for binary files.</summary>
    public string BinaryMessage => IsBinary
        ? (_binaryFilesIdentical ? "The files are binary and identical." : "The files are binary and differ.")
        : string.Empty;

    private DiffOptions Options => new(IgnoreWhiteSpace, IgnoreCase);

    private bool HasComparison =>
        !string.IsNullOrEmpty(LeftFilePath) || !string.IsNullOrEmpty(RightFilePath) || Lines.Count > 0;

    // ── Loading ───────────────────────────────────────────────────────────

    /// <summary>Compares two files. A path may be empty for a file that exists on one side only.</summary>
    public void LoadFiles(string leftPath, string rightPath) => LoadFiles(leftPath, rightPath, navigateToFirstDiff: true);

    private void LoadFiles(string leftPath, string rightPath, bool navigateToFirstDiff, int anchorLine = -1)
    {
        _pendingFiles = null;
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffFiles(leftPath, rightPath, Options);
        ApplyLoadedResult(result, navigateToFirstDiff, anchorLine);
    }

    public void LoadTexts(string leftText, string rightText, string leftPath = "", string rightPath = "")
    {
        _pendingFiles = null;
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffTexts(leftText, rightText, leftPath, rightPath, Options);
        ApplyLoadedResult(result, navigateToFirstDiff: true);
    }

    /// <summary>
    /// Selects a file pair without comparing it yet. Paths and existence flags (and so the
    /// Copy as Text command) are updated immediately, but reading and diffing the files is
    /// deferred until the content is needed (see <see cref="EnsureLoaded"/>).
    /// Like <see cref="LoadFiles(string, string)"/>, this discards unsaved edits of the previous pair.
    /// </summary>
    public void SetPendingFiles(string leftPath, string rightPath)
    {
        ApplyLoadedResult(new FileDiffResult
        {
            LeftFilePath = leftPath,
            RightFilePath = rightPath,
            LeftFileExists = !string.IsNullOrEmpty(leftPath) && File.Exists(leftPath),
            RightFileExists = !string.IsNullOrEmpty(rightPath) && File.Exists(rightPath)
        }, navigateToFirstDiff: false);
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        _pendingFiles = (leftPath, rightPath);
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Compares the files selected with <see cref="SetPendingFiles"/>, if that hasn't happened yet.
    /// Returns <see langword="false"/> when they could not be read.
    /// </summary>
    public bool EnsureLoaded()
    {
        if (_pendingFiles is not { } pending)
            return true;

        try
        {
            LoadFiles(pending.Left, pending.Right);
            return true;
        }
        catch (Exception ex)
        {
            // Nothing was loaded, so keep the pair pending (Copy as Text must not copy an empty text).
            _pendingFiles = pending;
            Debug.WriteLine($"[FileDiffViewModel] Failed to load '{pending.Left}' / '{pending.Right}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Shows a freshly loaded comparison: no unsaved changes, no undo history.</summary>
    private void ApplyLoadedResult(FileDiffResult result, bool navigateToFirstDiff, int anchorLine = -1)
    {
        _undoStack.Clear();
        UndoCommand.NotifyCanExecuteChanged();

        _leftFormat = result.LeftFormat;
        _rightFormat = result.RightFormat;
        _binaryFilesIdentical = result.BinaryFilesIdentical;
        IsBinary = result.IsBinary;
        LeftFileExists = result.LeftFileExists;
        RightFileExists = result.RightFileExists;

        ShowDiff(result, anchorLine, SyntaxBudgetOnLoad);

        _leftBaseline = ReconstructText(isLeft: true, InternalNewLine);
        _rightBaseline = ReconstructText(isLeft: false, InternalNewLine);
        _isLeftModified = false;
        _isRightModified = false;
        LeftStatusText = string.Empty;
        RightStatusText = string.Empty;
        SaveCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(LeftFormatText));
        OnPropertyChanged(nameof(RightFormatText));

        if (navigateToFirstDiff && CurrentDiffIndex >= 0)
            NavigateToCurrentHunk();
    }

    /// <summary>
    /// Replaces the displayed lines with <paramref name="result"/>. The current difference becomes
    /// the first one starting at or after <paramref name="anchorLine"/> (the first one when it is -1).
    /// </summary>
    private void ShowDiff(FileDiffResult result, int anchorLine, TimeSpan syntaxBudget)
    {
        _suppressTextEdited = true;

        var lines = new List<DiffLineViewModel>(result.Lines.Count);
        int changed = 0, added = 0, deleted = 0;
        foreach (var line in result.Lines)
        {
            var vm = new DiffLineViewModel(line);
            vm.LeftTextEdited += _onLeftTextEdited;
            vm.RightTextEdited += _onRightTextEdited;
            lines.Add(vm);

            switch (line.LineType)
            {
                case LineType.Modified: changed++; break;
                case LineType.Added: added++; break;
                case LineType.Deleted: deleted++; break;
            }
        }

        _hunkIndices = result.DiffHunkIndices;
        _markedHunk = (-1, -1);
        Lines.ReplaceAll(lines);

        ChangedLineCount = changed;
        AddedLineCount = added;
        DeletedLineCount = deleted;
        _textsIdentical = _hunkIndices.Count == 0 && lines.TrueForAll(l => l.LeftText == l.RightText);
        TotalDiffs = _hunkIndices.Count;
        CurrentDiffIndex = anchorLine >= 0 ? HunkIndexAtOrAfter(anchorLine) : (TotalDiffs > 0 ? 0 : -1);
        UpdateCurrentHunkMarker();
        UpdateSyntaxHighlighting(syntaxBudget);

        _suppressTextEdited = false;

        OnPropertyChanged(nameof(DiffCounterText));
        OnPropertyChanged(nameof(HasDiffs));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LineStatsText));
        NotifyNavigationCommands();
    }

    /// <summary>
    /// Compares the given texts again with the current options, keeping paths, encodings, undo
    /// history and the unsaved state of each side.
    /// </summary>
    private void Rediff(string leftText, string rightText, int anchorLine, bool navigate)
    {
        var result = _diffService.DiffTexts(leftText, rightText, LeftFilePath, RightFilePath, Options);
        ShowDiff(result, anchorLine, SyntaxBudgetOnRediff);

        SetSideModified(isLeft: true, leftText != _leftBaseline);
        SetSideModified(isLeft: false, rightText != _rightBaseline);

        if (navigate && CurrentDiffIndex >= 0)
            NavigateToCurrentHunk();
    }

    /// <summary>The difference containing <paramref name="lineIndex"/>, else the next one, else the last one.</summary>
    private int HunkIndexAtOrAfter(int lineIndex)
    {
        if (_hunkIndices.Count == 0)
            return -1;
        for (int i = 0; i < _hunkIndices.Count; i++)
        {
            if (HunkEnd(_hunkIndices[i]) > lineIndex)
                return i;
        }
        return _hunkIndices.Count - 1;
    }

    /// <summary>Index just past the last line of the difference starting at <paramref name="start"/>.</summary>
    private int HunkEnd(int start)
    {
        int end = start;
        while (end < Lines.Count && Lines[end].LineType != LineType.Unchanged)
            end++;
        return end;
    }

    // ── Editing state ─────────────────────────────────────────────────────

    private void OnLeftTextEdited(DiffLineViewModel line)
    {
        if (_suppressTextEdited) return;
        SetSideModified(isLeft: true, modified: true);
        if (line.Model.LeftLineNumber is int number)
            _leftHighlighter?.UpdateLine(number - 1, line.LeftText);
    }

    private void OnRightTextEdited(DiffLineViewModel line)
    {
        if (_suppressTextEdited) return;
        SetSideModified(isLeft: false, modified: true);
        if (line.Model.RightLineNumber is int number)
            _rightHighlighter?.UpdateLine(number - 1, line.RightText);
    }

    private void SetSideModified(bool isLeft, bool modified)
    {
        if (isLeft)
        {
            if (_isLeftModified == modified) return;
            _isLeftModified = modified;
            LeftStatusText = modified ? ModifiedStatus : string.Empty;
        }
        else
        {
            if (_isRightModified == modified) return;
            _isRightModified = modified;
            RightStatusText = modified ? ModifiedStatus : string.Empty;
        }
        SaveCommand.NotifyCanExecuteChanged();
    }

    // ── Save ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        bool saved = false;

        var leftText = ReconstructText(isLeft: true, InternalNewLine);
        var rightText = ReconstructText(isLeft: false, InternalNewLine);

        if (_isLeftModified && !string.IsNullOrEmpty(LeftFilePath))
        {
            if (TryWriteFile(LeftFilePath, leftText, _leftFormat))
            {
                _leftBaseline = leftText;
                saved = true;
            }
            else
            {
                LeftStatusText = "✖ Save failed";
            }
        }

        if (_isRightModified && !string.IsNullOrEmpty(RightFilePath))
        {
            if (TryWriteFile(RightFilePath, rightText, _rightFormat))
            {
                _rightBaseline = rightText;
                saved = true;
            }
            else
            {
                RightStatusText = "✖ Save failed";
            }
        }

        if (!saved)
            return;

        bool leftSaved = _isLeftModified && leftText == _leftBaseline;
        bool rightSaved = _isRightModified && rightText == _rightBaseline;

        // Refresh the comparison (edits made in the panes are only diffed again now), staying
        // where the user was working; a side that failed to save keeps its edits.
        Rediff(leftText, rightText, CurrentHunkLineIndex, navigate: false);

        if (leftSaved) LeftStatusText = SavedStatus;
        if (rightSaved) RightStatusText = SavedStatus;
        LeftFileExists = !string.IsNullOrEmpty(LeftFilePath) && File.Exists(LeftFilePath);
        RightFileExists = !string.IsNullOrEmpty(RightFilePath) && File.Exists(RightFilePath);
    }

    private bool CanSave() => _isLeftModified || _isRightModified;

    private static bool TryWriteFile(string path, string text, TextFileFormat format)
    {
        try
        {
            // Texts are kept with InternalNewLine; write them back with the file's own line endings.
            var content = format.NewLine == InternalNewLine ? text : text.Replace(InternalNewLine, format.NewLine);
            File.WriteAllText(path, content, format.Encoding);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[FileDiffViewModel] Saving '{path}' failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Joins the lines of one side (filler lines skipped) with <paramref name="newLine"/>.</summary>
    private string ReconstructText(bool isLeft, string newLine)
    {
        var text = new StringBuilder();
        bool first = true;
        foreach (var line in Lines)
        {
            var lineNumber = isLeft ? line.Model.LeftLineNumber : line.Model.RightLineNumber;
            if (!lineNumber.HasValue)
                continue;

            if (!first)
                text.Append(newLine);
            text.Append(isLeft ? line.LeftText : line.RightText);
            first = false;
        }
        return text.ToString();
    }

    // ── Recompare / swap / options ────────────────────────────────────────

    /// <summary>
    /// Compares again. Files without unsaved changes are re-read from disk (they may have been
    /// changed by another program); sides with unsaved changes keep them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRecompare))]
    private void Recompare()
    {
        if (_pendingFiles != null)
        {
            EnsureLoaded();
            return;
        }

        int anchor = CurrentHunkLineIndex;
        bool hasPath = !string.IsNullOrEmpty(LeftFilePath) || !string.IsNullOrEmpty(RightFilePath);

        if (hasPath && !_isLeftModified && !_isRightModified)
        {
            try
            {
                LoadFiles(LeftFilePath, RightFilePath, navigateToFirstDiff: false, anchorLine: anchor);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[FileDiffViewModel] Recompare failed: {ex.Message}");
                LeftStatusText = "✖ Could not reload";
            }
            return;
        }

        var leftText = ReconstructText(isLeft: true, InternalNewLine);
        var rightText = ReconstructText(isLeft: false, InternalNewLine);
        if (!_isLeftModified)
            leftText = TryRereadSide(isLeft: true) ?? leftText;
        if (!_isRightModified)
            rightText = TryRereadSide(isLeft: false) ?? rightText;

        Rediff(leftText, rightText, anchor, navigate: false);
    }

    private bool CanRecompare() => HasComparison;

    /// <summary>Reads one side from disk again (updating its format and saved text); null when it can't be read.</summary>
    private string? TryRereadSide(bool isLeft)
    {
        var path = isLeft ? LeftFilePath : RightFilePath;
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            var content = FileDiffService.ReadFile(path);
            if (content.IsBinary)
                throw new IOException("The file is now binary.");

            var text = content.Text.Replace("\r\n", InternalNewLine).Replace('\r', '\n');
            var format = content.Format;
            if (isLeft)
            {
                _leftFormat = format;
                _leftBaseline = text;
                OnPropertyChanged(nameof(LeftFormatText));
            }
            else
            {
                _rightFormat = format;
                _rightBaseline = text;
                OnPropertyChanged(nameof(RightFormatText));
            }
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[FileDiffViewModel] Re-reading '{path}' failed: {ex.Message}");
            if (isLeft) LeftStatusText = "✖ Could not reload";
            else RightStatusText = "✖ Could not reload";
            return null;
        }
    }

    /// <summary>Exchanges the left and right side, including unsaved changes.</summary>
    [RelayCommand(CanExecute = nameof(CanRecompare))]
    private void Swap()
    {
        if (_pendingFiles is { } pending)
        {
            SetPendingFiles(pending.Right, pending.Left);
            return;
        }

        var leftText = ReconstructText(isLeft: true, InternalNewLine);
        var rightText = ReconstructText(isLeft: false, InternalNewLine);
        int anchor = CurrentHunkLineIndex;

        (LeftFilePath, RightFilePath) = (RightFilePath, LeftFilePath);
        (LeftFileExists, RightFileExists) = (RightFileExists, LeftFileExists);
        (_leftFormat, _rightFormat) = (_rightFormat, _leftFormat);
        (_leftBaseline, _rightBaseline) = (_rightBaseline, _leftBaseline);
        (_isLeftModified, _isRightModified) = (_isRightModified, _isLeftModified);
        (_leftHighlighter, _rightHighlighter) = (_rightHighlighter, _leftHighlighter);
        (LeftStatusText, RightStatusText) = (RightStatusText, LeftStatusText);
        OnPropertyChanged(nameof(LeftFormatText));
        OnPropertyChanged(nameof(RightFormatText));

        var undo = _undoStack.Reverse().Select(s => s with { LeftText = s.RightText, RightText = s.LeftText }).ToList();
        _undoStack.Clear();
        foreach (var state in undo)
            _undoStack.Push(state);

        if (!IsBinary)
            Rediff(rightText, leftText, anchor, navigate: false);
    }

    partial void OnIgnoreWhiteSpaceChanged(bool value)
    {
        ConfigService.Instance.Config.IgnoreWhiteSpace = value;
        ConfigService.Instance.Save();
        RediffWithCurrentOptions();
    }

    partial void OnIgnoreCaseChanged(bool value)
    {
        ConfigService.Instance.Config.IgnoreCase = value;
        ConfigService.Instance.Save();
        RediffWithCurrentOptions();
    }

    /// <summary>Applies changed compare options to the displayed comparison (unsaved edits are kept).</summary>
    private void RediffWithCurrentOptions()
    {
        if (_pendingFiles != null || IsBinary || Lines.Count == 0)
            return;

        Rediff(ReconstructText(isLeft: true, InternalNewLine), ReconstructText(isLeft: false, InternalNewLine),
            CurrentHunkLineIndex, navigate: false);
    }

    partial void OnSyntaxHighlightingChanged(bool value)
    {
        ConfigService.Instance.Config.SyntaxHighlighting = value;
        ConfigService.Instance.Save();
        UpdateSyntaxHighlighting(SyntaxBudgetOnLoad);
    }

    [RelayCommand]
    private void ToggleSplitterOrientation() => IsHorizontalSplit = !IsHorizontalSplit;

    partial void OnIsHorizontalSplitChanged(bool value)
    {
        ConfigService.Instance.Config.HorizontalSplit = value;
        ConfigService.Instance.Save();
    }

    // ── Syntax highlighting ───────────────────────────────────────────────

    private SyntaxSideHighlighter? _leftHighlighter;
    private SyntaxSideHighlighter? _rightHighlighter;

    // Row of each line of the left and of the right file.
    private int[] _leftLineRows = [];
    private int[] _rightLineRows = [];

    /// <summary>
    /// Gives the rows their syntax highlighting after they have been built. Lines that are the same
    /// as before keep their colors; the others are tokenized (partly right away, see
    /// <see cref="SyntaxSideHighlighter.SetLines"/>).
    /// </summary>
    private void UpdateSyntaxHighlighting(TimeSpan synchronousBudget)
    {
        var (leftLines, leftRows) = SideLines(isLeft: true);
        var (rightLines, rightRows) = SideLines(isLeft: false);
        _leftLineRows = leftRows;
        _rightLineRows = rightRows;

        // Each side tokenizes with its own copy of the grammar, so both can run at the same time.
        _leftHighlighter = PrepareHighlighter(_leftHighlighter, LeftFilePath, RightFilePath, leftLines, grammarCopy: 0);
        _rightHighlighter = PrepareHighlighter(_rightHighlighter, RightFilePath, LeftFilePath, rightLines, grammarCopy: 1);
        _leftHighlighter?.SetLines(leftLines, synchronousBudget);
        _rightHighlighter?.SetLines(rightLines, synchronousBudget);

        for (int i = 0; i < leftRows.Length; i++)
            Lines[leftRows[i]].LeftSyntax = _leftHighlighter?.GetLine(i);
        for (int i = 0; i < rightRows.Length; i++)
            Lines[rightRows[i]].RightSyntax = _rightHighlighter?.GetLine(i);

        OnPropertyChanged(nameof(LeftFormatText));
        OnPropertyChanged(nameof(RightFormatText));
    }

    /// <summary>
    /// The highlighter for one side: the current one while the language stays the same, else a new
    /// one. A file without a known language gets the other file's language (e.g. "a.cs" against
    /// "a.cs.orig"); null when there is nothing to highlight.
    /// </summary>
    private SyntaxSideHighlighter? PrepareHighlighter(SyntaxSideHighlighter? current, string path, string otherPath,
        string[] lines, int grammarCopy)
    {
        SyntaxLanguage? language = null;
        if (SyntaxHighlighting && !IsBinary && lines.Length > 0 && TextLength(lines) <= SyntaxGrammars.MaxTextLength)
            language = SyntaxGrammars.FindLanguage(path) ?? SyntaxGrammars.FindLanguage(otherPath);

        if (current != null && current.Language == language)
            return current;

        current?.Stop();
        if (language == null || SyntaxGrammars.GetGrammar(language, grammarCopy) is not { } grammar)
            return null;
        return new SyntaxSideHighlighter(grammar, _onSyntaxLinesChanged);
    }

    private static long TextLength(string[] lines)
    {
        long length = 0;
        foreach (var line in lines)
            length += line.Length + 1;
        return length;
    }

    /// <summary>Lines of one side and the rows they are shown in.</summary>
    private (string[] Lines, int[] Rows) SideLines(bool isLeft)
    {
        var lines = new List<string>(Lines.Count);
        var rows = new List<int>(Lines.Count);
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (isLeft ? line.HasLeftLine : line.HasRightLine)
            {
                lines.Add(isLeft ? line.LeftText : line.RightText);
                rows.Add(i);
            }
        }
        return (lines.ToArray(), rows.ToArray());
    }

    private void OnSyntaxLinesChanged(SyntaxSideHighlighter highlighter, int first, int count)
    {
        bool isLeft = highlighter == _leftHighlighter;
        if (!isLeft && highlighter != _rightHighlighter)
            return;

        var rows = isLeft ? _leftLineRows : _rightLineRows;
        int end = Math.Min(first + count, rows.Length);
        for (int i = first; i < end; i++)
        {
            var line = Lines[rows[i]];
            if (isLeft)
                line.LeftSyntax = highlighter.GetLine(i);
            else
                line.RightSyntax = highlighter.GetLine(i);
        }
    }

    /// <summary>Stops background work (syntax highlighting) when the comparison is closed.</summary>
    public void Dispose()
    {
        _leftHighlighter?.Stop();
        _rightHighlighter?.Stop();
        _leftHighlighter = null;
        _rightHighlighter = null;
    }

    // ── Merging ───────────────────────────────────────────────────────────

    /// <summary>Replaces the current difference in the right file with the left file's lines.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyToRight))]
    private void CopyToRight() => CopyCurrentDifference(toRight: true);

    /// <summary>Replaces the current difference in the left file with the right file's lines.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyToLeft))]
    private void CopyToLeft() => CopyCurrentDifference(toRight: false);

    private bool CanCopyToRight() => CurrentHunkLineIndex >= 0 && !IsBinary && !string.IsNullOrEmpty(RightFilePath);

    private bool CanCopyToLeft() => CurrentHunkLineIndex >= 0 && !IsBinary && !string.IsNullOrEmpty(LeftFilePath);

    private void CopyCurrentDifference(bool toRight)
    {
        var (start, end) = CurrentHunkRange();
        if (start < 0)
            return;

        var leftText = ReconstructText(isLeft: true, InternalNewLine);
        var rightText = ReconstructText(isLeft: false, InternalNewLine);
        _undoStack.Push(new UndoState(leftText, rightText, start));
        UndoCommand.NotifyCanExecuteChanged();

        var merged = BuildMergedText(toRight, start, end);
        if (toRight)
            rightText = merged;
        else
            leftText = merged;

        // The copied difference disappears; continue with the next one.
        Rediff(leftText, rightText, anchorLine: start, navigate: true);
    }

    /// <summary>
    /// Text of the target side with the lines of the difference [<paramref name="start"/>,
    /// <paramref name="end"/>) taken from the other side.
    /// </summary>
    private string BuildMergedText(bool toRight, int start, int end)
    {
        var text = new StringBuilder();
        bool first = true;
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            bool inDifference = i >= start && i < end;
            // Outside the difference keep the target's own lines; inside take the source's lines.
            bool useLeft = inDifference ? toRight : !toRight;
            var lineNumber = useLeft ? line.Model.LeftLineNumber : line.Model.RightLineNumber;
            if (!lineNumber.HasValue)
                continue;

            if (!first)
                text.Append(InternalNewLine);
            text.Append(useLeft ? line.LeftText : line.RightText);
            first = false;
        }
        return text.ToString();
    }

    /// <summary>Line range [Start, End) of the current difference, or (-1, -1).</summary>
    private (int Start, int End) CurrentHunkRange()
    {
        int start = CurrentHunkLineIndex;
        if (start < 0 || start >= Lines.Count)
            return (-1, -1);
        return (start, HunkEnd(start));
    }

    /// <summary>Undoes the last Copy to Right/Left.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!_undoStack.TryPop(out var state))
            return;
        UndoCommand.NotifyCanExecuteChanged();
        Rediff(state.LeftText, state.RightText, state.AnchorLine, navigate: true);
    }

    private bool CanUndo() => _undoStack.Count > 0;

    // ── Unified diff ──────────────────────────────────────────────────────

    /// <summary>
    /// The differences in the unified diff format (diff -u, git, patch), with the current edits
    /// and ignore options. Empty when there are no differences.
    /// </summary>
    public string CreateUnifiedDiff()
    {
        if (!EnsureLoaded() || IsBinary)
            return string.Empty;

        var rows = Lines.Select(line => new UnifiedDiffRow(
            line.LineType,
            line.Model.LeftLineNumber.HasValue ? line.LeftText : null,
            line.Model.RightLineNumber.HasValue ? line.RightText : null)).ToList();
        return UnifiedDiffWriter.Write(rows, DiffLabel(LeftFilePath), DiffLabel(RightFilePath),
            _leftFormat.NewLine, _rightFormat.NewLine);
    }

    /// <summary>File name for the diff header; a side without a file is /dev/null, as in diff -u and git.</summary>
    private static string DiffLabel(string path) =>
        string.IsNullOrEmpty(path) ? "/dev/null" : path.Replace('\\', '/');

    // ── Copy as text / explorer ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanCopyAsText))]
    private void CopyAsText()
    {
        if (!EnsureLoaded() || IsBinary)
            return;

        var text = ReconstructText(isLeft: true, Environment.NewLine);
        if (string.IsNullOrEmpty(text))
            text = ReconstructText(isLeft: false, Environment.NewLine);
        CopyAsTextRequested?.Invoke(text);
    }

    private bool CanCopyAsText() => LeftFileExists || RightFileExists;

    [RelayCommand]
    private void OpenInExplorer(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        FileManagerService.RevealFile(filePath);
    }

    // ── Navigation ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoFirst))]
    private void FirstDiff()
    {
        CurrentDiffIndex = 0;
        NavigateToCurrentHunk();
    }

    private bool CanGoFirst() => TotalDiffs > 0 && CurrentDiffIndex != 0;

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void PrevDiff()
    {
        if (CurrentDiffIndex > 0)
        {
            CurrentDiffIndex--;
            NavigateToCurrentHunk();
        }
        OnPropertyChanged(nameof(DiffCounterText));
        NotifyNavigationCommands();
    }

    private bool CanGoPrev() => CurrentDiffIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDiff()
    {
        if (CurrentDiffIndex < TotalDiffs - 1)
        {
            CurrentDiffIndex++;
            NavigateToCurrentHunk();
        }
        OnPropertyChanged(nameof(DiffCounterText));
        NotifyNavigationCommands();
    }

    private bool CanGoNext() => CurrentDiffIndex >= 0 && CurrentDiffIndex < TotalDiffs - 1;

    [RelayCommand(CanExecute = nameof(CanGoLast))]
    private void LastDiff()
    {
        CurrentDiffIndex = TotalDiffs - 1;
        NavigateToCurrentHunk();
    }

    private bool CanGoLast() => TotalDiffs > 0 && CurrentDiffIndex != TotalDiffs - 1;

    private void NavigateToCurrentHunk()
    {
        int lineIndex = CurrentHunkLineIndex;
        if (lineIndex >= 0)
            ScrollToLineRequested?.Invoke(lineIndex);
    }

    /// <summary>
    /// Scrolls to the line with the given 1-based number in the left or right file (or to the
    /// first line after it, when that line is not shown). Returns false when there is no such line.
    /// </summary>
    public bool GoToLine(int lineNumber, bool isLeft)
    {
        EnsureLoaded();
        for (int i = 0; i < Lines.Count; i++)
        {
            var number = isLeft ? Lines[i].Model.LeftLineNumber : Lines[i].Model.RightLineNumber;
            if (number >= lineNumber)
            {
                ScrollToLineRequested?.Invoke(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds the next line containing <paramref name="text"/> on one side, starting after (or, when
    /// searching backwards, before) the line <paramref name="fromIndex"/> and wrapping around.
    /// Returns the line index and the position of the match, or (-1, -1).
    /// </summary>
    public (int LineIndex, int Position) Find(string text, bool isLeft, int fromIndex, bool forward, bool matchCase)
    {
        EnsureLoaded();
        int count = Lines.Count;
        if (string.IsNullOrEmpty(text) || count == 0)
            return (-1, -1);

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int step = 1; step <= count; step++)
        {
            int i = forward
                ? (fromIndex + step) % count
                : ((fromIndex - step) % count + count) % count;
            var line = Lines[i];
            if ((isLeft ? line.Model.LeftLineNumber : line.Model.RightLineNumber) == null)
                continue;

            int position = (isLeft ? line.LeftText : line.RightText).IndexOf(text, comparison);
            if (position >= 0)
                return (i, position);
        }
        return (-1, -1);
    }

    partial void OnCurrentDiffIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DiffCounterText));
        OnPropertyChanged(nameof(CurrentHunkLineIndex));
        OnPropertyChanged(nameof(StatusText));
        UpdateCurrentHunkMarker();
        NotifyNavigationCommands();
    }

    partial void OnTotalDiffsChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiffs));
        OnPropertyChanged(nameof(DiffCounterText));
    }

    /// <summary>Flags the lines of the current difference so the view can mark them.</summary>
    private void UpdateCurrentHunkMarker()
    {
        var range = CurrentHunkRange();
        if (range == _markedHunk)
            return;

        for (int i = Math.Max(0, _markedHunk.Start); i < Math.Min(_markedHunk.End, Lines.Count); i++)
            Lines[i].IsInCurrentHunk = false;
        for (int i = Math.Max(0, range.Start); i < range.End; i++)
            Lines[i].IsInCurrentHunk = true;
        _markedHunk = range;
    }

    private void NotifyNavigationCommands()
    {
        FirstDiffCommand.NotifyCanExecuteChanged();
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
        LastDiffCommand.NotifyCanExecuteChanged();
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
        RecompareCommand.NotifyCanExecuteChanged();
        SwapCommand.NotifyCanExecuteChanged();
    }
}
