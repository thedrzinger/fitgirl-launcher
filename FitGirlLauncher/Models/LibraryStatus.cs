namespace FitGirlLauncher.Models;

public enum LibraryStatus
{
    /// <summary>No library folder configured yet.</summary>
    NotSet,

    /// <summary>Folder is set but can't be reached (moved, deleted, network share down, permissions).</summary>
    Unreachable,

    /// <summary>Folder is fine but contains no [FitGirl Repack] folders.</summary>
    Empty,

    /// <summary>Games found and displayed.</summary>
    Ready
}
