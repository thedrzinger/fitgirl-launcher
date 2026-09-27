using System.Text.RegularExpressions;

namespace FitGirlLauncher.Services;

/// <summary>
/// Turns raw "[FitGirl Repack]" folder names into clean display titles.
/// Pure + static so it's trivial to test.
/// </summary>
public static class TitleCleaner
{
    /// <summary>The tag that marks a folder as a FitGirl repack (also used as the scan filter).</summary>
    public const string RepackTag = "[FitGirl Repack]";

    // [FitGirl Repack], tolerant of _ . - and spaces between the words, case-insensitive
    private static readonly Regex FitGirlTag = new(
        @"\[\s*Fit[\s._-]*Girl[\s._-]*Repack[\s._-]*\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Dotted versions: 2.1, v1.8.3, 1.20 — but NOT bare years/numbers like "2077" or "GTA 5"
    private static readonly Regex DottedVersion = new(
        @"\bv?\d+(\.\d+)+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bare "v2" / "V3" style version markers
    private static readonly Regex PlainVersion = new(
        @"\bv\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 8-digit date stamps, e.g. 20250410
    private static readonly Regex DateStamp = new(
        @"\b\d{8}\b",
        RegexOptions.Compiled);

    private static readonly Regex X64 = new(
        @"\bx64\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Scene/repack junk words (whole words only)
    private static readonly Regex JunkWords = new(
        @"\b(?:repack|retail|cracked|english|multilingual|multi|pc|steam|fitgirl|girl)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Dots used as word separators -> spaces, but keep real abbreviations:
    // "Red.Dead" -> "Red Dead" while "F.E.A.R" stays (the run after each dot is a single letter).
    private static readonly Regex DotSeparator = new(
        @"\.(\w{2,})",
        RegexOptions.Compiled);

    private static readonly Regex MultiSpace = new(
        @"\s{2,}",
        RegexOptions.Compiled);

    public static string Clean(string folderName)
    {
        var title = FitGirlTag.Replace(folderName, " ");
        title = title.Replace('_', ' ');
        // Strip versions BEFORE touching dots — otherwise "v1.12.10" gets split into "v1 12 10"
        // and the version pattern no longer recognizes it.
        title = DottedVersion.Replace(title, " ");
        title = DateStamp.Replace(title, " ");
        title = X64.Replace(title, " ");
        title = JunkWords.Replace(title, " ");
        title = PlainVersion.Replace(title, " ");
        title = DotSeparator.Replace(title, " $1");
        title = MultiSpace.Replace(title, " ").Trim().Trim('-', '.');

        return string.IsNullOrWhiteSpace(title) ? folderName : title;
    }
}
