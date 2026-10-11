using System.Diagnostics;
using System.Reflection;
using TransparentTaskbar.Settings;
using TransparentTaskbar.Localization;
using TransparentTaskbar.Packaging;

namespace TransparentTaskbar;

internal readonly record struct InstallationResult(string ExecutablePath, bool StartWithWindows);

internal static class InstallService
{
    internal static InstallationResult? ShowInstaller(IWin32Window? owner)
    {
        if (PackageIdentity.IsPackaged) return null;
        using var dialog = new InstallWindow();
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    internal static void LaunchInstalled(string executable)
    {
        var launch = new ProcessStartInfo(executable) { UseShellExecute = true };
        launch.ArgumentList.Add($"--wait-for-exit={Environment.ProcessId}");
        Process.Start(launch);
    }

    private sealed class InstallWindow : Form
    {
        private readonly CheckBox startupChoice = new() { Text = AppText.Get("InstallerStartup"), Checked = true, AutoSize = true };
        private readonly Button install = new() { Text = AppText.Get("InstallerButton"), Width = 180, Height = 42, FlatStyle = FlatStyle.Flat };
        private readonly Label status = new() { AutoSize = false, Width = 560, Height = 80 };
        internal InstallationResult? Result { get; private set; }
        private bool installing;

        internal InstallWindow()
        {
            Text = AppText.Get("InstallerTitle");
            Icon = AppAssets.LoadIcon();
            BackColor = Color.FromArgb(17, 27, 41);
            ForeColor = Color.FromArgb(229, 237, 244);
            Font = new Font("Segoe UI", 10);
            AutoScaleDimensions = new(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(620, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            var title = new Label { Text = AppText.Get("InstallerHeading"), Font = new Font("Segoe UI", 18, FontStyle.Bold), Size = new(564, 50), Location = new Point(28, 25) };
            var explanation = new Label { Text = AppText.Get("InstallerExplanation"), Size = new(560, 54), Location = new Point(30, 82) };
            startupChoice.Location = new Point(30, 143);
            startupChoice.ForeColor = ForeColor;
            install.Location = new Point(410, 254);
            install.BackColor = Color.FromArgb(102, 196, 206);
            install.ForeColor = Color.FromArgb(17, 27, 41);
            status.Location = new Point(30, 185);
            Controls.AddRange([title, explanation, startupChoice, status, install]);
            install.Click += async (_, _) => await InstallAsync();
            FormClosing += (_, args) => { if (installing && args.CloseReason == CloseReason.UserClosing) args.Cancel = true; };
        }

        private async Task InstallAsync()
        {
            install.Enabled = false;
            startupChoice.Enabled = false;
            installing = true;
            ControlBox = false;
            status.Text = AppText.Get("InstallerCopying");
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Glasscape");
                var destination = Path.Combine(root, version);
                var source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
                var executable = Path.Combine(destination, "TransparentTaskbar.exe");
                var shouldStart = startupChoice.Checked;
                await Task.Run(() =>
                {
                    if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                    {
                        InstallationCopyGuard.Copy(source, destination,
                            AppText.Get("InstallerInvalidPath"), AppText.Get("InstallerSourceParent"));
                    }
                    if (!File.Exists(executable)) throw new FileNotFoundException(AppText.Get("InstallerMissingExe"), executable);
                });
                if (IsDisposed || Disposing) return;
                CreateShortcut(executable, destination);
                new StartupService(executable).SetEnabled(shouldStart);
                Result = new InstallationResult(executable, shouldStart);
                DialogResult = DialogResult.OK;
            }
            catch (Exception error)
            {
                AppLog.Write(error);
                status.Text = AppText.Format("InstallerFailed", error.Message);
                install.Enabled = true;
                startupChoice.Enabled = true;
            }
            finally { installing = false; if (!IsDisposed) ControlBox = true; }
        }

        private static void CreateShortcut(string executable, string directory)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Glasscape");
            Directory.CreateDirectory(folder);
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException(AppText.Get("InstallerShortcutFailed"));
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, "Glasscape.lnk"));
            try
            {
                shortcut.TargetPath = executable;
                shortcut.WorkingDirectory = directory;
                shortcut.Description = AppText.Get("InstallerShortcutDescription");
                shortcut.IconLocation = executable + ",0";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
