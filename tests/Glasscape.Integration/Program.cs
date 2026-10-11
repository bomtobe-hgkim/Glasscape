using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Glasscape;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var sharedMutex = new Mutex(false, "344635E9-9AE4-4E60-B128-D53E25AB70A7", out var available);
        if (!available) { Console.Error.WriteLine("FAIL: Close TranslucentTB/Glasscape before integration verification."); return 1; }
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "integration"));
        Directory.CreateDirectory(output);
        using var log = new StreamWriter(Path.Combine(output, "result.log")) { AutoFlush = true };
        Console.SetOut(log); Console.SetError(log);
        Application.ThreadException += (_, eventArgs) => Console.Error.WriteLine("UI THREAD EXCEPTION: " + eventArgs.Exception);
        Console.WriteLine($"Elevated={new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}");
        if (!VerifyResponsiveWorker()) return 1;
        var backend = new WindowsTaskbarBackend();
        using var controller = new TaskbarController(backend);
        using var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        var phase = 0;
        var result = 1;
        Bitmap? before = null;
        Bitmap? clear = null;
        nint taskbar = 0;
        using var visibility = new TaskbarVisibilityScope();
        using var fixture = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.FromArgb(36, 190, 220),
            Bounds = new Rectangle(Screen.PrimaryScreen!.Bounds.Left, Screen.PrimaryScreen.Bounds.Bottom - 150,
                Screen.PrimaryScreen.Bounds.Width, 150)
        };
        fixture.Show();
        timer.Tick += (_, _) =>
        {
            try
            {
                switch (phase++)
                {
                    case 0:
                        var taskbars = backend.FindTaskbars();
                        if (taskbars.Count == 0) throw new Exception("No interactive taskbar found.");
                        taskbar = taskbars[0].Handle;
                        Console.WriteLine($"Detected {taskbars.Count} taskbar(s), XAML={taskbars[0].IsXaml}, Explorer PID={taskbars[0].ProcessId}");
                        before = Capture(taskbar);
                        before.Save(Path.Combine(output, "before.png"), ImageFormat.Png);
                        var count = controller.Refresh();
                        if (count != taskbars.Count) throw new Exception("Not all taskbars applied.");
                        Console.WriteLine("PASS: Real native service attached and all taskbar transparency calls succeeded.");
                        break;
                    case 1:
                        clear = Capture(taskbar);
                        clear.Save(Path.Combine(output, "transparent.png"), ImageFormat.Png);
                        controller.Dispose();
                        Console.WriteLine("PASS: Real native restore call succeeded.");
                        break;
                    default:
                        using (var restored = Capture(taskbar))
                        {
                            restored.Save(Path.Combine(output, "restored.png"), ImageFormat.Png);
                            var changed = BackgroundDistance(before!, clear!);
                            var restoredDistance = BackgroundDistance(before!, restored);
                            Console.WriteLine($"Background pixel difference: clear={changed:F2}, restored={restoredDistance:F2}");
                            if (changed < 2 || restoredDistance > changed / 2)
                                throw new Exception("Screenshot pixels did not prove both visible transparency and restoration.");
                        }
                        Console.WriteLine("PASS: Taskbar background visibly changed and returned to its original appearance.");
                        result = 0;
                        timer.Stop();
                        context.ExitThread();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex}");
                timer.Stop();
                context.ExitThread();
            }
        };
        timer.Start();
        try { Application.Run(context); }
        catch (Exception error) { Console.Error.WriteLine("MAIN LOOP EXCEPTION: " + error); return 1; }
        before?.Dispose();
        clear?.Dispose();
        if (result != 0) return result;

        // A second client in the same process exercises reconnect and the real tray lifecycle.
        using var trayContext = new TrayApplicationContext(background: true,
            settingsStore: new Glasscape.Settings.SettingsStore(Path.Combine(output, "tray-test-settings")));
        using var exitTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        exitTimer.Tick += (_, _) => { exitTimer.Stop(); trayContext.ExitThread(); };
        exitTimer.Start();
        try { Application.Run(trayContext); }
        catch (Exception error) { Console.Error.WriteLine("TRAY LOOP EXCEPTION: " + error); return 1; }
        Console.WriteLine("PASS: Tray message loop started, reapplied and exited cleanly.");
        fixture.Close();
        visibility.Dispose();
        return 0;
    }

    private static bool VerifyResponsiveWorker()
    {
        using var backend = new BlockingBackend();
        using var worker = new TaskbarWorker(backend, (_, _) => { });
        if (!backend.Entered.Wait(5000)) { Console.WriteLine("FAIL: Worker did not initialize."); return false; }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        worker.Dispose();
        backend.Release.Set();
        if (watch.ElapsedMilliseconds > 2000 || !backend.Restored.Wait(4000) || backend.Applied)
        {
            Console.WriteLine("FAIL: Exit blocked or late transparency was applied after cancellation.");
            return false;
        }
        var workerThread = (Thread)typeof(TaskbarWorker).GetField("thread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(worker)!;
        if (!workerThread.Join(4000)) { Console.WriteLine("FAIL: Worker did not finish cleanup."); return false; }
        Console.WriteLine("PASS: Exit stays responsive during blocked initialization; no late transparency is applied.");
        return true;
    }

    private static Bitmap Capture(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var bounds)) throw new Exception("GetWindowRect failed.");
        var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        return bitmap;
    }

    private static double BackgroundDistance(Bitmap first, Bitmap second)
    {
        // Sample the empty left edge; avoid centered app icons, clock, and notification balloons.
        var width = Math.Min(80, Math.Min(first.Width, second.Width));
        var height = Math.Min(first.Height, second.Height);
        double difference = 0;
        var samples = 0;
        for (var y = height / 3; y < height * 2 / 3; y += 2)
        for (var x = 8; x < width; x += 2)
        {
            var a = first.GetPixel(x, y);
            var b = second.GetPixel(x, y);
            difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            samples += 3;
        }
        return difference / samples;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }

    private sealed class BlockingBackend : ITaskbarBackend, IDisposable
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly ManualResetEventSlim Release = new();
        internal readonly ManualResetEventSlim Restored = new();
        internal bool Applied;
        public IReadOnlyList<TaskbarWindow> FindTaskbars() => [new(10, 100, true)];
        public void Connect(TaskbarWindow primary) { Entered.Set(); Release.Wait(); }
        public void MakeTransparent(TaskbarWindow taskbar) => Applied = true;
        public void Restore() => Restored.Set();
        public void Disconnect() { }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); Restored.Dispose(); }
    }

    // A hidden taskbar has only a two-pixel reveal strip. Temporarily reveal it
    // for screenshots, and always restore the user's original autohide setting.
    private sealed class TaskbarVisibilityScope : IDisposable
    {
        private AppBarData data;
        private readonly nuint originalState;
        internal TaskbarVisibilityScope()
        {
            data.Size = (uint)Marshal.SizeOf<AppBarData>();
            data.Window = NativeMethods.FindWindow("Shell_TrayWnd", null);
            originalState = SHAppBarMessage(4, ref data);
            if ((originalState & 1) != 0)
            {
                data.Parameter = (nint)(originalState & ~1u);
                SHAppBarMessage(10, ref data);
            }
        }
        public void Dispose()
        {
            if ((originalState & 1) == 0) return;
            data.Parameter = (nint)originalState;
            SHAppBarMessage(10, ref data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        internal uint Size;
        internal nint Window;
        internal uint CallbackMessage, Edge;
        internal Rect Bounds;
        internal nint Parameter;
    }
    [DllImport("shell32.dll")]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
}
