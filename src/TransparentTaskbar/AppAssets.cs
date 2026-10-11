using System.Reflection;

namespace TransparentTaskbar;

internal static class AppAssets
{
    internal static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TransparentTaskbar.Assets.AppIcon.ico");
        if (stream is null) return (Icon)SystemIcons.Application.Clone();
        using var original = new Icon(stream);
        return (Icon)original.Clone();
    }
}
