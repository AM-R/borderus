using System.Collections.Concurrent;
using System.Windows.Threading;
using Borderus.Models;
using Borderus.Native;
using Borderus.Rendering;

namespace Borderus.Services;

internal sealed class WindowBorderService : IDisposable
{
    private readonly ConcurrentDictionary<nint, BorderOverlay> _overlays = new();
    private readonly OverlayPool _pool;
    private readonly ConcurrentDictionary<nint, FrameOffsets> _frameOffsets = new();
    private readonly ConcurrentDictionary<nint, bool> _elevatedWindows = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _reconcileTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly NativeMethods.WinEventProc _eventCallback;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly nint[] _eventHooks = new nint[4];
    private readonly System.Threading.Timer _moveTimer;
    private BorderSettings _settings;
    private nint _foregroundWindow;
    private nint _movingWindow;
    private double _activeDashOffset;
    private double _inactiveDashOffset;
    private bool _disposed;

    public WindowBorderService(Dispatcher dispatcher, BorderSettings settings)
    {
        _dispatcher = dispatcher;
        _pool = new OverlayPool();
        _settings = settings.Copy();
        _foregroundWindow = NativeMethods.GetForegroundWindow();
        _eventCallback = OnWinEvent;
        uint flags = NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess;
        _eventHooks[0] = NativeMethods.SetWinEventHook(NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange, 0, _eventCallback, 0, 0, flags);
        _eventHooks[1] = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground, 0, _eventCallback, 0, 0, NativeMethods.WineventOutOfContext);
        _eventHooks[2] = NativeMethods.SetWinEventHook(NativeMethods.EventObjectDestroy,
            NativeMethods.EventObjectHide, 0, _eventCallback, 0, 0, flags);
        _eventHooks[3] = NativeMethods.SetWinEventHook(NativeMethods.EventSystemMoveSizeStart,
            NativeMethods.EventSystemMoveSizeEnd, 0, _eventCallback, 0, 0, flags);
        _moveTimer = new System.Threading.Timer(_ => PositionMovingWindow(), null,
            Timeout.Infinite, Timeout.Infinite);

