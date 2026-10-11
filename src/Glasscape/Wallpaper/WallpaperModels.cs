using System.Drawing;

namespace Glasscape.Wallpaper;

public sealed record MonitorInfo(string Id, string Name, Rectangle Bounds, bool Primary);

public sealed record WallpaperStatus(bool IsRunning, bool IsPaused, string? PauseReason,
    string? Error, string? ActiveTitle, int MonitorCount);

[Flags]
internal enum WallpaperPauseCause
{
    None = 0,
    User = 1,
    SessionLocked = 2,
    Suspended = 4,
    Battery = 8,
    Fullscreen = 16
}
