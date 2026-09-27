using System.IO;
using FitGirlLauncher.Models;

namespace FitGirlLauncher.Services;

public record ScanResult(IReadOnlyList<GameEntry> Games, string? Error)
{
    public static ScanResult NoGames() => new(Array.Empty<GameEntry>(), null);
}

/// <summary>
/// Scans a library folder for subfolders whose name contains "[FitGirl Repack]".
/// Everything else in the folder is ignored.
/// </summary>
public static class LibraryScanner
{
    public static ScanResult Scan(string libraryPath)
    {
        try
        {
            if (!Directory.Exists(libraryPath))
                return new ScanResult(Array.Empty<GameEntry>(), $"Folder not found: {libraryPath}");

            var games = new List<GameEntry>();
            foreach (var dir in Directory.EnumerateDirectories(libraryPath))
            {
                var name = Path.GetFileName(dir);
                // Tolerate underscored variants like "[FitGirl_Repack]" as well as the
                // canonical "[FitGirl Repack]" — normalize separators before matching.
                var normalized = name.Replace('_', ' ');
                if (!normalized.Contains(TitleCleaner.RepackTag, StringComparison.OrdinalIgnoreCase))
                    continue;
                games.Add(new GameEntry(name, dir, TitleCleaner.Clean(name)));
            }
            return new ScanResult(games, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return new ScanResult(Array.Empty<GameEntry>(), ex.Message);
        }
    }
}
