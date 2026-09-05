<#
.SYNOPSIS
Writes the run flag, starts tModLoader, and clicks through the screen it shows when
there is no audio device.

.DESCRIPTION
On a machine with no audio device tModLoader's sound engine puts up an in-game
"No audio hardware found" panel with a Continue button before any mod loads, so no code
in the mod can get past it and an unattended run sits behind it forever. This watches
the client log for that warning and clicks Continue in the game window until mods start
loading. A game that has not read its flag by the deadline is killed rather than left up:
it holds the mod's file lock and blocks the next build, and nobody is watching to close it.

.EXAMPLE
.\terragent\Tests\launch.ps1 "drive 600 fresh"
.\terragent\Tests\launch.ps1 "walk up a mountain"
#>
[CmdletBinding()]
param(
    [string]$Flag = "run",
    # The repo's .env, then the common Steam location.
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
    [int]$WatchSeconds = 300
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

    /// Clicks at a point given as fractions of the window's width and height.
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

$flagPath = Join-Path $Install "tModLoader-Logs\agent\run-tests.flag"
$clientLog = Join-Path $Install "tModLoader-Logs\client.log"
New-Item -ItemType Directory -Force (Split-Path $flagPath) | Out-Null
Set-Content -Path $flagPath -Value $Flag -NoNewline -Encoding ascii
Write-Host "flag: $Flag"

$launched = Get-Date
Start-Process -FilePath (Join-Path $Install "start-tModLoader.bat") -WorkingDirectory $Install -WindowStyle Minimized

# The .bat hands off to a shell that hands off to dotnet, so the game is found by what
# it is running rather than by what was started. The name matters: a shell whose
# command line merely mentioned the dll matched once, and was killed for it.
$game = $null
$deadline = (Get-Date).AddSeconds(60)
while ($null -eq $game -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $game = Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*tModLoader.dll*" } |
        Select-Object -First 1
}
if ($null -eq $game) {
    throw "tModLoader did not start"
}
Write-Host "game: pid $($game.ProcessId)"

# The panel's Continue button, as fractions of the window: the left of two buttons
# under the message box. Measured on the 1309x759 window the game opens at.
$continueAcross = 0.298
$continueDown = 0.867

$deadline = (Get-Date).AddSeconds($WatchSeconds)
$read = $false
$clicks = 0
while ((Get-Date) -lt $deadline) {
    if (-not (Test-Path $flagPath)) {
        Write-Host "flag read; the mod is in charge"
        $read = $true
        break
    }
    $process = Get-Process -Id $game.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) {
        Write-Host "game exited before reading the flag"
        exit 2
    }

    # Only this launch's log lines count; the file is rewritten each start but a
    # stale one from a previous start can still be on disk for the first seconds.
    $recent = @()
    if ((Test-Path $clientLog) -and (Get-Item $clientLog).LastWriteTime -ge $launched) {
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
    Start-Sleep -Seconds 1
}

if (-not $read) {
    Write-Host "flag not read within $WatchSeconds s; killing the game"
    Stop-Process -Id $game.ProcessId -Force -ErrorAction SilentlyContinue
    Remove-Item $flagPath -ErrorAction SilentlyContinue
    exit 3
}
