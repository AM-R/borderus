param(
    [string]$AppPath = (Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows\Borderus.exe')
)

$native = @'
using System;
using System.Runtime.InteropServices;

public static class BorderusSmokeNative
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern void NotifyWinEvent(uint eventId, IntPtr hwnd, int objectId, int childId);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect rect, int size);
}
'@
Add-Type -TypeDefinition $native

$borderus = $null
$notepad = $null
try {
    $borderus = Start-Process -FilePath $AppPath -WindowStyle Hidden -PassThru
    $notepad = Start-Process -FilePath 'notepad.exe' -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 50
        $notepad.Refresh()
    } until ($notepad.MainWindowHandle -ne 0 -or [DateTime]::UtcNow -ge $deadline)
    if ($notepad.MainWindowHandle -eq 0) { throw 'Notepad window did not open.' }

    $target = $notepad.MainWindowHandle
    $targetRect = New-Object BorderusSmokeNative+Rect
    [BorderusSmokeNative]::GetWindowRect($target, [ref]$targetRect) | Out-Null
    $overlay = [IntPtr]::Zero
    $overlayDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        [BorderusSmokeNative]::EnumWindows({
            param($hwnd, $param)
            $pidValue = [uint32]0
            [BorderusSmokeNative]::GetWindowThreadProcessId($hwnd, [ref]$pidValue) | Out-Null
            $candidateRect = New-Object BorderusSmokeNative+Rect
            [BorderusSmokeNative]::GetWindowRect($hwnd, [ref]$candidateRect) | Out-Null
            $nearTarget = [Math]::Abs($candidateRect.Left - $targetRect.Left) -le 50 -and
                [Math]::Abs($candidateRect.Top - $targetRect.Top) -le 50
            if ($pidValue -eq $borderus.Id -and [BorderusSmokeNative]::GetWindowTextLength($hwnd) -eq 0 -and
                [BorderusSmokeNative]::IsWindowVisible($hwnd) -and $nearTarget) {
                $script:overlay = $hwnd
                return $false
            }
            return $true
        }, [IntPtr]::Zero) | Out-Null
        if ($overlay -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 10 }
    } until ($overlay -ne [IntPtr]::Zero -or [DateTime]::UtcNow -ge $overlayDeadline)
    if ($overlay -eq [IntPtr]::Zero) { throw 'Border overlay was not created.' }
    $overlayHasNoOwner = [BorderusSmokeNative]::GetWindow($overlay, 4) -eq [IntPtr]::Zero
    if (-not $overlayHasNoOwner) { throw 'Border overlay still has an owner and can bleed through higher windows.' }
    $overlayDirectlyAboveTarget = [BorderusSmokeNative]::GetWindow($target, 3) -eq $overlay
    if (-not $overlayDirectlyAboveTarget) {
        throw 'Border overlay is not directly above its target in Z-order.'
    }

    $overlayRect = New-Object BorderusSmokeNative+Rect
    [BorderusSmokeNative]::GetWindowRect($target, [ref]$targetRect) | Out-Null
    [BorderusSmokeNative]::GetWindowRect($overlay, [ref]$overlayRect) | Out-Null
    $firstFrameAligned = [Math]::Abs($targetRect.Left - $overlayRect.Left) -le 50 -and
        [Math]::Abs($targetRect.Top - $overlayRect.Top) -le 50
    if (-not $firstFrameAligned) { throw 'Overlay became visible before receiving its target coordinates.' }
    $leftOffset = $targetRect.Left - $overlayRect.Left

    [BorderusSmokeNative]::NotifyWinEvent(0x000A, $target, 0, 0)
    Start-Sleep -Milliseconds 20
    $watch = [Diagnostics.Stopwatch]::StartNew()
    [BorderusSmokeNative]::MoveWindow($target, 320, 220, 720, 480, $true) | Out-Null
    do {
        Start-Sleep -Milliseconds 5
        [BorderusSmokeNative]::GetWindowRect($target, [ref]$targetRect) | Out-Null
        [BorderusSmokeNative]::GetWindowRect($overlay, [ref]$overlayRect) | Out-Null
    } until ($overlayRect.Left -eq ($targetRect.Left - $leftOffset) -or $watch.ElapsedMilliseconds -ge 1000)
    $watch.Stop()
    [BorderusSmokeNative]::NotifyWinEvent(0x000B, $target, 0, 0)

    if ($overlayRect.Left -ne ($targetRect.Left - $leftOffset)) {
        throw "Border did not follow the moved window. target=$($targetRect.Left), overlay=$($overlayRect.Left), offset=$leftOffset, appExited=$($borderus.HasExited)"
    }
    $configPath = Join-Path (Split-Path -Parent $AppPath) 'settings.json'
    $configDeadline = [DateTime]::UtcNow.AddSeconds(3)
    while (-not (Test-Path -LiteralPath $configPath) -and [DateTime]::UtcNow -lt $configDeadline) {
        Start-Sleep -Milliseconds 20
    }
    if (-not (Test-Path -LiteralPath $configPath)) { throw 'settings.json was not created next to the executable.' }
    $savedSettings = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    if ($null -eq $savedSettings.Active -or $null -eq $savedSettings.Inactive) { throw 'Independent profiles were not saved.' }
    if ($savedSettings.Active.Thickness -ne 1 -or $savedSettings.Inactive.Thickness -ne 1) {
        throw 'Default profile thickness is not 1 px.'
    }
    if ($savedSettings.Active.CornerRadius -ne 4 -or $savedSettings.Inactive.CornerRadius -ne 4) {
        throw 'Default profile corner radius is not 4 px.'
    }
    if ($null -eq $savedSettings.Active.AnimateGradient -or $null -eq $savedSettings.Inactive.AnimateGradient) {
        throw 'Gradient animation setting was not saved for both profiles.'
    }
    if ($null -eq $savedSettings.Active.UseElevatedColor -or $null -eq $savedSettings.Inactive.UseElevatedColor -or
        [string]::IsNullOrWhiteSpace($savedSettings.Active.ElevatedColor) -or
        [string]::IsNullOrWhiteSpace($savedSettings.Inactive.ElevatedColor)) {
        throw 'Elevated-window color setting was not saved for both profiles.'
    }
    foreach ($profile in @($savedSettings.Active, $savedSettings.Inactive)) {
        if ($null -eq $profile.ShowTop -or $null -eq $profile.ShowRight -or
            $null -eq $profile.ShowBottom -or $null -eq $profile.ShowLeft) {
            throw 'Visible-side settings were not saved for both profiles.'
        }
    }
    if ($null -eq $savedSettings.LayoutIndicator -or
        $null -eq $savedSettings.LayoutIndicator.Enabled -or
        $null -eq $savedSettings.LayoutIndicator.Size -or
        $null -eq $savedSettings.LayoutIndicator.Opacity -or
        $null -eq $savedSettings.LayoutIndicator.ShowContainer -or
        $null -eq $savedSettings.LayoutIndicator.Content -or
        $null -eq $savedSettings.LayoutIndicator.Anchor -or
        $null -eq $savedSettings.LayoutIndicator.Side -or
        $null -eq $savedSettings.LayoutIndicator.OffsetX -or
        $null -eq $savedSettings.LayoutIndicator.OffsetY) {
        throw 'Layout indicator settings were not saved.'
    }

    $closeWatch = [Diagnostics.Stopwatch]::StartNew()
    $notepad.CloseMainWindow() | Out-Null
    do { Start-Sleep -Milliseconds 5 } until (-not [BorderusSmokeNative]::IsWindowVisible($overlay) -or $closeWatch.ElapsedMilliseconds -ge 500)
    $closeWatch.Stop()
    if ([BorderusSmokeNative]::IsWindowVisible($overlay)) { throw 'Overlay remained visible after its owner closed.' }

    [pscustomobject]@{
        OverlayHasNoOwner = $overlayHasNoOwner
        OverlayDirectlyAboveTarget = $overlayDirectlyAboveTarget
        FirstFrameAligned = $firstFrameAligned
        MoveSyncMilliseconds = $watch.ElapsedMilliseconds
        ConfigNextToExecutable = $true
        IndependentProfiles = $true
        CloseCleanupMilliseconds = $closeWatch.ElapsedMilliseconds
    }
}
finally {
    if ($notepad -and -not $notepad.HasExited) { Stop-Process -Id $notepad.Id }
    if ($borderus -and -not $borderus.HasExited) { Stop-Process -Id $borderus.Id }
}
