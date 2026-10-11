namespace Glasscape.Settings;

public enum WallpaperFit { Fill, Fit }
public enum MonitorMode { Primary, Selected, All }

public sealed class WallpaperEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ThumbnailPath { get; set; }
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? Language { get; set; }
    public bool TaskbarEnabled { get; set; } = true;
    public int TaskbarOpacity { get; set; }
    public bool? TaskbarAutoHide { get; set; }
    public bool StartWithWindows { get; set; }
    public bool PauseWhenFullscreen { get; set; } = true;
    public bool PauseOnBattery { get; set; } = true;
    public bool MuteWallpaper { get; set; } = true;
    public int WallpaperVolume { get; set; }
    public WallpaperFit WallpaperFit { get; set; }
    public MonitorMode MonitorMode { get; set; }
    public string? SelectedMonitorId { get; set; }
    public List<WallpaperEntry> Wallpapers { get; set; } = [];
    public Guid? ActiveWallpaperId { get; set; }
    public bool BundledSamplesInitialized { get; set; }
    public List<Guid> InitializedBundledSamples { get; set; } = [];
}
