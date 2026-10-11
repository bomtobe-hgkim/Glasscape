using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glasscape.Settings;

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
    private readonly string? legacyFilePath;

    public SettingsStore(string? directory = null)
        : this(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glasscape"),
            directory is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), string.Concat("Transparent", "Taskbar")) : null)
    {
    }

    public SettingsStore(string directory, string? legacyDirectory)
    {
        this.directory = Path.GetFullPath(directory);
        filePath = Path.Combine(this.directory, "settings.json");
        legacyFilePath = legacyDirectory is null ? null : Path.Combine(Path.GetFullPath(legacyDirectory), "settings.json");
    }

    public string? LastLoadWarning { get; private set; }

    public AppSettings Load()
    {
        LastLoadWarning = null;
        var sourcePath = !File.Exists(filePath) && legacyFilePath is not null && File.Exists(legacyFilePath)
            ? legacyFilePath : filePath;
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath), JsonOptions)
                ?? throw new JsonException("The settings document is null.");
            Normalize(settings, Path.GetDirectoryName(sourcePath)!);
            if (sourcePath != filePath)
            {
                try { Save(settings); }
                catch (Exception migrationError) when (migrationError is IOException or UnauthorizedAccessException)
                {
                    LastLoadWarning = $"Legacy settings were loaded but could not be saved to the new profile: {migrationError.Message}";
                }
            }
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException error)
        {
            var backup = Path.Combine(directory, $"settings.corrupt-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
            try
            {
                if (sourcePath == filePath) File.Move(filePath, backup);
                else
                {
                    Directory.CreateDirectory(directory);
                    File.Copy(sourcePath, backup);
                }
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

    private void Normalize(AppSettings settings, string? baseDirectory = null)
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
            if (entry is null || NormalizePath(entry.FilePath, baseDirectory) is not { } path) continue;
            entry.FilePath = path;
            if (entry.Id == Guid.Empty || !identities.Add(entry.Id))
            {
                entry.Id = Guid.NewGuid();
                identities.Add(entry.Id);
            }
            entry.Title = string.IsNullOrWhiteSpace(entry.Title) ? Path.GetFileNameWithoutExtension(path) : entry.Title.Trim();
            entry.CreatedUtc = entry.CreatedUtc == default ? DateTimeOffset.UtcNow : entry.CreatedUtc.ToUniversalTime();
            entry.ThumbnailPath = NormalizePath(entry.ThumbnailPath, baseDirectory);
            entries.Add(entry);
        }
        settings.Wallpapers = entries;
        if (settings.ActiveWallpaperId is { } active && !identities.Contains(active)) settings.ActiveWallpaperId = null;
    }

    private string? NormalizePath(string? path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path, baseDirectory ?? directory); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
