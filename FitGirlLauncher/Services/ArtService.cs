using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using FitGirlLauncher.Models;

namespace FitGirlLauncher.Services;

/// <summary>
/// Resolves cover art from SteamGridDB and caches it on disk (keyed by cleaned title)
/// under %AppData%\FitGirlLauncher\art-cache, so repeat scans never re-hit the API.
/// Works a background queue at ~1 request/second to stay under the API rate limit.
/// </summary>
public class ArtService
{
    private static readonly HttpClient DownloadHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly ConcurrentDictionary<string, string> _index = new();
    private readonly HashSet<string> _queued = new();
    private readonly object _queueLock = new();
    private readonly ConcurrentQueue<GameEntry> _pending = new();
    private readonly Dictionary<string, List<GameEntry>> _inFlight = new();
    private readonly object _inFlightLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private Task? _worker;

    /// <summary>
    /// Spacing between API calls. SGDB's free tier is undocumented but visibly throttles
    /// bursts (with 404s!), so we stay comfortably under ~1 request/second.
    /// </summary>
    private const int RequestDelayMs = 1500;

    /// <summary>Raised (on the UI thread) for problems the user can act on, e.g. a rejected API key.</summary>
    public event Action<string>? ErrorReported;

    public ArtService()
    {
        LoadIndex();
    }

    /// <summary>
    /// Makes sure <paramref name="game"/> gets cover art: instantly from the local cache
    /// if we've matched it before, otherwise enqueued for a background API lookup.
    /// </summary>
    public void EnsureArt(GameEntry game, string apiKey)
    {
        var key = KeyFor(game.CleanTitle);

        if (_index.TryGetValue(key, out var cachedFile))
        {
            var cachedPath = Path.Combine(SettingsStore.ArtCacheDir, cachedFile);
            if (File.Exists(cachedPath))
            {
                ApplyArt(key, cachedPath, game);
                return;
            }
        }

        if (game.ArtResolved)
            return;

        var isPiggybacking = false;
        lock (_inFlightLock)
        {
            if (_inFlight.TryGetValue(key, out var waiting))
            {
                // A lookup for this title is already running — piggyback on its result
                // (e.g. the user hit Refresh while art was still loading).
                if (!waiting.Contains(game))
                    waiting.Add(game);
                isPiggybacking = true;
            }
            else
            {
                _inFlight[key] = new List<GameEntry> { game };
            }
        }

        // Show the tile's spinner for the duration of the lookup.
        game.ArtLoading = true;
        if (isPiggybacking)
            return;

        lock (_queueLock)
        {
            if (_queued.Contains(key))
                return;
            _queued.Add(key);
        }

        _pending.Enqueue(game);
        _worker ??= Task.Run(WorkerLoop);
    }

