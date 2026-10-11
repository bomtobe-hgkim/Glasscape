using System.Text.Json;
using Glasscape.Settings;
using Glasscape.UI;
using Glasscape.Localization;
using System.Globalization;
using System.Resources;
using System.Collections;
using System.Text.RegularExpressions;

internal static class LocalizationChecks
{
    internal static IReadOnlyList<string> RunResourceChecks()
    {
        var failures = new List<string>();
        if (AppText.ResolveCulture(null, CultureInfo.GetCultureInfo("ko-KR")).Name != "ko-KR") failures.Add("Korean Windows UI culture must choose Korean.");
        if (AppText.ResolveCulture(null, CultureInfo.GetCultureInfo("fr-FR")).Name != "en-US") failures.Add("An unsupported Windows UI culture must fall back to English.");
        if (AppText.ResolveCulture("en-US", CultureInfo.GetCultureInfo("ko-KR")).Name != "en-US") failures.Add("An explicit English preference must override Korean Windows.");
        if (AppText.ResolveCulture("ko-KR", CultureInfo.GetCultureInfo("en-US")).Name != "ko-KR") failures.Add("An explicit Korean preference must override English Windows.");
        if (AppText.ResolveCulture("unsupported", CultureInfo.GetCultureInfo("fr-FR")).Name != "en-US") failures.Add("An unknown saved preference must use the system fallback.");
        var resources = new ResourceManager("Glasscape.Localization.Strings", typeof(AppText).Assembly);
        var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, true)!.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        var korean = resources.GetResourceSet(CultureInfo.GetCultureInfo("ko-KR"), true, false)!.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        if (!english.Keys.Order().SequenceEqual(korean.Keys.Order())) failures.Add("Both languages must contain the full resource key set; a missing Korean entry must not silently fall back.");
        foreach (var key in english.Keys.Intersect(korean.Keys))
        {
            if (string.IsNullOrWhiteSpace(english[key]) || string.IsNullOrWhiteSpace(korean[key])) failures.Add($"Translation '{key}' is empty.");
            var englishArguments = Regex.Matches(english[key], @"\{\d+\}").Select(match => match.Value).Order();
            var koreanArguments = Regex.Matches(korean[key], @"\{\d+\}").Select(match => match.Value).Order();
            if (!englishArguments.SequenceEqual(koreanArguments)) failures.Add($"Translation '{key}' changes its format arguments.");
        }
        var original = new WallpaperEntry { Id = new Guid("070571bc-861b-4a5e-8a51-5e1e5c96fd01"), Title = "오로라의 흐름" };
        AppText.SetLanguage("en-US");
        if (AppText.WallpaperTitle(original) != "Aurora Flow") failures.Add("An existing Korean bundled title must display in English without mutating its stored title.");
        if (original.Title != "오로라의 흐름") failures.Add("Displaying a bundled title must preserve stored user data.");
        original.Title = "My renamed scene";
        if (AppText.WallpaperTitle(original) != "My renamed scene") failures.Add("A user's renamed bundled wallpaper must retain its title.");
        var personal = new WallpaperEntry { Title = "오로라의 흐름" };
        if (AppText.WallpaperTitle(personal) != "오로라의 흐름") failures.Add("A personal import sharing a bundled title must not be renamed.");
        AppText.SetLanguage("ko-KR");
        if (!AppText.Format("PlaybackRunning", "scene", 2).Contains("2", StringComparison.Ordinal)) failures.Add("Localized playback status must keep its monitor count.");
        var directory = Path.Combine(Path.GetTempPath(), "Glasscape-localization-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            store.Save(new AppSettings { Language = "en-US" });
            if (store.Load().Language != "en-US") failures.Add("A saved English preference must survive a settings reload.");
            store.Save(new AppSettings { Language = "ko-KR" });
            if (store.Load().Language != "ko-KR") failures.Add("A saved Korean preference must survive a settings reload.");
            store.Save(new AppSettings { Language = null });
            if (store.Load().Language is not null) failures.Add("The system language preference must survive a settings reload.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        return failures;
    }

    internal static IReadOnlyList<string> Run()
    {
        var failures = new List<string>();
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Language\":\"en-US\"}")!;
        using var window = new MainWindow(settings, []) { ShowInTaskbar = false };
        window.Show();
        Application.DoEvents();
        var overview = Descendants(window).OfType<Button>().FirstOrDefault(button => button.AccessibleName == "Overview");
        if (overview is null) failures.Add("An explicit English preference must show English navigation.");
        if (Descendants(window).Any(control => control.Visible && ContainsKorean(control.Text)))
            failures.Add("The English overview contains untranslated Korean text.");
        var settingsPage = Descendants(window).OfType<Button>().FirstOrDefault(button => button.AccessibleName == "Settings");
        settingsPage?.PerformClick();
        var language = Descendants(window).OfType<ComboBox>().FirstOrDefault(combo => combo.Name == "Language");
        if (language is null) failures.Add("Settings must provide a usable language selector.");
        else
        {
            var changes = 0;
            window.SettingsChanged += _ => changes++;
            language.SelectedIndex = 2;
            Application.DoEvents();
            if (!Descendants(window).OfType<Button>().Any(button => button.AccessibleName == "설정"))
                failures.Add($"Selecting Korean must immediately translate the navigation (culture {AppText.Culture.Name}, text {AppText.Get("NavSettings")}, satellite {string.Join(",", typeof(AppText).Assembly.GetSatelliteAssembly(AppText.Culture).GetManifestResourceNames())}).");
            if (changes != 1) failures.Add("One language selection must persist exactly one settings change.");
            var serialized = JsonSerializer.Serialize(settings);
            if (!serialized.Contains("\"Language\":\"ko-KR\"", StringComparison.Ordinal))
                failures.Add("The selected Korean language must be persisted in AppSettings.");
            language.SelectedIndex = 1;
            Application.DoEvents();
            if (!Descendants(window).OfType<Button>().Any(button => button.AccessibleName == "Settings"))
                failures.Add("Switching back to English must refresh navigation without reopening the app.");
        }
        window.Close();
        return failures;
    }

    private static bool ContainsKorean(string text) => text.Any(character => character is >= '\uac00' and <= '\ud7a3');
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
