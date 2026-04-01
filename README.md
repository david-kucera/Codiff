# Codiff

`Codiff` is a cross-platform desktop diff tool built with `Avalonia` and `.NET 10`.
It compares files and folders side by side with synchronized views, visual highlights, and quick navigation.

![Codiff main window light](docs/light.png)

## Features

### File compare
- Side-by-side line diff for two files
- Inline editing on both sides
- Save changes back to disk with `Ctrl+S`
- Previous/next diff hunk navigation with counter
- Synchronized horizontal and vertical scrolling
- Clickable file paths to reveal files in the system file explorer
- Mini-map (`DiffMap`) for quick jump navigation through large diffs

### Folder compare
- Side-by-side folder comparison
- Tree view that focuses on differences
- Expand/collapse folders (`Expand All` / `Collapse All`)
- Sort by `Name`, `Size`, `Type`, and `Time`
- View modes: first directory only, second directory only, or both
- Double-click a file row to open a dedicated file diff window
- Actions for selected item:
  - Copy full path to clipboard
  - Delete from selected side
  - Delete from both sides
- Optional statistics panel (identical / one-side-only / modified)

![Codiff main window dark](docs/dark.png)

### App behavior
- Light and dark theme toggle
- Recent comparisons menu (files and directories)
- Recent comparisons are persisted and can be cleared from the menu
- Directory convenience actions:
  - Re-compare
  - Swap directories
  - Move both sides up one folder level

## Tech stack

- `.NET 10`
- `C#`
- `Avalonia 11`
- `CommunityToolkit.Mvvm`
- `DiffPlex`

## Getting started

### Prerequisites

- `.NET SDK 10`

### Run from source

```bash
dotnet run --project src/Codiff/Codiff.csproj
```

## Usage

1. Start the application.
2. Choose `Open Files` or `Open Folders`.
3. Inspect differences using color highlights.
4. For file diffs, edit either side and press `Ctrl+S` to save.
5. For folder diffs, double-click a file row to open detailed file comparison.

## Configuration

Application settings are saved automatically in:
- `%AppData%/Codiff/config.json`

Saved settings include:
- active theme
- recent comparisons (up to 10 entries)