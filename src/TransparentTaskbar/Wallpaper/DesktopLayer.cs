using TransparentTaskbar.Localization;
// SPDX-License-Identifier: GPL-3.0-only
// Glasscape changes Copyright (c) 2026 BOMTOBE.
// Desktop discovery/parenting adapted from Lively Wallpaper, Dani John and contributors,
// revision 25a0a5f4ed1988ff42117d3a17feb321062237fe (WinDesktopCore.cs).
// Modified validation, timeout and host positioning; scope documented on 2026-10-11.
// See LICENSE-GPL-3.0.md and THIRD_PARTY_NOTICES.md.
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TransparentTaskbar.Wallpaper;

internal sealed record DesktopLayer(nint Progman, nint Parent, nint ShellView, nint WorkerW,
    uint ExplorerProcessId, bool Raised)
{
    internal bool IsValid
    {
        get
        {
            if (!WallpaperNativeMethods.IsWindow(Progman) || !WallpaperNativeMethods.IsWindow(Parent) ||
                !WallpaperNativeMethods.IsWindow(WorkerW) || !WallpaperNativeMethods.IsWindow(ShellView)) return false;
            WallpaperNativeMethods.GetWindowThreadProcessId(Progman, out var currentExplorer);
            return currentExplorer == ExplorerProcessId &&
                WallpaperNativeMethods.WindowClass(Progman) == "Progman" &&
                WallpaperNativeMethods.WindowClass(WorkerW) == "WorkerW";
        }
    }

    internal static DesktopLayer Discover()
    {
        var progman = WallpaperNativeMethods.FindWindow("Progman", null);
        if (progman == 0) throw new InvalidOperationException(AppText.Get("DesktopNotFound"));
        // Undocumented Explorer message used by Lively; timeout prevents a hung Explorer blocking the UI.
        WallpaperNativeMethods.SendMessageTimeout(progman, 0x052C, 0xD, 1, 0x0002, 750, out _);
        var raised = (WallpaperNativeMethods.GetWindowLongPtr(progman, WallpaperNativeMethods.GwlExStyle).ToInt64() &
            WallpaperNativeMethods.WsExNoRedirectionBitmap) != 0;
        nint worker = 0, shellView = 0;
        if (raised)
        {
            shellView = WallpaperNativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
            worker = WallpaperNativeMethods.FindWindowEx(progman, 0, "WorkerW", null);
        }
        else
        {
            WallpaperNativeMethods.EnumWindows((window, _) =>
            {
                var view = WallpaperNativeMethods.FindWindowEx(window, 0, "SHELLDLL_DefView", null);
                if (view == 0) return true;
                var next = WallpaperNativeMethods.FindWindowEx(0, window, "WorkerW", null);
                if (next == 0) return true;
                shellView = view;
                worker = next;
                return false;
            }, 0);
        }
        if (worker == 0 || shellView == 0)
            throw new InvalidOperationException(AppText.Get("DesktopNotReady"));
        WallpaperNativeMethods.GetWindowThreadProcessId(progman, out var explorerId);
        var layer = new DesktopLayer(progman, raised ? progman : worker, shellView, worker, explorerId, raised);
        layer.EnsureWorkerAtBottom();
        return layer;
    }

    internal void Attach(nint host, Rectangle bounds)
    {
        if (!IsValid) throw new InvalidOperationException(AppText.Get("DesktopRestarted"));
        var style = WallpaperNativeMethods.GetWindowLongPtr(host, WallpaperNativeMethods.GwlStyle).ToInt64();
        WallpaperNativeMethods.SetWindowLongPtr(host, WallpaperNativeMethods.GwlStyle,
            (nint)((style & ~WallpaperNativeMethods.WsPopup) | WallpaperNativeMethods.WsChild));
        // Required before parenting on the raised Windows 11 desktop. Also enables passive hit testing.
        var extended = WallpaperNativeMethods.GetWindowLongPtr(host, WallpaperNativeMethods.GwlExStyle).ToInt64();
        WallpaperNativeMethods.SetWindowLongPtr(host, WallpaperNativeMethods.GwlExStyle,
            (nint)(extended | WallpaperNativeMethods.WsExLayered | WallpaperNativeMethods.WsExTransparent |
                WallpaperNativeMethods.WsExNoActivate | WallpaperNativeMethods.WsExToolWindow));
        if (!WallpaperNativeMethods.SetLayeredWindowAttributes(host, 0, 255, 2))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), AppText.Get("DesktopRenderFailed"));
        Marshal.SetLastPInvokeError(0);
        var previous = WallpaperNativeMethods.SetParent(host, Parent);
        var error = Marshal.GetLastPInvokeError();
        if ((previous == 0 && error != 0) || WallpaperNativeMethods.GetParent(host) != Parent)
            throw new Win32Exception(error, AppText.Get("DesktopAttachFailed"));
        Position(host, bounds, show: true);
        // Disabled child input goes to its shell parent, while rendering remains active.
        WallpaperNativeMethods.EnableWindow(host, false);
        EnsureWorkerAtBottom();
    }

    internal void Position(nint host, Rectangle bounds, bool show = false)
    {
        var client = WallpaperNativeMethods.Rect.FromRectangle(bounds);
        Marshal.SetLastPInvokeError(0);
        var offset = WallpaperNativeMethods.MapWindowPoints(0, Parent, ref client, 2);
        var error = Marshal.GetLastPInvokeError();
        if (offset == 0 && error != 0) throw new Win32Exception(error);
        var after = Raised ? ShellView : (nint)1;
        var flags = WallpaperNativeMethods.SwpNoActivate | WallpaperNativeMethods.SwpFrameChanged;
        if (show) flags |= WallpaperNativeMethods.SwpShowWindow;
        if (!WallpaperNativeMethods.SetWindowPos(host, after, client.Left, client.Top,
            client.Right - client.Left, client.Bottom - client.Top, flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), AppText.Get("DesktopPositionFailed"));
    }

    internal void EnsureWorkerAtBottom()
    {
        if (Raised && WallpaperNativeMethods.IsWindow(WorkerW))
            WallpaperNativeMethods.SetWindowPos(WorkerW, 1, 0, 0, 0, 0,
                WallpaperNativeMethods.SwpNoMove | WallpaperNativeMethods.SwpNoSize | WallpaperNativeMethods.SwpNoActivate);
    }

    internal void Redraw()
    {
        if (WallpaperNativeMethods.IsWindow(Parent))
            WallpaperNativeMethods.RedrawWindow(Parent, 0, 0,
                WallpaperNativeMethods.RdwInvalidate | WallpaperNativeMethods.RdwAllChildren);
    }
}
