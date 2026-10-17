using System.Diagnostics;
using System.Text;
using Codiff.Collections;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class FileDiffViewModel : ObservableObject
{
    /// <summary>
    /// Shared split-orientation preference.  Updated whenever any instance
    /// changes <see cref="IsHorizontalSplit"/>; new instances are initialised
    /// from it so that popup windows inherit the user's current preference.
    /// </summary>
    private static bool s_preferHorizontalSplit;

    private readonly FileDiffService _diffService = new();

    // Created once and shared by all lines, so subscribing to every line doesn't allocate a delegate per line.
    private readonly Action _onLeftTextEdited;
    private readonly Action _onRightTextEdited;

    public FileDiffViewModel()
    {
        // Start with the last-used orientation preference.
        _isHorizontalSplit = s_preferHorizontalSplit;
        _onLeftTextEdited = OnLeftTextEdited;
        _onRightTextEdited = OnRightTextEdited;
    }

    /// <summary>
    /// The diff lines. The instance never changes: loading a diff replaces the content with a
    /// single reset notification instead of one notification per line.
    /// </summary>
    public BulkObservableCollection<DiffLineViewModel> Lines { get; } = new();

    [ObservableProperty]
    private string _leftFilePath = string.Empty;

    [ObservableProperty]
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
    private bool _leftFileExists;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyAsTextCommand))]
    private bool _rightFileExists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SplitterOrientationMenuHeader))]
    [NotifyPropertyChangedFor(nameof(IsVerticalSplit))]
    private bool _isHorizontalSplit;

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

    private TextFileFormat _leftFormat = TextFileFormat.Default;
    private TextFileFormat _rightFormat = TextFileFormat.Default;

    /// <summary>Files selected with <see cref="SetPendingFiles"/> that have not been compared yet.</summary>
    private (string Left, string Right)? _pendingFiles;

    private IReadOnlyList<int> _hunkIndices = Array.Empty<int>();

    public string DiffCounterText => TotalDiffs > 0
        ? $"{(CurrentDiffIndex >= 0 ? CurrentDiffIndex + 1 : 0)} / {TotalDiffs}"
        : "No diffs";

    public bool HasDiffs => TotalDiffs > 0;

    /// <summary>Index of the first line of the current difference, or -1 when there is none.</summary>
    public int CurrentHunkLineIndex =>
        CurrentDiffIndex >= 0 && CurrentDiffIndex < _hunkIndices.Count ? _hunkIndices[CurrentDiffIndex] : -1;

    /// <summary>Compares two files. A path may be empty for a file that exists on one side only.</summary>
    public void LoadFiles(string leftPath, string rightPath) => LoadFiles(leftPath, rightPath, navigateToFirstDiff: true);

    private void LoadFiles(string leftPath, string rightPath, bool navigateToFirstDiff)
    {
        _pendingFiles = null;
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffFiles(leftPath, rightPath);
        ApplyResult(result, navigateToFirstDiff);
    }

    public void LoadTexts(string leftText, string rightText, string leftPath = "", string rightPath = "")
    {
        _pendingFiles = null;
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffTexts(leftText, rightText, leftPath, rightPath);
        ApplyResult(result);
    }

    /// <summary>
    /// Selects a file pair without comparing it yet. Paths and existence flags (and so the
    /// Copy as Text command) are updated immediately, but reading and diffing the files is
    /// deferred until the content is needed (see <see cref="EnsureLoaded"/>).
    /// Like <see cref="LoadFiles"/>, this discards unsaved edits of the previous pair.
    /// </summary>
    public void SetPendingFiles(string leftPath, string rightPath)
    {
        ApplyResult(new FileDiffResult
        {
            LeftFilePath = leftPath,
            RightFilePath = rightPath,
            LeftFileExists = !string.IsNullOrEmpty(leftPath) && File.Exists(leftPath),
            RightFileExists = !string.IsNullOrEmpty(rightPath) && File.Exists(rightPath)
        });
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        _pendingFiles = (leftPath, rightPath);
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

    private void ApplyResult(FileDiffResult result, bool navigateToFirstDiff = true)
    {
        _suppressTextEdited = true;

        _hunkIndices = result.DiffHunkIndices;
        TotalDiffs = _hunkIndices.Count;
        CurrentDiffIndex = TotalDiffs > 0 ? 0 : -1;

        var lines = new List<DiffLineViewModel>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            var vm = new DiffLineViewModel(line);
            vm.LeftTextEdited += _onLeftTextEdited;
            vm.RightTextEdited += _onRightTextEdited;
            lines.Add(vm);
        }
        Lines.ReplaceAll(lines);

        _isLeftModified = false;
        _isRightModified = false;
        LeftStatusText = string.Empty;
        RightStatusText = string.Empty;

        LeftFileExists = result.LeftFileExists;
        RightFileExists = result.RightFileExists;
        _leftFormat = result.LeftFormat;
        _rightFormat = result.RightFormat;

        _suppressTextEdited = false;

        OnPropertyChanged(nameof(DiffCounterText));
        OnPropertyChanged(nameof(HasDiffs));

        if (navigateToFirstDiff && CurrentDiffIndex >= 0)
            NavigateToCurrentHunk();
    }

    private void OnLeftTextEdited()
    {
        if (_suppressTextEdited) return;
        if (!_isLeftModified)
        {
            _isLeftModified = true;
            LeftStatusText = "● Modified";
        }
    }

    private void OnRightTextEdited()
    {
        if (_suppressTextEdited) return;
        if (!_isRightModified)
        {
            _isRightModified = true;
            RightStatusText = "● Modified";
        }
    }

    [RelayCommand]
    private void Save()
    {
        bool leftSaved = false;
        bool rightSaved = false;
        bool failed = false;

        if (_isLeftModified && !string.IsNullOrEmpty(LeftFilePath))
        {
            if (TryWriteFile(LeftFilePath, ReconstructText(isLeft: true, _leftFormat.NewLine), _leftFormat.Encoding))
            {
                _isLeftModified = false;
                LeftStatusText = "✔ Saved";
                leftSaved = true;
            }
            else
            {
                LeftStatusText = "✖ Save failed";
                failed = true;
            }
        }

        if (_isRightModified && !string.IsNullOrEmpty(RightFilePath))
        {
            if (TryWriteFile(RightFilePath, ReconstructText(isLeft: false, _rightFormat.NewLine), _rightFormat.Encoding))
            {
                _isRightModified = false;
                RightStatusText = "✔ Saved";
                rightSaved = true;
            }
            else
            {
                RightStatusText = "✖ Save failed";
                failed = true;
            }
        }

        // Reload the diff to reflect saved changes – unless a side failed to save, because
        // reloading would throw away its unsaved edits.
        if ((leftSaved || rightSaved) && !failed &&
            !string.IsNullOrEmpty(LeftFilePath) && !string.IsNullOrEmpty(RightFilePath))
        {
            // Stay where the user was editing instead of jumping to the first difference.
            LoadFiles(LeftFilePath, RightFilePath, navigateToFirstDiff: false);

            // Reloading clears the status texts; keep showing what was just saved.
            if (leftSaved) LeftStatusText = "✔ Saved";
            if (rightSaved) RightStatusText = "✔ Saved";
        }
    }

    private static bool TryWriteFile(string path, string text, Encoding encoding)
    {
        try
        {
            File.WriteAllText(path, text, encoding);
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

    [RelayCommand]
    private void ToggleSplitterOrientation() => IsHorizontalSplit = !IsHorizontalSplit;

    partial void OnIsHorizontalSplitChanged(bool value) => s_preferHorizontalSplit = value;

    [RelayCommand(CanExecute = nameof(CanCopyAsText))]
    private void CopyAsText()
    {
        if (!EnsureLoaded())
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

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void PrevDiff()
    {
        if (CurrentDiffIndex > 0)
        {
            CurrentDiffIndex--;
            NavigateToCurrentHunk();
        }
        OnPropertyChanged(nameof(DiffCounterText));
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
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
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoNext() => CurrentDiffIndex >= 0 && CurrentDiffIndex < TotalDiffs - 1;

    private void NavigateToCurrentHunk()
    {
        int lineIndex = CurrentHunkLineIndex;
        if (lineIndex >= 0)
            ScrollToLineRequested?.Invoke(lineIndex);
    }

    partial void OnCurrentDiffIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DiffCounterText));
        OnPropertyChanged(nameof(CurrentHunkLineIndex));
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalDiffsChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiffs));
        OnPropertyChanged(nameof(DiffCounterText));
    }
}
