using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransparentTaskbar.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string directory;
    private readonly string filePath;

    public SettingsStore(string? directory = null)
    {
        this.directory = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransparentTaskbar"));
        filePath = Path.Combine(this.directory, "settings.json");
    }

    public string? LastLoadWarning { get; private set; }

    public AppSettings Load()
    {
        LastLoadWarning = null;
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions)
                ?? throw new JsonException("The settings document is null.");
            Normalize(settings);
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException error)
        {
            var backup = Path.Combine(directory, $"settings.corrupt-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
            try
            {
                File.Move(filePath, backup);
                LastLoadWarning = $"Settings could not be read and defaults were loaded. Original data was preserved at {backup}. {error.Message}";
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                LastLoadWarning = $"Settings could not be read and defaults were loaded. The original file was retained because its backup failed: {backupError.Message}";
            }
            return new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastLoadWarning = $"Settings could not be accessed and defaults were loaded: {error.Message}";
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        Directory.CreateDirectory(directory);
        var temporaryFile = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryFile, filePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Normalize(AppSettings settings)
    {
        settings.SchemaVersion = 1;
        settings.TaskbarOpacity = Math.Clamp(settings.TaskbarOpacity, 0, 100);
        settings.WallpaperVolume = Math.Clamp(settings.WallpaperVolume, 0, 100);
        if (!Enum.IsDefined(settings.WallpaperFit)) settings.WallpaperFit = WallpaperFit.Fill;
        if (!Enum.IsDefined(settings.MonitorMode)) settings.MonitorMode = MonitorMode.Primary;
        settings.SelectedMonitorId = string.IsNullOrWhiteSpace(settings.SelectedMonitorId) ? null : settings.SelectedMonitorId.Trim();

        var entries = new List<WallpaperEntry>();
        var identities = new HashSet<Guid>();
        foreach (var entry in settings.Wallpapers ?? [])
        {
            if (entry is null || NormalizePath(entry.FilePath) is not { } path) continue;
            entry.FilePath = path;
            if (entry.Id == Guid.Empty || !identities.Add(entry.Id))
            {
                entry.Id = Guid.NewGuid();
                identities.Add(entry.Id);
            }
            entry.Title = string.IsNullOrWhiteSpace(entry.Title) ? Path.GetFileNameWithoutExtension(path) : entry.Title.Trim();
            entry.CreatedUtc = entry.CreatedUtc == default ? DateTimeOffset.UtcNow : entry.CreatedUtc.ToUniversalTime();
            entry.ThumbnailPath = NormalizePath(entry.ThumbnailPath);
            entries.Add(entry);
        }
        settings.Wallpapers = entries;
        if (settings.ActiveWallpaperId is { } active && !identities.Contains(active)) settings.ActiveWallpaperId = null;
    }

    private string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path, directory); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