    private async Task WorkerLoop()
    {
        while (_pending.TryDequeue(out var game))
        {
            try
            {
                var apiKey = SettingsStore.Load().SteamGridDbApiKey.Trim();
                if (apiKey.Length == 0)
                {
                    MarkResolved(KeyFor(game.CleanTitle), game);
                }
                else
                {
                    await ResolveAsync(game, apiKey);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Bad key: mark this game and everything still queued as resolved,
                // stop the queue, and tell the user once.
                MarkResolved(KeyFor(game.CleanTitle), game);
                DrainQueueAsResolved();
                ReportError("Cover art is off — SteamGridDB rejected the API key. Check Settings.");
                return;
            }
            catch (Exception)
            {
                // Transient network/API hiccup: leave it unresolved so a Refresh retries.
                FailInFlight(KeyFor(game.CleanTitle));
            }

            await Task.Delay(100);
        }
    }

    private async Task ResolveAsync(GameEntry game, string apiKey)
    {
        var key = KeyFor(game.CleanTitle);
        var client = new SteamGridDbClient(apiKey);

        await ThrottleAsync();
        var results = await client.SearchAsync(game.CleanTitle);
        var candidates = PickCandidates(results, game.CleanTitle);
        if (candidates.Count == 0)
        {
            if (await TrySteamFallbackAsync(key, game))
                return;
            MarkResolved(key, game);
            return;
        }

        // SteamGridDB can hold several entries under the exact same name (two different
        // games called "Waterpark Simulator" exist, and only one of them has art).
        // Walk the candidates best-first and take the first one that actually yields art.
        SgdbGame? match = null;
        string? url = null;
        foreach (var candidate in candidates)
        {
            await ThrottleAsync();
            var candidateUrl = await client.GetCoverUrlAsync(candidate.Id);
            if (!string.IsNullOrEmpty(candidateUrl))
            {
                match = candidate;
                url = candidateUrl;
                break;
            }
        }

        if (match == null || string.IsNullOrEmpty(url))
        {
            if (await TrySteamFallbackAsync(key, game))
                return;
            MarkResolved(key, game);
            return;
        }

        var fileName = $"sgdb-{match.Id}{ExtensionFor(url)}";
        await CacheAndApplyAsync(key, fileName, url, game);
    }

    /// <summary>Downloads an art image (skipping the download when it's already cached),
    /// records it in the index, and applies it to the game.</summary>
    private async Task CacheAndApplyAsync(string key, string fileName, string url, GameEntry game)
    {
        var filePath = Path.Combine(SettingsStore.ArtCacheDir, fileName);
        if (!File.Exists(filePath))
        {
            Directory.CreateDirectory(SettingsStore.ArtCacheDir);
            using var stream = await DownloadHttp.GetStreamAsync(url);
            await using var file = File.Create(filePath);
            await stream.CopyToAsync(file);
        }

        _index[key] = fileName;
        SaveIndex();
        ApplyArt(key, filePath, game);
    }

    /// <summary>Steam store-art fallback for when SteamGridDB has nothing. Resolves the
    /// game's Steam cover and applies it. Returns true when art was applied, false when
    /// there is no Steam art (the caller then marks the game resolved → placeholder).
    /// Network errors are NOT swallowed: they propagate to the worker's catch so the game
    /// stays unresolved and a later refresh retries.</summary>
    private async Task<bool> TrySteamFallbackAsync(string key, GameEntry game)
    {
        var steamUrl = await TrySteamCoverAsync(game);
        if (string.IsNullOrEmpty(steamUrl))
            return false;

        var fileName = $"steam-{StableSteamId(steamUrl)}{ExtensionFor(steamUrl)}";
        await CacheAndApplyAsync(key, fileName, steamUrl, game);
        return true;
    }

    /// <summary>The game's Steam store cover (capsule v5, else header banner) for a cleaned
    /// title. Null/empty when the game has no Steam listing or the listing exposes no store
    /// art. Runs its own storesearch + appdetails through the shared throttle.</summary>
    private async Task<string?> TrySteamCoverAsync(GameEntry game)
    {
        var steam = new SteamStoreClient();

        await ThrottleAsync();
        var hit = await steam.ResolveAppIdAsync(game.CleanTitle);
        if (hit is null)
            return null; // genuinely not on Steam — no store art

        await ThrottleAsync();
        var details = await steam.GetAppDetailsAsync(hit.AppId);
        return details?.CoverImageUrl; // "" when delisted / no store art
    }

    /// <summary>Stable, unique cache-filename stem for a Steam store image — the App ID
    /// embedded in the URL (.../steam/apps/{appid}/...).</summary>
    private static string StableSteamId(string url)
    {
        const string marker = "/steam/apps/";
        var i = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            var rest = url.Substring(i + marker.Length);
            var slash = rest.IndexOf('/');
            var id = slash > 0 ? rest.Substring(0, slash) : rest;
            if (id.Length > 0)
                return id;
        }
        return "store";
    }

    // ---- matching ----

    /// <summary>
    /// Scores candidates: exact &gt; prefix &gt; contains. Roman numerals are normalized
    /// to digits on both sides, so "Red Dead Redemption 2" matches "Red Dead Redemption II".
    /// Returns the matches best-first (ties keep the API's order), capped — the caller
    /// tries each one in turn until one yields art, which covers duplicate names.
    /// </summary>
    private static List<SgdbGame> PickCandidates(IReadOnlyList<SgdbGame> results, string cleanTitle, int max = 4)
    {
        var target = Normalize(cleanTitle);
        if (target.Length == 0)
            return new List<SgdbGame>();

        var scored = new List<(SgdbGame Game, int Score)>();
        foreach (var candidate in results)
        {
            var name = Normalize(candidate.Name);
            if (name.Length == 0)
                continue;

            var score = name == target ? 3
                : name.StartsWith(target, StringComparison.Ordinal) ||
                  target.StartsWith(name, StringComparison.Ordinal) ? 2
                : name.Contains(target, StringComparison.Ordinal) ? 1
                : 0;

            if (score >= 1)
                scored.Add((candidate, score));
        }

        // OrderBy is stable, so same-score candidates keep their original order.
        return scored
            .OrderByDescending(t => t.Score)
            .Take(max)
            .Select(t => t.Game)
            .ToList();
    }

