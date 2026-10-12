using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Text.Json;
using Glasscape.Wallpaper;

// MpvSession tests launch this executable again as a scripted stand-in for mpv.
if (args.FirstOrDefault(argument => argument.StartsWith("--input-ipc-server=", StringComparison.Ordinal)) is { } ipc)
    return await RunFakePlayerAsync(ipc[(ipc.LastIndexOf('\\') + 1)..], Environment.GetEnvironmentVariable(FakePlayer.ModeVariable) ?? "");

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
    }),
    ("A player that answers slowly while opening the video is waited for, not reported as timed out", () =>
        Equal("ready", FakePlayer.Observe("slow-get:1500", TimeSpan.FromSeconds(4)))),
    ("A slow reply to a playback command is retried instead of stopping the wallpaper", () =>
        Equal("ready", FakePlayer.Observe("slow-set:1500", TimeSpan.FromSeconds(4)))),
    ("A player that exits before showing video is not restarted on its own, since the file may be bad", () =>
        Equal(true, FakePlayer.Observe("exit:2", TimeSpan.FromSeconds(2)).StartsWith("fatal: ", StringComparison.Ordinal)))
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

// Modes: "slow-get:<ms>" or "slow-set:<ms>" delay the first such reply, as mpv does while it opens
// the file and GPU output; "exit:<code>" quits before the IPC pipe exists.
static async Task<int> RunFakePlayerAsync(string pipeName, string mode)
{
    var (kind, value) = mode.Split(':') is [var name, var number] ? (name, int.Parse(number)) : ("", 0);
    if (kind == "exit") return value;
    using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    await server.WaitForConnectionAsync();
    using var reader = new StreamReader(server);
    using var writer = new StreamWriter(server) { AutoFlush = true };
    var delayed = false;
    while (await reader.ReadLineAsync() is { } line)
    {
        using var request = JsonDocument.Parse(line);
        var id = request.RootElement.GetProperty("request_id").GetInt64();
        var command = request.RootElement.GetProperty("command")[0].GetString();
        if (!delayed && (kind, command) is ("slow-get", "get_property") or ("slow-set", "set_property"))
        {
            delayed = true;
            await Task.Delay(value);
        }
        await writer.WriteLineAsync(command == "get_property"
            ? $"{{\"request_id\":{id},\"error\":\"success\",\"data\":{{\"w\":1920,\"h\":1080}}}}"
            : $"{{\"request_id\":{id},\"error\":\"success\"}}");
    }
    return 0;
}

static class FakePlayer
{
    internal const string ModeVariable = "GLASSCAPE_FAKE_MPV";

    // Returns every readiness or failure report made while observing, in order.
    internal static string Observe(string mode, TimeSpan duration)
    {
        Environment.SetEnvironmentVariable(ModeVariable, mode);
        var reports = new ConcurrentQueue<string>();
        using var host = new WallpaperHostWindow();
        using var session = new MpvSession(Environment.ProcessPath!, "C:\\fake.mp4", host,
            new MonitorInfo("main", "Main", new Rectangle(0, 0, 1920, 1080), true),
            new MpvPlaybackState(false, true, 0, true), (_, fault) => reports.Enqueue(fault is null ? "ready"
                : $"{(fault.Recoverable ? "recoverable" : "fatal")}: {fault.Message}"));
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < duration) Thread.Sleep(50);
        return string.Join(" | ", reports);
    }
}
