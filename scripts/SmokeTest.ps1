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
    $borderus = Start-Process -FilePath $AppPath -PassThru
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
    $settings = [IntPtr]::Zero
    $settingsDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        [BorderusSmokeNative]::EnumWindows({
            param($hwnd, $param)
            $pidValue = [uint32]0
            [BorderusSmokeNative]::GetWindowThreadProcessId($hwnd, [ref]$pidValue) | Out-Null
            if ($pidValue -eq $borderus.Id -and [BorderusSmokeNative]::GetWindowTextLength($hwnd) -gt 0 -and
                [BorderusSmokeNative]::IsWindowVisible($hwnd)) {
                $script:settings = $hwnd
                return $false
            }
            return $true
        }, [IntPtr]::Zero) | Out-Null
        if ($settings -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 10 }
    } until ($settings -ne [IntPtr]::Zero -or [DateTime]::UtcNow -ge $settingsDeadline)
    if ($settings -eq [IntPtr]::Zero) { throw 'Borderus Settings window was not found.' }

    $settingsRect = New-Object BorderusSmokeNative+Rect
    [BorderusSmokeNative]::GetWindowRect($settings, [ref]$settingsRect) | Out-Null
    $settingsOverlay = [IntPtr]::Zero
    $settingsOverlayDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $candidate = [BorderusSmokeNative]::GetWindow($settings, 3)
        $pidValue = [uint32]0
        [BorderusSmokeNative]::GetWindowThreadProcessId($candidate, [ref]$pidValue) | Out-Null
        if ($pidValue -eq $borderus.Id -and [BorderusSmokeNative]::GetWindowTextLength($candidate) -eq 0 -and
            [BorderusSmokeNative]::IsWindowVisible($candidate)) {
            $settingsOverlay = $candidate
        }
        else {
            Start-Sleep -Milliseconds 10
        }
    } until ($settingsOverlay -ne [IntPtr]::Zero -or [DateTime]::UtcNow -ge $settingsOverlayDeadline)
    if ($settingsOverlay -eq [IntPtr]::Zero) {
        throw 'Settings border overlay was not created directly above the Settings window.'
    }

    $settingsOverlayRect = New-Object BorderusSmokeNative+Rect
    [BorderusSmokeNative]::GetWindowRect($settingsOverlay, [ref]$settingsOverlayRect) | Out-Null
    $settingsLeftOffset = $settingsRect.Left - $settingsOverlayRect.Left
    $settingsTopOffset = $settingsRect.Top - $settingsOverlayRect.Top
    $settingsRightOffset = $settingsOverlayRect.Right - $settingsRect.Right
    $settingsBottomOffset = $settingsOverlayRect.Bottom - $settingsRect.Bottom
    [BorderusSmokeNative]::MoveWindow($settings, 180, 160, $settingsRect.Right - $settingsRect.Left, $settingsRect.Bottom - $settingsRect.Top, $true) | Out-Null
    $settingsMoveWatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 2
        [BorderusSmokeNative]::GetWindowRect($settings, [ref]$settingsRect) | Out-Null
        [BorderusSmokeNative]::GetWindowRect($settingsOverlay, [ref]$settingsOverlayRect) | Out-Null
        $settingsAligned = $settingsOverlayRect.Left -eq ($settingsRect.Left - $settingsLeftOffset) -and
            $settingsOverlayRect.Top -eq ($settingsRect.Top - $settingsTopOffset) -and
            $settingsOverlayRect.Right -eq ($settingsRect.Right + $settingsRightOffset) -and
            $settingsOverlayRect.Bottom -eq ($settingsRect.Bottom + $settingsBottomOffset)
    } until ($settingsAligned -or $settingsMoveWatch.ElapsedMilliseconds -ge 250)
    $settingsMoveWatch.Stop()
    if (-not $settingsAligned) { throw 'Settings border did not follow the moved Settings window.' }

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
    $topOffset = $targetRect.Top - $overlayRect.Top
    $rightOffset = $overlayRect.Right - $targetRect.Right
    $bottomOffset = $overlayRect.Bottom - $targetRect.Bottom

    [BorderusSmokeNative]::NotifyWinEvent(0x000A, $target, 0, 0)
    Start-Sleep -Milliseconds 20
    $resizeSyncMaxMilliseconds = 0L
    foreach ($geometry in @(
        @(320, 220, 640, 420),
        @(200, 220, 760, 420),
        @(200, 120, 760, 520),
        @(200, 120, 820, 560),
        @(320, 220, 720, 480)
    )) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        [BorderusSmokeNative]::MoveWindow($target, $geometry[0], $geometry[1], $geometry[2], $geometry[3], $true) | Out-Null
        do {
            Start-Sleep -Milliseconds 2
            [BorderusSmokeNative]::GetWindowRect($target, [ref]$targetRect) | Out-Null
            [BorderusSmokeNative]::GetWindowRect($overlay, [ref]$overlayRect) | Out-Null
            $aligned = $overlayRect.Left -eq ($targetRect.Left - $leftOffset) -and
                $overlayRect.Top -eq ($targetRect.Top - $topOffset) -and
                $overlayRect.Right -eq ($targetRect.Right + $rightOffset) -and
                $overlayRect.Bottom -eq ($targetRect.Bottom + $bottomOffset)
        } until ($aligned -or $watch.ElapsedMilliseconds -ge 250)
        $watch.Stop()
        $resizeSyncMaxMilliseconds = [Math]::Max($resizeSyncMaxMilliseconds, $watch.ElapsedMilliseconds)
        if (-not $aligned) {
            throw "Border did not follow window resize. target=$($targetRect.Left),$($targetRect.Top),$($targetRect.Right),$($targetRect.Bottom) overlay=$($overlayRect.Left),$($overlayRect.Top),$($overlayRect.Right),$($overlayRect.Bottom)"
        }
    }
    [BorderusSmokeNative]::NotifyWinEvent(0x000B, $target, 0, 0)
    $configPath = Join-Path $env:APPDATA 'Borderus\settings.json'
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
    if ($null -eq $savedSettings.BordersEnabled) { throw 'Border feature state was not saved.' }
    if ($null -eq $savedSettings.LayoutIndicator -or
        $null -eq $savedSettings.LayoutIndicator.Enabled -or
        $null -eq $savedSettings.LayoutIndicator.Size -or
        $null -eq $savedSettings.LayoutIndicator.Opacity -or
        $null -eq $savedSettings.LayoutIndicator.ShowContainer -or
        $null -eq $savedSettings.LayoutIndicator.Content -or
        $null -eq $savedSettings.LayoutIndicator.Anchor -or
        $null -eq $savedSettings.LayoutIndicator.DefaultSide -or
        $null -eq $savedSettings.LayoutIndicator.WebSide -or
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
        SettingsOverlayDirectlyAboveTarget = $true
        SettingsMoveSyncMilliseconds = $settingsMoveWatch.ElapsedMilliseconds
        ResizeSyncMaxMilliseconds = $resizeSyncMaxMilliseconds
        ConfigNextToExecutable = $true
        IndependentProfiles = $true
        CloseCleanupMilliseconds = $closeWatch.ElapsedMilliseconds
    }
}
finally {
    if ($notepad -and -not $notepad.HasExited) { Stop-Process -Id $notepad.Id }
    if ($borderus -and -not $borderus.HasExited) { Stop-Process -Id $borderus.Id }
}
