// SPDX-License-Identifier: GPL-3.0-only
// Glasscape changes Copyright (c) 2026 BOMTOBE.
// Interop declarations adapted from TranslucentTB 2026.2 ExplorerTAP API.
// Upstream authors and source links: THIRD_PARTY_NOTICES.md.
// Modified for the C# client; license scope documented on 2026-10-11.
// See LICENSE-GPL-3.0.md for the complete license.
using System.Runtime.InteropServices;
using System.Text;

namespace TransparentTaskbar;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsCallback(nint hwnd, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(nint parent, EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hwnd, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hwnd, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string className, string? title);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowCompositionAttribute(nint hwnd, ref CompositionAttributeData data);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wparam, nint lparam,
        uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hwnd, uint message, nuint wparam, nint lparam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        internal int State;
        internal uint Flags;
        internal uint Color;
        internal int Animation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CompositionAttributeData
    {
        internal int Attribute;
        internal nint Data;
        internal nuint Size;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int InjectExplorerTap(nint taskbar, ref Guid iid, out nint service);
}

// Keep this order identical to the upstream 2026.2 ITaskbarAppearanceService.idl (API v4).
[ComImport, Guid("5bcf9150-c28a-4ef2-913c-4c3ea2f5ead0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarAppearanceService
{
    [PreserveSig] int SetTaskbarAppearance(nint taskbar, int brush, uint color);
    [PreserveSig] int SetTaskbarBlur(nint taskbar, uint color, float blurAmount);
    [PreserveSig] int ReturnTaskbarToDefaultAppearance(nint taskbar);
    [PreserveSig] int SetTaskbarBorderVisibility(nint taskbar, int visible);
    [PreserveSig] int RestoreAllTaskbarsToDefault();
    [PreserveSig] int RestoreAllTaskbarsToDefaultWhenProcessDies(uint processId);
    [PreserveSig] int KillExplorerWhenPackageUninstalls([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
}
