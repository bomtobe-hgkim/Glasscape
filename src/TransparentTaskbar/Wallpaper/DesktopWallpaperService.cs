using TransparentTaskbar.Localization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TransparentTaskbar.Settings;

namespace TransparentTaskbar.Wallpaper;

/// <summary>
/// Call the public methods on the application's STA UI thread. Refresh is intended for a roughly two-second UI timer.
/// Desktop HWNDs are borrowed; only this service's host windows and mpv processes are destroyed.
/// </summary>
public sealed class DesktopWallpaperService : IDisposable
{
    private readonly SynchronizationContext ownerContext;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly List<MpvSession> sessions = [];
    private readonly string playerPath;
    private DesktopLayer? layer;
    private Guid? activeId;
    private string? activePath, activeTitle, lastError;
    private IReadOnlyList<MonitorInfo> selectedMonitors = [];
    private uint previousExplorerId;
    private volatile bool disposed;
    private bool stopped, userPaused, sessionLocked, suspended, displaysChanged;
    private bool recovering, mediaFailed;
    private int retryCount;
    private DateTimeOffset nextRetry;
    private PlaybackPreferences playback = new(true, true, true, 0, true);

    public event Action<WallpaperStatus>? StatusChanged;
    public WallpaperStatus Status { get; private set; } = new(false, false, null, null, null, 0);

    public DesktopWallpaperService()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("DesktopWallpaperService must be created on the UI STA thread.");
        ownerContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        playerPath = Path.Combine(AppContext.BaseDirectory, "mpv", "mpv.exe");
        sessionLocked = WallpaperNativeMethods.IsSessionLocked();
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        Application.ApplicationExit += ApplicationExit;
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors() => Screen.AllScreens
        .Select(screen => new MonitorInfo(screen.DeviceName,
            screen.DeviceName.Replace("\\\\.\\", "", StringComparison.Ordinal) +
                $" · {screen.Bounds.Width} × {screen.Bounds.Height}", screen.Bounds, screen.Primary))
        .OrderByDescending(monitor => monitor.Primary).ThenBy(monitor => monitor.Bounds.Left)
        .ThenBy(monitor => monitor.Bounds.Top).ToArray();

