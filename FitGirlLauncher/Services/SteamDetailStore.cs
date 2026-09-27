using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FitGirlLauncher.Models;

namespace FitGirlLauncher.Services;

/// <summary>Persisted Steam detail for one game (keyed by game folder name).</summary>
public class SteamDetailRecord
{
    public int? SteamAppId { get; set; }
    public string? StoreName { get; set; }
    public string? Description { get; set; }
    public List<string> ScreenshotUrls { get; set; } = new();
    public List<string> ScreenshotThumbUrls { get; set; } = new();
    public SteamLookupStatus SteamLookupStatus { get; set; } = SteamLookupStatus.NotFetched;
    public DateTime LastFetchedUtc { get; set; }
}

/// <summary>
/// Persists Steam store lookups in %AppData%\FitGirlLauncher\steam-details.json,
/// keyed by game folder name. Store data barely changes, so a game is only
/// fetched from the network the first time its detail page is opened (or on an
/// explicit refresh) — never on app launch.
/// </summary>
public class SteamDetailStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private Dictionary<string, SteamDetailRecord> _records =
        new(StringComparer.Ordinal);

    public SteamDetailStore()
    {
        Load();
    }

    public SteamDetailRecord? Get(string folderName) =>
        _records.TryGetValue(folderName, out var record) ? record : null;

    public void Save(string folderName, SteamDetailRecord record)
    {
        _records[folderName] = record;
        Persist();
    }

    /// <summary>Applies a lookup result to the game and persists it. Single source of
    /// truth for the record shape — used by the game detail page (and any future
    /// bulk-prefetch feature).</summary>
    public void ApplyTo(GameEntry game, SteamDetailResult result)
    {
        game.SteamAppId = result.AppId;
        game.StoreName = result.StoreName;
        game.Description = result.Description;
        game.ScreenshotUrls = new List<string>(result.ScreenshotUrls);
        game.ScreenshotThumbUrls = new List<string>(result.ScreenshotThumbUrls);
        game.SteamLookupStatus = result.Status;
        game.LastFetchedUtc = DateTime.UtcNow;

        Save(game.FolderName, new SteamDetailRecord
        {
            SteamAppId = result.AppId,
            StoreName = result.StoreName,
            Description = result.Description,
            ScreenshotUrls = new List<string>(result.ScreenshotUrls),
            ScreenshotThumbUrls = new List<string>(result.ScreenshotThumbUrls),
            SteamLookupStatus = result.Status,
            LastFetchedUtc = game.LastFetchedUtc
        });
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsStore.SteamDetailFilePath))
            {
                var json = File.ReadAllText(SettingsStore.SteamDetailFilePath);
                _records = JsonSerializer.Deserialize<Dictionary<string, SteamDetailRecord>>(json, JsonOptions)
                           ?? new Dictionary<string, SteamDetailRecord>(StringComparer.Ordinal);
            }
        }
        catch
        {
            // Corrupt or unreadable file — start fresh rather than crash the app.
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(SettingsStore.AppDataDir);
        File.WriteAllText(SettingsStore.SteamDetailFilePath, JsonSerializer.Serialize(_records, JsonOptions));
    }
}
