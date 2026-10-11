using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using TransparentTaskbar.Localization;
using TransparentTaskbar.Settings;
using TransparentTaskbar.UI;
using TransparentTaskbar.Wallpaper;

internal static class Program
{
    private static readonly List<string> failures = [];
    private static readonly List<string> observations = [];
    private static readonly (string Name, string Control)[] pages = [("overview", "NavOverview"), ("taskbar", "NavTaskbar"), ("wallpaper", "NavWallpaper"), ("settings", "NavSettings")];

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--store-capture"))
        {
            try { return CaptureStoreScreenshots(args); }
            catch (Exception error) { Console.Error.WriteLine("FAIL: hidden capture: " + error.Message); return 1; }
        }
        failures.AddRange(LocalizationChecks.RunResourceChecks());
        if (!args.Contains("--interactive-layout"))
        {
            foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            Console.WriteLine(failures.Count == 0 ? "PASS: localization resources, culture fallback, bundled titles and settings persistence. No windows were shown." : $"FAIL: {failures.Count} localization checks.");
            return failures.Count == 0 ? 0 : 1;
        }
        failures.AddRange(LocalizationChecks.Run());
        if (args.Contains("--localization-only"))
        {
            foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            Console.WriteLine(failures.Count == 0 ? "PASS: localization preference and live language selection." : $"FAIL: {failures.Count} localization checks.");
            return failures.Count == 0 ? 0 : 1;
        }
        var requested = Option(args, "--culture");
        foreach (var culture in requested is null ? new[] { "en-US", "ko-KR" } : new[] { requested }) ValidateLayout(culture, args);
        foreach (var failure in failures.Distinct()) Console.Error.WriteLine("FAIL: " + failure);
        var summary = failures.Count == 0 ? "PASS: English/Korean first show, DPI round trip, minimum size, imports and empty library fit on every connected monitor." : $"FAIL: {failures.Count} layout checks.";
        Console.WriteLine(summary);
        var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(WorkspaceRoot(), "artifacts/ui/dpi"));
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "result.log"), observations.Concat(failures.Distinct().Select(f => "FAIL: " + f)).Append(summary));
        return failures.Count == 0 ? 0 : 1;
    }

    private static void ValidateLayout(string culture, string[] args)
    {
        var output = Path.Combine(Path.GetFullPath(Option(args, "--output") ?? Path.Combine(WorkspaceRoot(), "artifacts/ui/dpi")), culture);
        Directory.CreateDirectory(output);
        var screens = Screen.AllScreens.OrderByDescending(s => s.Primary).ToArray();
        var monitors = screens.Select(s => new MonitorInfo(s.DeviceName, s.DeviceName, s.Bounds, s.Primary)).ToArray();
        var settings = new AppSettings { Language = culture, Wallpapers = [new WallpaperEntry { Title = "Test scenery", FilePath = "sample.mp4" }] };
        using var window = new MainWindow(settings, monitors) { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
        observations.Add($"Constructor DPI={window.DeviceDpi}, scale={window.AutoScaleDimensions}");
        foreach (var screen in screens.Concat(screens.Take(1)))
        {
            window.Location = new Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20);
            if (!window.Visible) window.Show();
            Application.DoEvents();
            Console.WriteLine($"{culture} / Monitor {screen.DeviceName}: DPI={window.DeviceDpi}, scale={window.AutoScaleDimensions}, client={window.ClientSize}");
            observations.Add($"{culture} / Monitor {screen.DeviceName}: DPI={window.DeviceDpi}, scale={window.AutoScaleDimensions}, client={window.ClientSize}, workArea={screen.WorkingArea}");
            settings.Wallpapers.Add(new WallpaperEntry { Title = "Imported scenery", FilePath = Guid.NewGuid() + ".mp4" });
            window.UpdateState(settings, new(false, false, null, null, null, 0), AppText.Get("TaskbarWaiting"), false);
            foreach (var page in pages)
            {
                Descendants(window).OfType<Button>().Single(b => b.Name == page.Control).PerformClick();
                Application.DoEvents();
                var context = $"{culture} / {screen.DeviceName} {window.DeviceDpi} DPI / {page.Name}";
                CheckText(window, context);
                CheckButtons(window, context); CheckCulture(window, culture, context);
                CheckToggles(window, context);
                CheckLibrary(window, context);
                CheckPreview(window, context);
                CheckColumns(window, context);
                if (args.Contains("--capture"))
                {
                    using var bitmap = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(bitmap, window.ClientRectangle with { Width = window.Width, Height = window.Height });
                    bitmap.Save(Path.Combine(output, $"{window.DeviceDpi}-{page.Name}.png"), ImageFormat.Png);
                }
            }
            var fullSize = window.Size;
            window.Size = window.MinimumSize;
            Application.DoEvents();
            foreach (var page in pages)
            {
                Descendants(window).OfType<Button>().Single(b => b.Name == page.Control).PerformClick();
                Application.DoEvents();
                var context = $"{culture} / {screen.DeviceName} {window.DeviceDpi} DPI / minimum size / {page.Name}";
                CheckText(window, context);
                CheckButtons(window, context); CheckCulture(window, culture, context);
                CheckToggles(window, context);
                CheckLibrary(window, context);
                CheckColumns(window, context);
                if (args.Contains("--capture"))
                {
                    using var bitmap = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                    bitmap.Save(Path.Combine(output, $"{window.DeviceDpi}-minimum-{page.Name}.png"), ImageFormat.Png);
                }
            }
            window.Size = fullSize;
        }
        settings.Wallpapers.Clear();
        window.UpdateState(settings, new(false, false, null, null, null, 0), AppText.Get("TaskbarWaiting"), false);
        foreach (var screen in screens)
        {
            window.Location = new Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20);
            Descendants(window).OfType<Button>().Single(b => b.Name == "NavWallpaper").PerformClick();
            Application.DoEvents();
            var empty = Descendants(window).OfType<GlassButton>().Single(b => b.AccessibleName == AppText.Get("ImportVideoAccessible"));
            using var graphics = empty.CreateGraphics();
            var required = TextRenderer.MeasureText(graphics, empty.Text, empty.Font);
            if (required.Height > empty.Height || empty.Width > empty.Parent!.ClientSize.Width)
                failures.Add($"{window.DeviceDpi} DPI: empty-library import button overflows its viewport or clips its text.");
        }
        window.Hide();
        foreach (var screen in screens)
        {
            using var fresh = new MainWindow(new AppSettings { Language = culture }, monitors)
            {
                StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                Location = new Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20)
            };
            fresh.Show();
            Application.DoEvents();
            var context = $"{culture} / {fresh.DeviceDpi} DPI / first show";
            CheckText(fresh, context); CheckButtons(fresh, context); CheckToggles(fresh, context); CheckColumns(fresh, context); CheckPreview(fresh, context);
            fresh.Close();
        }
        window.Close();
    }

    private static void CheckText(Control root, string context)
    {
        foreach (var label in Descendants(root).OfType<Label>().Where(l => l.Visible && l.Text.Length > 0))
        {
            using var graphics = label.CreateGraphics();
            var required = TextRenderer.MeasureText(graphics, label.Text, label.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.Default);
            var available = label.ClientSize - label.Padding.Size;
            var labelLeft = label.PointToScreen(Point.Empty).X;
            for (var ancestor = label.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                var ancestorLeft = ancestor.PointToScreen(Point.Empty).X;
                var ancestorRight = ancestor.PointToScreen(new Point(ancestor.ClientSize.Width, 0)).X;
                available.Width = Math.Min(available.Width, ancestorRight - labelLeft);
                if (labelLeft < ancestorLeft)
                    failures.Add($"{context}: '{label.Text.Replace('\n', ' ')}' is clipped at the left edge of its viewport.");
            }
            if (required.Height > available.Height || (!label.AutoEllipsis && required.Width > available.Width))
                failures.Add($"{context}: '{label.Text.Replace('\n', ' ')}' needs {required}, has {available}.");
        }
    }

    private static void CheckToggles(Control root, string context)
    {
        foreach (var toggle in Descendants(root).OfType<GlassToggle>().Where(t => t.Visible))
        {
            using var bitmap = new Bitmap(toggle.Width, toggle.Height);
            toggle.DrawToBitmap(bitmap, toggle.ClientRectangle);
            var track = toggle.Checked ? GlassTheme.Teal : GlassTheme.Line;
            var left = bitmap.Width;
            var right = -1;
            for (var x = 0; x < bitmap.Width; x++)
            for (var y = 0; y < bitmap.Height; y++)
                if (bitmap.GetPixel(x, y).ToArgb() == track.ToArgb()) { left = Math.Min(left, x); right = Math.Max(right, x); }
            if (left < 1 || right >= bitmap.Width - 1 || right - left < bitmap.Width * .75)
                failures.Add($"{context}: toggle '{toggle.AccessibleName}' track is clipped or does not scale ({left}..{right}, width {bitmap.Width}).");
        }
    }

    private static void CheckLibrary(MainWindow window, string context)
    {
        foreach (var card in Descendants(window).OfType<GlassPanel>().Where(c => c.Visible && c.AccessibleRole == AccessibleRole.ListItem))
        {
            if (card.Parent!.ClientSize.Height < card.Height)
                failures.Add($"{context}: wallpaper card is taller than its viewport ({card.Height} > {card.Parent.ClientSize.Height}).");
            var title = Descendants(card).OfType<Label>().Single(l => l.Text == card.AccessibleName);
            var peer = Descendants(window).OfType<GlassButton>().Single(b => b.Name == "ApplyWallpaper");
            using var graphics = title.CreateGraphics();
            var titleHeight = title.Font.GetHeight(graphics);
            var peerHeight = peer.Font.GetHeight(graphics);
            if (Math.Abs(titleHeight - peerHeight) > 1)
                failures.Add($"{context}: imported wallpaper text has a different scale from its toolbar ({titleHeight:F1} vs {peerHeight:F1}).");
        }
    }

    private static void CheckPreview(MainWindow window, string context)
    {
        var preview = Descendants(window).OfType<DesktopPreview>().Single();
        var title = preview.SceneTitle;
        try
        {
            preview.SceneTitle = "MMMMM";
            using var bitmap = new Bitmap(preview.Width, preview.Height);
            preview.DrawToBitmap(bitmap, preview.ClientRectangle);
            var top = bitmap.Height;
            var bottom = -1;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).ToArgb() == Color.White.ToArgb()) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            if (bottom - top + 1 < window.DeviceDpi * 17 / 72f * .6f)
                failures.Add($"{context}: preview title does not scale or is clipped ({bottom - top + 1}px at {window.DeviceDpi} DPI).");
        }
        finally { preview.SceneTitle = title; }
    }

    private static void CheckColumns(Control root, string context)
    {
        foreach (var table in Descendants(root).OfType<TableLayoutPanel>().Where(t => t.Visible))
            if (table.GetColumnWidths().Sum() > table.ClientSize.Width + 1)
                failures.Add($"{context}: table columns overflow their viewport ({table.GetColumnWidths().Sum()} > {table.ClientSize.Width}).");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void CheckButtons(Control root, string context)
    {
        foreach (var button in Descendants(root).OfType<GlassButton>().Where(button => button.Visible))
        {
            using var graphics = button.CreateGraphics();
            var required = TextRenderer.MeasureText(graphics, button.Text, button.Font, new Size(button.WrapText ? button.Width : int.MaxValue, int.MaxValue), button.WrapText ? TextFormatFlags.WordBreak : TextFormatFlags.Default);
            if (required.Width > button.Width || required.Height > button.Height)
                failures.Add($"{context}: button '{button.Text.Replace('\n', ' ')}' needs {required}, has {button.Size}.");
        }
    }

    private static void CheckCulture(Control root, string culture, string context)
    {
        if (culture != "en-US") return;
        foreach (var control in Descendants(root).Where(control => control.Visible && control.Text != "한국어"))
            if (control.Text.Any(character => character is >= '\uac00' and <= '\ud7a3'))
                failures.Add($"{context}: untranslated Korean text '{control.Text}'.");
    }

    private static string? Option(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : args.FirstOrDefault(arg => arg.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];
    }

    private static string WorkspaceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TransparentTaskbar.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run this harness from a Glasscape source checkout.");
    }

    private static int CaptureStoreScreenshots(string[] args)
    {
        // Rendering this branch must never call Show/ShowDialog/Activate or read the user's settings.
        // Native handles created by DrawToBitmap remain hidden; all fixture paths are project assets.
        var requestedCulture = Option(args, "--culture");
        var cultures = requestedCulture is null ? new[] { "en-US", "ko-KR" } : new[] { requestedCulture };
        if (cultures.Any(culture => culture is not ("en-US" or "ko-KR"))) throw new ArgumentException("--culture must be en-US or ko-KR.");
        var requestedPage = Option(args, "--page");
        var selectedPages = requestedPage is null ? pages : pages.Where(page => page.Name == requestedPage).ToArray();
        if (selectedPages.Length == 0) throw new ArgumentException("--page must be overview, taskbar, wallpaper or settings.");
        var scene = Option(args, "--scene") ?? "aurora-flow";
        if (scene is not ("aurora-flow" or "ocean-drift")) throw new ArgumentException("--scene must be aurora-flow or ocean-drift.");
        var width = int.TryParse(Option(args, "--width"), out var parsedWidth) ? parsedWidth : 1440;
        var height = int.TryParse(Option(args, "--height"), out var parsedHeight) ? parsedHeight : 960;
        if (width < 1366 || height < 768) throw new ArgumentException("Store captures need a client area of at least 1366 × 768 pixels.");
        var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(WorkspaceRoot(), "artifacts/store/screenshots"));
        var captures = new List<object>();
        foreach (var culture in cultures)
        {
            var settings = new AppSettings { Language = culture };
            BundledWallpapers.Seed(settings, Path.Combine(WorkspaceRoot(), "assets"));
            var expected = new[] { "aurora-flow.mp4", "ocean-drift.mp4" };
            if (!settings.Wallpapers.Select(entry => Path.GetFileName(entry.FilePath)).Order().SequenceEqual(expected))
                throw new InvalidDataException("The Store fixture must contain exactly the two original bundled videos.");
            MonitorInfo[] monitors = [new("capture-display-1", "DISPLAY1", new Rectangle(0, 0, 1920, 1080), true)];
            using var window = new MainWindow(settings, monitors)
            {
                StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                Location = new Point(-32000, -32000), ClientSize = new Size(width, height)
            };
            _ = window.Handle;
            window.ClientSize = new Size(width, height);
            // Match the production PackageIdentity.IsPackaged branch: the Store
            // application omits the portable installer. No playback is simulated.
            var installer = Descendants(window).SingleOrDefault(control => control.Name == "Install");
            if (installer is not null) installer.Visible = false;
            window.PerformLayout();
            var selected = settings.Wallpapers.Single(entry => Path.GetFileNameWithoutExtension(entry.FilePath) == scene);
            var card = Descendants(window).Single(control => control.AccessibleRole == AccessibleRole.ListItem && control.AccessibleName == AppText.WallpaperTitle(selected));
            ClickHidden(card);
            var cultureDirectory = Path.Combine(output, culture);
            Directory.CreateDirectory(cultureDirectory);
            var pageHashes = new HashSet<string>();
            foreach (var page in selectedPages)
            {
                ClickHidden(Descendants(window).OfType<Button>().Single(button => button.Name == page.Control));
                window.PerformLayout();
                if (window.Visible) throw new InvalidOperationException("A Store capture window must stay hidden.");
                using var bitmap = HiddenControlRenderer.Render(window);
                if (window.Visible) throw new InvalidOperationException("DrawToBitmap unexpectedly made a capture window visible.");
                var ownVisibility = ValidatePageVisibility(window, page.Name);
                var content = ValidateCaptureContent(bitmap);
                var file = Path.Combine(cultureDirectory, page.Name + ".png");
                bitmap.Save(file, ImageFormat.Png);
                var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                if (!pageHashes.Add(sha256)) throw new InvalidDataException($"The {culture}/{page.Name} capture duplicates a different page.");
                captures.Add(new { culture, page = page.Name, scene, file = Path.GetRelativePath(output, file), width = bitmap.Width, height = bitmap.Height, dpi = window.DeviceDpi, visible = window.Visible, ownVisibility, colors = content.Colors, lightTextPixels = content.TextPixels, sha256 });
                Console.WriteLine($"Captured hidden {culture}/{page.Name}: {bitmap.Width} × {bitmap.Height} ({window.DeviceDpi} DPI).");
            }
        }
        File.WriteAllText(Path.Combine(output, "capture.json"), JsonSerializer.Serialize(new { mode = "hidden-control-DrawToBitmap-composition", variant = "packaged-variant-hides-installer", fixture = "two bundled originals; no user settings or profile data; no simulated playback", captures }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void ClickHidden(Control control) => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [EventArgs.Empty]);

    private static (int Colors, int TextPixels) ValidateCaptureContent(Bitmap bitmap)
    {
        var colors = new HashSet<int>();
        var textPixels = 0;
        // Exclude the title bar and window frame: a decorated blank form must fail.
        for (var y = 64; y < bitmap.Height - 24; y += 2)
        for (var x = 24; x < bitmap.Width - 24; x += 2)
        {
            var pixel = bitmap.GetPixel(x, y);
            colors.Add(pixel.ToArgb());
            if (pixel.R > 210 && pixel.G > 215 && pixel.B > 220) textPixels++;
        }
        if (colors.Count < 256 || textPixels < 200)
            throw new InvalidDataException($"Capture is missing application content: {colors.Count} colors, {textPixels} light text pixels in the client area.");
        return (colors.Count, textPixels);
    }

    private static object ValidatePageVisibility(MainWindow window, string page)
    {
        bool GroupVisible(string captionKey) => HiddenControlRenderer.IsOwnVisible(Descendants(window).OfType<Label>().Single(label => label.Text == AppText.Get(captionKey)).Parent!);
        var taskbar = GroupVisible("TaskbarSection");
        var wallpaper = GroupVisible("WallpaperSection");
        var preferences = GroupVisible("SettingsSection");
        var title = Descendants(window).OfType<Label>().Single(label => label.Text == AppText.Get("MyWallpapers"));
        var library = HiddenControlRenderer.IsOwnVisible(title.Parent!.Parent!);
        if (taskbar != (page is "overview" or "taskbar") || wallpaper != (page is "overview" or "wallpaper") || preferences != (page == "settings") || library != (page is "overview" or "wallpaper"))
            throw new InvalidDataException($"Hidden capture visibility does not match the selected {page} page.");
        return new { taskbar, wallpaper, preferences, library };
    }
}
