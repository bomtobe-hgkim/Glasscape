using TransparentTaskbar.Localization;
using TransparentTaskbar.Packaging;
using TransparentTaskbar.Settings;

namespace TransparentTaskbar;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppText.SetLanguage(new SettingsStore().Load().Language);
        if (!PackageIdentity.IsPackaged && args.Contains("--install", StringComparer.OrdinalIgnoreCase))
        {
            if (InstallService.ShowInstaller(null) is { } installed) InstallService.LaunchInstalled(installed.ExecutablePath);
            return;
        }
        if (args.FirstOrDefault(argument => argument.StartsWith("--wait-for-exit=", StringComparison.OrdinalIgnoreCase)) is { } waitArgument
            && int.TryParse(waitArgument.AsSpan(16), out var previousPid) && previousPid != Environment.ProcessId)
        {
            try { using var previous = System.Diagnostics.Process.GetProcessById(previousPid); previous.WaitForExit(10000); }
            catch (ArgumentException) { }
        }
        using var ownInstance = new Mutex(false, "TransparentTaskbar.CSharp.36F9A0D0-215D-4E27-8C3C-95C1C531F184", out var firstInstance);
        if (!firstInstance)
        {
            var existing = NativeMethods.FindWindow(null!, "TransparentTaskbar.Messages");
            if (existing != 0 && !args.Contains("--background", StringComparer.OrdinalIgnoreCase))
                NativeMethods.PostMessage(existing, NativeMethods.RegisterWindowMessage("Glasscape.ShowSettings"), 0, 0);
            return;
        }
        // Reserve the same session-local mutex as upstream so neither app can
        // unexpectedly overwrite the other app's taskbar settings or process watch.
        using var upstreamInstance = new Mutex(false, "344635E9-9AE4-4E60-B128-D53E25AB70A7", out var noTranslucentTb);
        if (!noTranslucentTb)
        {
            MessageBox.Show(AppText.Get("ConflictingApp"), "Glasscape", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try
        {
            using var context = new TrayApplicationContext(args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            MessageBox.Show(ex.Message, AppText.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
