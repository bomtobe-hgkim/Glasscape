using Microsoft.Win32;
using TransparentTaskbar.Packaging;
using Windows.ApplicationModel;

namespace TransparentTaskbar.Settings;

public sealed class StartupService
{
    public const string StartupTaskId = "GlasscapeStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TransparentTaskbar";
    private readonly string command;
    private readonly bool packaged = PackageIdentity.IsPackaged;

    public StartupService(string executablePath) => command = BuildCommand(executablePath);

    public bool IsEnabled
    {
        get
        {
            EnsurePortable();
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return string.Equals(key?.GetValue(ValueName) as string, command, StringComparison.Ordinal);
        }
    }

    public void SetEnabled(bool enabled)
    {
        EnsurePortable();
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
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
