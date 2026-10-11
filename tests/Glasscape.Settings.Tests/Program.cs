using System.Text.Json;
using TransparentTaskbar.Settings;

var temporaryRoot = Path.Combine(Path.GetTempPath(), "glasscape-settings-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryRoot);
var tests = new (string Name, Action Run)[]
{
    ("Missing settings return safe defaults without writing a user profile", () =>
    {
        var folder = Path.Combine(temporaryRoot, "missing");
        var settings = new SettingsStore(folder).Load();
        Check(settings.SchemaVersion == 1 && settings.TaskbarEnabled && settings.TaskbarOpacity == 0, "Default taskbar must be fully clear.");
        Check(!settings.StartWithWindows && settings.PauseWhenFullscreen && settings.PauseOnBattery, "Startup requires user choice; wallpaper pauses by default.");
        Check(settings.MuteWallpaper && settings.WallpaperVolume == 0 && settings.Wallpapers.Count == 0 && settings.ActiveWallpaperId is null, "Wallpaper defaults must be silent and empty.");
        Check(!Directory.Exists(folder), "Loading defaults must not create or modify files.");
    }),
    ("Save and reload preserve startup choices and a missing wallpaper", () =>
    {
        var store = Store("roundtrip");
        var id = Guid.Parse("b6d2eaed-217a-4aa8-aea1-601118210399");
        var missingFile = Path.Combine(temporaryRoot, "absent-video.mp4");
        store.Save(new AppSettings
        {
            StartWithWindows = true, TaskbarEnabled = false, TaskbarOpacity = 67,
            PauseWhenFullscreen = false, PauseOnBattery = false, MuteWallpaper = false, WallpaperVolume = 42,
            WallpaperFit = WallpaperFit.Fit, MonitorMode = MonitorMode.Selected, SelectedMonitorId = "DISPLAY2", ActiveWallpaperId = id,
            Wallpapers = [new() { Id = id, FilePath = missingFile, Title = "Ocean", CreatedUtc = DateTimeOffset.Parse("2026-10-08T12:00:00Z"), ThumbnailPath = Path.Combine(temporaryRoot, "absent-thumbnail.png") }]
        });
        var loaded = store.Load();
        Check(loaded.StartWithWindows && !loaded.TaskbarEnabled && loaded.TaskbarOpacity == 67 && loaded.WallpaperVolume == 42, "User choices must survive restart.");
        Check(!loaded.PauseWhenFullscreen && !loaded.PauseOnBattery && !loaded.MuteWallpaper, "Playback choices must survive restart.");
        Check(loaded.WallpaperFit == WallpaperFit.Fit && loaded.MonitorMode == MonitorMode.Selected && loaded.SelectedMonitorId == "DISPLAY2", "Monitor and fit choices must survive restart.");
        Check(loaded.ActiveWallpaperId == id && loaded.Wallpapers.Single().FilePath == missingFile && loaded.Wallpapers.Single().Title == "Ocean", "An unavailable wallpaper must remain in the library.");
        Check(loaded.Wallpapers.Single().CreatedUtc == DateTimeOffset.Parse("2026-10-08T12:00:00Z") && loaded.Wallpapers.Single().ThumbnailPath!.EndsWith("absent-thumbnail.png"), "Wallpaper metadata must survive restart.");
    }),
    ("Malformed JSON is preserved while safe defaults recover", () =>
    {
        var folder = Folder("corrupt");
        var path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, "{ bad-json");
        var store = new SettingsStore(folder);
        var loaded = store.Load();
        Check(loaded.TaskbarEnabled && !loaded.StartWithWindows, "Corrupt settings must recover safely.");
        var backups = Directory.GetFiles(folder, "settings.corrupt-*.json");
        Check(backups.Length == 1 && File.ReadAllText(backups[0]) == "{ bad-json", "The original corrupt data must be preserved.");
        Check(!string.IsNullOrWhiteSpace(store.LastLoadWarning), "Recovery must be reportable to the UI.");
        store.Save(loaded);
        Check(new SettingsStore(folder).Load().TaskbarEnabled, "Recovered settings must be saveable.");
    }),
    ("Invalid ranges and enum values normalize before persistence", () =>
    {
        var store = Store("validation");
        store.Save(new AppSettings { SchemaVersion = 500, TaskbarOpacity = -7, WallpaperVolume = 300, WallpaperFit = (WallpaperFit)999, MonitorMode = (MonitorMode)999, SelectedMonitorId = "  ", ActiveWallpaperId = Guid.NewGuid(), Wallpapers = null! });
        var loaded = store.Load();
        Check(loaded.SchemaVersion == 1 && loaded.TaskbarOpacity == 0 && loaded.WallpaperVolume == 100, "Numeric controls must stay within their valid range.");
        Check(loaded.WallpaperFit == WallpaperFit.Fill && loaded.MonitorMode == MonitorMode.Primary && loaded.SelectedMonitorId is null, "Invalid enum and empty monitor choices must become safe defaults.");
        Check(loaded.Wallpapers.Count == 0 && loaded.ActiveWallpaperId is null, "An empty library must not select a nonexistent entry.");
    }),
    ("Relative and duplicate library data normalize without checking file existence", () =>
    {
        var folder = Folder("library");
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(new AppSettings
        {
            ActiveWallpaperId = id,
            Wallpapers = [new() { Id = id, FilePath = "missing.mp4", Title = " " }, new() { Id = id, FilePath = "other.mp4" }, new() { FilePath = "bad\0path.mp4" }, null!]
        }));
        var loaded = new SettingsStore(folder).Load();
        Check(loaded.Wallpapers.Count == 2 && loaded.Wallpapers.Select(x => x.Id).Distinct().Count() == 2, "Valid missing entries must survive; invalid paths and null entries must not crash.");
        Check(loaded.Wallpapers[0].FilePath == Path.Combine(folder, "missing.mp4") && loaded.Wallpapers[0].Title == "missing", "Relative paths must normalize consistently with a readable title.");
        Check(loaded.ActiveWallpaperId == id && loaded.Wallpapers.All(x => x.CreatedUtc != default), "Selection and stable metadata must normalize.");
    }),
    ("A locked settings file recovers without crashing or losing its data", () =>
    {
        var folder = Folder("locked-load");
        var path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, "{\"TaskbarOpacity\":35}");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var store = new SettingsStore(folder);
            Check(store.Load().TaskbarOpacity == 0 && store.LastLoadWarning is not null, "Unreadable settings must return reportable defaults.");
        }
        Check(File.ReadAllText(path) == "{\"TaskbarOpacity\":35}", "An unreadable file must not be discarded.");
    }),
    ("A failed atomic save preserves the previous file and cleans temporary output", () =>
    {
        var folder = Folder("locked-save");
        var store = new SettingsStore(folder);
        store.Save(new AppSettings { TaskbarOpacity = 35 });
        var path = Path.Combine(folder, "settings.json");
        var previous = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ThrowsFileAccessFailure(() => store.Save(new AppSettings { TaskbarOpacity = 75 }));
        Check(File.ReadAllText(path) == previous, "Failure must not truncate or replace existing settings.");
        Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "A failed save must clean its staging file.");
        store.Save(new AppSettings { TaskbarOpacity = 75 });
        Check(store.Load().TaskbarOpacity == 75, "A subsequent save must recover normally.");
    }),
    ("Unexpected JSON types recover instead of escaping into the UI", () =>
    {
        var folder = Folder("wrong-type");
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"TaskbarOpacity\":\"bad\",\"Wallpapers\":false}");
        var store = new SettingsStore(folder);
        Check(store.Load().TaskbarOpacity == 0 && store.LastLoadWarning is not null, "Wrong property types must be treated as corrupt settings.");
    }),
    ("Startup command safely quotes the complete executable path", () =>
    {
        Check(StartupService.BuildCommand(@"C:\Program Files\Glasscape\TransparentTaskbar.exe") == "\"C:\\Program Files\\Glasscape\\TransparentTaskbar.exe\" --background", "Spaces must remain within the quoted executable path.");
        Check(StartupService.BuildCommand(@"C:\Glasscape\app.exe") == "\"C:\\Glasscape\\app.exe\" --background", "The command must be canonical even without spaces.");
    }),
    ("Startup command rejects relative and injected executable paths", () =>
    {
        foreach (var path in new[] { "", " ", "app.exe", "C:\\Glasscape\\app.exe\" --evil", "C:\\bad\0app.exe" })
            Throws<ArgumentException>(() => StartupService.BuildCommand(path));
    }),
    ("A fresh library includes only the two released videos even with excluded files present", () =>
    {
        var root = SampleFolder("bundled-fresh");
        var settings = new AppSettings();
        BundledWallpapers.Seed(settings, root);
        Check(settings.Wallpapers.Select(w => Path.GetFileName(w.FilePath)).SequenceEqual(new[] { "aurora-flow.mp4", "ocean-drift.mp4" }), "Only the two released defaults may be seeded, even when all five video files exist.");
        Check(settings.Wallpapers.Select(w => w.Id).SequenceEqual(new[] { Guid.Parse("070571bc-861b-4a5e-8a51-5e1e5c96fd01"), Guid.Parse("070571bc-861b-4a5e-8a51-5e1e5c96fd02") }), "The two released defaults must retain their existing identities.");
        Check(settings.Wallpapers.All(w => File.Exists(w.FilePath) && File.Exists(w.ThumbnailPath)), "Every bundled entry must point at the packaged video and poster.");
        Check(settings.ActiveWallpaperId is null, "Adding defaults must not start playback.");
    }),
    ("Upgrading preserves a retired default and never restores removed original defaults", () =>
    {
        var root = SampleFolder("bundled-upgrade");
        var town = new WallpaperEntry { Id = Guid.Parse("c021cfd5-99bd-49b7-9866-2c49f8da98cf"), Title = "My town", FilePath = Path.Combine(temporaryRoot, "old-town.mp4"), ThumbnailPath = Path.Combine(temporaryRoot, "old-town.png"), CreatedUtc = DateTimeOffset.Parse("2026-10-08T12:00:00Z") };
        var settings = new AppSettings { BundledSamplesInitialized = true, ActiveWallpaperId = town.Id, Wallpapers = [town] };
        BundledWallpapers.Seed(settings, root);
        Check(settings.Wallpapers.Count == 1 && ReferenceEquals(settings.Wallpapers.Single(), town), "Retired defaults must remain in the user's library; removed original defaults must stay removed.");
        Check(settings.ActiveWallpaperId == town.Id && town.FilePath == Path.Combine(temporaryRoot, "old-town.mp4") && town.Title == "My town", "An existing selected entry must keep its path, title and identity.");
        Check(town.ThumbnailPath == Path.Combine(temporaryRoot, "old-town.png") && town.CreatedUtc == DateTimeOffset.Parse("2026-10-08T12:00:00Z"), "Retired entry metadata must remain unchanged.");
        BundledWallpapers.Seed(settings, root);
        Check(settings.Wallpapers.Count == 1, "Restart must not add excluded or removed defaults.");
    }),
    ("Personal imports named rain rain2 and town retain their metadata and selection", () =>
    {
        var root = SampleFolder("bundled-personal-imports");
        var imports = new[] { "rain", "rain2", "town" }.Select((name, index) => new WallpaperEntry
        {
            Id = Guid.NewGuid(), Title = "Personal " + name,
            FilePath = Path.Combine(temporaryRoot, "personal", name + ".mp4"),
            ThumbnailPath = Path.Combine(temporaryRoot, "personal", name + ".png"),
            CreatedUtc = DateTimeOffset.Parse("2026-10-08T12:00:00Z").AddDays(index)
        }).ToArray();
        var settings = new AppSettings { Wallpapers = [.. imports], ActiveWallpaperId = imports[1].Id };
        BundledWallpapers.Seed(settings, root);
        var store = Store("bundled-personal-imports-settings");
        store.Save(settings);
        var reloaded = store.Load();
        BundledWallpapers.Seed(reloaded, root);
        Check(reloaded.Wallpapers.Count == 5 && reloaded.ActiveWallpaperId == imports[1].Id, "Only two defaults may be added without changing the selected personal import.");
        foreach (var imported in imports)
        {
            var actual = reloaded.Wallpapers.Single(w => w.Id == imported.Id);
            Check(actual.FilePath == imported.FilePath && actual.Title == imported.Title && actual.ThumbnailPath == imported.ThumbnailPath && actual.CreatedUtc == imported.CreatedUtc, "Personal imports must survive seeding and persistence without metadata changes.");
        }
    }),
    ("Removing an initialized default survives saving and restarting", () =>
    {
        var root = SampleFolder("bundled-removal");
        var settings = new AppSettings();
        BundledWallpapers.Seed(settings, root);
        settings.Wallpapers.RemoveAll(w => Path.GetFileName(w.FilePath) == "aurora-flow.mp4");
        var store = Store("bundled-removal-settings");
        store.Save(settings);
        var reloaded = store.Load();
        BundledWallpapers.Seed(reloaded, root);
        Check(reloaded.Wallpapers.Count == 1 && Path.GetFileName(reloaded.Wallpapers.Single().FilePath) == "ocean-drift.mp4", "A removed default must stay removed after restart.");
    }),
    ("An incomplete sample folder retries missing defaults after files arrive", () =>
    {
        var root = SampleFolder("bundled-partial");
        var missing = Path.Combine(root, "samples", "ocean-drift.mp4");
        File.Delete(missing);
        var settings = new AppSettings();
        BundledWallpapers.Seed(settings, root);
        Check(settings.Wallpapers.Count == 1, "Missing packaged files must not create broken default entries.");
        File.WriteAllText(missing, "video fixture");
        BundledWallpapers.Seed(settings, root);
        Check(settings.Wallpapers.Count == 2 && settings.Wallpapers.Select(w => w.Id).Distinct().Count() == 2, "A later complete package must add the missing released default without duplicates.");
    }),
    ("Repairing an incomplete package does not resurrect a removed default", () =>
    {
        var root = SampleFolder("bundled-partial-removal");
        var missing = Path.Combine(root, "samples", "ocean-drift.mp4"); File.Delete(missing);
        var settings = new AppSettings(); BundledWallpapers.Seed(settings, root);
        settings.Wallpapers.RemoveAll(w => Path.GetFileName(w.FilePath) == "aurora-flow.mp4");
        var store = Store("bundled-partial-removal-settings"); store.Save(settings);
        var reloaded = store.Load();
        File.WriteAllText(missing, "video fixture"); BundledWallpapers.Seed(reloaded, root);
        Check(reloaded.Wallpapers.Count == 1 && Path.GetFileName(reloaded.Wallpapers.Single().FilePath) == "ocean-drift.mp4", "Repair must add the never-offered released sample while preserving removal of an already offered sample.");
    })
};
var failures = 0;
try
{
    foreach (var (name, run) in tests)
    {
        try { run(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
    }
}
finally
{
    if (Path.GetDirectoryName(temporaryRoot) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
        Directory.Delete(temporaryRoot, true);
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} settings tests passed. No registry writes were performed.");
return failures == 0 ? 0 : 1;

string Folder(string name) { var path = Path.Combine(temporaryRoot, name); Directory.CreateDirectory(path); return path; }
SettingsStore Store(string name) => new(Path.Combine(temporaryRoot, name));
string SampleFolder(string name)
{
    var root = Folder(name);
    var samples = Path.Combine(root, "samples"); Directory.CreateDirectory(samples);
    foreach (var sample in new[] { "aurora-flow", "ocean-drift", "rain", "rain2", "town" })
    {
        File.WriteAllText(Path.Combine(samples, sample + ".mp4"), "video fixture");
        File.WriteAllText(Path.Combine(samples, sample + ".png"), "poster fixture");
    }
    return root;
}
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
static void ThrowsFileAccessFailure(Action action)
{
    try { action(); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
    throw new InvalidOperationException("Expected an I/O or access-denied save failure.");
}
