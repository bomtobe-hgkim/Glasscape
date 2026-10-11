using Microsoft.Win32;
using Glasscape.Settings;

internal static class DesktopFeatureChecks
{
    internal static void Run()
    {
        var hide = new TaskbarAutoHideService();
        var originalHide = hide.IsEnabled;
        try
        {
            hide.SetEnabled(!originalHide);
            if (hide.IsEnabled == originalHide) throw new Exception("Auto-hide did not change through the Windows API.");
            hide.SetEnabled(originalHide);
            if (hide.IsEnabled != originalHide) throw new Exception("Auto-hide preference was not restored.");
            Console.WriteLine("PASS: Actual Windows auto-hide control changes and restores the user's preference.");
        }
        finally { hide.SetEnabled(originalHide); }

        const string path = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string name = "Glasscape";
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        var exists = key.GetValueNames().Contains(name);
        var original = exists ? key.GetValue(name) : null;
        var kind = exists ? key.GetValueKind(name) : RegistryValueKind.String;
        var startup = new StartupService(Environment.ProcessPath!);
        try
        {
            startup.SetEnabled(true);
            if (!startup.IsEnabled || key.GetValue(name) as string != StartupService.BuildCommand(Environment.ProcessPath!))
                throw new Exception("The startup entry does not contain the exact quoted command.");
            startup.SetEnabled(false);
            if (startup.IsEnabled || key.GetValue(name) is not null) throw new Exception("Startup disable did not remove the app's own entry.");
            Console.WriteLine("PASS: Actual HKCU startup enable/disable uses the exact quoted background command.");
        }
        finally
        {
            if (exists) key.SetValue(name, original!, kind);
            else key.DeleteValue(name, false);
        }
        Console.WriteLine("PASS: The user's original startup registration was restored.");
    }
}
