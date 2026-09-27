using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FitGirlLauncher.Models;
using FitGirlLauncher.Services;
using FitGirlLauncher.Views;

namespace FitGirlLauncher.ViewModels;

/// <summary>One entry in the screenshot strip — a small thumb URL for the strip and the
/// full-res URL for click-to-enlarge, plus the lazily-loaded thumbnail image.</summary>
public class ScreenshotItem : ObservableObject
{
    private ImageSource? _thumb;

    public ScreenshotItem(string thumbUrl, string fullUrl)
    {
        ThumbUrl = thumbUrl;
        FullUrl = fullUrl;
    }

    /// <summary>Small (600x338) URL used to fill the strip thumbnail.</summary>
    public string ThumbUrl { get; }

    /// <summary>Full-res (1920x1080) URL, opened when the thumbnail is clicked.</summary>
    public string FullUrl { get; }

    /// <summary>Null until the thumbnail has finished downloading (placeholder shows meanwhile).</summary>
    public ImageSource? Thumb
    {
        get => _thumb;
        set => SetProperty(ref _thumb, value);
    }
}

/// <summary>
/// The game detail page. Steam data is cache-first: only the first open of a game
/// (or an explicit Refresh) hits the network — see SteamDetailStore. NotOnSteam is
/// re-checked on open rather than served from cache, because the old literal search
/// produced false negatives for titles with punctuation ("Volkolak - The Will of Gods").
/// </summary>
public class GameDetailViewModel : ObservableObject
{
    private readonly GameEntry _game;
    private readonly SteamDetailStore _steamStore;
    private readonly SteamDetailsService _steamService;
    private readonly Action<GameEntry> _launch;
    private readonly Action _close;
    private bool _fetchInFlight;

    /// <summary>Session-wide thumbnail cache so going back and forth doesn't re-download.</summary>
    private static readonly ConcurrentDictionary<string, ImageSource> ThumbnailCache = new();

