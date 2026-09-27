using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FitGirlLauncher.Services;

public record SgdbGame(int Id, string Name);

internal record SgdbSearchResponse(bool Success, List<SgdbGame>? Data);
internal record SgdbGridResponse(bool Success, int Total, List<SgdbGrid>? Data);
internal record SgdbGrid(int Id, int Score, string? Style, string? Url, string? Thumb);

/// <summary>
/// SteamGridDB API v2 client.
/// Base: https://www.steamgriddb.com/api/v2 — bearer-token auth.
/// Endpoints used:
///   GET /search/autocomplete/{term}  — find a game by name
///   GET /grids/game/{gameId}         — cover art (grid) files for a game
/// </summary>
public class SteamGridDbClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://www.steamgriddb.com/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _apiKey;

    public SteamGridDbClient(string apiKey)
    {
        _apiKey = apiKey;
    }

    /// <summary>Searches for games by name. Returns nothing if there's no match.</summary>
    public async Task<List<SgdbGame>> SearchAsync(string term)
    {
        using var response = await SendAsync(
            $"/api/v2/search/autocomplete/{Uri.EscapeDataString(term)}");
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<SgdbSearchResponse>(JsonOptions);
        return doc?.Data ?? new List<SgdbGame>();
    }

    /// <summary>
    /// Gets a downloadable cover-art URL for a game, portrait sizes first, then any size.
    /// "No files match" comes back as a 200 with an empty data array and means "try the
    /// next, wider filter". A 404 is ambiguous ("Game not found" vs. rate-limit noise)
    /// — SendAsync sorts that out.
    /// </summary>
    public async Task<string?> GetCoverUrlAsync(int gameId)
    {
        foreach (var dimensions in new[] { "660x930", "600x900", null })
        {
            var path = $"/api/v2/grids/game/{gameId}?types=static&mimes=image/jpeg,image/png" +
                       $"&nsfw=false&humor=false&epilepsy=false&limit=1";
            if (dimensions != null)
                path += $"&dimensions={dimensions}";

            using var response = await SendAsync(path);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                continue;
            response.EnsureSuccessStatusCode();

            var doc = await response.Content.ReadFromJsonAsync<SgdbGridResponse>(JsonOptions);
            var url = doc?.Data?.FirstOrDefault()?.Url;
            if (!string.IsNullOrEmpty(url))
                return url;
            // 200 with empty data — same as a 404 here: widen the filter and try again.
        }
        return null;
    }

    /// <summary>Back-off between retries when the API answers with ambiguous 404s/429s.</summary>
    private static readonly int[] RetryDelaysMs = { 5000, 20000 };

    private async Task<HttpResponseMessage> SendAsync(string path)
    {
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            last?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            last = await Http.SendAsync(request);

            if (last.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("SteamGridDB rejected the API key.");

            var retryable = last.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
            if (!retryable && last.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A 404 with "Game not found" is definitive — the game id doesn't exist,
                // retrying is pointless. Any OTHER 404 body (or none) is rate-limit
                // noise, which this API uses instead of a proper 429.
                var body = await last.Content.ReadAsStringAsync();
                retryable = !body.Contains("Game not found", StringComparison.OrdinalIgnoreCase);
            }

            if (retryable && attempt < RetryDelaysMs.Length)
            {
                await Task.Delay(RetryDelaysMs[attempt]);
                continue;
            }
            break;
        }
        return last!;
    }
}
