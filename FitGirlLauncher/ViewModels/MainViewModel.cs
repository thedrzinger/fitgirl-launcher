using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using FitGirlLauncher.Models;
using FitGirlLauncher.Services;

namespace FitGirlLauncher.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ArtService _artService = new();
    private readonly InstallStateStore _installState = new();
    private readonly SteamDetailStore _steamDetails = new();
    private readonly SteamDetailsService _steamService = new();

    public MainViewModel()
    {
        var settings = SettingsStore.Load();
        Settings = new SettingsViewModel(settings);
        Settings.Saved += OnSettingsSaved;
        _artService.ErrorReported += message => StatusText = message;

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;

        // Top nav. Add future pages here (e.g. new NavItem("about", "About", "?")).
        NavItems.Add(new NavItem("settings", "Settings", "⚙"));

        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        OpenSettingsCommand = new RelayCommand(_ => IsSettingsOpen = true);
        OpenGameCommand = new RelayCommand(p => OpenGame((GameEntry)p!));
        LocateExeCommand = new RelayCommand(p => LocateExe((GameEntry)p!));
        ClearExeCommand = new RelayCommand(p => ClearExe((GameEntry)p!));

        // Silent background update check (see UpdateService for the full flow).
        _ = UpdateService.Instance.CheckForUpdatesAsync();
        _ = RefreshAsync();
    }

    public ObservableCollection<GameEntry> Games { get; } = new();
    public ICollectionView GamesView { get; }
    public ObservableCollection<NavItem> NavItems { get; } = new();
    public SettingsViewModel Settings { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenGameCommand { get; }
    public RelayCommand LocateExeCommand { get; }
    public RelayCommand ClearExeCommand { get; }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (SetProperty(ref _isSettingsOpen, value))
            {
                if (value)
                    SelectedGameDetail = null; // settings and the detail page don't stack
                SyncNavSelection();
                OnPropertyChanged(nameof(IsLibraryVisible));
            }
        }
    }

    private GameDetailViewModel? _selectedGameDetail;
    /// <summary>The game whose detail page is open. Null = library is visible.</summary>
    public GameDetailViewModel? SelectedGameDetail
    {
        get => _selectedGameDetail;
        set
        {
            if (SetProperty(ref _selectedGameDetail, value))
            {
                foreach (var nav in NavItems)
                    nav.IsSelected = false;
                OnPropertyChanged(nameof(IsDetailVisible));
                OnPropertyChanged(nameof(IsLibraryVisible));
            }
        }
    }

    public bool IsDetailVisible => _selectedGameDetail != null;
    public bool IsLibraryVisible => !_isSettingsOpen && _selectedGameDetail == null;

    public void OpenGame(GameEntry game)
    {
        IsSettingsOpen = false;
        SelectedGameDetail = new GameDetailViewModel(
            game, _steamDetails, _steamService, LaunchGame, () => SelectedGameDetail = null);
    }

    private LibraryStatus _status = LibraryStatus.NotSet;
    public LibraryStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private string _statusText = "Loading…";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                GamesView.Refresh();
        }
    }

    public string GameCountText =>
        Games.Count == 0 ? "No games" : $"{Games.Count} game{(Games.Count == 1 ? "" : "s")}";

    private bool FilterGame(object obj)
    {
        if (obj is not GameEntry game)
            return false;
        return string.IsNullOrWhiteSpace(SearchText)
               || game.CleanTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Called from the nav bar when a nav button is toggled.</summary>
    public void SelectNav(NavItem item)
    {
        IsSettingsOpen = item.Id == "settings" && item.IsSelected;
    }

    public async Task RefreshAsync()
    {
        if (IsScanning)
            return;
        IsScanning = true;
        StatusMessage = "";
        try
        {
            var path = Settings.LibraryPath.Trim();
            if (string.IsNullOrEmpty(path))
            {
                Games.Clear();
                Status = LibraryStatus.NotSet;
                StatusText = "No library folder set yet — open Settings to point at your games.";
                return;
            }

            var result = await Task.Run(() => LibraryScanner.Scan(path));

            Games.Clear();
            if (result.Error != null)
            {
                Status = LibraryStatus.Unreachable;
                StatusMessage = result.Error;
                StatusText = "Library unreachable.";
                return;
            }

            foreach (var game in result.Games)
            {
                var installedExe = _installState.GetExePath(game.FolderName);
                if (!string.IsNullOrEmpty(installedExe))
                    game.InstalledExePath = installedExe;

                // Restore persisted Steam details so re-scans don't lose them
                // (and the detail page can open without a network round-trip).
                var steam = _steamDetails.Get(game.FolderName);
                if (steam is not null)
                {
                    game.SteamAppId = steam.SteamAppId;
                    game.StoreName = steam.StoreName;
                    game.Description = steam.Description;
                    game.ScreenshotUrls = new List<string>(steam.ScreenshotUrls);
                    game.SteamLookupStatus = steam.SteamLookupStatus;
                    game.LastFetchedUtc = steam.LastFetchedUtc;
                }
                Games.Add(game);
            }

            if (Games.Count == 0)
            {
                Status = LibraryStatus.Empty;
                StatusText = "No [FitGirl Repack] folders found in this folder.";
                return;
            }

            Status = LibraryStatus.Ready;
            StatusText = $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} — {path}";

            // Keep the art cache in step with the library: drop covers for games that
            // are gone. Only when the scan found games — a network drive can briefly
            // list empty, and we don't want one hiccup wiping the whole cache.
            _artService.PruneToCurrentLibrary(Games.Select(g => g.CleanTitle));

            // Cached art pops in instantly; uncached games queue up for background lookups.
            foreach (var game in Games)
                _artService.EnsureArt(game, Settings.ApiKey.Trim());
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(GameCountText));
        }
    }

    // ---- install state / launching ----

    private void LaunchGame(GameEntry game)
    {
        var outcome = GameLauncher.TryLaunch(game.FolderPath, game.InstalledExePath);
        if (outcome.Success)
            StatusText = game.InstalledExePath == null
                ? $"Running the installer for {game.CleanTitle}…"
                : $"Launching {game.CleanTitle}…";
        else
            UiDialogs.Warn(outcome.Message);
    }

    private void LocateExe(GameEntry game)
    {
        var exePath = UiDialogs.ChooseExe($"Locate the installed game for: {game.CleanTitle}");
        if (exePath == null)
            return;

        _installState.SetExePath(game.FolderName, exePath);
        game.InstalledExePath = exePath;
        StatusText = $"Marked {game.CleanTitle} as installed — its game page will now launch the game instead of the installer.";
    }

    private void ClearExe(GameEntry game)
    {
        _installState.ClearExePath(game.FolderName);
        game.InstalledExePath = null;
        StatusText = $"Cleared install state for {game.CleanTitle} — clicking its tile will run the installer again.";
    }

    private void OnSettingsSaved()
    {
        IsSettingsOpen = false;
        _ = RefreshAsync();
    }

    private void SyncNavSelection()
    {
        foreach (var nav in NavItems)
            nav.IsSelected = _isSettingsOpen && nav.Id == "settings";
    }
}
