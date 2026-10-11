using Microsoft.Win32;
using Glasscape.Packaging;
using Windows.ApplicationModel;

namespace Glasscape.Settings;

public sealed class StartupService
{
    public const string StartupTaskId = "GlasscapeStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Glasscape";
    // Keep upgrade compatibility with registrations created before the rebrand.
    private static readonly string LegacyValueName = string.Concat("Transparent", "Taskbar");
    private readonly string command;
    private readonly string? legacyCommand;
    private readonly string? versionedInstallRoot;
    private readonly bool packaged = PackageIdentity.IsPackaged;

    public StartupService(string executablePath)
    {
        command = BuildCommand(executablePath);
        if (string.Equals(Path.GetFileName(executablePath), "Glasscape.exe", StringComparison.OrdinalIgnoreCase))
        {
            legacyCommand = BuildCommand(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executablePath))!, LegacyValueName + ".exe"));
            var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!;
            var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Glasscape");
            if (Version.TryParse(Path.GetFileName(executableDirectory), out _)
                && string.Equals(Path.GetDirectoryName(executableDirectory), installRoot, StringComparison.OrdinalIgnoreCase))
                versionedInstallRoot = installRoot;
        }
    }

    public bool IsEnabled
    {
        get
        {
            EnsurePortable();
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return string.Equals(key?.GetValue(ValueName) as string, command, StringComparison.Ordinal)
                || OwnsLegacyRegistration(key?.GetValue(LegacyValueName));
        }
    }

    public void SetEnabled(bool enabled)
    {
        EnsurePortable();
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
            if (OwnsLegacyRegistration(key.GetValue(LegacyValueName)))
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            if (OwnsLegacyRegistration(key?.GetValue(LegacyValueName)))
                key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
    }

    internal bool OwnsLegacyRegistration(object? value)
    {
        if (legacyCommand is null || value is not string registration) return false;
        if (string.Equals(registration, legacyCommand, StringComparison.OrdinalIgnoreCase)) return true;
        const string suffix = "\" --background";
        if (versionedInstallRoot is null || !registration.StartsWith('"') || !registration.EndsWith(suffix, StringComparison.Ordinal)
            || registration.Length <= suffix.Length + 1) return false;
        var executable = registration.Substring(1, registration.Length - suffix.Length - 1);
        try
        {
            if (!string.Equals(registration, BuildCommand(executable), StringComparison.OrdinalIgnoreCase)) return false;
            var previousDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
            return string.Equals(Path.GetFileName(executable), LegacyValueName + ".exe", StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(Path.GetFileName(previousDirectory), out _)
                && string.Equals(Path.GetDirectoryName(previousDirectory), versionedInstallRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public async Task<bool> GetIsEnabledAsync()
    {
        if (!packaged) return IsEnabled;
        var task = await StartupTask.GetAsync(StartupTaskId);
        return IsEnabledState(task.State);
    }

    // Await this on the UI thread. Windows owns the final state and may refuse an
    // enable request after the user disables startup in Task Manager or by policy.
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (!packaged)
        {
            SetEnabled(enabled);
            return IsEnabled;
        }
        var task = await StartupTask.GetAsync(StartupTaskId);
        if (!enabled)
        {
            task.Disable();
            return IsEnabledState(task.State);
        }
        return IsEnabledState(await task.RequestEnableAsync());
    }

    internal static bool IsEnabledState(StartupTaskState state) => state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private void EnsurePortable()
    {
        if (packaged) throw new InvalidOperationException("Packaged startup requires the asynchronous StartupService API.");
    }

    public static string BuildCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.IndexOfAny(['"', '\0']) >= 0 || !Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("Startup requires an absolute, unquoted executable path.", nameof(executablePath));
        return $"\"{Path.GetFullPath(executablePath)}\" --background";
    }
}
