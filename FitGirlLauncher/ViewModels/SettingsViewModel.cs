using System.Collections.ObjectModel;
using System.Windows;
using FitGirlLauncher.Models;
using FitGirlLauncher.Services;

namespace FitGirlLauncher.ViewModels;

/// <summary>Backs the Settings page. Saves into the shared AppSettings instance + settings.json.</summary>
public class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _store;

    private string _apiKey;
    private string _updateStatus = "Not checked yet.";
    private bool _checkingUpdates;
    private bool _hasPendingUpdate;

    /// <summary>Library folders for the Settings page, one row each. Always holds at least one row.</summary>
    public ObservableCollection<LibraryPathRow> LibraryFolders { get; } = new();

    public SettingsViewModel(AppSettings store)
    {
        _store = store;
        _apiKey = store.SteamGridDbApiKey;
        _hasPendingUpdate = UpdateService.Instance.HasPendingUpdate;

        // Seed one row per saved folder; if none are saved, start with one empty
        // row so there's always something to type into / browse.
        var saved = store.LibraryPaths ?? new List<string>();
        if (saved.Count == 0)
            LibraryFolders.Add(new LibraryPathRow(LibraryFolders));
        else
            foreach (var path in saved)
                LibraryFolders.Add(new LibraryPathRow(LibraryFolders) { Path = path });

        LibraryFolders.CollectionChanged += (_, _) => SyncRemoveButtons();
        SyncRemoveButtons(); // rows seeded above predate the subscription

        SaveCommand = new RelayCommand(_ => Save());
        AddFolderCommand = new RelayCommand(_ =>
        {
            LibraryFolders.Add(new LibraryPathRow(LibraryFolders));
            SyncRemoveButtons();
        });
        RestartToUpdateCommand = new RelayCommand(_ =>
        {
            if (!UpdateService.Instance.ApplyPendingUpdate())
                UiDialogs.Warn("The update couldn't be applied. Check update.log in your AppData FitGirlLauncher folder for details.");
        });
        CheckUpdatesCommand = new RelayCommand(async _ =>
        {
            if (CheckingUpdates)
                return;
            CheckingUpdates = true;
            UpdateStatus = "Checking for updates…";
            try
            {
                UpdateStatus = await UpdateService.Instance.CheckAndDownloadAsync();
            }
            finally
            {
                CheckingUpdates = false;
            }
        });

        // Keep the "Restart to update" button in sync when a background check lands.
        UpdateService.Instance.UpdateReady += (_, _) => HasPendingUpdate = true;
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    /// <summary>The running app's version, shown in the Updates card.</summary>
    public string CurrentVersion => UpdateService.Instance.CurrentVersion;

    public string UpdateStatus
    {
        get => _updateStatus;
        set => SetProperty(ref _updateStatus, value);
    }

    public bool CheckingUpdates
    {
        get => _checkingUpdates;
        set
        {
            if (SetProperty(ref _checkingUpdates, value))
                OnPropertyChanged(nameof(UpdateButtonText));
        }
    }

    /// <summary>True once an update is downloaded and waiting for a restart.</summary>
    public bool HasPendingUpdate
    {
        get => _hasPendingUpdate;
        set
        {
            if (SetProperty(ref _hasPendingUpdate, value))
                OnPropertyChanged(nameof(RestartButtonVisibility));
        }
    }

    public string UpdateButtonText => CheckingUpdates ? "Checking…" : "Check for updates";

    public Visibility RestartButtonVisibility =>
        HasPendingUpdate ? Visibility.Visible : Visibility.Collapsed;

    public RelayCommand SaveCommand { get; }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand CheckUpdatesCommand { get; }

    public RelayCommand RestartToUpdateCommand { get; }

    /// <summary>Raised after a successful save — main VM uses it to rescan.</summary>
    public event Action? Saved;

    /// <summary>Hides the "×" on the first row so at least one row always remains.</summary>
    private void SyncRemoveButtons()
    {
        for (var i = 0; i < LibraryFolders.Count; i++)
            LibraryFolders[i].IsFirstRow = i == 0;
    }

    private void Save()
    {
        // Trim each row, drop blanks (an "Add folder" row that was never filled in),
        // and de-duplicate so the same folder isn't scanned twice.
        _store.LibraryPaths = LibraryFolders
            .Select(r => r.Path.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _store.SteamGridDbApiKey = ApiKey.Trim();
        SettingsStore.Save(_store);
        Saved?.Invoke();
    }
}
