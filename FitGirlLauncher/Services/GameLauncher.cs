using System.Diagnostics;
using System.IO;

namespace FitGirlLauncher.Services;

public record LaunchOutcome(bool Success, string Message);

/// <summary>
/// Launches a game: the recorded installed exe if there is one, otherwise the
/// repack's setup.exe. UseShellExecute is on because installers often need to
/// trigger UAC elevation, which plain Process.Start can't do.
/// </summary>
public static class GameLauncher
{
    public static LaunchOutcome TryLaunch(string gameFolder, string? installedExePath, string? arguments = null)
    {
        var exePath = installedExePath;

        if (exePath != null && !File.Exists(exePath))
            return new LaunchOutcome(false, $"The recorded game file is missing:\n{exePath}");

        if (exePath == null)
        {
            var setup = Path.Combine(gameFolder, "setup.exe");
            if (!File.Exists(setup))
                return new LaunchOutcome(false, $"No setup.exe found in the game folder:\n{gameFolder}");
            exePath = setup;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });
            return new LaunchOutcome(true, string.Empty);
        }
        catch (Exception ex)
        {
            return new LaunchOutcome(false, $"Couldn't launch {Path.GetFileName(exePath)}:\n{ex.Message}");
        }
    }
}
