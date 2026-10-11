using System.Globalization;
using System.Resources;
using Glasscape.Settings;

namespace Glasscape.Localization;

public static class AppText
{
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static readonly ResourceManager Resources = new("Glasscape.Localization.Strings", typeof(AppText).Assembly);
    public static CultureInfo Culture { get; private set; } = ResolveCulture(null);

    public static CultureInfo ResolveCulture(string? preference, CultureInfo? systemCulture = null)
    {
        if (string.Equals(preference, "en-US", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo("en-US");
        if (string.Equals(preference, "ko-KR", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo("ko-KR");
        return CultureInfo.GetCultureInfo((systemCulture ?? SystemCulture).TwoLetterISOLanguageName == "ko" ? "ko-KR" : "en-US");
    }

    public static void SetLanguage(string? preference)
    {
        Culture = ResolveCulture(preference);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    public static string Get(string key) => Resources.GetString(key, Culture) ?? throw new MissingManifestResourceException($"Missing Glasscape translation: {key}");
    public static string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);

    // Localize the two original bundled titles only when their stored title is still a default.
    // Imported files and user-renamed entries retain the user's own title.
    public static string WallpaperTitle(WallpaperEntry entry) => WallpaperTitle(entry.Id, entry.Title);
    public static string WallpaperTitle(Guid? id, string? title)
    {
        var key = id == new Guid("070571bc-861b-4a5e-8a51-5e1e5c96fd01") ? "AuroraFlow"
            : id == new Guid("070571bc-861b-4a5e-8a51-5e1e5c96fd02") ? "OceanDrift" : null;
        if (key is not null && (title == Resources.GetString(key, CultureInfo.GetCultureInfo("en-US")) || title == Resources.GetString(key, CultureInfo.GetCultureInfo("ko-KR"))))
            return Get(key);
        return title ?? "";
    }
}
