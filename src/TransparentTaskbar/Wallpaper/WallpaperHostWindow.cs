namespace TransparentTaskbar.Wallpaper;

internal sealed class WallpaperHostWindow : NativeWindow, IDisposable
{
    private bool disposed;
    internal event Action? Destroyed;

    internal WallpaperHostWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "Glasscape Wallpaper",
            Style = unchecked((int)(WallpaperNativeMethods.WsPopup |
                WallpaperNativeMethods.WsClipChildren | WallpaperNativeMethods.WsClipSiblings)),
            ExStyle = (int)(WallpaperNativeMethods.WsExNoActivate | WallpaperNativeMethods.WsExToolWindow),
            X = -32000, Y = -32000, Width = 1, Height = 1
        });
    }

    protected override void WndProc(ref Message message)
    {
        // Never activate or consume desktop input.
        if (message.Msg == 0x0021) { message.Result = 3; return; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message.Msg == 0x0084) { message.Result = -1; return; } // WM_NCHITTEST / HTTRANSPARENT
        base.WndProc(ref message);
        if (message.Msg == 0x0082 && !disposed) Destroyed?.Invoke(); // WM_NCDESTROY
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Destroyed = null;
        if (Handle != 0) DestroyHandle();
    }
}
