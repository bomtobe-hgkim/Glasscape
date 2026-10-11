using System.ComponentModel;
using System.Runtime.InteropServices;
using TransparentTaskbar.Localization;

namespace TransparentTaskbar.Settings;

public sealed class TaskbarAutoHideService
{
    public bool IsEnabled => (ReadState() & 1) != 0;

    public void SetEnabled(bool enabled)
    {
        var data = CreateData();
        var current = SHAppBarMessage(4, ref data).ToUInt32();
        var next = CombineState(current, enabled);
        if (current == next) return;
        data.Parameter = (nint)next;
        SHAppBarMessage(10, ref data);
        if ((ReadState() & 1) != (next & 1))
            throw new InvalidOperationException(AppText.Get("AutoHideFailed"));
    }

    public static uint CombineState(uint current, bool enabled) => enabled ? current | 1u : current & ~1u;

    private static uint ReadState()
    {
        var data = CreateData();
        return SHAppBarMessage(4, ref data).ToUInt32();
    }

    private static AppBarData CreateData()
    {
        var hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd == 0) throw new Win32Exception(AppText.Get("AutoHideNotReady"));
        return new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = hwnd };
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData
    {
        internal uint Size;
        internal nint Window;
        internal uint CallbackMessage, Edge;
        internal Rectangle Bounds;
        internal nint Parameter;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
}
