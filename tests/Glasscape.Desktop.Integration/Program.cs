internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "integration"));
        Directory.CreateDirectory(output);
        using var log = new StreamWriter(Path.Combine(output, "wallpaper-result.log")) { AutoFlush = true };
        Console.SetOut(log); Console.SetError(log);
        try { DesktopFeatureChecks.Run(); return WallpaperChecks.Run(output) ? 0 : 1; }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }
}
