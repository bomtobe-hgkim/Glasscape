namespace Glasscape;

internal static class AppLog
{
    internal static void Write(Exception error)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glasscape");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
