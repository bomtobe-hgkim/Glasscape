using System.Globalization;

namespace Glasscape.Wallpaper;

internal static class MpvLaunchArguments
{
    internal static IReadOnlyList<string> Create(nint host, string pipeName, string filePath,
        bool fill, bool muted, int volume)
    {
        return
        [
            "--no-config", "--load-scripts=no", "--loop-file=inf", "--keep-open=yes",
            "--force-window=yes", "--no-border", "--no-osc", "--osd-level=0",
            "--input-default-bindings=no", "--input-vo-keyboard=no", "--input-cursor=no",
            "--input-terminal=no", "--terminal=no", "--cursor-autohide=no",
            "--stop-screensaver=no", "--media-controls=no", "--hwdec=auto-safe",
            "--wid=" + unchecked((uint)host.ToInt64()).ToString(CultureInfo.InvariantCulture),
            "--input-ipc-server=\\\\.\\pipe\\" + pipeName,
            "--panscan=" + (fill ? "1" : "0"),
            "--mute=" + (muted ? "yes" : "no"),
            "--volume=" + Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture),
            "--", filePath
        ];
    }
}
