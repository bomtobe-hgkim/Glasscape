using Microsoft.Win32;
using TransparentTaskbar.Settings;
using Windows.ApplicationModel;

var failures = 0;
await Check("Unpackaged execution is detected without a Store dependency", () =>
{
    var identity = typeof(StartupService).Assembly.GetType("TransparentTaskbar.Packaging.PackageIdentity");
    if (identity?.GetProperty("IsPackaged")?.GetValue(null) is not false)
        throw new Exception("A portable test process must be detected as unpackaged.");
    return Task.CompletedTask;
});
await Check("Portable asynchronous startup read preserves the actual registration", async () =>
{
    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
    var before = key?.GetValue("TransparentTaskbar");
    var startup = new StartupService(Environment.ProcessPath!);
    var read = typeof(StartupService).GetMethod("GetIsEnabledAsync");
    if (read is null) throw new Exception("Startup has no asynchronous read API for the UI.");
    var enabled = await (Task<bool>)read.Invoke(startup, null)!;
    var expected = string.Equals(before as string, StartupService.BuildCommand(Environment.ProcessPath!), StringComparison.Ordinal);
    if (enabled != expected) throw new Exception("Portable startup state must match the exact quoted background command.");
    if (!Equals(before, key?.GetValue("TransparentTaskbar"))) throw new Exception("Reading startup state changed the user's registration.");
});
await Check("Invalid executable paths are rejected before startup registration", () =>
{
    foreach (var path in new[] { "app.exe", "C:\\bad\"argument.exe", "C:\\bad\0name.exe", " " })
    {
        try { _ = new StartupService(path); }
        catch (ArgumentException) { continue; }
        throw new Exception("Invalid startup executable was accepted: " + path);
    }
    return Task.CompletedTask;
});
await Check("Policy-enabled startup is reported as enabled and all disabled states remain off", () =>
{
    var classify = typeof(StartupService).GetMethod("IsEnabledState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    if (classify is null) throw new Exception("Startup has no shared classifier for the authoritative Windows states.");
    foreach (var (state, expected) in new[]
    {
        (StartupTaskState.Disabled, false), (StartupTaskState.DisabledByUser, false),
        (StartupTaskState.Enabled, true), (StartupTaskState.DisabledByPolicy, false),
        (StartupTaskState.EnabledByPolicy, true)
    })
    {
        if (!Equals(classify.Invoke(null, [state]), expected)) throw new Exception("Incorrect enabled state for " + state);
    }
    return Task.CompletedTask;
});
return failures == 0 ? 0 : 1;

async Task Check(string name, Func<Task> run)
{
    try { await run(); Console.WriteLine("PASS: " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + " — " + error.Message); }
}
