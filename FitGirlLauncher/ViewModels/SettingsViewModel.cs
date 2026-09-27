using System.Windows;
using FitGirlLauncher.Models;
using FitGirlLauncher.Services;

namespace FitGirlLauncher.ViewModels;

/// <summary>Backs the Settings page. Saves into the shared AppSettings instance + settings.json.</summary>
public class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _store;

    private string _libraryPath;
    private string _apiKey;
    private string _updateStatus = "Not checked yet.";
    private bool _checkingUpdates;
    private bool _hasPendingUpdate;

    public SettingsViewModel(AppSettings store)
    {
        _store = store;
        _libraryPath = store.LibraryPath;
        _apiKey = store.SteamGridDbApiKey;
        _hasPendingUpdate = UpdateService.Instance.HasPendingUpdate;
        SaveCommand = new RelayCommand(_ => Save());
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
                UpdateStatus = await UpdateService.Instance.CheckForUpdatesAsync();
            }
            finally
            {
                CheckingUpdates = false;
            }
        });

        // Keep the "Restart to update" button in sync when a background check lands.
        UpdateService.Instance.UpdateReady += (_, _) => HasPendingUpdate = true;
    }

    public string LibraryPath
    {
        get => _libraryPath;
        set => SetProperty(ref _libraryPath, value);
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

    public RelayCommand CheckUpdatesCommand { get; }

    public RelayCommand RestartToUpdateCommand { get; }

    /// <summary>Raised after a successful save — main VM uses it to rescan.</summary>
    public event Action? Saved;

    private void Save()
    {
        _store.LibraryPath = LibraryPath.Trim();
        _store.SteamGridDbApiKey = ApiKey.Trim();
        SettingsStore.Save(_store);
        Saved?.Invoke();
    }
}
