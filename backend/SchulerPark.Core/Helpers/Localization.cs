namespace SchulerPark.Core.Helpers;

using SchulerPark.Core.Enums;

/// <summary>
/// The two UI languages. A user's <c>PreferredLanguage</c> follows the language
/// they last used the web app in and selects the language of their emails and
/// push notifications. German is the default (the app's fallback language).
/// </summary>
public static class Localization
{
    public const string German = "de";
    public const string English = "en";
    public const string Default = German;

    public static readonly IReadOnlyList<string> Supported = [German, English];

    public static bool IsSupported(string? language) =>
        language != null && Supported.Contains(language, StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps any BCP-47-ish tag ("en-GB", "EN", null) onto a supported code.</summary>
    public static string Normalize(string? language) =>
        language != null && language.StartsWith(English, StringComparison.OrdinalIgnoreCase)
            ? English
            : German;

    public static bool IsGerman(string? language) => Normalize(language) == German;

    public static string TimeSlotLabel(TimeSlot slot, string? language) =>
        (slot, IsGerman(language)) switch
        {
            (TimeSlot.Morning, true) => "Vormittag",
            (TimeSlot.Afternoon, true) => "Nachmittag",
            (TimeSlot.Morning, false) => "Morning",
            _ => "Afternoon",
        };
}
