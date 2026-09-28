using System.IO;
using System.Windows.Media;

namespace FitGirlLauncher.Models;

/// <summary>One game folder in the library, plus its runtime state.</summary>
public class GameEntry : ObservableObject
{
    public GameEntry(string folderName, string folderPath, string cleanTitle)
    {
        FolderName = folderName;
        FolderPath = folderPath;
        CleanTitle = cleanTitle;
    }

    /// <summary>Original folder name — the stable key for persisted per-game data.</summary>
    public string FolderName { get; }

    public string FolderPath { get; }

    private DateTime? _folderCreatedUtc;
    /// <summary>Folder creation time (UTC) — the "recently added" proxy for the
    /// library sort. One filesystem call per game, then cached; if the folder is
    /// unreachable at sort time it sorts as oldest rather than breaking the view.</summary>
    public DateTime FolderCreatedUtc
    {
        get
        {
            if (_folderCreatedUtc is not { } created)
            {
                try
                {
                    created = Directory.GetCreationTimeUtc(FolderPath);
                }
                catch
                {
                    created = DateTime.MinValue;
                }
                _folderCreatedUtc = created;
            }
            return created;
        }
    }

    /// <summary>Title cleaned from folder cruft, used for display and art lookup.</summary>
    public string CleanTitle { get; }

    /// <summary>
    /// Name of the library folder (last path segment) this game was found in.
    /// Set right after scanning; used to group the library view into
    /// per-folder sections.
    /// </summary>
    public string LibraryFolderName { get; set; } = "Library";

    /// <summary>Position of this game's library folder in the settings order (0-based),
    /// set during the scan. Lets the view re-sort tiles within a section without
    /// reordering the sections themselves.</summary>
    public int LibraryFolderIndex { get; set; }

    private ImageSource? _artImage;
    public ImageSource? ArtImage
    {
        get => _artImage;
        set
        {
            if (SetProperty(ref _artImage, value))
                OnPropertyChanged(nameof(HasArt));
        }
    }

    public bool HasArt => _artImage != null;

    private bool _artResolved;
    /// <summary>True once art was found+cached, or definitively not found (so we don't re-query).</summary>
    public bool ArtResolved
    {
        get => _artResolved;
        set => SetProperty(ref _artResolved, value);
    }

    private bool _artLoading;
    /// <summary>True while this tile's cover art is being looked up / downloaded.</summary>
    public bool ArtLoading
    {
        get => _artLoading;
        set => SetProperty(ref _artLoading, value);
    }

    // ── Steam store details (persisted per game; fetched lazily — see SteamDetailStore) ──

    private int? _steamAppId;
    public int? SteamAppId
    {
        get => _steamAppId;
        set => SetProperty(ref _steamAppId, value);
    }

    private string? _storeName;
    /// <summary>The matched Steam store game's name — handy for spotting wrong matches.</summary>
    public string? StoreName
    {
        get => _storeName;
        set => SetProperty(ref _storeName, value);
    }

    private string? _description;
    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    private List<string> _screenshotUrls = new();
    public List<string> ScreenshotUrls
    {
        get => _screenshotUrls;
        set
        {
            if (SetProperty(ref _screenshotUrls, value))
                OnPropertyChanged(nameof(HasScreenshots));
        }
    }

    public bool HasScreenshots => _screenshotUrls.Count > 0;

    /// <summary>Smaller (600x338) screenshot URLs for the strip, aligned by index with
    /// <see cref="ScreenshotUrls"/> (the full-res ones used for click-to-enlarge).</summary>
    private List<string> _screenshotThumbUrls = new();
    public List<string> ScreenshotThumbUrls
    {
        get => _screenshotThumbUrls;
        set => SetProperty(ref _screenshotThumbUrls, value);
    }

    private SteamLookupStatus _steamLookupStatus;
    public SteamLookupStatus SteamLookupStatus
    {
        get => _steamLookupStatus;
        set => SetProperty(ref _steamLookupStatus, value);
    }

    private DateTime _lastFetchedUtc;
    public DateTime LastFetchedUtc
    {
        get => _lastFetchedUtc;
        set => SetProperty(ref _lastFetchedUtc, value);
    }
}
