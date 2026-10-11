namespace TransparentTaskbar.Settings;

internal static class BundledWallpapers
{
    internal static void Seed(AppSettings settings, string baseDirectory)
    {
        (Guid Id, string Name, string Title, int Version)[] samples =
        [
            (new Guid("070571bc-861b-4a5e-8a51-5e1e5c96fd01"), "aurora-flow", "오로라의 흐름", 1),
            (new Guid("070571bc-861b-4a5e-8a51-5e1e5c96fd02"), "ocean-drift", "느린 바다", 1)
        ];
        settings.InitializedBundledSamples ??= [];
        // The legacy flag covered the original two samples, including ones the user removed.
        if (settings.BundledSamplesInitialized)
            foreach (var sample in samples.Where(sample => sample.Version == 1))
                if (!settings.InitializedBundledSamples.Contains(sample.Id)) settings.InitializedBundledSamples.Add(sample.Id);
        foreach (var (id, name, title, _) in samples)
        {
            var path = Path.Combine(baseDirectory, "samples", name + ".mp4");
            if (!File.Exists(path)) continue;
            var entry = settings.Wallpapers.FirstOrDefault(item => item.Id == id);
            if (entry is null && settings.InitializedBundledSamples.Contains(id)) continue;
            if (entry is null) { entry = new WallpaperEntry { Id = id, Title = title }; settings.Wallpapers.Add(entry); }
            entry.FilePath = path;
            var poster = Path.ChangeExtension(path, ".png");
            entry.ThumbnailPath = File.Exists(poster) ? poster : null;
            if (!settings.InitializedBundledSamples.Contains(id)) settings.InitializedBundledSamples.Add(id);
        }
        settings.BundledSamplesInitialized = samples.Where(sample => sample.Version == 1).All(sample => settings.InitializedBundledSamples.Contains(sample.Id));
    }
}
