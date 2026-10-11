using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Glasscape.Packaging;

public static class PackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, 0);
            return result switch
            {
                AppModelErrorNoPackage => false,
                0 or ErrorInsufficientBuffer => true,
                _ => throw new Win32Exception(result)
            };
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
