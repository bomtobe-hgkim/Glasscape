#Requires -Version 5.1
#Requires -PSEdition Desktop
param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\dist\TransparentTaskbar-win-x64\TransparentTaskbar.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\published-verification')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (@(Get-Process -Name TransparentTaskbar -ErrorAction SilentlyContinue).Count) { throw 'Close the existing TransparentTaskbar process before testing.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class PublishedTaskbarCheck {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct AppBarData { public uint Size; public IntPtr Window; public uint Callback, Edge; public Rect Bounds; public IntPtr Parameter; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    public static IntPtr MessageWindow() { return FindWindow(null, "TransparentTaskbar.Messages"); }
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    public static uint ExitCode(IntPtr process) { uint code; if (!GetExitCodeProcess(process, out code)) throw new System.ComponentModel.Win32Exception(); return code; }
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    public static int State(IntPtr window, int state, bool set) {
        var data = new AppBarData { Size=(uint)Marshal.SizeOf(typeof(AppBarData)), Window=window, Parameter=new IntPtr(state) };
        return (int)SHAppBarMessage(set ? 10u : 4u, ref data).ToUInt32();
    }
    public static Rectangle Bounds(IntPtr window) {
        Rect r; if (!GetWindowRect(window, out r)) throw new Exception("GetWindowRect failed");
        return new Rectangle(r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top);
    }
    public static Bitmap Capture(IntPtr window, string file) {
        var r=Bounds(window); var bitmap = new Bitmap(r.Width, r.Height);
        using (var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
        bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png); return bitmap;
    }
    public static double Difference(Bitmap a, Bitmap b) {
        if (a.Size != b.Size) throw new Exception("Taskbar size changed during capture");
        double sum=0; int count=0;
        for (int x=8; x<Math.Min(64,a.Width-8); x++) for (int y=a.Height/3; y<a.Height*2/3; y++) {
            var p=a.GetPixel(x,y); var q=b.GetPixel(x,y);
            sum+=Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B); count+=3;
        }
        if (count==0) throw new Exception("Taskbar sample was empty"); return sum/count;
    }
}
'@
$previousDpi = [PublishedTaskbarCheck]::SetThreadDpiAwarenessContext([IntPtr](-4))
$dpiError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
Write-Host "DPI: PMv2 previousContext=$previousDpi; lastError=$dpiError"
if ($previousDpi -eq [IntPtr]::Zero) { throw 'Could not set per-monitor DPI awareness for screenshot coordinates.' }
$taskbar = [PublishedTaskbarCheck]::FindWindow('Shell_TrayWnd', $null)
if ($taskbar -eq [IntPtr]::Zero) { throw 'The Explorer taskbar was not found.' }
$savedState = [PublishedTaskbarCheck]::State($taskbar, 0, $false)
Write-Host "Initial taskbar: state=$savedState; bounds=$([PublishedTaskbarCheck]::Bounds($taskbar))"
$owned = @{}
$fixture = $null
$images = @()
$testStarted = Get-Date
$settingsPath = Join-Path $env:LOCALAPPDATA 'TransparentTaskbar\settings.json'
$hadSettings = Test-Path -LiteralPath $settingsPath
$originalSettings = if ($hadSettings) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
New-Item -ItemType Directory -Path (Split-Path -Parent $settingsPath) -Force | Out-Null
[IO.File]::WriteAllText($settingsPath, '{"TaskbarEnabled":true,"TaskbarOpacity":0,"TaskbarAutoHide":false,"ActiveWallpaperId":null}', (New-Object Text.UTF8Encoding($false)))
function Get-OwnedProcess([int]$ProcessId) {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $p -and $p.Path -eq $Executable -and $owned.ContainsKey($ProcessId) -and $p.StartTime -eq $owned[$ProcessId]) { return $p }
}
function Start-TestApp {
    $started = Get-Date
    Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $Executable + '"') -WindowStyle Hidden
    for ($i=0; $i -lt 80; $i++) {
        $p = @(Get-Process -Name TransparentTaskbar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable -and $_.StartTime -ge $started.AddSeconds(-1) })
        if ($p.Count -eq 1) { $owned[$p[0].Id]=$p[0].StartTime; return $p[0] }
        Start-Sleep -Milliseconds 250
    }
    throw 'The published executable did not start through Explorer.'
}
function Capture-Test([string]$Name) {
    if ($null -ne $fixture) { [void][PublishedTaskbarCheck]::SetWindowPos($fixture.Handle, $taskbar, 0, 0, 0, 0, 0x13) }
    $bounds = [PublishedTaskbarCheck]::Bounds($taskbar)
    Write-Host "Capture $Name`: taskbar=$bounds; PNG=$($bounds.Width)x$($bounds.Height); state=$([PublishedTaskbarCheck]::State($taskbar,0,$false))"
    if (-not $screen.Contains($bounds)) { throw "Taskbar is outside the visible primary screen: taskbar=$bounds; screen=$screen." }
    $bitmap = [PublishedTaskbarCheck]::Capture($taskbar, (Join-Path $OutputDirectory ($Name + '.png')))
    $script:images += $bitmap
    return $bitmap
}
try {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    [void][PublishedTaskbarCheck]::State($taskbar, (($savedState -band (-bnot 1)) -bor 2), $true)
    $screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    Write-Host "After disabling auto-hide: state=$([PublishedTaskbarCheck]::State($taskbar,0,$false)); screen=$screen"
    $fixture = New-Object Windows.Forms.Form
    $fixture.StartPosition='Manual'
    $fixture.FormBorderStyle='None'; $fixture.ShowInTaskbar=$false; $fixture.TopMost=$false; $fixture.BackColor=[Drawing.Color]::Cyan
    $fixture.Bounds=New-Object Drawing.Rectangle($screen.Left, ($screen.Bottom-150), $screen.Width, 150)
    $fixture.Show(); [Windows.Forms.Application]::DoEvents()
    if (-not [PublishedTaskbarCheck]::SetWindowPos($fixture.Handle, $taskbar, 0, 0, 0, 0, 0x13)) { throw 'Could not place the verification fixture behind the taskbar.' }
    Start-Sleep -Milliseconds 1200
    Write-Host "Fixture: managed=$($fixture.Bounds); native=$([PublishedTaskbarCheck]::Bounds($fixture.Handle))"
    for ($i=0; $i -lt 30 -and -not $screen.Contains([PublishedTaskbarCheck]::Bounds($taskbar)); $i++) { Start-Sleep -Milliseconds 100 }
    $before = Capture-Test 'before'
    $app = Start-TestApp
    Start-Sleep -Seconds 2
    $running = Capture-Test 'running'
    $clearDifference = [PublishedTaskbarCheck]::Difference($before, $running)
    if ($clearDifference -le 2) { throw "Taskbar did not visibly clear (difference $clearDifference)." }
    # A second launch must leave the original owner as the only app process.
    Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $Executable + '"') -WindowStyle Hidden
    Start-Sleep -Seconds 1
    $instances = @(Get-Process -Name TransparentTaskbar -ErrorAction SilentlyContinue)
    if ($instances.Count -ne 1 -or $instances[0].Id -ne $app.Id) { throw 'Single-instance verification failed.' }
    $window = [PublishedTaskbarCheck]::MessageWindow()
    $appHandle = $app.Handle
    [uint32]$messagePid = 0
    $thread = [PublishedTaskbarCheck]::GetWindowThreadProcessId($window, [ref]$messagePid)
    if ($messagePid -ne $app.Id) { throw "Message-window ownership mismatch: HWND=$window; windowPID=$messagePid; appPID=$($app.Id); thread=$thread." }
    $posted = [PublishedTaskbarCheck]::PostThreadMessage($thread, 0x12, [IntPtr]::Zero, [IntPtr]::Zero)
    $postError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    if (-not $posted) { throw "WM_QUIT failed: HWND=$window; windowPID=$messagePid; appPID=$($app.Id); thread=$thread; lastError=$postError." }
    if (-not $app.WaitForExit(10000)) { throw 'The application did not exit gracefully.' }
    $exitCode = [PublishedTaskbarCheck]::ExitCode($appHandle)
    if ($exitCode -ne 0) { throw "The application exited with code $exitCode." }
    Start-Sleep -Seconds 2
    $after = Capture-Test 'after-graceful'
    $restoreDifference = [PublishedTaskbarCheck]::Difference($before, $after)
    if ($restoreDifference -gt 2) { throw "Graceful exit did not restore the taskbar (difference $restoreDifference)." }
    $app = Start-TestApp
    Start-Sleep -Seconds 2
    $crashRunning = Capture-Test 'running-before-crash'
    if ([PublishedTaskbarCheck]::Difference($before, $crashRunning) -le 2) { throw 'Relaunched taskbar did not visibly clear.' }
    $verified = Get-OwnedProcess $app.Id
    if ($null -eq $verified) { throw 'Refusing to stop an unverified process.' }
    Stop-Process -Id $verified.Id -Force
    if (-not $app.WaitForExit(10000)) { throw 'The application did not stop.' }
    Start-Sleep -Seconds 2
    $afterCrash = Capture-Test 'after-crash'
    $crashDifference = [PublishedTaskbarCheck]::Difference($before, $afterCrash)
    if ($crashDifference -gt 2) { throw "Crash watch did not restore the taskbar (difference $crashDifference)." }
    "PASS: clear=$clearDifference; graceful-restore=$restoreDifference; crash-restore=$crashDifference; screenshots=$OutputDirectory"
}
finally {
    try {
        foreach ($id in @($owned.Keys)) { $remaining = Get-OwnedProcess $id; if ($null -ne $remaining) { Stop-Process -Id $remaining.Id -Force -ErrorAction SilentlyContinue } }
        # Handle a startup failure using the exact executable path and creation time.
        Get-Process -Name TransparentTaskbar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable -and $_.StartTime -ge $testStarted } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }
    finally {
        [void][PublishedTaskbarCheck]::State($taskbar, $savedState, $true)
        if ($hadSettings) { [IO.File]::WriteAllBytes($settingsPath, $originalSettings) }
        else { Remove-Item -LiteralPath $settingsPath -ErrorAction SilentlyContinue }
        if ($null -ne $fixture) { $fixture.Close(); $fixture.Dispose() }
        foreach ($bitmap in $images) { $bitmap.Dispose() }
        [void][PublishedTaskbarCheck]::SetThreadDpiAwarenessContext($previousDpi)
    }
}
