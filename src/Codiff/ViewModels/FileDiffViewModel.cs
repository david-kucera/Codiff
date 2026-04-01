using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class FileDiffViewModel : ObservableObject
{
    private readonly FileDiffService _diffService = new();

    [ObservableProperty]
    private ObservableCollection<DiffLineViewModel> _lines = new();

    [ObservableProperty]
    private string _leftFilePath = string.Empty;

    [ObservableProperty]
    private string _rightFilePath = string.Empty;

    [ObservableProperty]
    private int _currentDiffIndex = -1;

    [ObservableProperty]
    private int _totalDiffs;

    [ObservableProperty]
    private double _scrollOffset;

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
    private bool _isHorizontalSplit;

    public string SplitterOrientationMenuHeader =>
        IsHorizontalSplit ? "Toggle to Vertical Split" : "Toggle to Horizontal Split";

    /// <summary>Raised when the view should copy the given text to the clipboard.</summary>
    public event Action<string>? CopyAsTextRequested;

    private bool _isLeftModified;
    private bool _isRightModified;
    private bool _suppressTextEdited;

    private IReadOnlyList<int> _hunkIndices = Array.Empty<int>();

    public string DiffCounterText => TotalDiffs > 0
        ? $"{(CurrentDiffIndex >= 0 ? CurrentDiffIndex + 1 : 0)} / {TotalDiffs}"
        : "No diffs";

    public bool HasDiffs => TotalDiffs > 0;

    public void LoadFiles(string leftPath, string rightPath)
    {
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffFiles(leftPath, rightPath);
        ApplyResult(result);
    }

    public void LoadTexts(string leftText, string rightText, string leftPath = "", string rightPath = "")
    {
        LeftFilePath = leftPath;
        RightFilePath = rightPath;
        var result = _diffService.DiffTexts(leftText, rightText, leftPath, rightPath);
        ApplyResult(result);
    }

    private void ApplyResult(FileDiffResult result)
    {
        _suppressTextEdited = true;

        _hunkIndices = result.DiffHunkIndices;
        TotalDiffs = _hunkIndices.Count;
        CurrentDiffIndex = TotalDiffs > 0 ? 0 : -1;

        Lines.Clear();
        foreach (var line in result.Lines)
        {
            var vm = new DiffLineViewModel(line);
            vm.LeftTextEdited += OnLeftTextEdited;
            vm.RightTextEdited += OnRightTextEdited;
            Lines.Add(vm);
        }

        _isLeftModified = false;
        _isRightModified = false;
        LeftStatusText = string.Empty;
        RightStatusText = string.Empty;

        _leftFileExists = result.LeftFileExists;
        _rightFileExists = result.RightFileExists;
        OnPropertyChanged("LeftFileExists");
        OnPropertyChanged("RightFileExists");

        _suppressTextEdited = false;

        OnPropertyChanged(nameof(DiffCounterText));
        OnPropertyChanged(nameof(HasDiffs));

        if (CurrentDiffIndex >= 0)
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
        bool saved = false;

        if (_isLeftModified && !string.IsNullOrEmpty(LeftFilePath))
        {
            var text = ReconstructText(isLeft: true);
            File.WriteAllText(LeftFilePath, text);
            _isLeftModified = false;
            LeftStatusText = "✔ Saved";
            saved = true;
        }

        if (_isRightModified && !string.IsNullOrEmpty(RightFilePath))
        {
            var text = ReconstructText(isLeft: false);
            File.WriteAllText(RightFilePath, text);
            _isRightModified = false;
            RightStatusText = "✔ Saved";
            saved = true;
        }

        if (saved)
        {
            // Reload the diff to reflect saved changes
            if (!string.IsNullOrEmpty(LeftFilePath) && !string.IsNullOrEmpty(RightFilePath))
                LoadFiles(LeftFilePath, RightFilePath);
        }
    }

    private string ReconstructText(bool isLeft)
    {
        var textLines = new List<string>();
        foreach (var line in Lines)
        {
            if (isLeft)
            {
                if (line.Model.LeftLineNumber.HasValue)
                    textLines.Add(line.LeftText);
            }
            else
            {
                if (line.Model.RightLineNumber.HasValue)
                    textLines.Add(line.RightText);
            }
        }
        return string.Join(Environment.NewLine, textLines);
    }

    [RelayCommand]
    private void ToggleSplitterOrientation() => IsHorizontalSplit = !IsHorizontalSplit;

    [RelayCommand(CanExecute = nameof(CanCopyAsText))]
    private void CopyAsText()
    {
        var text = ReconstructText(isLeft: true);
        if (string.IsNullOrEmpty(text))
            text = ReconstructText(isLeft: false);
        CopyAsTextRequested?.Invoke(text);
    }

    private bool CanCopyAsText() => LeftFileExists || RightFileExists;

    [RelayCommand]
    private void OpenInExplorer(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", $"-R \"{filePath}\"");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null)
                Process.Start("xdg-open", dir);
        }
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

    private const double LineHeightPx = 24.0;

    private void NavigateToCurrentHunk()
    {
        if (CurrentDiffIndex >= 0 && CurrentDiffIndex < _hunkIndices.Count)
        {
            int lineIndex = _hunkIndices[CurrentDiffIndex];
            ScrollOffset = lineIndex * LineHeightPx;
        }
    }

    partial void OnCurrentDiffIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DiffCounterText));
        PrevDiffCommand.NotifyCanExecuteChanged();
        NextDiffCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalDiffsChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiffs));
        OnPropertyChanged(nameof(DiffCounterText));
    }
}