        _reconcileTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background,
            (_, _) => ReconcileWindows(), _dispatcher);
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(35), DispatcherPriority.Render,
            Animate, _dispatcher);
        _reconcileTimer.Start();
        _animationTimer.Start();
        ReconcileWindows();
    }

    public void Apply(BorderSettings settings)
    {
        bool wasEnabled = _settings.Enabled;
        bool fullscreenVisibilityChanged = _settings.ShowInFullscreen != settings.ShowInFullscreen;
        _settings = settings.Copy();
        if (!_settings.Enabled)
        {
            HideAll();
            return;
        }
        if (!wasEnabled || fullscreenVisibilityChanged)
        {
            ReconcileWindows();
            if (!wasEnabled) return;
        }
        foreach (var pair in _overlays)
        {
            bool active = pair.Key == _foregroundWindow;
            pair.Value.Render(_settings, GetDashOffset(active), active, IsElevated(pair.Key));
            PositionWindow(pair.Key);
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint hWnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_disposed || hWnd == 0) return;

        if (eventType == NativeMethods.EventSystemForeground)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Send, () => SetForegroundWindow(hWnd));
            return;
        }
        if (eventType == NativeMethods.EventSystemMoveSizeStart)
        {
            _movingWindow = hWnd;
            _moveTimer.Change(0, 8);
            return;
        }
        if (eventType == NativeMethods.EventSystemMoveSizeEnd)
        {
            PositionWindow(hWnd);
            _movingWindow = 0;
            _moveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        if (objectId != NativeMethods.ObjidWindow) return;
        if (eventType is NativeMethods.EventObjectDestroy or NativeMethods.EventObjectHide)
        {
            if (_overlays.TryGetValue(hWnd, out var overlay)) overlay.HideImmediately();
            _dispatcher.BeginInvoke(DispatcherPriority.Send, () => RemoveOverlay(hWnd));
            return;
        }
        if (eventType == NativeMethods.EventObjectShow)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Send, ReconcileWindows);
            return;
        }

        PositionWindow(hWnd);
    }

    private void PositionMovingWindow()
    {
        nint hWnd = _movingWindow;
        if (hWnd != 0) PositionWindow(hWnd);
    }

    private void PositionWindow(nint hWnd)
    {
        if (!_settings.Enabled || !_overlays.TryGetValue(hWnd, out var overlay)) return;
        double padding = GetPadding(hWnd);
        if (_frameOffsets.TryGetValue(hWnd, out var offsets) && NativeMethods.GetWindowRect(hWnd, out var rect))
        {
            rect.Left += offsets.Left;
            rect.Top += offsets.Top;
            rect.Right += offsets.Right;
            rect.Bottom += offsets.Bottom;
            overlay.Position(rect, padding);
        }
        else if (NativeMethods.TryGetFrameBounds(hWnd, out rect))
        {
            overlay.Position(rect, padding);
        }
    }

    private void SetForegroundWindow(nint hWnd)
    {
        nint previous = _foregroundWindow;
        _foregroundWindow = hWnd;
        RenderOverlay(hWnd);
        _ = RenderInactiveAfterTransition(previous);
    }

    private async Task RenderInactiveAfterTransition(nint hWnd)
    {
        if (hWnd == 0) return;
        await Task.Delay(120);
        if (_disposed || hWnd == _foregroundWindow) return;
        if (!ShouldTrack(hWnd)) RemoveOverlay(hWnd);
        else RenderOverlay(hWnd);
    }

    private void RenderOverlay(nint hWnd)
    {
        if (!_overlays.TryGetValue(hWnd, out var overlay)) return;
        bool active = hWnd == _foregroundWindow;
        overlay.Render(_settings, GetDashOffset(active), active, IsElevated(hWnd));
        PositionWindow(hWnd);
    }

    private void ReconcileWindows()
    {
        if (!_settings.Enabled || _disposed) return;

        var found = new HashSet<nint>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!ShouldTrack(hWnd) || !NativeMethods.TryGetFrameBounds(hWnd, out var rect)) return true;
            if (NativeMethods.GetWindowRect(hWnd, out var windowRect))
                _frameOffsets[hWnd] = new FrameOffsets(rect.Left - windowRect.Left, rect.Top - windowRect.Top,
                    rect.Right - windowRect.Right, rect.Bottom - windowRect.Bottom);
            found.Add(hWnd);
            _elevatedWindows.GetOrAdd(hWnd, NativeMethods.IsProcessElevated);

            if (!_overlays.TryGetValue(hWnd, out var overlay))
            {
                overlay = _pool.Borrow(hWnd);
                if (overlay is null || !_overlays.TryAdd(hWnd, overlay))
                {
                    overlay?.Close();
                    return true;
                }
                overlay.ShowPrepared();
                bool active = hWnd == _foregroundWindow;
                overlay.Render(_settings, GetDashOffset(active), active, IsElevated(hWnd));
                overlay.Position(rect, GetPadding(hWnd));
            }
            overlay.Position(rect, GetPadding(hWnd));
            return true;
        }, 0);

        foreach (var hWnd in _overlays.Keys.Where(h => !found.Contains(h) || !NativeMethods.IsWindow(h)).ToArray())
            RemoveOverlay(hWnd);
    }

    private bool ShouldTrack(nint hWnd)
    {
        if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd) || NativeMethods.IsCloaked(hWnd)) return false;
        if (!_settings.ShowInFullscreen && NativeMethods.IsFullscreenWindow(hWnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == _ownProcessId || processId == 0) return false;
        long extendedStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GwlExStyle).ToInt64();
        if ((extendedStyle & NativeMethods.WsExToolWindow) != 0) return false;
        return NativeMethods.GetWindowTextLength(hWnd) > 0;
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!_settings.Enabled) return;
        AdvanceAnimation(_settings.Active, ref _activeDashOffset);
        AdvanceAnimation(_settings.Inactive, ref _inactiveDashOffset);
        foreach (var pair in _overlays)
        {
            bool active = pair.Key == _foregroundWindow;
            BorderProfile profile = active ? _settings.Active : _settings.Inactive;
            if ((profile.Animate && profile.LineStyle != BorderLineStyle.Solid)
                || (profile.AnimateGradient && profile.FillStyle == BorderFillStyle.AlongLine))
                pair.Value.Render(_settings, GetDashOffset(active), active, IsElevated(pair.Key));
        }
    }

    private static void AdvanceAnimation(BorderProfile profile, ref double offset)
    {
        bool animateDash = profile.Animate && profile.LineStyle != BorderLineStyle.Solid;
        bool animateGradient = profile.AnimateGradient && profile.FillStyle == BorderFillStyle.AlongLine;
        if (!animateDash && !animateGradient) return;
        double sign = profile.Direction == AnimationDirection.Clockwise ? -1 : 1;
        offset += sign * Math.Clamp(profile.AnimationSpeed, 0.25, 40) * 0.18;
    }

    private BorderProfile GetProfile(nint hWnd) => hWnd == _foregroundWindow ? _settings.Active : _settings.Inactive;
    private double GetPadding(nint hWnd)
    {
        uint dpi = NativeMethods.GetDpiForWindow(hWnd);
        double scale = dpi == 0 ? 1 : dpi / 96d;
        BorderProfile profile = GetProfile(hWnd);
        return (profile.Thickness + Math.Clamp(profile.Padding, -30, 30)) * scale;
    }
    private bool IsElevated(nint hWnd) => _elevatedWindows.TryGetValue(hWnd, out bool elevated) && elevated;
    private double GetDashOffset(bool active) => active ? _activeDashOffset : _inactiveDashOffset;

    private void RemoveOverlay(nint hWnd)
    {
        _frameOffsets.TryRemove(hWnd, out _);
        _elevatedWindows.TryRemove(hWnd, out _);
        if (_overlays.TryRemove(hWnd, out var overlay))
            _pool.Return(overlay);
    }

    private void HideAll()
    {
        foreach (var hWnd in _overlays.Keys.ToArray()) RemoveOverlay(hWnd);
    }

    public void Dispose()
    {
        _disposed = true;
        _reconcileTimer.Stop();
        _animationTimer.Stop();
        foreach (nint hook in _eventHooks)
            if (hook != 0) NativeMethods.UnhookWinEvent(hook);
        _moveTimer.Dispose();
        HideAll();
        _pool.Dispose();
    }

    private readonly record struct FrameOffsets(int Left, int Top, int Right, int Bottom);
}