    private static string Normalize(string title)
    {
        var t = title.ToLowerInvariant();
        var chars = new char[t.Length];
        var len = 0;
        foreach (var c in t)
        {
            if (char.IsLetterOrDigit(c))
                chars[len++] = c;
            else if (len > 0 && chars[len - 1] != ' ')
                chars[len++] = ' ';
        }
        t = new string(chars, 0, len).Trim();

        // Roman numeral tokens -> digits, so both sides of the comparison line up.
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (RomanNumerals.TryGetValue(parts[i], out var number))
                parts[i] = number.ToString();
        }
        return string.Join(' ', parts);
    }

    private static readonly Dictionary<string, int> RomanNumerals = new()
    {
        ["i"] = 1, ["ii"] = 2, ["iii"] = 3, ["iv"] = 4, ["v"] = 5,
        ["vi"] = 6, ["vii"] = 7, ["viii"] = 8, ["ix"] = 9, ["x"] = 10
    };

    private static string KeyFor(string title) => Normalize(title);

    private static string ExtensionFor(string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        return ext.Length > 0 ? ext : ".jpg";
    }

    // ---- UI + file plumbing ----

    /// <summary>Takes the group of tiles waiting on this title out of the in-flight table.</summary>
    private List<GameEntry> TakeGames(string key)
    {
        lock (_inFlightLock)
        {
            var games = _inFlight.TryGetValue(key, out var list) ? list : new List<GameEntry>();
            _inFlight.Remove(key);
            return games;
        }
    }

    /// <summary>Tiles keep their placeholder, but we don't re-query for this title again.</summary>
    private void MarkResolved(string key, GameEntry trigger)
    {
        var games = TakeGames(key);
        if (!games.Contains(trigger))
            games.Add(trigger);
        Unqueue(key);
        ResolveOnUi(() =>
        {
            foreach (var game in games)
            {
                game.ArtResolved = true;
                game.ArtLoading = false;
            }
        });
    }

    /// <summary>Applies the art to every tile waiting on this title (plus the one that triggered it).</summary>
    private void ApplyArt(string key, string filePath, GameEntry trigger)
    {
        var games = TakeGames(key);
        if (!games.Contains(trigger))
            games.Add(trigger);
        Unqueue(key);
        ResolveOnUi(() =>
        {
            var image = TryLoadImage(filePath);
            foreach (var game in games)
            {
                game.ArtResolved = true;
                game.ArtLoading = false;
                if (image != null)
                    game.ArtImage = image;
            }
        });
    }

    /// <summary>Failed transiently — drop it so the next Refresh tries again.</summary>
    private void FailInFlight(string key)
    {
        var games = TakeGames(key);
        Unqueue(key);
        ResolveOnUi(() =>
        {
            foreach (var game in games)
                game.ArtLoading = false;
        });
    }

    private static BitmapImage? TryLoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null; // Undecodable file — tile keeps its placeholder.
        }
    }

    private static void ResolveOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.InvokeAsync(action);
    }

    private void DrainQueueAsResolved()
    {
        var drained = new List<GameEntry>();
        while (_pending.TryDequeue(out var game))
            drained.Add(game);

        var keys = new List<string>();
        lock (_inFlightLock)
        {
            foreach (var game in drained)
            {
                var key = KeyFor(game.CleanTitle);
                keys.Add(key);
                lock (_queueLock)
                {
                    _queued.Remove(key);
                }
            }
            foreach (var key in keys.Distinct())
                _inFlight.Remove(key);
        }

        ResolveOnUi(() =>
        {
            foreach (var game in drained)
            {
                game.ArtResolved = true;
                game.ArtLoading = false;
            }
        });
    }

    private void Unqueue(string key)
    {
        lock (_queueLock)
        {
            _queued.Remove(key);
        }
    }

    private async Task ThrottleAsync()
    {
        await Task.Run(() =>
        {
            var wait = _lastRequestUtc.AddMilliseconds(RequestDelayMs) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);
            _lastRequestUtc = DateTime.UtcNow;
        });
    }

    private void ReportError(string message)
    {
        ResolveOnUi(() => ErrorReported?.Invoke(message));
    }

    /// <summary>
    /// Deletes cached covers for titles that are no longer in the library, so the cache
    /// always mirrors the current game list. Only call this after a scan that actually
    /// read the library — an unreachable folder must never wipe the cache.
    /// </summary>
    public void PruneToCurrentLibrary(IEnumerable<string> currentTitles)
    {
        var current = new HashSet<string>(currentTitles.Select(KeyFor), StringComparer.Ordinal);
        var stale = _index
            .Where(pair => !current.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToList();
        if (stale.Count == 0)
            return;

        foreach (var key in stale)
        {
            if (!_index.TryRemove(key, out var fileName) || fileName == null)
                continue;
            try
            {
                var path = Path.Combine(SettingsStore.ArtCacheDir, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // File missing or locked — the index entry is already gone.
            }
        }
        SaveIndex();
    }

    private void LoadIndex()
    {
        try
        {
            if (File.Exists(SettingsStore.ArtIndexFilePath))
            {
                var json = File.ReadAllText(SettingsStore.ArtIndexFilePath);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map != null)
                {
                    foreach (var (key, file) in map)
                        _index[key] = file;
                }
            }
        }
        catch
        {
            // Corrupt index — start fresh.
        }
    }

    private void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.ArtCacheDir);
            File.WriteAllText(SettingsStore.ArtIndexFilePath, JsonSerializer.Serialize(_index));
        }
        catch
        {
            // Non-fatal — worst case we re-fetch art next time.
        }
    }
}
