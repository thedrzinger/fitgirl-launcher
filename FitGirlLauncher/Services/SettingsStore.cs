using System.IO;
using System.Text.Json;

namespace FitGirlLauncher.Services;

/// <summary>Persisted app settings (library folders, API key, update repo URL).</summary>
public class AppSettings
{
    /// <summary>
    /// Game library folders, each holding individual game folders.
    /// Older versions stored a single <c>LibraryPath</c> string — Load() seeds
    /// this list from that value so existing installs keep their folder.
    /// </summary>
    public List<string> LibraryPaths { get; set; } = new();
    public string SteamGridDbApiKey { get; set; } = "";

    /// <summary>
    /// GitHub repo hosting the Velopack releases. Edit this in settings.json to
    /// point the app at another repo (e.g. a test channel) without a rebuild.
    /// </summary>
    public string UpdateRepoUrl { get; set; } = DefaultUpdateRepoUrl;

    /// <summary>Used when settings.json has no (or a blank) UpdateRepoUrl.</summary>
    public const string DefaultUpdateRepoUrl = "https://github.com/thedrzinger/fitgirl-launcher";
}

/// <summary>
/// Local persistence under %AppData%\FitGirlLauncher:
/// settings.json, install-state.json, art-cache\, steam-details.json.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FitGirlLauncher");

    public static string SettingsFilePath => Path.Combine(AppDataDir, "settings.json");
    public static string InstallStateFilePath => Path.Combine(AppDataDir, "install-state.json");
    public static string ArtCacheDir => Path.Combine(AppDataDir, "art-cache");
    public static string ArtIndexFilePath => Path.Combine(ArtCacheDir, "index.json");
    public static string SteamDetailFilePath => Path.Combine(AppDataDir, "steam-details.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

                // One-time migration: pre-multi-folder installs saved a single
                // "LibraryPath" string. If the new list came out empty but the old
                // string is present, seed it so the user's configured folder
                // survives the upgrade. The file is rewritten in the new format
                // on the next save.
                var legacy = JsonSerializer.Deserialize<LegacySettings>(json, JsonOptions);
                if (legacy is not null
                    && settings.LibraryPaths is { Count: 0 }
                    && !string.IsNullOrWhiteSpace(legacy.LibraryPath))
                {
                    settings.LibraryPaths = new List<string> { legacy.LibraryPath };
                }

                return settings;
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to defaults.
        }
        return new AppSettings();
    }

    /// <summary>Probe for the old single-folder "LibraryPath" key — migration only.</summary>
    private sealed class LegacySettings
    {
        public string? LibraryPath { get; set; }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
