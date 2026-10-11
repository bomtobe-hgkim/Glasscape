using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Glasscape.Settings;
using Glasscape.Wallpaper;

internal static class WallpaperChecks
{
    internal static bool Run(string outputDirectory)
    {
        var directory = Path.Combine(outputDirectory, "wallpaper");
        Directory.CreateDirectory(directory);
        var runtime = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "mpv", "mpv.exe"));
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "aurora-flow.mp4");
        if (!File.Exists(runtime) || !File.Exists(sample))
        {
            Console.Error.WriteLine("FAIL: Wallpaper verification requires bundled mpv/mpv.exe and samples/aurora-flow.mp4.");
            return false;
        }
        var existing = BundledPlayers(runtime);
        var owned = new HashSet<int>();
        var baselines = new Dictionary<string, Bitmap>();
        DesktopWallpaperService? service = null;
        object? shell = null;
        var minimized = false;
        var success = false;
        var autoHide = new TaskbarAutoHideService();
        var originalAutoHide = autoHide.IsEnabled;
        using var context = new ApplicationContext();
        using var start = new System.Windows.Forms.Timer { Interval = 50 };
        start.Tick += (_, _) => { start.Stop(); _ = CheckAsync(); };
        start.Start();
        Application.Run(context);
        return success;

        async Task CheckAsync()
        {
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")
                    ?? throw new InvalidOperationException("Windows Shell automation is unavailable."));
                ((dynamic)shell!).MinimizeAll();
                minimized = true;
                await Task.Delay(1000); // Let the original window minimize animations finish.
                var monitors = DesktopWallpaperService.GetMonitors();
                var primary = monitors.First(monitor => monitor.Primary);
                foreach (var monitor in monitors) baselines[monitor.Id] = Capture(Central(monitor.Bounds));
                baselines[primary.Id].Save(Path.Combine(directory, "before.png"), ImageFormat.Png);
                DescribeHitTest(primary.Bounds, "before playback");
                var entry = new WallpaperEntry { FilePath = sample, Title = "Integration Aurora" };
                var settings = new AppSettings
                {
                    ActiveWallpaperId = entry.Id, Wallpapers = [entry], MonitorMode = MonitorMode.Primary,
                    MuteWallpaper = true, WallpaperVolume = 0, WallpaperFit = WallpaperFit.Fill,
                    PauseWhenFullscreen = false, PauseOnBattery = false
                };
                service = new DesktopWallpaperService();
                service.StatusChanged += status => Console.WriteLine(
                    $"Wallpaper status: running={status.IsRunning}, paused={status.IsPaused}, monitors={status.MonitorCount}, error={status.Error}");
                service.Apply(settings);
                RememberOwned();
                await Ready(1);
                await Delay(750);
                using var first = Capture(Central(primary.Bounds));
                first.Save(Path.Combine(directory, "aurora-first.png"), ImageFormat.Png);
                Require(Difference(baselines[primary.Id], first) > 3,
                    "Video readiness was reported but primary-screen pixels did not change from the desktop.");
                await Delay(1100);
                using var second = Capture(Central(primary.Bounds));
                second.Save(Path.Combine(directory, "aurora-moving.png"), ImageFormat.Png);
                var animation = Difference(first, second);
                Require(animation > 0.5, $"Video did not visibly animate (pixel difference {animation:F3}).");
                Console.WriteLine($"PASS: Aurora renders visibly and animates on the primary monitor; difference={animation:F3}.");
                VerifyClickThrough(primary.Bounds);

                service.SetUserPaused(true);
                await Delay(1200); // Allow the bounded IPC command and the final presented frame to settle.
                Require(service.Status.IsPaused, "Pause status was not published.");
                using var paused = Capture(Central(primary.Bounds));
                paused.Save(Path.Combine(directory, "paused-first.png"), ImageFormat.Png);
                await Delay(1100);
                using var still = Capture(Central(primary.Bounds));
                still.Save(Path.Combine(directory, "paused-still.png"), ImageFormat.Png);
                var pauseDifference = Difference(paused, still);
                Require(pauseDifference < 0.3, $"Pause IPC did not stop animation (difference {pauseDifference:F3}).");
                service.SetUserPaused(false);
                await Delay(1200);
                using var resumed = Capture(Central(primary.Bounds));
                resumed.Save(Path.Combine(directory, "resumed.png"), ImageFormat.Png);
                Require(!service.Status.IsPaused && Difference(still, resumed) > 0.5,
                    "Resume IPC did not restart visible animation.");
                Console.WriteLine($"PASS: Pause freezes actual pixels and resume changes them; paused difference={pauseDifference:F3}.");

                settings.MonitorMode = MonitorMode.All;
                service.Apply(settings);
                RememberOwned();
                await Ready(monitors.Count);
                await Delay(1000);
                for (var index = 0; index < monitors.Count; index++)
                {
                    var monitor = monitors[index];
                    using var rendered = Capture(Central(monitor.Bounds));
                    rendered.Save(Path.Combine(directory, $"monitor-{index + 1}.png"), ImageFormat.Png);
                    var difference = Difference(baselines[monitor.Id], rendered);
                    Require(difference > 3, $"Monitor {monitor.Id} reported ready without visible video (difference {difference:F3}).");
                    VerifyClickThrough(monitor.Bounds);
                    Console.WriteLine($"PASS: Video is visible on {monitor.Id}, bounds={monitor.Bounds}, difference={difference:F3}.");
                }
                Require(owned.Count > 0, "No newly owned bundled mpv process could be identified.");
                service.Stop();
                await WaitUntil(() => OwnedPlayersGone(), 5000, "Stop left an owned mpv process running.");
                await Task.Delay(1000);
                VerifyRestored(primary, "restored-after-stop.png");
                Console.WriteLine("PASS: Stop removes owned players and restores the original desktop pixels.");

                settings.MonitorMode = MonitorMode.Primary;
                service.Apply(settings);
                RememberOwned();
                await Ready(1);
                service.Dispose();
                service = null;
                await WaitUntil(() => OwnedPlayersGone(), 5000, "Dispose left an owned mpv process running.");
                await Task.Delay(1000);
                VerifyRestored(primary, "restored-after-dispose.png");
                Require(existing.IsSubsetOf(BundledPlayers(runtime)), "An existing unrelated bundled mpv process exited during verification.");
                Require(autoHide.IsEnabled == originalAutoHide, "Wallpaper playback changed the Windows taskbar auto-hide preference.");
                Console.WriteLine("PASS: Dispose restores desktop pixels, cleans up only owned mpv, and preserves taskbar auto-hide.");
                success = true;
            }
            catch (Exception error) { Console.Error.WriteLine($"FAIL: Wallpaper integration: {error}"); }
            finally
            {
                try
                {
                    RememberOwned();
                    service?.Dispose();
                    service = null;
                    await WaitUntil(() => OwnedPlayersGone(), 5000, "An owned player remained after failure cleanup.");
                }
                catch (Exception error) { success = false; Console.Error.WriteLine($"FAIL: Wallpaper cleanup: {error.Message}"); }
                foreach (var bitmap in baselines.Values) bitmap.Dispose();
                try { if (minimized) ((dynamic)shell!).UndoMinimizeALL(); }
                catch (Exception error) { success = false; Console.Error.WriteLine($"FAIL: Window restoration: {error.Message}"); }
                finally
                {
                    if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
                    context.ExitThread();
                }
            }
        }

        void RememberOwned() => owned.UnionWith(BundledPlayers(runtime).Except(existing));
        bool OwnedPlayersGone() => !BundledPlayers(runtime).Overlaps(owned);
        async Task Ready(int count)
        {
            await WaitUntil(() => service!.Status.IsRunning && service.Status.MonitorCount == count,
                15000, $"Expected visible-ready players on {count} monitor(s).");
            RememberOwned();
        }
        async Task Delay(int milliseconds)
        {
            await Task.Delay(milliseconds);
            if (service?.Status.Error is { } error) throw new InvalidOperationException(error);
        }
        async Task WaitUntil(Func<bool> condition, int timeout, string message)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (service?.Status.Error is { } error) throw new InvalidOperationException(error);
                if (watch.ElapsedMilliseconds >= timeout) throw new TimeoutException(message);
                await Task.Delay(100);
            }
        }
        void VerifyRestored(MonitorInfo primary, string name)
        {
            using var restored = Capture(Central(primary.Bounds));
            restored.Save(Path.Combine(directory, name), ImageFormat.Png);
            var difference = Difference(baselines[primary.Id], restored);
            Require(difference < 2, $"Original desktop did not return (pixel difference {difference:F3}).");
        }
    }

    private static HashSet<int> BundledPlayers(string executable)
    {
        var result = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("mpv"))
        {
            using (process)
                try { if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) result.Add(process.Id); }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
        }
        return result;
    }

    private static Rectangle Central(Rectangle monitor)
    {
        var width = Math.Min(800, Math.Max(160, monitor.Width - 400));
        var height = Math.Min(450, Math.Max(100, monitor.Height - 300));
        return new Rectangle(monitor.Left + (monitor.Width - width) / 2,
            monitor.Top + (monitor.Height - height) / 2, width, height);
    }

    private static Bitmap Capture(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static double Difference(Bitmap first, Bitmap second)
    {
        double difference = 0;
        var samples = 0;
        for (var y = 0; y < first.Height; y += 4)
        for (var x = 0; x < first.Width; x += 4)
        {
            var a = first.GetPixel(x, y);
            var b = second.GetPixel(x, y);
            difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            samples += 3;
        }
        return difference / samples;
    }

    private static void VerifyClickThrough(Rectangle monitor)
    {
        var window = DescribeHitTest(monitor, "during playback");
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        Require(process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase),
            $"Desktop hit testing reaches {process.ProcessName} instead of Explorer.");
    }

    private static nint DescribeHitTest(Rectangle monitor, string stage)
    {
        var point = new NativePoint(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2);
        var target = WindowFromPoint(point);
        Console.WriteLine($"Hit-test diagnostics ({stage}): monitor={monitor}, point={point}, target=0x{target:X}, threadDpi={GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext())}");
        var depth = 0;
        for (var window = target; window != 0 && depth < 8; window = GetParent(window), depth++)
            DescribeWindow(window, $"target chain depth={depth}", point);
        var progman = FindWindow("Progman", null);
        EnumChildWindows(progman, (window, _) =>
        {
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (title.ToString() != "Glasscape Wallpaper") return true;
            DescribeWindow(window, "native wallpaper host after mpv child creation", point);
            EnumChildWindows(window, (child, _) => { DescribeWindow(child, "wallpaper descendant", point); return true; }, 0);
            return true;
        }, 0);
        return target;
    }

    private static void DescribeWindow(nint window, string label, NativePoint point)
    {
        var name = new StringBuilder(256);
        var title = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        GetWindowText(window, title, title.Capacity);
        var thread = GetWindowThreadProcessId(window, out var process);
        GetWindowRect(window, out var bounds);
        var position = unchecked((nint)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16)));
        var replied = SendMessageTimeout(window, 0x0084, 0, position, 2, 150, out var hit);
        Console.WriteLine($"{label}: hwnd=0x{window:X}, class={name}, title={title}, pid={process}, tid={thread}, enabled={IsWindowEnabled(window)}, style=0x{unchecked((uint)GetWindowLongPtr(window, -16).ToInt64()):X8}, exStyle=0x{unchecked((uint)GetWindowLongPtr(window, -20).ToInt64()):X8}, parent=0x{GetParent(window):X}, rect=({bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}), dpi={GetDpiForWindow(window)}, WM_NCHITTEST={(replied == 0 ? "timeout" : unchecked((int)hit).ToString())}");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder title, int count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint parent, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
}
