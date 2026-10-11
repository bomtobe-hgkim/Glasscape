namespace Glasscape;

// Explorer initialization can block in the native helper for 35 seconds.
// Keep all COM ownership on this STA and keep the tray UI responsive.
internal sealed class TaskbarWorker : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly Thread thread;
    private readonly object gate = new();
    private Control? dispatcher;
    private ApplicationContext? loop;
    private int refreshRequested;
    private int disposed;

    internal TaskbarWorker(ITaskbarBackend backend, Action<int, Exception?> report)
    {
        thread = new Thread(() => Run(backend, report)) { IsBackground = true, Name = "Taskbar transparency" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal void RequestRefresh() => Interlocked.Exchange(ref refreshRequested, 1);

    private void Run(ITaskbarBackend backend, Action<int, Exception?> report)
    {
        using var controller = new TaskbarController(backend);
        using var inbox = new Control();
        inbox.CreateControl();
        using var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        lock (gate) { dispatcher = inbox; loop = context; }
        var nextAttempt = DateTime.MinValue;
        timer.Tick += (_, _) =>
        {
            if (stopping.IsCancellationRequested) { context.ExitThread(); return; }
            if (Interlocked.Exchange(ref refreshRequested, 0) != 0) nextAttempt = DateTime.MinValue;
            if (DateTime.UtcNow < nextAttempt) return;
            try { report(controller.Refresh(stopping.Token), null); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { context.ExitThread(); }
            catch (Exception ex) { nextAttempt = DateTime.UtcNow.AddSeconds(10); report(0, ex); }
        };
        try
        {
            if (!stopping.IsCancellationRequested)
            {
                timer.Start();
                Application.Run(context);
            }
        }
        finally
        {
            timer.Stop();
            lock (gate) { dispatcher = null; loop = null; }
            try { controller.Dispose(); }
            catch (Exception ex) { AppLog.Write(ex); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping.Cancel();
        lock (gate)
        {
            if (dispatcher is not null && loop is not null)
            {
                try { dispatcher.BeginInvoke(loop.ExitThread); }
                catch (InvalidOperationException) { }
            }
        }
        // Normal shutdown restores immediately on the owner STA. If a native call
        // is stuck, process exit triggers the helper's registered restoration watch.
        thread.Join(1000);
    }
}
