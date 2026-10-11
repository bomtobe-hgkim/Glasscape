using System.Drawing;

namespace TransparentTaskbar.Wallpaper;

internal static class WallpaperPolicy
{
    internal static IReadOnlyList<MonitorInfo> SelectMonitors(IReadOnlyList<MonitorInfo> monitors,
        bool all, string? selectedId)
    {
        if (all) return monitors.ToArray();
        if (selectedId is not null)
            return monitors.Where(x => string.Equals(x.Id, selectedId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var primary = monitors.FirstOrDefault(x => x.Primary) ?? monitors.FirstOrDefault();
        return primary is null ? [] : [primary];
    }

    internal static WallpaperPauseCause EvaluatePause(bool userPaused, bool locked, bool suspended,
        bool pauseOnBattery, bool onBattery, bool pauseOnFullscreen, bool fullscreen)
    {
        var causes = WallpaperPauseCause.None;
        if (userPaused) causes |= WallpaperPauseCause.User;
        if (locked) causes |= WallpaperPauseCause.SessionLocked;
        if (suspended) causes |= WallpaperPauseCause.Suspended;
        if (pauseOnBattery && onBattery) causes |= WallpaperPauseCause.Battery;
        if (pauseOnFullscreen && fullscreen) causes |= WallpaperPauseCause.Fullscreen;
        return causes;
    }

    internal static bool CoversMonitor(Rectangle window, Rectangle monitor, int tolerance = 2)
        => window.Width > 0 && window.Height > 0 && monitor.Width > 0 && monitor.Height > 0 &&
           (long)window.Left <= (long)monitor.Left + tolerance &&
           (long)window.Top <= (long)monitor.Top + tolerance &&
           (long)window.Right >= (long)monitor.Right - tolerance &&
           (long)window.Bottom >= (long)monitor.Bottom - tolerance;

    internal static bool ExcludesForegroundProcess(uint foregroundId, uint ownId,
        IEnumerable<uint> wallpaperPlayerIds, string processName) => foregroundId == ownId ||
            wallpaperPlayerIds.Contains(foregroundId) ||
            processName is "ShellExperienceHost" or "StartMenuExperienceHost" or "SearchHost";
}
