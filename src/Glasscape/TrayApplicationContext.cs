using System.Diagnostics;
using Glasscape.Settings;
using Glasscape.UI;
using Glasscape.Wallpaper;
using Glasscape.Localization;
using Glasscape.Packaging;

namespace Glasscape;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore store;
    private AppSettings settings;
    private readonly StartupService startup;
    private WindowsTaskbarBackend? taskbarBackend;
    private readonly TaskbarAutoHideService autoHide = new();
    private readonly DesktopWallpaperService wallpaper;
    private readonly NotifyIcon tray;
    private readonly Icon icon = AppAssets.LoadIcon();
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem pauseMenu;
    private readonly Control dispatcher = new();
    private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 350 };
    private readonly ShellMessageWindow window;
    private TaskbarWorker? taskbarWorker;
    private MainWindow? mainWindow;
    private bool lastStartupIntent;
    private bool startupChangePending;
    private bool? lastAutoHideIntent;
    private int taskbarGeneration;
    private bool userPaused;
    private bool exiting;
    private string taskbarStatusKey = "TaskbarConnecting";
    private string? lastError;

    internal TrayApplicationContext(bool background = false, SettingsStore? settingsStore = null)
    {
        store = settingsStore ?? new SettingsStore();
        dispatcher.CreateControl();
        settings = store.Load();
        AppText.SetLanguage(settings.Language);
        SeedBundledSamples();
        startup = new StartupService(Environment.ProcessPath ?? throw new InvalidOperationException(AppText.Get("ExecutableNotFound")));
        lastStartupIntent = settings.StartWithWindows;
        try
        {
            if (settings.TaskbarAutoHide is { } configured) autoHide.SetEnabled(configured);
            lastAutoHideIntent = settings.TaskbarAutoHide = autoHide.IsEnabled;
        }
        catch (Exception error) { AppLog.Write(error); }
        wallpaper = new DesktopWallpaperService();
        wallpaper.StatusChanged += OnWallpaperStatus;
        menu.Items.Add(AppText.Get("TrayOpen"), null, (_, _) => ShowSettings());
        pauseMenu = new ToolStripMenuItem(AppText.Get("TrayPause"), null, (_, _) => TogglePlayback());
        menu.Items.Add(pauseMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(AppText.Get("TrayExit"), null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = icon, Text = AppText.Get("TrayIdle"), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        window = new ShellMessageWindow(() => taskbarWorker?.RequestRefresh(), ShowSettings);
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSettings(); };
        pollTimer.Tick += (_, _) =>
        {
            if (exiting) return;
            try { wallpaper.Refresh(settings); }
            catch (Exception error) { ReportError(error); }
        };
        ApplyTaskbar();
        wallpaper.Apply(settings);
        pollTimer.Start();
        startupChangePending = true;
        dispatcher.BeginInvoke((Action)(async () => await RefreshStartupAsync()));
        if (!background) ShowSettings();
        if (store.LastLoadWarning is { } warning)
        {
            AppLog.Write(new InvalidOperationException(warning));
            mainWindow?.ShowMessage(AppText.Get("SettingsLoadWarning"), true);
        }
        Application.ApplicationExit += OnApplicationExit;
    }

    private void SeedBundledSamples()
    {
        BundledWallpapers.Seed(settings, AppContext.BaseDirectory);
    }

    private async Task RefreshStartupAsync()
    {
        startupChangePending = true;
        try
        {
            var enabled = await startup.GetIsEnabledAsync();
            if (exiting) return;
            lastStartupIntent = settings.StartWithWindows = enabled;
        }
        catch (Exception error) { AppLog.Write(error); }
        finally { startupChangePending = false; if (!exiting) UpdateWindow(); }
    }

    private async Task ChangeStartupAsync(bool enabled)
    {
        startupChangePending = true;
        mainWindow?.SetStartupPending(true);
        try
        {
            var actual = await startup.SetEnabledAsync(enabled);
            if (exiting) return;
            lastStartupIntent = settings.StartWithWindows = actual;
            if (enabled && !actual) mainWindow?.ShowMessage(AppText.Get("StartupDenied"), true);
        }
        catch (Exception error) { settings.StartWithWindows = lastStartupIntent; if (!exiting) ReportError(error); }
        finally
        {
            startupChangePending = false;
            if (!exiting) { SaveSettings(); UpdateWindow(); }
        }
    }

    private void ShowSettings()
    {
        if (exiting) return;
        if (mainWindow is null || mainWindow.IsDisposed)
        {
            mainWindow = new MainWindow(settings, DesktopWallpaperService.GetMonitors());
            mainWindow.SettingsChanged += OnSettingsChanged;
            mainWindow.VideoImportRequested += ImportVideo;
            mainWindow.WallpaperApplyRequested += ApplyWallpaper;
            mainWindow.WallpaperRemoveRequested += RemoveWallpaper;
            mainWindow.PauseRequested += TogglePlayback;
            mainWindow.StopWallpaperRequested += StopWallpaper;
            mainWindow.WindowsSettingsRequested += () => Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
            mainWindow.InstallRequested += () =>
            {
                if (PackageIdentity.IsPackaged) return;
                if (InstallService.ShowInstaller(mainWindow) is { } installed)
                {
                    settings.StartWithWindows = installed.StartWithWindows;
                    InstallService.LaunchInstalled(installed.ExecutablePath);
                    ExitThread();
                }
            };
            mainWindow.FormClosing += (_, args) =>
            {
                if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; mainWindow.Hide(); }
            };
        }
        UpdateWindow();
        mainWindow.Show();
        if (mainWindow.WindowState == FormWindowState.Minimized) mainWindow.WindowState = FormWindowState.Normal;
        mainWindow.Activate();
        if (!startupChangePending)
        {
            startupChangePending = true;
            mainWindow.SetStartupPending(true);
            dispatcher.BeginInvoke((Action)(async () => await RefreshStartupAsync()));
        }
    }

    private void OnSettingsChanged(AppSettings changed)
    {
        if (exiting) return;
        settings = changed;
        if (!startupChangePending && changed.StartWithWindows != lastStartupIntent) _ = ChangeStartupAsync(changed.StartWithWindows);
        if (changed.TaskbarAutoHide is { } desired && changed.TaskbarAutoHide != lastAutoHideIntent)
        {
            try { autoHide.SetEnabled(desired); lastAutoHideIntent = desired; }
            catch (Exception error) { changed.TaskbarAutoHide = lastAutoHideIntent; ReportError(error); }
        }
        ApplyTaskbar();
        wallpaper.Apply(settings);
        wallpaper.UpdatePlayback(settings);
        saveTimer.Stop();
        saveTimer.Start();
        UpdateWindow();
    }

    private void ApplyTaskbar()
    {
        if (settings.TaskbarEnabled && taskbarWorker is null)
        {
            taskbarBackend = new WindowsTaskbarBackend { Opacity = settings.TaskbarOpacity };
            var generation = ++taskbarGeneration;
            taskbarWorker = new TaskbarWorker(taskbarBackend, (count, error) =>
            {
                try
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        if (exiting || !settings.TaskbarEnabled || generation != taskbarGeneration) return;
                        var key = error is not null ? "TaskbarRetrying" : count == 0 ? "TaskbarWaiting" : "TaskbarApplied";
                        if (taskbarStatusKey != key) { taskbarStatusKey = key; UpdateWindow(); }
                        if (error is not null && lastError != error.Message) { lastError = error.Message; ReportError(error); }
                        if (error is null) lastError = null;
                    });
                }
                catch (InvalidOperationException) { }
            });
        }
        else if (!settings.TaskbarEnabled && taskbarWorker is not null)
        {
            taskbarWorker.Dispose();
            taskbarWorker = null;
            taskbarBackend = null;
            taskbarGeneration++;
            taskbarStatusKey = "TaskbarDefault";
        }
        if (taskbarBackend is not null) taskbarBackend.Opacity = settings.TaskbarOpacity;
        taskbarWorker?.RequestRefresh();
    }

    private void OnWallpaperStatus(WallpaperStatus status)
    {
        if (exiting) return;
        pauseMenu.Text = AppText.Get(userPaused ? "TrayPlay" : "TrayPause");
        pauseMenu.Enabled = settings.ActiveWallpaperId is not null;
        tray.Text = AppText.Get(status.IsRunning ? status.IsPaused ? "TrayPaused" : "TrayPlaying" : "TrayIdle");
        UpdateWindow();
    }

    private void UpdateWindow()
    {
        menu.Items[0].Text = AppText.Get("TrayOpen");
        menu.Items[3].Text = AppText.Get("TrayExit");
        pauseMenu.Text = AppText.Get(userPaused ? "TrayPlay" : "TrayPause");
        tray.Text = AppText.Get(wallpaper.Status.IsRunning ? wallpaper.Status.IsPaused ? "TrayPaused" : "TrayPlaying" : "TrayIdle");
        if (mainWindow is null || mainWindow.IsDisposed) return;
        mainWindow.UpdateMonitors(DesktopWallpaperService.GetMonitors());
        mainWindow.UpdateState(settings, wallpaper.Status, AppText.Get(taskbarStatusKey), lastStartupIntent);
        mainWindow.SetStartupPending(startupChangePending);
    }

    private void ImportVideo(string file)
    {
        try
        {
            var path = Path.GetFullPath(file);
            if (!File.Exists(path)) throw new FileNotFoundException(AppText.Get("VideoNotFound"), path);
            if (!new[] { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".m4v" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(AppText.Get("VideoFormatError"));
            var entry = settings.Wallpapers.FirstOrDefault(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new WallpaperEntry { FilePath = path, Title = Path.GetFileNameWithoutExtension(path) };
                var poster = Path.ChangeExtension(path, ".png");
                if (File.Exists(poster)) entry.ThumbnailPath = poster;
                settings.Wallpapers.Add(entry);
            }
            ApplyWallpaper(entry.Id);
            mainWindow?.ShowMessage(AppText.Get("VideoImported"));
        }
        catch (Exception error) { ReportError(error); }
    }

    private void ApplyWallpaper(Guid id)
    {
        settings.ActiveWallpaperId = id;
        userPaused = false;
        wallpaper.SetUserPaused(false);
        wallpaper.Apply(settings);
        SaveSettings();
        UpdateWindow();
    }

    private void RemoveWallpaper(Guid id)
    {
        if (settings.ActiveWallpaperId == id) StopWallpaper();
        settings.Wallpapers.RemoveAll(item => item.Id == id);
        SaveSettings();
        UpdateWindow();
    }

    private void TogglePlayback()
    {
        userPaused = !userPaused;
        wallpaper.SetUserPaused(userPaused);
        UpdateWindow();
    }

    private void StopWallpaper()
    {
        settings.ActiveWallpaperId = null;
        userPaused = false;
        wallpaper.Stop();
        SaveSettings();
        UpdateWindow();
    }

    private void SaveSettings()
    {
        try { store.Save(settings); }
        catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error)
    {
        AppLog.Write(error);
        mainWindow?.ShowMessage(error.Message, true);
    }

    private void Stop()
    {
        if (exiting) return;
        exiting = true;
        pollTimer.Stop();
        saveTimer.Stop();
        SaveSettings();
        wallpaper.StatusChanged -= OnWallpaperStatus;
        wallpaper.Dispose();
        taskbarWorker?.Dispose();
        mainWindow?.Dispose();
        tray.Visible = false;
        tray.Dispose();
        icon.Dispose();
        menu.Dispose();
        pollTimer.Dispose();
        saveTimer.Dispose();
        dispatcher.Dispose();
        window.DestroyHandle();
        Application.ApplicationExit -= OnApplicationExit;
    }

    private void OnApplicationExit(object? sender, EventArgs args) => Stop();
    protected override void ExitThreadCore() { Stop(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing) { if (disposing) Stop(); base.Dispose(disposing); }

    private sealed class ShellMessageWindow : NativeWindow
    {
        private readonly uint taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        private readonly uint showSettings = NativeMethods.RegisterWindowMessage("Glasscape.ShowSettings");
        private readonly Action refresh;
        private readonly Action show;
        internal ShellMessageWindow(Action refresh, Action show)
        {
            this.refresh = refresh;
            this.show = show;
            CreateHandle(new CreateParams { Caption = "Glasscape.Messages", Style = unchecked((int)0x80000000) });
        }
        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == showSettings) show();
            if ((uint)message.Msg == taskbarCreated || message.Msg is 0x007E or 0x001A) refresh();
            if (message.Msg == 0x0011) { message.Result = 1; return; }
            base.WndProc(ref message);
        }
    }
}
