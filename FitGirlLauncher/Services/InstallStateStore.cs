using System.IO;
using System.Text.Json;

namespace FitGirlLauncher.Services;

/// <summary>
/// Persists "game folder name -> installed exe path" in
/// %AppData%\FitGirlLauncher\install-state.json.
/// </summary>
public class InstallStateStore
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public InstallStateStore()
    {
        Load();
    }

    public string? GetExePath(string folderName)
    {
        lock (_lock)
            return _map.TryGetValue(folderName, out var path) ? path : null;
    }

    public void SetExePath(string folderName, string exePath)
    {
        lock (_lock)
        {
            _map[folderName] = exePath;
            Save();
        }
    }

    public void ClearExePath(string folderName)
    {
        lock (_lock)
        {
            if (_map.Remove(folderName))
                Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsStore.InstallStateFilePath))
            {
                var json = File.ReadAllText(SettingsStore.InstallStateFilePath);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map != null)
                    foreach (var (folder, path) in map)
                        _map[folder] = path;
            }
        }
        catch
        {
            // Corrupt state file — start fresh rather than crashing.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.AppDataDir);
            File.WriteAllText(
                SettingsStore.InstallStateFilePath,
                JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non-fatal — the next change will rewrite the file.
        }
    }
}
