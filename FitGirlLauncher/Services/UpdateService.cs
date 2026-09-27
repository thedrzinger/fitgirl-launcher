using System.IO;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace FitGirlLauncher.Services;

/// <summary>
/// Self-updating via Velopack. The startup check is notify-only: it reports a
/// newer version (UpdateAvailable) but never downloads. Downloads happen only
/// when the user clicks "Check for updates" in Settings (UpdateReady). The new
/// version swaps in when the user restarts.
///
/// Everything here is designed to fail silently — an update problem must never
/// take down the launcher. Failures are written to
/// %AppData%\FitGirlLauncher\update.log for diagnosis.
/// </summary>
public sealed class UpdateService
{
    public static UpdateService Instance { get; } = new();

    private readonly UpdateManager _manager = new(new GithubSource(GetRepoUrl(), null, false));
    private readonly object _gate = new();
    private UpdateInfo? _pending;
    private bool _busy;

    /// <summary>Raised when a check finds a newer version. Args: (currentVersion, newVersion).</summary>
    public event Action<string, string>? UpdateAvailable;

    /// <summary>Raised after a new version has been downloaded. Args: (currentVersion, newVersion).</summary>
    public event Action<string, string>? UpdateReady;

    /// <summary>The running app's version (from the &lt;Version&gt; tag in the csproj).</summary>
    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>True once an update has been downloaded and is waiting for a restart.</summary>
    public bool HasPendingUpdate
    {
        get { lock (_gate) return _pending != null; }
    }

    private UpdateService()
    {
        Log($"Started. CurrentVersion={CurrentVersion}, packaged={_manager.IsInstalled}, repo={GetRepoUrl()}");
    }

    /// <summary>
    /// The GitHub repo hosting the Velopack releases. Read from settings.json
    /// (UpdateRepoUrl) so switching repos — e.g. to a test channel — never
    /// needs a rebuild; falls back to the default when missing or blank.
    /// accessToken stays null: the app only reads public releases; the token
    /// lives on the machine that publishes (VPK_TOKEN for `vpk upload`).
    /// </summary>
    private static string GetRepoUrl()
    {
        var url = SettingsStore.Load().UpdateRepoUrl?.Trim();
        return string.IsNullOrWhiteSpace(url) ? AppSettings.DefaultUpdateRepoUrl : url;
    }

    /// <summary>
    /// Checks GitHub for a newer release without downloading anything. Raises
    /// <see cref="UpdateAvailable"/> when one exists. Never throws.
    /// </summary>
    public async Task<bool> CheckOnlyAsync()
    {
        lock (_gate)
        {
            if (_busy)
                return false;
            _busy = true;
        }

        try
        {
            var newVersion = await _manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                Log($"Check: no update available (current {CurrentVersion}).");
                return false;
            }

            var newVersionString = newVersion.TargetFullRelease.Version.ToString();
            Log($"Check: update {newVersionString} available (current {CurrentVersion}) — not downloading; install it from Settings.");
            UpdateAvailable?.Invoke(CurrentVersion, newVersionString);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Update check failed: {ex}");
            return false;
        }
        finally
        {
            lock (_gate)
                _busy = false;
        }
    }

    /// <summary>
    /// Checks GitHub for a newer release and, if one exists, downloads it and
    /// raises <see cref="UpdateReady"/>. Never throws — always returns a
    /// user-facing message.
    /// </summary>
    public async Task<string> CheckAndDownloadAsync()
    {
        lock (_gate)
        {
            if (_busy)
                return "An update check is already running.";
            _busy = true;
        }

        try
        {
            var newVersion = await _manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                Log($"Manual check: no update available (current {CurrentVersion}).");
                return $"You're on the latest version ({CurrentVersion}).";
            }

            var newVersionString = newVersion.TargetFullRelease.Version.ToString();
            lock (_gate)
            {
                if (_pending != null)
                    return $"Version {newVersionString} is already downloaded — restart from Settings to install it.";
                _pending = newVersion;
            }

            Log($"Manual check: downloading {newVersionString}…");
            await _manager.DownloadUpdatesAsync(newVersion, null, CancellationToken.None);
            Log($"Manual check: {newVersionString} downloaded, ready to install.");
            UpdateReady?.Invoke(CurrentVersion, newVersionString);
            return $"Version {newVersionString} is available (you're on {CurrentVersion}) — downloaded, ready to install.";
        }
        catch (Exception ex)
        {
            Log($"Update check failed: {ex}");
            return "Couldn't check for updates right now — try again later.";
        }
        finally
        {
            lock (_gate)
                _busy = false;
        }
    }

    /// <summary>
    /// Applies the downloaded update and restarts. The app exits from here;
    /// Velopack installs the new version and relaunches it.
    /// </summary>
    public bool ApplyPendingUpdate()
    {
        lock (_gate)
        {
            if (_pending == null)
                return false;
        }

        try
        {
            _manager.ApplyUpdatesAndRestart(_pending);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Applying the update failed: {ex}");
            return false;
        }
    }

    /// <summary>Appends a line to the update log. Never throws.</summary>
    public static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FitGirlLauncher");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "update.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* logging must never take the app down */ }
    }
}
