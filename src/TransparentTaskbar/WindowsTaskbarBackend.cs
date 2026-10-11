using TransparentTaskbar.Localization;
// SPDX-License-Identifier: GPL-3.0-only
// Glasscape changes Copyright (c) 2026 BOMTOBE.
// Taskbar classification/restoration adapted from TranslucentTB 2026.2.
// Upstream authors and source links: THIRD_PARTY_NOTICES.md.
// Modified for .NET lifecycle/error handling; scope documented on 2026-10-11.
// See LICENSE-GPL-3.0.md for the complete license.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TransparentTaskbar;

internal sealed class WindowsTaskbarBackend : ITaskbarBackend
{
    private ITaskbarAppearanceService? service;
    private nint nativeLibrary;
    private readonly HashSet<nint> classicTaskbars = [];
    private int opacity;

    internal int Opacity
    {
        get => Volatile.Read(ref opacity);
        set => Volatile.Write(ref opacity, Math.Clamp(value, 0, 100));
    }

    public IReadOnlyList<TaskbarWindow> FindTaskbars()
    {
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary == 0) return [];
        NativeMethods.GetWindowThreadProcessId(primary, out var pid);
        var xaml = IsXamlTaskbar(primary, pid);
        var taskbars = new List<TaskbarWindow> { new(primary, pid, xaml) };
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (GetClass(hwnd) == "Shell_SecondaryTrayWnd")
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var secondaryPid);
                if (secondaryPid == pid) taskbars.Add(new(hwnd, pid, xaml));
            }
            return true;
        }, 0);
        return taskbars;
    }

    private static bool IsXamlTaskbar(nint primary, uint pid)
    {
        using var explorer = Process.GetProcessById(checked((int)pid));
        var taskbarDll = Path.Combine(Environment.SystemDirectory, "Taskbar.dll");
        var hasTaskbarDll = explorer.Modules.Cast<ProcessModule>().Any(module =>
            string.Equals(module.FileName, taskbarDll, StringComparison.OrdinalIgnoreCase));
        if (!hasTaskbarDll) return false;

        var islandCount = 0;
        NativeMethods.EnumChildWindows(primary, (hwnd, _) =>
        {
            var title = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            if (GetClass(hwnd) == "Windows.UI.Composition.DesktopWindowContentBridge"
                && title.ToString() == "DesktopWindowXamlSource") islandCount++;
            return true;
        }, 0);
        return islandCount switch
        {
            1 => true,
            2 => false, // Original Windows 11 22000 mixed taskbar uses the classic path.
            _ => throw new InvalidOperationException(AppText.Get("TaskbarUnsupported"))
        };
    }

    private static string GetClass(nint hwnd)
    {
        var text = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public void Connect(TaskbarWindow primary)
    {
        if (!primary.IsXaml) return;
        if (nativeLibrary == 0)
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "ExplorerTAP.dll");
            if (!File.Exists(dllPath))
                throw new FileNotFoundException(AppText.Get("TaskbarMissingModule"), dllPath);
            nativeLibrary = NativeLibrary.Load(dllPath);
            // Retain until process exit: the DLL registers an in-process COM proxy factory.
        }

        var inject = Marshal.GetDelegateForFunctionPointer<NativeMethods.InjectExplorerTap>(
            NativeLibrary.GetExport(nativeLibrary, "InjectExplorerTAP"));
        var iid = typeof(ITaskbarAppearanceService).GUID;
        Check(inject(primary.Handle, ref iid, out var pointer));
        try
        {
            service = (ITaskbarAppearanceService)Marshal.GetObjectForIUnknown(pointer);
            // Register restoration BEFORE modifying Explorer. Another client's watch may
            // still be completing; propagate that failure so the tray timer retries.
            Check(service.RestoreAllTaskbarsToDefaultWhenProcessDies((uint)Environment.ProcessId));
        }
        catch
        {
            Disconnect();
            throw;
        }
        finally
        {
            if (pointer != 0) Marshal.Release(pointer);
        }
    }

    public void MakeTransparent(TaskbarWindow taskbar)
    {
        var color = (uint)Math.Round(Opacity * 255.0 / 100.0) << 24;
        if (taskbar.IsXaml)
        {
            var appearance = service ?? throw new InvalidOperationException(AppText.Get("TaskbarNotConnected"));
            Check(appearance.SetTaskbarAppearance(taskbar.Handle, 1, color)); // SolidColor, ABGR opacity.
            Check(appearance.SetTaskbarBorderVisibility(taskbar.Handle, 0));
            return;
        }

        var policy = new NativeMethods.AccentPolicy { State = 2, Flags = 2, Color = color };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new NativeMethods.CompositionAttributeData
            {
                Attribute = 19, Data = buffer, Size = (nuint)Marshal.SizeOf<NativeMethods.AccentPolicy>()
            };
            if (!NativeMethods.SetWindowCompositionAttribute(taskbar.Handle, ref data))
                throw new Win32Exception(Marshal.GetLastWin32Error(), AppText.Get("TaskbarTransparencyFailed"));
            classicTaskbars.Add(taskbar.Handle);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Restore()
    {
        Exception? failure = null;
        if (service is not null)
        {
            try { Check(service.RestoreAllTaskbarsToDefault()); }
            catch (Exception ex) { failure = ex; }
        }
        foreach (var hwnd in classicTaskbars)
        {
            if (!NativeMethods.IsWindow(hwnd)) continue;
            // Ask Explorer to rebuild its own original accent, as the upstream app does.
            var result = NativeMethods.SendMessageTimeout(hwnd, 0x031E, 1, 0, 2, 1000, out _);
            if (result == 0) failure ??= new Win32Exception(Marshal.GetLastWin32Error(), AppText.Get("TaskbarRestoreFailed"));
        }
        if (failure is not null) throw failure;
    }

    public void Disconnect()
    {
        if (service is not null)
        {
            var oldService = service;
            service = null;
            Marshal.FinalReleaseComObject(oldService);
        }
        classicTaskbars.Clear();
    }

    private static void Check(int hresult)
    {
        if (hresult >= 0) return;
        var message = hresult switch
        {
            unchecked((int)0x80070666) => AppText.Get("TaskbarOldModule"),
            unchecked((int)0x80070005) => AppText.Get("TaskbarAccessDenied"),
            unchecked((int)0x800401E3) => AppText.Get("TaskbarServiceMissing"),
            unchecked((int)0x80070102) => AppText.Get("TaskbarConnectTimeout"),
            unchecked((int)0x8000000E) => AppText.Get("TaskbarOtherApp"),
            _ => AppText.Get("TaskbarFailed")
        };
        throw new COMException($"{message} (0x{hresult:X8})", hresult);
    }
}
