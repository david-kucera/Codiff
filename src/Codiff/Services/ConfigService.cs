using System.Text.Json;
using Codiff.Models;

namespace Codiff.Services;

/// <summary>
/// Singleton service that loads and saves application settings (theme, recent comparisons)
/// to a JSON config file in the user's application-data directory.
/// </summary>
public class ConfigService
{
    public static readonly ConfigService Instance = new();

    // The CODIFF_CONFIG_DIR environment variable can point the settings somewhere else
    // (a portable copy, or automated tests that must not touch the user's settings).
    private static readonly string ConfigDir =
        Environment.GetEnvironmentVariable("CODIFF_CONFIG_DIR") is { Length: > 0 } configDir
            ? configDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Codiff");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private ConfigService() { }

    public AppConfig Config { get; private set; } = new();

    /// <summary>Loads config from disk; silently uses defaults on any error.</summary>
    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch
        {
            // Silently fall back to defaults; a missing or corrupted config file
            // is non-fatal for a desktop application with no logging infrastructure.
            Config = new AppConfig();
        }
    }

    /// <summary>Persists the current config to disk; silently ignores errors.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Silently ignore save errors (e.g. read-only filesystem, disk full,
            // permission denied). Loss of the config is non-fatal.
        }
    }

    /// <summary>
    /// Adds (or promotes) a comparison entry to the front of the recent list,
    /// keeping at most 10 entries, then saves.
    /// </summary>
    public void AddRecentComparison(bool isFolder, string leftPath, string rightPath)
    {
        Config.RecentComparisons.RemoveAll(r =>
            r.IsFolder == isFolder &&
            r.LeftPath == leftPath &&
            r.RightPath == rightPath);

        Config.RecentComparisons.Insert(0, new RecentComparison
        {
            IsFolder = isFolder,
            LeftPath = leftPath,
            RightPath = rightPath
        });

        if (Config.RecentComparisons.Count > 10)
            Config.RecentComparisons.RemoveRange(10, Config.RecentComparisons.Count - 10);

        Save();
    }

    /// <summary>Clears all recent comparisons and saves.</summary>
    public void ClearRecentComparisons()
    {
        Config.RecentComparisons.Clear();
        Save();
    }
}

/// <summary>Root configuration object serialised to / from JSON.</summary>
public class AppConfig
{
    public bool IsDarkTheme { get; set; }
    public List<RecentComparison> RecentComparisons { get; set; } = [];

    // ── File comparison options ──────────────────────────────────────────
    public bool IgnoreWhiteSpace { get; set; } = true;
    public bool IgnoreCase { get; set; }

    /// <summary>Show the two files above each other instead of side by side.</summary>
    public bool HorizontalSplit { get; set; }

    // ── Which files the folder comparison lists ──────────────────────────
    public bool ShowIdenticalFiles { get; set; }
    public bool ShowDifferentFiles { get; set; } = true;
    public bool ShowLeftOnlyFiles { get; set; } = true;
    public bool ShowRightOnlyFiles { get; set; } = true;
}