    public GameDetailViewModel(GameEntry game, SteamDetailStore steamStore, SteamDetailsService steamService,
        Action<GameEntry> launch, Action close)
    {
        _game = game;
        _steamStore = steamStore;
        _steamService = steamService;
        _launch = launch;
        _close = close;

        BackCommand = new RelayCommand(_ => _close());
        LaunchCommand = new RelayCommand(_ => _launch(_game));
        RefreshCommand = new RelayCommand(_ => _ = FetchAsync());
        OpenScreenshotCommand = new RelayCommand(p => OpenScreenshot(p as ScreenshotItem));

        _game.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(GameEntry.InstalledExePath):
                    OnPropertyChanged(nameof(LaunchText));
                    break;
                case nameof(GameEntry.ArtLoading):
                    OnPropertyChanged(nameof(ArtLoading));
                    break;
                case nameof(GameEntry.LastFetchedUtc):
                    OnPropertyChanged(nameof(FetchedLine));
                    break;
                case nameof(GameEntry.StoreName):
                case nameof(GameEntry.SteamAppId):
                    OnPropertyChanged(nameof(StoreLine));
                    break;
            }
        };

        if (_game.SteamLookupStatus == SteamLookupStatus.Found)
        {
            // Cached — show it, no network.
            _ = LoadThumbnailsAsync();
        }
        else
        {
            // NotFetched (first open), Failed (retry), or NotOnSteam (re-check — see class note).
            _ = FetchAsync();
        }
    }

    public GameEntry Game => _game;
    public string Title => _game.CleanTitle;
    public bool ArtLoading => _game.ArtLoading;
    public string LaunchText => _game.IsInstalled ? "Launch game" : "Run installer";

    public string FetchedLine => _game.LastFetchedUtc > DateTime.MinValue
        ? $"Steam details fetched {new DateTime(_game.LastFetchedUtc.Ticks, DateTimeKind.Utc).ToLocalTime():MMM d, yyyy HH:mm}"
        : "";

    /// <summary>"On Steam as "…" — doubles as a wrong-match tripwire (the folder title
    /// and the store title side by side).</summary>
    public string StoreLine
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_game.StoreName))
                return "";
            return _game.SteamAppId is { } id
                ? $"On Steam as “{_game.StoreName}” — App ID {id}"
                : $"On Steam as “{_game.StoreName}”";
        }
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand LaunchCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenScreenshotCommand { get; }

    public ObservableCollection<ScreenshotItem> Screenshots { get; } = new();
    public bool HasScreenshots => Screenshots.Count > 0;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _showNotFound;
    public bool ShowNotFound
    {
        get => _showNotFound;
        set => SetProperty(ref _showNotFound, value);
    }

    private bool _showFailed;
    public bool ShowFailed
    {
        get => _showFailed;
        set => SetProperty(ref _showFailed, value);
    }

    private string _failMessage = "";
    public string FailMessage
    {
        get => _failMessage;
        set => SetProperty(ref _failMessage, value);
    }

    /// <summary>Fetches from the network, persists the result, and updates the page state.</summary>
    private async Task FetchAsync()
    {
        if (_fetchInFlight)
            return;
        _fetchInFlight = true;
        IsLoading = true;
        ShowNotFound = false;
        ShowFailed = false;
        try
        {
            var result = await Task.Run(() => _steamService.ResolveAsync(_game.CleanTitle));
            _steamStore.ApplyTo(_game, result);

            ShowNotFound = result.Status == SteamLookupStatus.NotOnSteam;

            if (result.Status == SteamLookupStatus.Found)
                await LoadThumbnailsAsync();
            else
            {
                Screenshots.Clear();
                OnPropertyChanged(nameof(HasScreenshots));
            }
        }
        catch (Exception ex)
        {
            // Record the failure but keep any previously fetched data on the page.
            _game.SteamLookupStatus = SteamLookupStatus.Failed;
            _game.LastFetchedUtc = DateTime.UtcNow;
            _steamStore.Save(_game.FolderName, new SteamDetailRecord
            {
                SteamAppId = _game.SteamAppId,
                StoreName = _game.StoreName,
                Description = _game.Description,
                ScreenshotUrls = new List<string>(_game.ScreenshotUrls),
                SteamLookupStatus = SteamLookupStatus.Failed,
                LastFetchedUtc = _game.LastFetchedUtc
            });
            FailMessage = ex.Message;
            ShowFailed = true;
        }
        finally
        {
            _fetchInFlight = false;
            IsLoading = false;
        }
    }

    /// <summary>Adds all screenshots to the strip (as placeholders) and fills in thumbnails as they land.</summary>
    private async Task LoadThumbnailsAsync()
    {
        var urls = _game.ScreenshotUrls;
        if (urls.Count == 0)
            return;

        // Pair each full-res URL with its smaller thumbnail (falls back to full-res when
        // the cache predates thumb support).
        var thumbs = _game.ScreenshotThumbUrls;
        var items = urls.Select((full, i) => new ScreenshotItem(
            i < thumbs.Count ? thumbs[i] : full,
            full)).ToList();
        foreach (var item in items)
            Screenshots.Add(item);
        OnPropertyChanged(nameof(HasScreenshots));

        var loads = items.Select(async item =>
        {
            var thumb = await DownloadThumbnailAsync(item.ThumbUrl);
            if (thumb is not null)
                Application.Current?.Dispatcher.Invoke(() => item.Thumb = thumb);
        }).ToList();

        await Task.WhenAll(loads);
    }

    private static readonly HttpClient ThumbHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Downloads the bytes and decodes them off the UI thread. A BitmapImage pointed
    /// straight at a web Uri cannot be Frozen ("This Freezable cannot be frozen") because
    /// its HTTP stream stays open — decoding from a MemoryStream is freezable, and
    /// freezing is required to hand the image to the UI thread.
    /// </summary>
    private static async Task<ImageSource?> DownloadThumbnailAsync(string url)
    {
        if (ThumbnailCache.TryGetValue(url, out var cached))
            return cached;
        try
        {
            var bytes = await ThumbHttp.GetByteArrayAsync(url);
            var image = await Task.Run(() => DecodeImage(bytes));
            ThumbnailCache[url] = image;
            return image;
        }
        catch
        {
            return null; // keep the placeholder rather than breaking the strip
        }
    }

    private static ImageSource DecodeImage(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        // NOTE: do NOT set BitmapCreateOptions.IgnoreImageCache here — with a
        // StreamSource (no UriSource) WPF's FinalizeCreation tries to evict a
        // null Uri from the imaging cache and throws ArgumentNullException.
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        using var stream = new MemoryStream(bytes);
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void OpenScreenshot(ScreenshotItem? item)
    {
        if (item is null)
            return;
        new ScreenshotWindow(item.FullUrl) { Owner = Application.Current.MainWindow }.Show();
    }
}
