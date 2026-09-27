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

    /// <summary>Original folder name — the stable key for install state.</summary>
    public string FolderName { get; }

    public string FolderPath { get; }

    /// <summary>Title cleaned from folder cruft, used for display and art lookup.</summary>
    public string CleanTitle { get; }

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

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    private string? _installedExePath;
    public string? InstalledExePath
    {
        get => _installedExePath;
        set
        {
            if (SetProperty(ref _installedExePath, value))
                IsInstalled = !string.IsNullOrEmpty(value);
        }
    }

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
