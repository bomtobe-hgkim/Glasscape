using System.Security.Cryptography;

namespace Glasscape;

internal static class TaskbarModuleCache
{
    internal static string Prepare(string sourcePath, string cacheDirectory)
    {
        var contents = File.ReadAllBytes(sourcePath);
        var hash = SHA256.HashData(contents);
        // Explorer pins the DLL for its lifetime. Separate contents by hash so an
        // app update never overwrites a module still loaded by the shell.
        var directory = Path.Combine(cacheDirectory, Convert.ToHexStringLower(hash));
        var target = Path.Combine(directory, "ExplorerTAP.dll");
        Directory.CreateDirectory(directory);
        if (Matches(target, hash)) return target;

        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, contents);
            try { File.Move(temporary, target); }
            catch (IOException) when (File.Exists(target))
            {
                // A concurrent client may have published the same DLL. Only
                // replace a damaged copy; never rewrite a valid loaded module.
                if (!Matches(target, hash)) File.Move(temporary, target, overwrite: true);
            }
        }
        finally { File.Delete(temporary); }
        return target;
    }

    private static bool Matches(string path, byte[] hash)
    {
        if (!File.Exists(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return SHA256.HashData(stream).AsSpan().SequenceEqual(hash);
    }
}
