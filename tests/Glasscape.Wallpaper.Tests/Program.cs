using System.Drawing;
using Glasscape.Wallpaper;

var monitors = new[]
{
    new MonitorInfo("left", "Left", new Rectangle(-1920, -160, 1920, 1080), false),
    new MonitorInfo("main", "Main", new Rectangle(0, 0, 2560, 1440), true)
};
var tests = new (string Name, Action Check)[]
{
    ("Primary selection uses the primary monitor, not enumeration order", () =>
        Equal("main", WallpaperPolicy.SelectMonitors(monitors, false, null).Single().Id)),
    ("Selected monitor is matched by id with negative desktop coordinates", () =>
        Equal(new Rectangle(-1920, -160, 1920, 1080), WallpaperPolicy.SelectMonitors(monitors, false, "left").Single().Bounds)),
    ("A disconnected selected monitor does not silently play on another monitor", () =>
        Equal(0, WallpaperPolicy.SelectMonitors(monitors, false, "unplugged").Count)),
    ("All monitor mode includes every monitor", () =>
        Equal(2, WallpaperPolicy.SelectMonitors(monitors, true, "unplugged").Count)),
    ("Unlocking does not undo user pause", () =>
        Equal(WallpaperPauseCause.User, WallpaperPolicy.EvaluatePause(true, false, false, true, false, true, false))),
    ("Automatic pause causes combine rather than overwriting each other", () =>
        Equal(WallpaperPauseCause.SessionLocked | WallpaperPauseCause.Battery | WallpaperPauseCause.Fullscreen,
            WallpaperPolicy.EvaluatePause(false, true, false, true, true, true, true))),
    ("Disabled battery and fullscreen rules leave playback running", () =>
        Equal(WallpaperPauseCause.None, WallpaperPolicy.EvaluatePause(false, false, false, false, true, false, true))),
    ("Suspended playback resumes only after all independent causes clear", () =>
        Equal(WallpaperPauseCause.Suspended | WallpaperPauseCause.User,
            WallpaperPolicy.EvaluatePause(true, false, true, false, false, false, false))),
    ("Full screen detection supports negative monitor origins", () =>
        Equal(true, WallpaperPolicy.CoversMonitor(new Rectangle(-1921, -161, 1922, 1082), monitors[0].Bounds))),
    ("A maximized working-area window is not full screen", () =>
        Equal(false, WallpaperPolicy.CoversMonitor(new Rectangle(0, 0, 2560, 1392), monitors[1].Bounds))),
    ("Zero-sized foreground windows cannot pause wallpaper", () =>
        Equal(false, WallpaperPolicy.CoversMonitor(Rectangle.Empty, Rectangle.Empty))),
    ("A user's fullscreen mpv video pauses wallpaper like any other fullscreen app", () =>
        Equal(false, WallpaperPolicy.ExcludesForegroundProcess(900, 1, [20, 21], "mpv"))),
    ("Owned desktop renderer and settings process do not trigger fullscreen pauses", () =>
    {
        Equal(true, WallpaperPolicy.ExcludesForegroundProcess(20, 1, [20, 21], "mpv"));
        Equal(true, WallpaperPolicy.ExcludesForegroundProcess(1, 1, [20, 21], "Glasscape"));
    }),
    ("A file name with spaces and shell syntax remains one literal mpv argument", () =>
    {
        const string path = "C:\\video folder\\한글 $(whoami) & demo.mp4";
        var arguments = MpvLaunchArguments.Create((nint)42, "glasscape-test", path, true, false, 25);
        Equal(path, arguments[^1]);
        Equal("--", arguments[^2]);
        Equal(1, arguments.Count(x => x == path));
        Equal(true, arguments.Contains("--panscan=1"));
        Equal(true, arguments.Contains("--volume=25"));
    }),
    ("mpv host handles are serialized unsigned even if the 32-bit sign bit is set", () =>
    {
        var arguments = MpvLaunchArguments.Create((nint)(-2147483647), "glasscape-test", "C:\\x.mp4", false, true, 200);
        Equal(true, arguments.Contains("--wid=2147483649"));
        Equal(true, arguments.Contains("--panscan=0"));
        Equal(true, arguments.Contains("--mute=yes"));
        Equal(true, arguments.Contains("--volume=100"));
    })
};

var failed = 0;
foreach (var test in tests)
{
    try { test.Check(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} wallpaper tests passed.");
return failed == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
