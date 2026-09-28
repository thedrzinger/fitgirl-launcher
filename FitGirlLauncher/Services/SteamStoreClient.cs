using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FitGirlLauncher.Services;

/// <summary>A game hit from Steam's store search (title → App ID).</summary>
public record SteamSearchHit(int AppId, string Name);

/// <summary>Store details for an App ID: matched name, short description, screenshot
/// URLs (full-res + smaller strip thumbs), and the store cover used as an art fallback.</summary>
public record SteamAppDetails(
    int AppId,
    string Name,
    string ShortDescription,
    IReadOnlyList<string> ScreenshotUrls,
    IReadOnlyList<string> ScreenshotThumbUrls,
    string CoverImageUrl);

/// <summary>
/// Keyless Steam storefront API:
///   GET /api/storesearch/?term={title}&amp;l=english&amp;cc=us  → title → App ID
///   GET /api/appdetails?appids={appid}&amp;l=english             → description + screenshots
/// Steam rate-limits appdetails to roughly 200 calls / 5 min per IP and answers 403/429
/// when you push it — we back off and retry. Keep this in mind if a bulk "prefetch whole
/// library" mode is added later (space calls ~1.5s apart).
/// </summary>
public class SteamStoreClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://store.steampowered.com"),
        Timeout = TimeSpan.FromSeconds(30)
    };
    private static readonly int[] BackoffDelaysMs = { 5_000, 20_000 };

    /// <summary>
    /// Resolves a cleaned title to its Steam App ID. Null when Steam has no listing for it.
    /// Tries progressively looser search terms — Steam's search is too literal about
    /// punctuation ("Volkolak - The Will of Gods" returns 0 results while the store title
    /// "Volkolak: The Will of Gods" only matches the dash-free variant), so a folder-name
    /// dash can produce a false "NotOnSteam".
    /// </summary>
    public async Task<SteamSearchHit?> ResolveAppIdAsync(string title)
    {
        foreach (var term in BuildSearchTerms(title))
        {
            var hit = await SearchAsync(term);
            if (hit is not null)
                return hit;
        }
        return null;
    }

    private static IEnumerable<string> BuildSearchTerms(string title)
    {
        // 1. The cleaned title, as-is (works for the vast majority of games).
        yield return title;

        // 2. Punctuation stripped — dashes/colons/commas become spaces.
        var stripped = StripPunctuation(title);
        if (stripped.Length > 0 &&
            !string.Equals(stripped, title, StringComparison.OrdinalIgnoreCase))
            yield return stripped;

        // 3. Last resort: the distinctive leading word. Game titles are unique enough
        //    that searching just "Volkolak" surfaces the right listing.
        var firstWord = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstWord is { Length: >= 5 } &&
            !string.Equals(firstWord, stripped, StringComparison.Ordinal))
            yield return firstWord;
    }

    private static string StripPunctuation(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<SteamSearchHit?> SearchAsync(string term)
    {
        var path = $"/api/storesearch/?term={Uri.EscapeDataString(term)}&l=english&cc=us";
        using var response = await SendWithBackoffAsync(path);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0)
            return null;

        var first = items[0];
        return new SteamSearchHit(
            first.GetProperty("id").GetInt32(),
            first.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "");
    }

    /// <summary>
    /// Up to <paramref name="max"/> store-search hits for a term — the full "items" array,
    /// not just the first. For the manual "Fix Steam match" picker, where the user picks
    /// from the candidates. Empty list when Steam has no results.
    /// </summary>
    public async Task<List<SteamSearchHit>> SearchMultipleAsync(string term, int max = 8)
    {
        var path = $"/api/storesearch/?term={Uri.EscapeDataString(term)}&l=english&cc=us";
        using var response = await SendWithBackoffAsync(path);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        var hits = new List<SteamSearchHit>();
        if (doc.RootElement.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (hits.Count >= max)
                    break;
                hits.Add(new SteamSearchHit(
                    item.GetProperty("id").GetInt32(),
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : ""));
            }
        }
        return hits;
    }

    /// <summary>
    /// Fetches store details for an App ID. Null when Steam says success=false (e.g. delisted).
    /// CAUTION: the top-level JSON key is NOT the requested appid — Steam keys the response
    /// by an internal ID (e.g. asked 3293260, key was 4811290). Read the first property.
    /// </summary>
    public async Task<SteamAppDetails?> GetAppDetailsAsync(int appId)
    {
        var path = $"/api/appdetails?appids={appId}&l=english";
        using var response = await SendWithBackoffAsync(path);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        var entry = doc.RootElement.EnumerateObject().FirstOrDefault();
        if (entry.Value.ValueKind != JsonValueKind.Object ||
            !entry.Value.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !entry.Value.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
            return null;

        var name = data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var shortDescription = data.TryGetProperty("short_description", out var sd)
            ? sd.GetString() ?? ""
            : "";

        var screenshots = new List<string>();
        var screenshotThumbs = new List<string>();
        if (data.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
        {
            foreach (var shot in shots.EnumerateArray())
            {
                var full = shot.TryGetProperty("path_full", out var f) ? f.GetString() : null;
                if (string.IsNullOrWhiteSpace(full))
                    continue;
                var thumb = shot.TryGetProperty("path_thumbnail", out var t) ? t.GetString() : null;
                screenshots.Add(full!);
                // Small 600x338 thumb for the strip; fall back to the full-res URL
                // if a (rare) screenshot ships without a thumbnail.
                screenshotThumbs.Add(string.IsNullOrWhiteSpace(thumb) ? full! : thumb!);
            }
        }

        // Store cover for the art fallback: prefer the larger capsule, else the header banner.
        var cover = data.TryGetProperty("capsule_imagev5", out var c5) ? c5.GetString() : null;
        if (string.IsNullOrWhiteSpace(cover))
            cover = data.TryGetProperty("header_image", out var hi) ? hi.GetString() : null;

        return new SteamAppDetails(appId, name, shortDescription, screenshots, screenshotThumbs, cover ?? "");
    }

    /// <summary>
    /// GET that backs off (5s, then 20s) on 403/429 — Steam's rate-limit answers —
    /// before giving up. On success the caller owns the response.
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithBackoffAsync(string path)
    {
        HttpResponseMessage? response = null;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                response?.Dispose();
                response = await Http.GetAsync(path);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= BackoffDelaysMs.Length)
                        break; // retries exhausted — fall through to throw below
                    await Task.Delay(BackoffDelaysMs[attempt]);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return response; // caller owns it now
            }
        }
        catch
        {
            response?.Dispose();
            throw;
        }
        response.EnsureSuccessStatusCode();
        return response;
    }
}
