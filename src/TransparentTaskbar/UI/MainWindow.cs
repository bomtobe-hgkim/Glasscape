using TransparentTaskbar.Settings;
using TransparentTaskbar.Wallpaper;
using System.Runtime.InteropServices;
using TransparentTaskbar.Localization;
using TransparentTaskbar.Packaging;

namespace TransparentTaskbar.UI;

public sealed class MainWindow : Form
{
    public event Action<AppSettings>? SettingsChanged;
    public event Action<string>? VideoImportRequested;
    public event Action<Guid>? WallpaperApplyRequested;
    public event Action<Guid>? WallpaperRemoveRequested;
    public event Action? PauseRequested;
    public event Action? StopWallpaperRequested;
    public event Action? InstallRequested;
    public event Action? WindowsSettingsRequested;
    private AppSettings settings;
    private IReadOnlyList<MonitorInfo> monitors;
    private bool updating;
    private int currentPage;
    private WallpaperStatus lastWallpaperStatus = new(false, false, null, null, null, 0);
    private Guid? selectedWallpaper;
    private Guid? displayedActiveWallpaper;
    private string libraryKey = "", previewKey = "";
    private readonly DesktopPreview preview = new();
    private readonly Label heading = TextLabel("", 24, true), description = TextLabel("", 10), monitorLabel = TextLabel("", 9), previewNote = TextLabel("", 8.5f), playback = TextLabel("", 9);
    private readonly Label inlineMessage = TextLabel("", 9), taskbarState = TextLabel("", 9);
    private readonly GlassToggle taskbar = new("TaskbarEnabled"), autoHide = new("TaskbarAutoHide"), startup = new("StartWithWindows"), fullscreen = new("PauseWhenFullscreen"), battery = new("PauseOnBattery"), muted = new("MuteWallpaper");
    private readonly TrackBar opacity = Slider("TaskbarOpacity"), volume = Slider("WallpaperVolume");
    private readonly Label opacityValue = TextLabel("0%", 10), volumeValue = TextLabel("0%", 10);
    private readonly ComboBox fit = Combo("WallpaperFit", []), mode = Combo("MonitorMode", []), monitorChoice = Combo("SelectedMonitor", []), language = Combo("Language", []);
    private readonly FlowLayoutPanel taskbarGroup = Stack(), wallpaperGroup = Stack(), settingsGroup = Stack();
    private readonly GlassPanel library = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new(0, 4, 0, 4) };
    private readonly GlassButton apply = new("", true), remove = new(""), pause = new(""), stop = new("");
    private readonly List<GlassButton> navigation = [];
    private readonly List<GlassButton> monitorButtons = [];
    private readonly Dictionary<Guid, GlassPanel> cardPanels = [];
    private readonly TableLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5 };
    private readonly List<Image> thumbnails = [];
    private Image? brandImage;

    public MainWindow(AppSettings settings, IReadOnlyList<MonitorInfo> monitors)
    {
        SuspendLayout();
        this.settings = settings; this.monitors = monitors; updating = true;
        AppText.SetLanguage(settings.Language);
        Bind(this, "WindowTitle"); Bind(apply, "ApplyVideo"); Bind(remove, "RemoveVideo"); Bind(stop, "StopWallpaper");
        apply.Name = "ApplyWallpaper"; remove.Name = "RemoveWallpaper"; pause.Name = "PauseWallpaper"; stop.Name = "StopWallpaper";
        ClientSize = new(1240, 960); MinimumSize = new(1000, 700);
        StartPosition = FormStartPosition.CenterScreen; BackColor = GlassTheme.Canvas; ForeColor = GlassTheme.Text; Font = GlassTheme.Font();
        // All layout dimensions below are authored at 100% scaling, regardless of the startup monitor.
        AutoScaleDimensions = new(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        using (var stream = GetType().Assembly.GetManifestResourceStream("TransparentTaskbar.Assets.AppIcon.ico"))
            Icon = stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
        var sidebar = BuildSidebar();
        var body = new Panel { Dock = DockStyle.Fill, Padding = new(28, 24, 28, 20), AutoScroll = true };
        sidebar.TabIndex = 0; body.TabIndex = 1; Controls.Add(body); Controls.Add(sidebar); body.Controls.Add(content);
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 116, 445, 265, 48, 32 }) content.RowStyles.Add(new(SizeType.Absolute, height));
        var header = new Panel { Dock = DockStyle.Fill };
        heading.Bounds = new(0, 20, 850, 49); heading.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        description.Bounds = new(1, 73, 850, 30); description.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(heading); header.Controls.Add(description); content.Controls.Add(header, 0, 0);
        var studio = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0, 0, 0, 22) };
        studio.ColumnStyles.Add(new(SizeType.Percent, 100)); studio.ColumnStyles.Add(new(SizeType.Absolute, 330));
        studio.Controls.Add(BuildPreview(), 0, 0); studio.Controls.Add(BuildControls(), 1, 0); content.Controls.Add(studio, 0, 1);
        BuildLibrary(); content.Controls.Add(library, 0, 2);
        inlineMessage.Dock = DockStyle.Fill; inlineMessage.Padding = new(0, 12, 0, 0); inlineMessage.AccessibleRole = AccessibleRole.StatusBar; content.Controls.Add(inlineMessage, 0, 3);
        inlineMessage.AutoEllipsis = playback.AutoEllipsis = true;
        var footer = L("Footer", 8); footer.Dock = DockStyle.Fill; content.Controls.Add(footer, 0, 4);
        AllowDrop = true; DragEnter += OnDragEnter; DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) ImportFiles(paths); };
        ConnectControls(); ApplyLocalization(); UpdateMonitors(monitors); Navigate(0); UpdateState(settings, lastWallpaperStatus, AppText.Get("TaskbarWaiting"), settings.StartWithWindows);
        updating = false;
        ResumeLayout(true);
    }

    private Panel BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Left, Width = 190, Padding = new(18, 30, 18, 20), BackColor = Color.FromArgb(16, 26, 39) };
        var brand = new Panel { Dock = DockStyle.Top, Height = 82 };
        brandImage = GlassTheme.ImageResource("TransparentTaskbar.Assets.AppIcon.png");
        brand.Controls.Add(new PictureBox { Image = brandImage, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new(0, 0, 42, 42), TabStop = false });
        brand.Controls.Add(new Label { Text = "Glasscape", Font = GlassTheme.Font(15.5f, FontStyle.Bold), ForeColor = GlassTheme.Text, Bounds = new(44, 0, 110, 30) });
        brand.Controls.Add(new Label { Text = "DESKTOP STUDIO", Font = GlassTheme.Font(7), ForeColor = GlassTheme.Muted, Bounds = new(46, 32, 122, 20) });
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 270, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        nav.Controls.Add(Bind(new Label { ForeColor = GlassTheme.Muted, Height = 35, Width = 154, Padding = new(8, 8, 0, 0) }, "SidebarCaption"));
        var labels = new[] { "NavOverview", "NavTaskbar", "NavWallpaper", "NavSettings" };
        for (var i = 0; i < labels.Length; i++)
        {
            var page = i; var button = B(labels[i] + "Button"); button.Name = labels[i]; button.Width = 154; button.Height = 45; button.Margin = new(0, 0, 0, 7); Bind(button, labels[i] + "Button", labels[i]);
            button.Click += (_, _) => Navigate(page); nav.Controls.Add(button); navigation.Add(button);
        }
        var bottom = L("SidebarFooter", 8.5f); bottom.Dock = DockStyle.Bottom; bottom.Height = 56;
        panel.Controls.Add(nav); panel.Controls.Add(brand); panel.Controls.Add(bottom); return panel;
    }

    private Control BuildPreview()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new(0, 0, 22, 0) };
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 35, 278, 29, 46, 28 }) panel.RowStyles.Add(new(SizeType.Absolute, height));
        var title = L("DesktopPreview", 11, true); title.Dock = DockStyle.Fill; panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(preview, 0, 1); previewNote.Dock = DockStyle.Fill; panel.Controls.Add(previewNote, 0, 2);
        var selectors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        foreach (var item in new[] { ("PrimaryMonitor", MonitorMode.Primary), ("ChooseMonitor", MonitorMode.Selected), ("AllMonitors", MonitorMode.All) })
        {
            selectors.ColumnStyles.Add(new(SizeType.Percent, 33.33f)); var button = B(item.Item1); button.WrapText = true; button.Dock = DockStyle.Fill; button.Margin = new(0, 0, 8, 0);
            button.Click += (_, _) => mode.SelectedIndex = (int)item.Item2; selectors.Controls.Add(button); monitorButtons.Add(button);
        }
        panel.Controls.Add(selectors, 0, 3); monitorLabel.Dock = DockStyle.Fill; panel.Controls.Add(monitorLabel, 0, 4); return panel;
    }

    private Control BuildControls()
    {
        var card = new GlassPanel { Dock = DockStyle.Fill, Margin = new(0), Padding = new(18, 18, 18, 12) };
        var groups = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        card.Controls.Add(groups); groups.Controls.Add(taskbarGroup); groups.Controls.Add(wallpaperGroup); groups.Controls.Add(settingsGroup);
        Section(taskbarGroup, "TaskbarSection", "TaskbarSectionTitle"); ToggleRow(taskbarGroup, "TaskbarEnabled", "TaskbarEnabledHelp", taskbar);
        RangeRow(taskbarGroup, "TaskbarOpacity", opacity, opacityValue); taskbarGroup.Controls.Add(Note("TaskbarOpacityHelp"));
        ToggleRow(taskbarGroup, "TaskbarAutoHide", "TaskbarAutoHideHelp", autoHide);
        var windows = B("WindowsTaskbarSettings"); windows.Width = 252; Bind(windows, "WindowsTaskbarSettings", "WindowsTaskbarSettingsAccessible"); windows.Click += (_, _) => WindowsSettingsRequested?.Invoke(); taskbarGroup.Controls.Add(windows);
        taskbarGroup.Controls.Add(Note("AutoHideHelp")); taskbarGroup.Controls.Add(taskbarState);
        Section(wallpaperGroup, "WallpaperSection", "WallpaperSectionTitle"); Field(wallpaperGroup, "WallpaperFit", fit); Field(wallpaperGroup, "MonitorMode", mode); Field(wallpaperGroup, "Monitor", monitorChoice);
        ToggleRow(wallpaperGroup, "Muted", "MutedHelp", muted); RangeRow(wallpaperGroup, "Volume", volume, volumeValue);
        Section(settingsGroup, "SettingsSection", "SettingsSectionTitle"); Field(settingsGroup, "Language", language);
        settingsGroup.Controls.Add(Note("LanguageHelp")); ToggleRow(settingsGroup, "Startup", "StartupHelp", startup);
        ToggleRow(settingsGroup, "Fullscreen", "FullscreenHelp", fullscreen); ToggleRow(settingsGroup, "Battery", "BatteryHelp", battery);
        settingsGroup.Controls.Add(Note("TrayHelp"));
        if (!PackageIdentity.IsPackaged)
        {
            var install = B("Install"); install.Name = "Install"; install.Width = 252; install.Click += (_, _) => InstallRequested?.Invoke(); settingsGroup.Controls.Add(install);
        }
        return card;
    }

    private void BuildLibrary()
    {
        var title = L("MyWallpapers", 16, true); title.Dock = DockStyle.Left; title.Width = 200;
        var import = B("ImportVideo", true); import.Dock = DockStyle.Right; import.Width = 175; import.Click += (_, _) => PickVideo();
        var top = new Panel { Dock = DockStyle.Top, Height = 48 }; top.Controls.Add(title); top.Controls.Add(import);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, WrapContents = false };
        apply.Width = 145; remove.Width = 158; pause.Width = 93; stop.Width = 124;
        foreach (var button in new[] { apply, remove, pause, stop }) { button.Margin = new(0, 5, 10, 0); toolbar.Controls.Add(button); }
        apply.Click += (_, _) => { if (selectedWallpaper is { } id) WallpaperApplyRequested?.Invoke(id); };
        remove.Click += (_, _) => { if (selectedWallpaper is { } id) WallpaperRemoveRequested?.Invoke(id); };
        pause.Click += (_, _) => PauseRequested?.Invoke(); stop.Click += (_, _) => StopWallpaperRequested?.Invoke();
        playback.Dock = DockStyle.Bottom; playback.Height = 24;
        library.Controls.Add(cards); library.Controls.Add(playback); library.Controls.Add(toolbar); library.Controls.Add(top);
    }

    private void ConnectControls()
    {
        taskbar.CheckedChanged += (_, _) => Change(s => s.TaskbarEnabled = taskbar.Checked);
        autoHide.CheckedChanged += (_, _) => Change(s => s.TaskbarAutoHide = autoHide.Checked);
        startup.CheckedChanged += (_, _) => Change(s => s.StartWithWindows = startup.Checked);
        fullscreen.CheckedChanged += (_, _) => Change(s => s.PauseWhenFullscreen = fullscreen.Checked);
        battery.CheckedChanged += (_, _) => Change(s => s.PauseOnBattery = battery.Checked);
        muted.CheckedChanged += (_, _) => Change(s => s.MuteWallpaper = muted.Checked);
        opacity.ValueChanged += (_, _) => Change(s => s.TaskbarOpacity = opacity.Value);
        volume.ValueChanged += (_, _) => Change(s => s.WallpaperVolume = volume.Value);
        fit.SelectedIndexChanged += (_, _) => Change(s => s.WallpaperFit = (WallpaperFit)Math.Max(0, fit.SelectedIndex));
        mode.SelectedIndexChanged += (_, _) => Change(s =>
        {
            s.MonitorMode = (MonitorMode)Math.Max(0, mode.SelectedIndex);
            if (s.MonitorMode == MonitorMode.Selected && monitorChoice.SelectedItem is MonitorChoice choice) s.SelectedMonitorId = choice.Monitor.Id;
        });
        monitorChoice.SelectedIndexChanged += (_, _) => { if (monitorChoice.SelectedItem is MonitorChoice choice) Change(s => s.SelectedMonitorId = choice.Monitor.Id); };
        language.SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            settings.Language = language.SelectedIndex switch { 1 => "en-US", 2 => "ko-KR", _ => null };
            AppText.SetLanguage(settings.Language);
            inlineMessage.Text = "";
            ApplyLocalization();
            SettingsChanged?.Invoke(settings);
        };
    }

    private void Change(Action<AppSettings> change)
    {
        if (updating) return; change(settings); RefreshDependentControls(); SettingsChanged?.Invoke(settings);
    }

    public void UpdateState(AppSettings settings, WallpaperStatus wallpaperStatus, string taskbarStatus, bool startupEnabled)
    {
        if (IsDisposed) return; updating = true;
        try
        {
            this.settings = settings;
            lastWallpaperStatus = wallpaperStatus;
            if (settings.ActiveWallpaperId != displayedActiveWallpaper) { displayedActiveWallpaper = settings.ActiveWallpaperId; if (displayedActiveWallpaper is { } active) selectedWallpaper = active; }
            taskbar.Checked = settings.TaskbarEnabled; autoHide.Checked = settings.TaskbarAutoHide ?? false; startup.Checked = startupEnabled; fullscreen.Checked = settings.PauseWhenFullscreen; battery.Checked = settings.PauseOnBattery; muted.Checked = settings.MuteWallpaper;
            opacity.Value = Math.Clamp(settings.TaskbarOpacity, 0, 100); volume.Value = Math.Clamp(settings.WallpaperVolume, 0, 100);
            fit.SelectedIndex = Math.Clamp((int)settings.WallpaperFit, 0, 1); mode.SelectedIndex = Math.Clamp((int)settings.MonitorMode, 0, 2);
            SelectMonitor(); taskbarState.Text = taskbarStatus;
            UpdatePlaybackText(wallpaperStatus);
            playback.ForeColor = wallpaperStatus.Error is null ? GlassTheme.Muted : Color.LightSalmon; preview.Paused = wallpaperStatus.IsPaused;
            RefreshLibrary(); UpdatePreview(); RefreshDependentControls();
        }
        finally { updating = false; }
    }

    public void UpdateMonitors(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitorChoice.Items.Count == monitors.Count && this.monitors.SequenceEqual(monitors)) return;
        var previous = updating; updating = true;
        try { this.monitors = monitors; monitorChoice.Items.Clear(); foreach (var item in monitors) monitorChoice.Items.Add(new MonitorChoice(item)); SelectMonitor(); RefreshDependentControls(); }
        finally { updating = previous; }
    }

    private void SelectMonitor()
    {
        var choices = monitorChoice.Items.Cast<MonitorChoice>().ToArray(); var index = Array.FindIndex(choices, c => c.Monitor.Id == settings.SelectedMonitorId);
        if (index < 0 && settings.SelectedMonitorId is null) index = Array.FindIndex(choices, c => c.Monitor.Primary);
        monitorChoice.SelectedIndex = index >= 0 ? index : settings.SelectedMonitorId is null && choices.Length > 0 ? 0 : -1;
    }

    private void RefreshDependentControls()
    {
        opacity.Enabled = taskbar.Checked; volume.Enabled = !muted.Checked; monitorChoice.Enabled = settings.MonitorMode == MonitorMode.Selected;
        opacityValue.Text = $"{opacity.Value}%"; volumeValue.Text = $"{volume.Value}%"; preview.Opacity = opacity.Value; preview.TaskbarEnabled = taskbar.Checked; preview.Invalidate();
        for (var i = 0; i < monitorButtons.Count; i++) { monitorButtons[i].Selected = i == (int)settings.MonitorMode; monitorButtons[i].Enabled = monitors.Count > 0; monitorButtons[i].Invalidate(); }
        var target = settings.MonitorMode == MonitorMode.Selected ? monitors.FirstOrDefault(m => m.Id == settings.SelectedMonitorId) : monitors.FirstOrDefault(m => m.Primary);
        monitorLabel.Text = settings.MonitorMode == MonitorMode.All ? AppText.Format("AllMonitorsDetail", monitors.Count) : target is null ? AppText.Get("NoMonitors") : $"{target.Name} · {target.Bounds.Width} × {target.Bounds.Height}";
        var selected = settings.Wallpapers.FirstOrDefault(w => w.Id == selectedWallpaper); apply.Enabled = selected is not null && File.Exists(selected.FilePath); remove.Enabled = selected is not null;
        foreach (var (id, panel) in cardPanels) { panel.BorderColor = id == selectedWallpaper ? GlassTheme.Teal : GlassTheme.Line; panel.Invalidate(); }
    }

    private void RefreshLibrary()
    {
        var key = AppText.Culture.Name + string.Join("|", settings.Wallpapers.Select(w => $"{w.Id}:{w.Title}:{w.FilePath}:{w.ThumbnailPath}:{File.Exists(w.FilePath)}"));
        if (key == libraryKey && cards.Controls.Count > 0) return; libraryKey = key; cards.SuspendLayout();
        try
        {
            foreach (Control control in cards.Controls.Cast<Control>().ToArray()) control.Dispose(); cards.Controls.Clear();
            foreach (var image in thumbnails) image.Dispose(); thumbnails.Clear(); cardPanels.Clear();
            if (!settings.Wallpapers.Any(w => w.Id == selectedWallpaper)) selectedWallpaper = settings.ActiveWallpaperId ?? settings.Wallpapers.FirstOrDefault()?.Id;
            if (settings.Wallpapers.Count == 0)
            {
                var scale = AutoScaleDimensions.Width / 96f;
                var empty = B("EmptyLibrary"); empty.Height = 92; Bind(empty, "EmptyLibrary", "ImportVideoAccessible");
                ScaleLibraryControl(empty);
                empty.Width = Math.Max((int)(400 * scale), cards.ClientSize.Width - (int)(28 * scale));
                empty.Click += (_, _) => PickVideo(); cards.Controls.Add(empty);
            }
            foreach (var entry in settings.Wallpapers)
            {
                var card = new GlassPanel { Width = 207, Height = 99, Padding = new(9), Margin = new(0, 0, 12, 8), TabStop = true, AccessibleName = AppText.WallpaperTitle(entry), AccessibleRole = AccessibleRole.ListItem };
                var picture = new PictureBox { Dock = DockStyle.Left, Width = 82, SizeMode = PictureBoxSizeMode.Zoom, BackColor = GlassTheme.Raised, TabStop = false };
                var image = LoadThumbnail(entry.ThumbnailPath); if (image is not null) { thumbnails.Add(image); picture.Image = image; }
                var text = new Panel { Dock = DockStyle.Fill, Padding = new(9, 8, 0, 0) }; var title = TextLabel(AppText.WallpaperTitle(entry), 9.5f, true); title.Dock = DockStyle.Top; title.Height = 42; title.AutoEllipsis = true;
                var exists = File.Exists(entry.FilePath); var detail = TextLabel(exists ? AppText.Format("LocalVideo", Path.GetExtension(entry.FilePath).TrimStart('.').ToUpperInvariant()) : AppText.Get("FileMissing"), 8); detail.Dock = DockStyle.Bottom; detail.Height = 32; detail.AutoEllipsis = true; detail.ForeColor = exists ? GlassTheme.Muted : Color.LightSalmon;
                text.Controls.Add(title); text.Controls.Add(detail); card.Controls.Add(text); card.Controls.Add(picture);
                void Select() { selectedWallpaper = entry.Id; UpdatePreview(); RefreshDependentControls(); }
                foreach (var control in new Control[] { card, picture, text, title, detail }) { control.Cursor = Cursors.Hand; control.Click += (_, _) => Select(); }
                card.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { Select(); e.Handled = true; } };
                ScaleLibraryControl(card); cards.Controls.Add(card); cardPanels[entry.Id] = card;
            }
            UpdatePreview();
        }
        finally { cards.ResumeLayout(); }
    }

    private void ScaleLibraryControl(Control control)
    {
        // Newly imported cards have not participated in the form's earlier DPI scaling pass.
        var scale = AutoScaleDimensions.Width / 96f;
        if (scale != 1) control.Scale(new SizeF(scale, scale));
    }

    private void UpdatePreview()
    {
        var entry = settings.Wallpapers.FirstOrDefault(w => w.Id == selectedWallpaper); var key = entry?.Id + ":" + entry?.ThumbnailPath;
        if (key != previewKey) { previewKey = key; preview.SetScene(LoadThumbnail(entry?.ThumbnailPath)); }
        preview.SceneTitle = entry is null ? AppText.Get("EmptyScene") : AppText.WallpaperTitle(entry);
        previewNote.Text = AppText.Get(entry?.ThumbnailPath is { } path && File.Exists(path) ? "PreviewThumbnailNote" : "PreviewSampleNote"); preview.Invalidate();
    }

    private void Navigate(int page)
    {
        currentPage = page;
        string[] titles = ["OverviewTitle", "TaskbarTitle", "WallpaperTitle", "SettingsTitle"];
        string[] descriptions = ["OverviewDescription", "TaskbarDescription", "WallpaperDescription", "SettingsDescription"];
        heading.Text = AppText.Get(titles[page]); description.Text = AppText.Get(descriptions[page]);
        for (var i = 0; i < navigation.Count; i++) { navigation[i].Selected = i == page; navigation[i].Invalidate(); }
        taskbarGroup.Visible = page is 0 or 1; wallpaperGroup.Visible = page is 0 or 2; settingsGroup.Visible = page == 3;
        var showLibrary = page is 0 or 2; library.Visible = showLibrary; content.RowStyles[2].Height = showLibrary ? 265 * AutoScaleDimensions.Height / 96f : 0;
    }

    private void PickVideo()
    {
        using var picker = new OpenFileDialog { Title = AppText.Get("VideoPickerTitle"), Filter = AppText.Get("VideoFilter"), Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(this) == DialogResult.OK) ImportFiles(picker.FileNames);
    }
    private void ImportFiles(IEnumerable<string> paths)
    {
        string[] formats = [".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv"];
        foreach (var path in paths)
        {
            if (!formats.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || !File.Exists(path)) { ShowMessage(AppText.Get("VideoFormatError"), true); continue; }
            VideoImportRequested?.Invoke(Path.GetFullPath(path));
        }
    }
    private void OnDragEnter(object? sender, DragEventArgs e) { e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None; }
    public void ShowMessage(string message, bool error = false) { inlineMessage.Text = message; inlineMessage.ForeColor = error ? Color.LightSalmon : GlassTheme.Teal; }
    public void SetStartupPending(bool pending) { startup.Enabled = !pending; }
    private static Image? LoadThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var image = Image.FromStream(stream); return new Bitmap(image); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException) { return null; }
    }
    private static Label TextLabel(string text, float size = 9.5f, bool bold = false) => new() { Text = text, Font = GlassTheme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = bold ? GlassTheme.Text : GlassTheme.Muted, Width = 260, Height = 25, Margin = new(0, 0, 0, 5) };
    private static FlowLayoutPanel Stack() => new() { Width = 260, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(0, 0, 0, 14) };
    private static TrackBar Slider(string name) => new() { Name = name, Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false, Height = 29, Width = 252, BackColor = GlassTheme.Surface, SmallChange = 1, LargeChange = 10 };
    private static ComboBox Combo(string name, string[] options) { var combo = new GlassCombo { Name = name, Width = 252, AccessibleName = name }; combo.Items.AddRange(options); return combo; }
    private static Label Note(string key) { var label = L(key, 8.5f); label.AutoSize = true; label.MaximumSize = new(252, 0); return label; }
    private static void Section(FlowLayoutPanel parent, string caption, string title) { parent.Controls.Add(L(caption, 8.5f)); var label = L(title, 16, true); label.Height = 34; parent.Controls.Add(label); }
    private static void ToggleRow(FlowLayoutPanel parent, string title, string help, GlassToggle toggle)
    {
        var row = new Panel { Width = 260, Height = 66 }; toggle.Location = new(210, 5); toggle.Tag = new TextBinding(null, title); toggle.AccessibleName = AppText.Get(title);
        var label = L(title, 9.5f, true); label.Bounds = new(0, 4, 202, 24); var note = L(help, 8.2f); note.Bounds = new(0, 31, 252, 30);
        label.Click += (_, _) => toggle.Checked = !toggle.Checked; row.Controls.Add(label); row.Controls.Add(note); row.Controls.Add(toggle); parent.Controls.Add(row);
    }
    private static void RangeRow(FlowLayoutPanel parent, string title, TrackBar slider, Label value) { var row = new Panel { Width = 260, Height = 28 }; var label = L(title, 9.5f, true); label.Width = 192; value.TextAlign = ContentAlignment.MiddleRight; value.Bounds = new(200, 0, 52, 24); row.Controls.Add(label); row.Controls.Add(value); slider.Tag = new TextBinding(null, title == "TaskbarOpacity" ? "TaskbarOpacityAccessible" : title); parent.Controls.Add(row); parent.Controls.Add(slider); }
    private static void Field(FlowLayoutPanel parent, string title, ComboBox combo) { parent.Controls.Add(L(title, 9, true)); combo.Tag = new TextBinding(null, title); combo.AccessibleName = AppText.Get(title); parent.Controls.Add(combo); }

    private void ApplyLocalization()
    {
        var previous = updating; updating = true;
        try
        {
            foreach (var control in Descendants(this).Prepend(this))
                if (control.Tag is TextBinding binding)
                {
                    if (binding.Key is { } key) control.Text = AppText.Get(key);
                    if (binding.AccessibleKey is { } accessibleKey) control.AccessibleName = AppText.Get(accessibleKey);
                }
            SetOptions(fit, ["Fill", "Fit"], (int)settings.WallpaperFit);
            SetOptions(mode, ["PrimaryMonitor", "SelectedMonitor", "AllMonitors"], (int)settings.MonitorMode);
            SetOptions(language, ["LanguageSystem", "LanguageEnglish", "LanguageKorean"], settings.Language switch { "en-US" => 1, "ko-KR" => 2, _ => 0 });
            preview.AccessibleName = AppText.Get("PreviewAccessible");
            RefreshLibrary(); UpdatePreview(); RefreshDependentControls(); UpdatePlaybackText(lastWallpaperStatus); Navigate(currentPage);
        }
        finally { updating = previous; }
    }

    private void UpdatePlaybackText(WallpaperStatus status)
    {
        pause.Enabled = stop.Enabled = status.IsRunning; pause.Text = AppText.Get(status.IsPaused ? "Play" : "Pause"); pause.AccessibleName = pause.Text;
        var title = AppText.WallpaperTitle(settings.ActiveWallpaperId, status.ActiveTitle);
        playback.Text = status.Error ?? (status.IsRunning
            ? status.IsPaused ? AppText.Format("PlaybackPaused", title, status.PauseReason ?? AppText.Get("Pause")) : AppText.Format("PlaybackRunning", title, status.MonitorCount)
            : AppText.Get("PlaybackIdle"));
    }

    private static void SetOptions(ComboBox combo, string[] keys, int selected)
    {
        combo.Items.Clear(); combo.Items.AddRange(keys.Select(AppText.Get).ToArray()); combo.SelectedIndex = Math.Clamp(selected, 0, keys.Length - 1);
    }
    private static Label L(string key, float size = 9.5f, bool bold = false) => Bind(TextLabel(AppText.Get(key), size, bold), key);
    private static GlassButton B(string key, bool accent = false) => Bind(new GlassButton(AppText.Get(key), accent), key);
    private static T Bind<T>(T control, string key, string? accessibleKey = null) where T : Control
    {
        control.Tag = new TextBinding(key, accessibleKey ?? key); control.Text = AppText.Get(key); control.AccessibleName = AppText.Get(accessibleKey ?? key); return control;
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private sealed record TextBinding(string? Key, string? AccessibleKey);
    protected override void Dispose(bool disposing) { if (disposing) { brandImage?.Dispose(); foreach (var image in thumbnails) image.Dispose(); } base.Dispose(disposing); }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    private sealed record MonitorChoice(MonitorInfo Monitor) { public override string ToString() => $"{Monitor.Name} · {Monitor.Bounds.Width} × {Monitor.Bounds.Height}"; }
}

