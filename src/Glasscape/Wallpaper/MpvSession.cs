using Glasscape.Localization;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Glasscape.Wallpaper;

internal sealed record MpvPlaybackState(bool Paused, bool Muted, int Volume, bool Fill);

/// <summary>Owns one mpv process. All pipe work is asynchronous and each command has a deadline.</summary>
internal sealed class MpvSession : IDisposable
{
    private readonly Process process;
    private readonly OwnedProcessJob job;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> requests = new();
    private readonly object stateLock = new();
    private readonly string pipeName = "glasscape-wallpaper-" + Guid.NewGuid().ToString("N");
    private readonly Action<MpvSession, string?> changed;
    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private MpvPlaybackState desiredState;
    private MpvPlaybackState? sentState;
    private long requestId;
    private int updateRunning, failed;
    private volatile bool disposed, ready;

    internal bool IsReady => ready && !disposed;
    internal uint ProcessId { get; private set; }
    internal WallpaperHostWindow Host { get; }
    internal MonitorInfo Monitor { get; set; }

    internal MpvSession(string executable, string filePath, WallpaperHostWindow host, MonitorInfo monitor,
        MpvPlaybackState state, Action<MpvSession, string?> changed)
    {
        Host = host;
        Monitor = monitor;
        desiredState = state;
        this.changed = changed;
        job = new OwnedProcessJob();
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in MpvLaunchArguments.Create(host.Handle, pipeName, filePath,
            state.Fill, state.Muted, state.Volume)) startInfo.ArgumentList.Add(argument);
        // A lock/battery/user pause present during startup must prevent the first video frames playing.
        startInfo.ArgumentList.Insert(startInfo.ArgumentList.Count - 2, "--pause=" + (state.Paused ? "yes" : "no"));
        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += ProcessExited;
        try
        {
            if (!process.Start()) throw new InvalidOperationException(AppText.Get("PlayerStartFailed"));
            ProcessId = (uint)process.Id;
            job.Assign(process);
            _ = DrainErrorsAsync();
            _ = ConnectAndValidateAsync();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void SetPlayback(MpvPlaybackState state)
    {
        lock (stateLock) desiredState = state;
        if (IsReady) StartUpdate();
    }

    private async Task DrainErrorsAsync()
    {
        try
        {
            // Drain the redirected stream so diagnostic output can never block the player.
            while (await process.StandardError.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                if (line.Contains("Error opening", StringComparison.OrdinalIgnoreCase))
                    Fail(AppText.Get("PlayerOpenFailed"));
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private async Task ConnectAndValidateAsync()
    {
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(10));
            while (!startup.IsCancellationRequested)
            {
                var attempt = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await attempt.ConnectAsync(600, startup.Token).ConfigureAwait(false);
                    pipe = attempt;
                    break;
                }
                catch (TimeoutException)
                {
                    attempt.Dispose();
                    await Task.Delay(150, startup.Token).ConfigureAwait(false);
                }
                catch { attempt.Dispose(); throw; }
            }
            startup.Token.ThrowIfCancellationRequested();
            writer = new StreamWriter(pipe!, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            _ = ReadMessagesAsync(pipe!);
            // file-loaded can occur before the pipe connects. Actual video output parameters confirm video decoding.
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                try
                {
                    var output = await RequestAsync(["get_property", "video-out-params"], startup.Token).ConfigureAwait(false);
                    if (output.ValueKind == JsonValueKind.Object && output.TryGetProperty("w", out var width) &&
                        output.TryGetProperty("h", out var height) && width.GetInt32() > 0 && height.GetInt32() > 0)
                        break;
                }
                catch (MpvCommandException) { } // Video can still be opening.
                await Task.Delay(150, startup.Token).ConfigureAwait(false);
            }
            if (disposed || Volatile.Read(ref failed) != 0) return;
            ready = true;
            changed(this, null);
            StartUpdate();
        }
        catch (OperationCanceledException) when (disposed || lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            Fail(AppText.Get("PlayerTimeout"));
        }
        catch (Exception error)
        {
            if (!disposed) Fail(AppText.Format("PlayerConnectFailed", error.Message));
        }
    }

    private async Task ReadMessagesAsync(NamedPipeClientStream connectedPipe)
    {
        try
        {
            using var reader = new StreamReader(connectedPipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (!disposed && await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                if (line.Length > 1_048_576) throw new IOException(AppText.Get("PlayerResponseTooLarge"));
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("request_id", out var id) && id.TryGetInt64(out var number) &&
                    requests.TryRemove(number, out var completion))
                {
                    var result = root.TryGetProperty("error", out var status) ? status.GetString() : "invalid response";
                    if (result == "success")
                        completion.TrySetResult(root.TryGetProperty("data", out var data) ? data.Clone() : default);
                    else completion.TrySetException(new MpvCommandException(result ?? "invalid response"));
                }
                else if (root.TryGetProperty("event", out var eventName) && eventName.GetString() == "end-file" &&
                    root.TryGetProperty("reason", out var reason) && reason.GetString() == "error")
                {
                    Fail(AppText.Get("PlayerInvalidVideo"));
                }
            }
            if (!disposed) Fail(AppText.Get("PlayerDisconnected"));
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception error)
        {
            if (!disposed) Fail(AppText.Format("PlayerCommunicationError", error.Message));
        }
        finally
        {
            foreach (var request in requests.Values) request.TrySetCanceled();
            requests.Clear();
        }
    }

    private async Task<JsonElement> RequestAsync(object[] command, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        deadline.CancelAfter(900);
        await commandLock.WaitAsync(deadline.Token).ConfigureAwait(false);
        var id = Interlocked.Increment(ref requestId);
        try
        {
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests[id] = completion;
            var payload = JsonSerializer.Serialize(new { command, request_id = id });
            if (writer is null) throw new IOException(AppText.Get("PlayerNotReady"));
            await writer.WriteLineAsync(payload.AsMemory(), deadline.Token).ConfigureAwait(false);
            await writer.FlushAsync(deadline.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            requests.TryRemove(id, out _);
            commandLock.Release();
        }
    }

    private void StartUpdate()
    {
        if (Interlocked.CompareExchange(ref updateRunning, 1, 0) == 0) _ = UpdateLoopAsync();
    }

    private async Task UpdateLoopAsync()
    {
        try
        {
            while (!disposed && IsReady)
            {
                MpvPlaybackState current;
                lock (stateLock) current = desiredState;
                if (current == sentState) return;
                // Pause first so later settings updates never accidentally resume a locked desktop.
                if (sentState?.Paused != current.Paused)
                    await RequestAsync(["set_property", "pause", current.Paused], lifetime.Token).ConfigureAwait(false);
                if (sentState?.Muted != current.Muted)
                    await RequestAsync(["set_property", "mute", current.Muted], lifetime.Token).ConfigureAwait(false);
                if (sentState?.Volume != current.Volume)
                    await RequestAsync(["set_property", "volume", current.Volume], lifetime.Token).ConfigureAwait(false);
                if (sentState?.Fill != current.Fill)
                    await RequestAsync(["set_property", "panscan", current.Fill ? 1 : 0], lifetime.Token).ConfigureAwait(false);
                sentState = current;
            }
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error)
        {
            if (!disposed) Fail(AppText.Format("PlayerSettingsFailed", error.Message));
        }
        finally
        {
            Interlocked.Exchange(ref updateRunning, 0);
            lock (stateLock)
                if (!disposed && IsReady && Volatile.Read(ref failed) == 0 && desiredState != sentState) StartUpdate();
        }
    }

    private void ProcessExited(object? sender, EventArgs args)
    {
        if (disposed) return;
        string code;
        try { code = process.ExitCode.ToString(); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { code = "?"; }
        Fail(AppText.Format("PlayerExited", code));
    }

    private void Fail(string message)
    {
        if (disposed || Interlocked.Exchange(ref failed, 1) != 0) return;
        ready = false;
        changed(this, message);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ready = false;
        lifetime.Cancel();
        process.Exited -= ProcessExited;
        // Closing the owned Job also cleans up after parent process crashes. No shell process is a member.
        job.Dispose();
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException) { }
        pipe?.Dispose();
        try { writer?.Dispose(); }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
        process.Dispose();
        foreach (var request in requests.Values) request.TrySetCanceled();
        requests.Clear();
        // The caller owns the HWND and destroys it on its STA thread after this process has been stopped.
    }

    private sealed class MpvCommandException(string message) : IOException(message);
}
