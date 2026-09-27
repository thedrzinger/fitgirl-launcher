using System.IO;
using System.Text.Json;

namespace FitGirlLauncher.Services;

/// <summary>Persisted app settings (library path, API key, update repo URL).</summary>
public class AppSettings
{
    public string LibraryPath { get; set; } = "";
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
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
