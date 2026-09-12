<#
.SYNOPSIS
Writes the run flag, starts tModLoader, clicks past the no-audio panel, and waits.

.DESCRIPTION
On a machine with no audio device tModLoader puts up a "No audio hardware found" panel
with a Continue button before any mod loads, so nothing in the mod can get past it and an
unattended run sits behind it forever. This watches the client log for that warning and
clicks Continue until mods start loading. A game that outlives the deadline is killed
rather than left up: it holds the mod's file lock and blocks the next build.

.EXAMPLE
.\Terragent\Tests\launch.ps1 120
#>
[CmdletBinding()]
param(
    # Seconds to drive, or "arena" to walk the pathfinding scenarios in a saved world.
    [string]$Flag = "120",
    [string]$Install = $(
        $envFile = Join-Path $PSScriptRoot "..\..\.env"
        $fromFile = if (Test-Path $envFile) {
            Get-Content $envFile |
                Where-Object { $_ -match '^\s*TMODLOADER_INSTALL_PATH\s*=\s*(.+?)\s*$' } |
                ForEach-Object { $Matches[1] } |
                Select-Object -First 1
        }
        if ($fromFile) { $fromFile } else { "C:\Program Files (x86)\Steam\steamapps\common\tModLoader" }
    ),
    [int]$WatchSeconds = 420
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class Window
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    public static bool Click(IntPtr hWnd, double acrossFraction, double downFraction)
    {
        Rect rect;
        if (hWnd == IntPtr.Zero || !GetWindowRect(hWnd, out rect)) return false;
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(300);
        int x = rect.Left + (int)((rect.Right - rect.Left) * acrossFraction);
        int y = rect.Top + (int)((rect.Bottom - rect.Top) * downFraction);
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        return true;
    }
}
"@

$logs = Join-Path $Install "tModLoader-Logs"
$flagPath = Join-Path $logs "terragent-run.flag"
# Whichever client log this run is actually writing, found each time rather than named.
# tModLoader falls back to client2.log, client3.log and so on when the first is held open,
# so a fixed name goes stale: the watcher then cannot see the no audio warning, never
# clicks Continue, and every run dies at that panel with an empty log to show for it.
function Get-ClientLog {
    Get-ChildItem -Path $logs -Filter "client*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
New-Item -ItemType Directory -Force $logs | Out-Null
Set-Content -Path $flagPath -Value "$Flag" -NoNewline -Encoding ascii
Write-Host "flag: $Flag"

$launched = Get-Date
Start-Process -FilePath (Join-Path $Install "start-tModLoader.bat") -WorkingDirectory $Install -WindowStyle Minimized

# The .bat hands off to a shell that hands off to dotnet, so the game is found by what it
# is running rather than by what was started.
$game = $null
$deadline = (Get-Date).AddSeconds(90)
while ($null -eq $game -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $game = Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*tModLoader.dll*" } |
        Select-Object -First 1
}
if ($null -eq $game) { throw "tModLoader did not start" }
Write-Host "game: pid $($game.ProcessId)"

# The panel's Continue button, as fractions of the window: the left of two buttons under
# the message box, measured on the window the game opens at.
$continueAcross = 0.298
$continueDown = 0.867

$deadline = (Get-Date).AddSeconds($WatchSeconds)
$clicks = 0
while ((Get-Date) -lt $deadline) {
    $process = Get-Process -Id $game.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) {
        Write-Host "game exited"
        exit 0
    }

    if (Test-Path $flagPath) {
        $recent = @()
        $clientLog = Get-ClientLog
        if ($clientLog -and (Get-Item $clientLog).LastWriteTime -ge $launched) {
            $recent = Get-Content $clientLog -ErrorAction SilentlyContinue
        }
        $noAudio = $recent | Where-Object { $_ -like "*No audio hardware found*" }
        $loading = $recent | Where-Object { $_ -like "*Finding Mods*" }
        if ($noAudio -and -not $loading -and $clicks -lt 10) {
            if ([Window]::Click($process.MainWindowHandle, $continueAcross, $continueDown)) {
                $clicks++
                Write-Host "clicked Continue on the no-audio panel"
                Start-Sleep -Seconds 3
            }
        }
    }

    Start-Sleep -Seconds 2
}

Write-Host "deadline reached; killing the game"
Stop-Process -Id $game.ProcessId -Force -ErrorAction SilentlyContinue
Remove-Item $flagPath -ErrorAction SilentlyContinue
exit 3
