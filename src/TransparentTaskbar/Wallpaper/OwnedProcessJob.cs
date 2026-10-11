using TransparentTaskbar.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TransparentTaskbar.Wallpaper;

internal sealed class OwnedProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;

    internal OwnedProcessJob()
    {
        handle = WallpaperNativeMethods.CreateJobObject(0, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var information = new WallpaperNativeMethods.JobExtendedLimitInformation
        {
            BasicLimitInformation = new() { LimitFlags = 0x2000 } // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };
        if (!WallpaperNativeMethods.SetInformationJobObject(handle, 9, ref information,
            (uint)Marshal.SizeOf<WallpaperNativeMethods.JobExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    internal void Assign(Process process)
    {
        if (!WallpaperNativeMethods.AssignProcessToJobObject(handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), AppText.Get("PlayerJobFailed"));
    }

    public void Dispose() => handle.Dispose();
}
