using Glasscape;

var tests = new (string Name, Action Run)[]
{
    ("Makes every monitor's taskbar transparent", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true), new(20, 100, true));
        using var controller = new TaskbarController(backend);
        Check(controller.Refresh() == 2, "Must report two applied taskbars.");
        Check(backend.Transparent.SetEquals([10, 20]), "Both backgrounds must be transparent.");
    }),
    ("Reconnects after Explorer replacement", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true));
        using var controller = new TaskbarController(backend);
        controller.Refresh();
        backend.Taskbars = [new(30, 200, true)];
        controller.Refresh();
        Check(backend.ConnectedProcess == 200 && backend.Transparent.Contains(30), "New Explorer must own the connection and transparency.");
    }),
    ("Recovers when Explorer has no taskbar temporarily", () =>
    {
        var backend = new DesktopBackend();
        using var controller = new TaskbarController(backend);
        Check(controller.Refresh() == 0, "No taskbar is not a successful application.");
        backend.Taskbars = [new(10, 100, true)];
        Check(controller.Refresh() == 1 && backend.Transparent.Contains(10), "Must apply after taskbar appears.");
    }),
    ("Retries a failed COM connection", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true)) { ConnectionFailure = true };
        using var controller = new TaskbarController(backend);
        ExpectFailure(() => controller.Refresh());
        backend.ConnectionFailure = false;
        Check(controller.Refresh() == 1 && backend.Transparent.Contains(10), "Failed connection must remain retryable.");
    }),
    ("Restores backgrounds after partial apply failure", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true), new(20, 100, true)) { FailingWindow = 20 };
        var controller = new TaskbarController(backend);
        ExpectFailure(() => controller.Refresh());
        controller.Dispose();
        Check(backend.Transparent.Count == 0 && backend.ConnectedProcess == 0, "Dispose must restore partial changes and release connection.");
    }),
    ("Restores once on exit and prevents reuse", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true));
        var controller = new TaskbarController(backend);
        controller.Refresh();
        controller.Dispose();
        controller.Dispose();
        Check(backend.RestoreCount == 1 && backend.Transparent.Count == 0, "Dispose must restore once.");
        try { controller.Refresh(); throw new Exception("Disposed controller must reject reuse."); }
        catch (ObjectDisposedException) { }
    }),
    ("Reapplies after shell changes its background", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, false));
        using var controller = new TaskbarController(backend);
        controller.Refresh();
        backend.Transparent.Clear();
        controller.Refresh();
        Check(backend.Transparent.Contains(10), "Shell repaint must not permanently remove transparency.");
    }),
    ("Cancellation during enumeration avoids native initialization", () =>
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true)) { AfterFind = cancellation.Cancel };
        using var controller = new TaskbarController(backend);
        try { controller.Refresh(cancellation.Token); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { }
        Check(backend.ConnectedProcess == 0, "Shutdown must not start native initialization after enumeration finishes.");
    }),
    ("Cancellation during native connection never applies transparency", () =>
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true)) { AfterConnect = cancellation.Cancel };
        var controller = new TaskbarController(backend);
        try { controller.Refresh(cancellation.Token); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { }
        controller.Dispose();
        Check(backend.Transparent.Count == 0 && backend.ConnectedProcess == 0, "Exit during initialization must never apply a late change.");
    }),
    ("A taskbar classified once is not reclassified while Explorer keeps running", () =>
    {
        var kinds = new TaskbarKindCache();
        var classified = 0;
        Check(kinds.IsXaml(100, () => { classified++; return true; }), "First classification must be used.");
        Check(kinds.IsXaml(100, () => throw new InvalidOperationException("Island count changed briefly.")),
            "A brief island change in the same Explorer must not fail or flip the taskbar kind.");
        Check(classified == 1, "The same Explorer must be classified once.");
    }),
    ("A restarted Explorer is classified again, and a failed classification is retried", () =>
    {
        var kinds = new TaskbarKindCache();
        kinds.IsXaml(100, () => true);
        ExpectFailure(() => kinds.IsXaml(200, () => throw new InvalidOperationException("Taskbar not ready.")));
        Check(!kinds.IsXaml(200, () => false), "The new Explorer's own classification must be used once it succeeds.");
    }),
    ("Releases connection even when restore fails", () =>
    {
        var backend = new DesktopBackend(new TaskbarWindow(10, 100, true)) { RestoreFailure = true };
        var controller = new TaskbarController(backend);
        controller.Refresh();
        ExpectFailure(controller.Dispose);
        Check(backend.ConnectedProcess == 0, "Native resources must be released after failed restore.");
        controller.Dispose();
    }),
    ("Stages the native module outside its protected installation directory", () =>
    {
        WithModule((source, cache) =>
        {
            var staged = TaskbarModuleCache.Prepare(source, cache);
            Check(Path.GetFullPath(staged).StartsWith(Path.GetFullPath(cache) + Path.DirectorySeparatorChar),
                "Explorer must load from the user cache instead of WindowsApps.");
            Check(File.ReadAllBytes(staged).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "The native module must be copied unchanged.");
        });
    }),
    ("Reuses a cached module while Explorer holds it open", () =>
    {
        WithModule((source, cache) =>
        {
            var staged = TaskbarModuleCache.Prepare(source, cache);
            using var loaded = File.Open(staged, FileMode.Open, FileAccess.Read, FileShare.Read);
            Check(TaskbarModuleCache.Prepare(source, cache) == staged, "Reconnection must reuse the loaded copy without overwriting it.");
        });
    }),
    ("Stages a new native version without overwriting Explorer's loaded module", () =>
    {
        WithModule((source, cache) =>
        {
            var previous = TaskbarModuleCache.Prepare(source, cache);
            using var loaded = File.Open(previous, FileMode.Open, FileAccess.Read, FileShare.Read);
            File.WriteAllBytes(source, [5, 6, 7, 8]);
            var updated = TaskbarModuleCache.Prepare(source, cache);
            Check(updated != previous, "Different native contents must have separate paths.");
            Check(File.ReadAllBytes(previous).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "The loaded version must remain intact.");
            Check(File.ReadAllBytes(updated).SequenceEqual(new byte[] { 5, 6, 7, 8 }), "The new native version must be available.");
        });
    }),
    ("Repairs a damaged cached native module before loading it", () =>
    {
        WithModule((source, cache) =>
        {
            var staged = TaskbarModuleCache.Prepare(source, cache);
            File.WriteAllBytes(staged, [9]);
            var repaired = TaskbarModuleCache.Prepare(source, cache);
            Check(File.ReadAllBytes(repaired).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "A damaged cached DLL must be replaced with the bundled bytes.");
        });
    }),
    ("Concurrent native staging publishes one complete module", () =>
    {
        WithModule((source, cache) =>
        {
            var paths = new string[16];
            Parallel.For(0, paths.Length, i => paths[i] = TaskbarModuleCache.Prepare(source, cache));
            Check(paths.Distinct().Count() == 1, "Concurrent clients must agree on the native module path.");
            Check(File.ReadAllBytes(paths[0]).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Concurrent staging must not expose a partial DLL.");
            Check(Directory.GetFiles(cache, "*.tmp", SearchOption.AllDirectories).Length == 0, "Staging must clean its temporary files.");
        });
    }),
    ("A cached module does not hide a missing bundled dependency", () =>
    {
        WithModule((source, cache) =>
        {
            TaskbarModuleCache.Prepare(source, cache);
            File.Delete(source);
            try { TaskbarModuleCache.Prepare(source, cache); }
            catch (FileNotFoundException) { return; }
            throw new Exception("A missing bundled dependency must be reported instead of trusting the cache.");
        });
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void ExpectFailure(Action run)
{
    try { run(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected native operation failure to be reported.");
}

static void WithModule(Action<string, string> run)
{
    var root = Path.Combine(Path.GetTempPath(), "Glasscape-module-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "WindowsApps"));
    var source = Path.Combine(root, "WindowsApps", "ExplorerTAP.dll");
    File.WriteAllBytes(source, [1, 2, 3, 4]);
    try { run(source, Path.Combine(root, "user-cache")); }
    finally { Directory.Delete(root, recursive: true); }
}

sealed class DesktopBackend(params TaskbarWindow[] taskbars) : ITaskbarBackend
{
    public IReadOnlyList<TaskbarWindow> Taskbars { get; set; } = taskbars;
    public HashSet<nint> Transparent { get; } = [];
    public uint ConnectedProcess { get; private set; }
    public int RestoreCount { get; private set; }
    public bool ConnectionFailure { get; set; }
    public bool RestoreFailure { get; set; }
    public nint FailingWindow { get; set; }
    public Action? AfterConnect { get; set; }
    public Action? AfterFind { get; set; }

    public IReadOnlyList<TaskbarWindow> FindTaskbars() { AfterFind?.Invoke(); return Taskbars; }

    public void Connect(TaskbarWindow primary)
    {
        if (ConnectionFailure) throw new InvalidOperationException("Explorer connection failed.");
        ConnectedProcess = primary.ProcessId;
        AfterConnect?.Invoke();
    }

    public void MakeTransparent(TaskbarWindow taskbar)
    {
        if (taskbar.ProcessId != ConnectedProcess) throw new InvalidOperationException("Stale Explorer connection.");
        if (taskbar.Handle == FailingWindow) throw new InvalidOperationException("Taskbar unavailable.");
        Transparent.Add(taskbar.Handle);
    }

    public void Restore()
    {
        RestoreCount++;
        if (RestoreFailure) throw new InvalidOperationException("Restore failed.");
        Transparent.Clear();
    }

    public void Disconnect() => ConnectedProcess = 0;
}
