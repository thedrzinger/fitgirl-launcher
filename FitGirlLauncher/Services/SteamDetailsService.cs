using FitGirlLauncher.Models;

namespace FitGirlLauncher.Services;

/// <summary>Outcome of a full Steam lookup for one game (search → details).</summary>
public record SteamDetailResult(
    SteamLookupStatus Status,
    int? AppId,
    string? StoreName,
    string? Description,
    IReadOnlyList<string> ScreenshotUrls,
    IReadOnlyList<string> ScreenshotThumbUrls,
    string? Error);

/// <summary>
/// Orchestrates the two keyless Steam calls for a cleaned title:
/// storesearch → App ID, then appdetails → description + screenshots.
/// NotOnSteam covers both "no search results" and "appdetails success:false".
/// Network failures throw — the caller records a Failed status and allows retry.
/// </summary>
public class SteamDetailsService
{
    private readonly SteamStoreClient _client = new();

    public async Task<SteamDetailResult> ResolveAsync(string cleanTitle)
    {
        var hit = await _client.ResolveAppIdAsync(cleanTitle);
        if (hit is null)
            return new SteamDetailResult(SteamLookupStatus.NotOnSteam, null, null, null, Array.Empty<string>(), Array.Empty<string>(), null);

        var details = await _client.GetAppDetailsAsync(hit.AppId);
        if (details is null)
            return new SteamDetailResult(SteamLookupStatus.NotOnSteam, hit.AppId, hit.Name, null, Array.Empty<string>(), Array.Empty<string>(), null);

        return new SteamDetailResult(
            SteamLookupStatus.Found,
            details.AppId,
            details.Name,
            details.ShortDescription,
            details.ScreenshotUrls,
            details.ScreenshotThumbUrls,
            null);
    }
}
