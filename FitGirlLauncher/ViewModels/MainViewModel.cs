using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using FitGirlLauncher.Models;
using FitGirlLauncher.Services;
using FitGirlLauncher.Views;

namespace FitGirlLauncher.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ArtService _artService = new();
    private readonly SteamDetailStore _steamDetails = new();
    private readonly SteamDetailsService _steamService = new();
    private readonly AppSettings _settings;

    public MainViewModel()
    {
        _settings = SettingsStore.Load();
        Settings = new SettingsViewModel(_settings);
        Settings.Saved += OnSettingsSaved;
        _artService.ErrorReported += message => StatusText = message;

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
        // Group tiles by library folder. WPF lays groups out in first-appearance
        // order, and the LibraryFolderIndex sort (see ApplySort) is always the
        // primary key — so sections follow the settings order no matter which
        // tile sort is active.
        GamesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GameEntry.LibraryFolderName)));

        // Library sort mode, persisted in settings.json ("az" or "recent").
        _sortMode = string.Equals(_settings.SortMode, "recent", StringComparison.OrdinalIgnoreCase)
            ? "recent"
            : "az";
        ApplySort();

        // Top nav. Add future pages here (e.g. new NavItem("about", "About", "?")).
        NavItems.Add(new NavItem("settings", "Settings", "⚙"));

        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        OpenSettingsCommand = new RelayCommand(_ => IsSettingsOpen = true);
        OpenGameCommand = new RelayCommand(p => OpenGame((GameEntry)p!));
        OpenFolderCommand = new RelayCommand(p => OpenFolder((GameEntry)p!));
        FixSteamMatchCommand = new RelayCommand(p => FixSteamMatch((GameEntry)p!));

        // Startup update check: notify-only, never downloads. If a newer version
        // exists, the dot in the nav bar lights up and the user installs it from
        // Settings (see UpdateService for the full flow).
        var updates = UpdateService.Instance;
        updates.UpdateAvailable += (_, _) => HasUpdateAvailable = true;
        updates.UpdateReady += (_, _) => HasUpdateAvailable = true;
        HasUpdateAvailable = updates.HasPendingUpdate; // a download from a previous session is still waiting
        _ = updates.CheckOnlyAsync();
        _ = RefreshAsync();
    }

    public ObservableCollection<GameEntry> Games { get; } = new();
    public ICollectionView GamesView { get; }
    public ObservableCollection<NavItem> NavItems { get; } = new();
    public SettingsViewModel Settings { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenGameCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand FixSteamMatchCommand { get; }

    private bool _hasUpdateAvailable;
    /// <summary>True when the startup check found a newer version (not yet downloaded)
    /// or an update is already downloaded and waiting for a restart. Shows the dot
    /// in the nav bar; clicking it opens Settings.</summary>
    public bool HasUpdateAvailable
    {
        get => _hasUpdateAvailable;
        private set => SetProperty(ref _hasUpdateAvailable, value);
    }

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

    private string _sortMode = "az";
    /// <summary>Library sort mode: "az" (A–Z by title) or "recent" (newest folder
    /// first). Persisted in settings.json on change.</summary>
    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (SetProperty(ref _sortMode, value) && value is not null)
            {
                ApplySort();
                _settings.SortMode = value;
                SettingsStore.Save(_settings);
            }
        }
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

    /// <summary>
    /// Orders the tiles. Sections always stay in library-folder (settings) order —
    /// the index sort pins that — and within each section the chosen mode applies.
    /// </summary>
    private void ApplySort()
    {
        var recent = _sortMode == "recent";
        GamesView.SortDescriptions.Clear();
        GamesView.SortDescriptions.Add(new SortDescription(
            nameof(GameEntry.LibraryFolderIndex), ListSortDirection.Ascending));
        GamesView.SortDescriptions.Add(new SortDescription(
            recent ? nameof(GameEntry.FolderCreatedUtc) : nameof(GameEntry.CleanTitle),
            recent ? ListSortDirection.Descending : ListSortDirection.Ascending));
        GamesView.Refresh();
    }

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
            // One scan pass per configured library folder, in settings order.
            var paths = Settings.LibraryFolders
                .Select(r => r.Path.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (paths.Count == 0)
            {
                Games.Clear();
                Status = LibraryStatus.NotSet;
                StatusText = "No library folders set yet — open Settings to point at your games.";
                return;
            }

            var scanned = new List<GameEntry>();
            var errors = new List<string>();

            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var result = await Task.Run(() => LibraryScanner.Scan(path));
                if (result.Error != null)
                {
                    errors.Add(result.Error);
                    continue;
                }
                // The folder's own name (last path segment) becomes the section
                // header for its games on the main page.
                var folderName = Path.GetFileName(
                    path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folderName))
                    folderName = path;
                foreach (var game in result.Games)
                {
                    game.LibraryFolderName = folderName;
                    game.LibraryFolderIndex = i;
                }
                scanned.AddRange(result.Games);
            }

            Games.Clear();

            if (scanned.Count == 0)
            {
                if (errors.Count == paths.Count)
                {
                    Status = LibraryStatus.Unreachable;
                    StatusMessage = string.Join(Environment.NewLine, errors);
                    StatusText = "Library unreachable.";
                }
                else
                {
                    Status = LibraryStatus.Empty;
                    StatusText = "No [FitGirl Repack] folders found in your library folders.";
                }
                return;
            }

            foreach (var game in scanned)
            {
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

            Status = LibraryStatus.Ready;
            StatusText = errors.Count > 0
                ? $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} — {paths.Count} library folder{(paths.Count == 1 ? "" : "s")} ({errors.Count} unreachable)"
                : $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} — {paths.Count} library folder{(paths.Count == 1 ? "" : "s")}";

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

    // ---- launching ----

    private void OpenFolder(GameEntry game)
    {
        try
        {
            Process.Start("explorer.exe", game.FolderPath);
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't open the folder for {game.CleanTitle}: {ex.Message}";
        }
    }

    private void FixSteamMatch(GameEntry game)
    {
        new SteamMatchWindow(game, _steamDetails)
        {
            Owner = Application.Current.MainWindow
        }.ShowDialog();
    }

    private void LaunchGame(GameEntry game)
    {
        // Always run the repack's setup.exe — the app has no notion of "installed".
        var outcome = GameLauncher.TryLaunch(game.FolderPath, null);
        if (outcome.Success)
            StatusText = $"Running the installer for {game.CleanTitle}…";
        else
            UiDialogs.Warn(outcome.Message);
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
