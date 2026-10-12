namespace Glasscape;

internal readonly record struct TaskbarWindow(nint Handle, uint ProcessId, bool IsXaml);

/// <summary>
/// Explorer keeps one taskbar implementation for its lifetime, but the XAML island count used to detect it
/// briefly changes within one Explorer process (Windows 11 26200), so each process is classified once.
/// </summary>
internal sealed class TaskbarKindCache
{
    private uint? processId;
    private bool isXaml;

    internal bool IsXaml(uint explorerProcessId, Func<bool> classify)
    {
        if (processId != explorerProcessId)
        {
            isXaml = classify();
            processId = explorerProcessId;
        }
        return isXaml;
    }
}

internal interface ITaskbarBackend
{
    IReadOnlyList<TaskbarWindow> FindTaskbars();
    void Connect(TaskbarWindow primary);
    void MakeTransparent(TaskbarWindow taskbar);
    void Restore();
    void Disconnect();
}

internal sealed class TaskbarController(ITaskbarBackend backend) : IDisposable
{
    private (uint ProcessId, bool IsXaml)? connection;
    private bool disposed;

    public int Refresh(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var taskbars = backend.FindTaskbars();
        if (taskbars.Count == 0) return 0;

        var primary = taskbars[0];
        var identity = (primary.ProcessId, primary.IsXaml);
        if (connection != identity)
        {
            backend.Disconnect();
            connection = null;
            cancellationToken.ThrowIfCancellationRequested();
            backend.Connect(primary);
            connection = identity;
        }

        foreach (var taskbar in taskbars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            backend.MakeTransparent(taskbar);
        }
        return taskbars.Count;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (connection is not null) backend.Restore();
        }
        finally
        {
            connection = null;
            backend.Disconnect();
        }
    }
}
