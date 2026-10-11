namespace Glasscape;

internal static class InstallationCopyGuard
{
    internal static void Copy(string source, string destination, string invalidPathMessage, string sourceParentMessage)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        EnsureUnlinkedPath(source, invalidPathMessage);
        EnsureUnlinkedPath(destination, invalidPathMessage);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return;
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(sourceParentMessage);

        // Finish both traversals before writing. Enumerate one directory at a time
        // so links are inspected before anything can traverse their targets.
        var files = ReadUnlinkedTree(source, invalidPathMessage);
        if (Directory.Exists(destination)) ReadUnlinkedTree(destination, invalidPathMessage);
        var copies = new List<(string Source, string Target)>();
        foreach (var file in files)
        {
            var target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(source, file)));
            if (!target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException(invalidPathMessage);
            EnsureUnlinkedPath(target, invalidPathMessage);
            copies.Add((file, target));
        }
        foreach (var (file, target) in copies)
        {
            EnsureUnlinkedPath(file, invalidPathMessage);
            EnsureUnlinkedPath(target, invalidPathMessage);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            EnsureUnlinkedPath(target, invalidPathMessage);
            File.Copy(file, target, true);
        }
    }

    private static List<string> ReadUnlinkedTree(string root, string message)
    {
        EnsureUnlinkedPath(root, message);
        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                var fullPath = Path.GetFullPath(item);
                if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(message);
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(message);
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(fullPath);
                else files.Add(fullPath);
            }
        }
        return files;
    }

    private static void EnsureUnlinkedPath(string path, string message)
    {
        for (string? cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException(message);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
