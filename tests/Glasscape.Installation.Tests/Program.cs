using System.Reflection;

var root = Path.Combine(Path.GetTempPath(), "glasscape-install-guard-" + Guid.NewGuid().ToString("N"));
var links = new List<(string Path, bool Directory)>();
var failures = 0;
var count = 0;
try
{
    Check("Ordinary nested files copy without changing the source", () =>
    {
        var (source, destination, _) = Fixture("ordinary");
        Copy(source, destination);
        Require(File.ReadAllText(Path.Combine(destination, "nested", "data.txt")) == "source data", "Nested data must be copied.");
        Require(File.ReadAllText(Path.Combine(source, "nested", "data.txt")) == "source data", "Copy must preserve source data.");
    });
    foreach (var directoryLink in new[] { false, true })
    {
        Check("A source " + (directoryLink ? "directory" : "file") + " link is refused before destination writes", () =>
        {
            var (source, destination, outside) = Fixture("source-link-" + directoryLink);
            var link = Path.Combine(source, "nested", directoryLink ? "redirected" : "linked.txt");
            Link(link, directoryLink ? outside : Path.Combine(outside, "data.txt"), directoryLink);
            Refuses(() => Copy(source, destination));
            Require(!File.Exists(Path.Combine(destination, "nested", "data.txt")), "A refused source must not partially copy ordinary files.");
            Preserved(destination, outside);
        });
        Check("A destination " + (directoryLink ? "directory" : "file") + " link is refused before overwrite", () =>
        {
            var (source, destination, outside) = Fixture("destination-link-" + directoryLink);
            var link = Path.Combine(destination, directoryLink ? "nested" : "nested/data.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            Link(link, directoryLink ? outside : Path.Combine(outside, "data.txt"), directoryLink);
            Refuses(() => Copy(source, destination));
            Require(!File.Exists(Path.Combine(destination, "ordinary.txt")), "A refused destination must not partially copy ordinary files.");
            Preserved(destination, outside);
        });
    }
    Check("A linked destination ancestor is refused without creating an external install folder", () =>
    {
        var (source, destination, outside) = Fixture("destination-ancestor");
        var link = Path.Combine(destination, "redirected");
        Link(link, outside, true);
        Refuses(() => Copy(source, Path.Combine(link, "new-install")));
        Require(!Directory.Exists(Path.Combine(outside, "new-install")), "An external installation folder must not be created.");
        Preserved(destination, outside);
    });
    Check("Overlapping source and destination trees are refused", () =>
    {
        var (source, _, _) = Fixture("overlap");
        Refuses(() => Copy(source, Path.Combine(source, "installed")));
        Require(!Directory.Exists(Path.Combine(source, "installed")), "An overlapping destination must not be created.");
    });
}
finally
{
    foreach (var (path, directory) in links)
    {
        if (directory) Directory.Delete(path); else File.Delete(path);
    }
    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(root)), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        throw new Exception("Unsafe fixture cleanup path.");
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
Console.WriteLine($"{count - failures}/{count} installation guard tests passed. No windows, shortcuts, startup registration or app processes were created.");
return failures == 0 ? 0 : 1;

void Check(string name, Action run)
{
    count++;
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
(string Source, string Destination, string Outside) Fixture(string name)
{
    var folder = Path.Combine(root, name);
    var source = Path.Combine(folder, "source");
    var destination = Path.Combine(folder, "destination");
    var outside = Path.Combine(folder, "outside");
    Directory.CreateDirectory(Path.Combine(source, "nested"));
    Directory.CreateDirectory(destination);
    Directory.CreateDirectory(outside);
    File.WriteAllText(Path.Combine(source, "ordinary.txt"), "ordinary data");
    File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "source data");
    File.WriteAllText(Path.Combine(destination, "marker.txt"), "existing destination");
    File.WriteAllText(Path.Combine(outside, "data.txt"), "external data");
    return (source, destination, outside);
}
void Link(string path, string target, bool directory)
{
    if (directory) Directory.CreateSymbolicLink(path, target); else File.CreateSymbolicLink(path, target);
    links.Add((path, directory));
}
static void Copy(string source, string destination)
{
    var type = Assembly.GetExecutingAssembly().GetType("TransparentTaskbar.InstallationCopyGuard")
        ?? throw new InvalidOperationException("A validated installation copy boundary is missing.");
    var method = type.GetMethod("Copy", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("A validated installation copy boundary is missing.");
    var copy = (Action<string, string, string, string>)method.CreateDelegate(typeof(Action<string, string, string, string>));
    copy(source, destination, "Invalid installation path.", "Overlapping installation path.");
}
static void Refuses(Action action)
{
    try { action(); }
    catch (IOException error) when (error.Message is "Invalid installation path." or "Overlapping installation path.") { return; }
    throw new Exception("Unsafe linked/overlapping copy was accepted.");
}
static void Preserved(string destination, string outside)
{
    Require(File.ReadAllText(Path.Combine(destination, "marker.txt")) == "existing destination", "Existing destination data must remain unchanged.");
    Require(File.ReadAllText(Path.Combine(outside, "data.txt")) == "external data", "External data must remain unchanged.");
}
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