    public void Apply(AppSettings settings)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(settings);
        stopped = false;
        mediaFailed = false;
        recovering = false;
        retryCount = 0;
        ApplyCore(settings);
    }

    public void UpdatePlayback(AppSettings settings)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(settings);
        playback = new(settings.PauseWhenFullscreen, settings.PauseOnBattery,
            settings.MuteWallpaper, Math.Clamp(settings.WallpaperVolume, 0, 100), settings.WallpaperFit == WallpaperFit.Fill);
        ApplyPlayback();
    }

    public void SetUserPaused(bool paused)
    {
        VerifyOwner();
        userPaused = paused;
        ApplyPlayback();
    }

    public void Refresh(AppSettings settings)
    {
        VerifyOwner();
        UpdatePlayback(settings);
        if (stopped || settings.ActiveWallpaperId is null) return;
        if (recovering)
        {
            if (DateTimeOffset.UtcNow >= nextRetry) ApplyCore(settings);
            return;
        }
        var current = SelectConfiguredMonitors(settings, GetMonitors());
        var explorerId = GetExplorerProcessId();
        var topologyChanged = displaysChanged || !current.SequenceEqual(selectedMonitors) ||
            (explorerId != 0 && previousExplorerId != 0 && explorerId != previousExplorerId);
        displaysChanged = false;

        // On Windows 11 the WorkerW child can be replaced while our Progman children continue playing.
        if (!topologyChanged && layer is { Raised: true } oldLayer &&
            WallpaperNativeMethods.IsWindow(oldLayer.Progman) &&
            sessions.Count > 0 && sessions.All(session => WallpaperNativeMethods.IsWindow(session.Host.Handle)) &&
            !WallpaperNativeMethods.IsWindow(oldLayer.WorkerW))
        {
            try
            {
                var replacement = DesktopLayer.Discover();
                if (replacement.Parent == oldLayer.Parent)
                {
                    layer = replacement;
                    foreach (var session in sessions) replacement.Position(session.Host.Handle, session.Monitor.Bounds);
                    replacement.EnsureWorkerAtBottom();
                    return;
                }
            }
            catch (Exception error) { Recover(error.Message); return; }
        }
        var invalidHost = sessions.Any(session => !WallpaperNativeMethods.IsWindow(session.Host.Handle));
        if (topologyChanged || invalidHost || (!mediaFailed && sessions.Count > 0 && layer?.IsValid != true))
        {
            mediaFailed = false;
            retryCount = 0;
            ApplyCore(settings);
        }
        // A playback error remains visible until an explicit Apply; this avoids restart loops for corrupt files.
    }

    public void Stop()
    {
        VerifyOwner();
        stopped = true;
        recovering = false;
        mediaFailed = false;
        lastError = null;
        activeId = null;
        activePath = activeTitle = null;
        selectedMonitors = [];
        StopSessions();
        Publish();
    }

    private void ApplyCore(AppSettings settings)
    {
        UpdatePlayback(settings);
        if (settings.ActiveWallpaperId is null)
        {
            Stop();
            return;
        }
        var entry = settings.Wallpapers.FirstOrDefault(item => item.Id == settings.ActiveWallpaperId);
        if (entry is null)
        {
            FailConfiguration(AppText.Get("WallpaperMissingEntry"));
            return;
        }
        activeTitle = string.IsNullOrWhiteSpace(entry.Title) ? Path.GetFileNameWithoutExtension(entry.FilePath) : entry.Title;
        string filePath;
        try { filePath = Path.GetFullPath(entry.FilePath); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            FailConfiguration(AppText.Get("WallpaperInvalidPath"));
            return;
        }
        if (!File.Exists(filePath))
        {
            FailConfiguration(AppText.Format("WallpaperMissingFile", filePath));
            return;
        }
        if (!File.Exists(playerPath))
        {
            FailConfiguration(AppText.Get("WallpaperMissingPlayer"));
            return;
        }
        var monitors = SelectConfiguredMonitors(settings, GetMonitors());
        if (monitors.Count == 0)
        {
            FailConfiguration(settings.MonitorMode == MonitorMode.Selected
                ? AppText.Get("WallpaperMissingMonitor")
                : AppText.Get("WallpaperNoMonitor"));
            return;
        }
        if (activeId == entry.Id && string.Equals(activePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            monitors.SequenceEqual(selectedMonitors) && layer?.IsValid == true && sessions.Count == monitors.Count &&
            sessions.All(session => WallpaperNativeMethods.IsWindow(session.Host.Handle)))
        {
            lastError = null;
            recovering = false;
            layer.EnsureWorkerAtBottom();
            Publish();
            return;
        }
        StopSessions();
        activeId = entry.Id;
        activePath = filePath;
        selectedMonitors = monitors;
        previousExplorerId = GetExplorerProcessId();
        lastError = null;
        try
        {
            layer = DesktopLayer.Discover();
            previousExplorerId = layer.ExplorerProcessId;
            foreach (var monitor in monitors)
            {
                var host = new WallpaperHostWindow();
                try
                {
                    layer.Attach(host.Handle, monitor.Bounds);
                    var state = PlaybackStateFor(monitor);
                    var session = new MpvSession(playerPath, filePath, host, monitor, state, SessionChanged);
                    host.Destroyed += () => Post(() => displaysChanged = true);
                    sessions.Add(session);
                }
                catch { host.Dispose(); throw; }
            }
            recovering = false;
            retryCount = 0;
            Publish();
        }
        catch (Exception error)
        {
            StopSessions();
            Recover(error.Message);
        }
    }

    private static IReadOnlyList<MonitorInfo> SelectConfiguredMonitors(AppSettings settings, IReadOnlyList<MonitorInfo> monitors)
    {
        if (settings.MonitorMode == MonitorMode.Selected && string.IsNullOrWhiteSpace(settings.SelectedMonitorId)) return [];
        return WallpaperPolicy.SelectMonitors(monitors, settings.MonitorMode == MonitorMode.All,
            settings.MonitorMode == MonitorMode.Selected ? settings.SelectedMonitorId : null);
    }

    private void FailConfiguration(string error)
    {
        StopSessions();
        lastError = error;
        recovering = false;
        mediaFailed = true;
        Publish();
    }

    private void Recover(string error)
    {
        StopSessions();
        lastError = error;
        retryCount++;
        recovering = retryCount <= 3;
        nextRetry = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, retryCount));
        if (!recovering) lastError += AppText.Get("WallpaperReconnectStopped");
        Publish();
    }

    private void SessionChanged(MpvSession session, string? error) => Post(() =>
    {
        if (!sessions.Contains(session)) return;
        if (error is not null)
        {
            lastError = error;
            mediaFailed = true;
            recovering = false;
            StopSessions();
        }
        else ApplyPlayback();
        Publish();
    });

    private void ApplyPlayback()
    {
        foreach (var session in sessions) session.SetPlayback(PlaybackStateFor(session.Monitor));
        Publish();
    }

    private MpvPlaybackState PlaybackStateFor(MonitorInfo monitor) => new(
        PauseCausesFor(monitor) != WallpaperPauseCause.None, playback.Muted, playback.Volume, playback.Fill);

    private WallpaperPauseCause PauseCausesFor(MonitorInfo monitor)
    {
        var onBattery = WallpaperNativeMethods.GetSystemPowerStatus(out var power) &&
            (power.AcLineStatus == 0 || power.SystemStatusFlag == 1);
        return WallpaperPolicy.EvaluatePause(userPaused, sessionLocked, suspended,
            playback.PauseOnBattery, onBattery, playback.PauseWhenFullscreen, ForegroundCovers(monitor.Bounds));
    }

    private bool ForegroundCovers(Rectangle monitor)
    {
        var foreground = WallpaperNativeMethods.GetForegroundWindow();
        if (foreground == 0 || !WallpaperNativeMethods.IsWindowVisible(foreground) || WallpaperNativeMethods.IsIconic(foreground)) return false;
        WallpaperNativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (WallpaperPolicy.ExcludesForegroundProcess(processId, (uint)Environment.ProcessId,
            sessions.Select(session => session.ProcessId), "")) return false;
        var className = WallpaperNativeMethods.WindowClass(foreground);
        if (className is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (WallpaperNativeMethods.DwmGetWindowAttribute(foreground, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (WallpaperPolicy.ExcludesForegroundProcess(processId, (uint)Environment.ProcessId,
                sessions.Select(session => session.ProcessId), process.ProcessName)) return false;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        if (WallpaperNativeMethods.DwmGetWindowBounds(foreground, 9, out var frame,
            Marshal.SizeOf<WallpaperNativeMethods.Rect>()) != 0 && !WallpaperNativeMethods.GetWindowRect(foreground, out frame)) return false;
        return WallpaperPolicy.CoversMonitor(frame.ToRectangle(), monitor);
    }

    private static uint GetExplorerProcessId()
    {
        var desktop = WallpaperNativeMethods.FindWindow("Progman", null);
        if (desktop == 0) return 0;
        WallpaperNativeMethods.GetWindowThreadProcessId(desktop, out var processId);
        return processId;
    }

    private void StopSessions()
    {
        var owned = sessions.ToArray();
        sessions.Clear();
        foreach (var session in owned)
        {
            try { session.Dispose(); }
            finally { session.Host.Dispose(); }
        }
        layer?.Redraw();
        layer = null;
    }

    private void Publish()
    {
        if (disposed) return;
        var readyCount = sessions.Count(session => session.IsReady);
        var paused = sessions.Select(session => PauseCausesFor(session.Monitor)).ToArray();
        var allPaused = paused.Length > 0 && paused.All(cause => cause != WallpaperPauseCause.None);
        var cause = paused.Aggregate(WallpaperPauseCause.None, (current, item) => current | item);
        var reason = PauseDescription(cause);
        if (!allPaused && cause != WallpaperPauseCause.None) reason = AppText.Format("WallpaperSomeMonitors", reason);
        if (sessions.Count > 0 && readyCount == 0 && cause == WallpaperPauseCause.None) reason = AppText.Get("WallpaperPreparing");
        var status = new WallpaperStatus(readyCount > 0, allPaused, reason, lastError, activeTitle is null ? null : AppText.WallpaperTitle(activeId, activeTitle), readyCount);
        if (status == Status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static string? PauseDescription(WallpaperPauseCause cause)
    {
        if (cause == WallpaperPauseCause.None) return null;
        var reasons = new List<string>();
        if (cause.HasFlag(WallpaperPauseCause.User)) reasons.Add(AppText.Get("PauseUser"));
        if (cause.HasFlag(WallpaperPauseCause.SessionLocked)) reasons.Add(AppText.Get("PauseLocked"));
        if (cause.HasFlag(WallpaperPauseCause.Suspended)) reasons.Add(AppText.Get("PauseSuspended"));
        if (cause.HasFlag(WallpaperPauseCause.Battery)) reasons.Add(AppText.Get("PauseBattery"));
        if (cause.HasFlag(WallpaperPauseCause.Fullscreen)) reasons.Add(AppText.Get("PauseFullscreen"));
        return string.Join(", ", reasons);
    }

    private void SessionSwitch(object sender, SessionSwitchEventArgs args) => Post(() =>
    {
        switch (args.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect: sessionLocked = true; break;
            case SessionSwitchReason.SessionUnlock: sessionLocked = false; break;
            case SessionSwitchReason.ConsoleConnect: sessionLocked = WallpaperNativeMethods.IsSessionLocked(); break;
        }
        ApplyPlayback();
    });

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs args) => Post(() =>
    {
        if (args.Mode == PowerModes.Suspend) suspended = true;
        if (args.Mode == PowerModes.Resume) { suspended = false; displaysChanged = true; }
        ApplyPlayback();
    });

    private void DisplaySettingsChanged(object? sender, EventArgs args) => Post(() => displaysChanged = true);
    private void ApplicationExit(object? sender, EventArgs args) => Dispose();

    private void Post(Action action)
    {
        if (disposed) return;
        try { ownerContext.Post(_ => { if (!disposed) action(); }, null); }
        catch (Exception error) when (error is ObjectDisposedException or System.ComponentModel.InvalidAsynchronousStateException) { }
    }

    private void VerifyOwner()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("Wallpaper service methods must be called on their original UI thread.");
    }

    public void Dispose()
    {
        if (disposed) return;
        VerifyOwner();
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        Application.ApplicationExit -= ApplicationExit;
        stopped = true;
        disposed = true;
        StopSessions();
        StatusChanged = null;
    }

    private sealed record PlaybackPreferences(bool PauseWhenFullscreen, bool PauseOnBattery, bool Muted, int Volume, bool Fill);
}
