namespace FitGirlLauncher.Models;

/// <summary>Outcome of a Steam store lookup for a game. Persisted so we only hit the network once.</summary>
public enum SteamLookupStatus
{
    /// <summary>No lookup has run for this game yet.</summary>
    NotFetched,

    /// <summary>Found on Steam; description and screenshots are available.</summary>
    Found,

    /// <summary>No Steam listing (storesearch came back empty, or appdetails said success=false).</summary>
    NotOnSteam,

    /// <summary>The lookup failed (network etc.) — retry via the same refresh action.</summary>
    Failed
}
