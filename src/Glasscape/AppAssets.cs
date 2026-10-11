using System.Reflection;

namespace Glasscape;

internal static class AppAssets
{
    internal static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Glasscape.Assets.AppIcon.ico");
        if (stream is null) return (Icon)SystemIcons.Application.Clone();
        using var original = new Icon(stream);
        return (Icon)original.Clone();
    }
}
