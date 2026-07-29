using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using Borderus.Models;
using Borderus.Native;
using Borderus.Rendering;

namespace Borderus.Services;

internal sealed class LayoutIndicatorService : IDisposable
{
    private readonly LayoutIndicatorOverlay _overlay = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _layoutTimer;
    private readonly System.Threading.Timer _moveTimer;
    private readonly NativeMethods.WinEventProc _eventCallback;
    private readonly nint[] _hooks = new nint[4];
    private LayoutIndicatorSettings _settings;
    private bool _disposed;

    public LayoutIndicatorService(BorderSettings settings)
    {
        _settings = settings.LayoutIndicator.Copy();
        _eventCallback = OnWinEvent;
        uint flags = NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess;
        _hooks[0] = NativeMethods.SetWinEventHook(NativeMethods.EventObjectFocus,
            NativeMethods.EventObjectFocus, 0, _eventCallback, 0, 0, flags);
        _hooks[1] = NativeMethods.SetWinEventHook(NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange, 0, _eventCallback, 0, 0, flags);
        _hooks[2] = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground, 0, _eventCallback, 0, 0, flags);
        _hooks[3] = NativeMethods.SetWinEventHook(NativeMethods.EventSystemMoveSizeStart,
            NativeMethods.EventSystemMoveSizeEnd, 0, _eventCallback, 0, 0, flags);

        _moveTimer = new System.Threading.Timer(_ => QueueRefresh(), null, Timeout.Infinite, Timeout.Infinite);
        _layoutTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background,
            (_, _) => Refresh(), _dispatcher);
        _layoutTimer.Start();
    }

    public void Apply(BorderSettings settings)
    {
        _settings = settings.LayoutIndicator.Copy();
        if (!_settings.Enabled) _overlay.HideImmediately();
        else Refresh();
    }

    private void OnWinEvent(nint hook, uint eventType, nint hWnd, int objectId, int childId,
        uint eventThread, uint eventTime)
    {
        if (_disposed) return;
        if (eventType == NativeMethods.EventSystemMoveSizeStart)
        {
            _moveTimer.Change(0, 8);
            return;
        }
        if (eventType == NativeMethods.EventSystemMoveSizeEnd)
            _moveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (eventType == NativeMethods.EventObjectLocationChange && objectId != NativeMethods.ObjidCaret &&
            hWnd != NativeMethods.GetForegroundWindow())
            return;
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (!_disposed) _dispatcher.BeginInvoke(DispatcherPriority.Send, Refresh);
    }

    private void Refresh()
    {
        if (_disposed || !_settings.Enabled)
        {
            _overlay.HideImmediately();
            return;
        }

        nint foreground = NativeMethods.GetForegroundWindow();
        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new NativeMethods.GuiThreadInfo { Size = Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        if (threadId == 0)
        {
            _overlay.HideImmediately();
            return;
        }

        bool hasGuiInfo = NativeMethods.GetGUIThreadInfo(threadId, ref info);
        NativeMethods.Rect caretRect = default;
        bool hasCaret = hasGuiInfo && info.CaretWindow != 0 && TryGetCaretRect(info, out caretRect);
        if (!hasCaret && !TryGetAutomationRect(out caretRect))
        {
            _overlay.HideImmediately();
            return;
        }

        NativeMethods.Rect anchorRect = caretRect;
        if (_settings.Anchor == LayoutIndicatorAnchor.Field && hasGuiInfo && info.FocusWindow != 0 &&
            info.FocusWindow != foreground && NativeMethods.GetWindowRect(info.FocusWindow, out var focusRect) &&
            focusRect.Width >= 40 && focusRect.Height >= 14)
            anchorRect = focusRect;

        uint dpi = NativeMethods.GetDpiForWindow(foreground);
        double scale = dpi == 0 ? 1 : dpi / 96d;
        double size = Math.Clamp(_settings.Size, 10, 80);
        double widthFactor = _settings.Content == LayoutIndicatorContent.FlagOnly ? 1.45 : 1.75;
        int width = (int)Math.Ceiling(size * widthFactor * scale);
        int height = (int)Math.Ceiling(size * scale);
        (int x, int y) = Position(anchorRect, width, height, scale);

        (string language, string region) = GetLayout(threadId);
        _overlay.Update(region, language, _settings, x, y, scale);
    }

    private (int X, int Y) Position(NativeMethods.Rect anchor, int width, int height, double scale)
    {
        int gap = (int)Math.Ceiling(6 * scale);
        int offsetX = (int)Math.Round(Math.Clamp(_settings.OffsetX, -100, 100) * scale);
        int offsetY = (int)Math.Round(Math.Clamp(_settings.OffsetY, -100, 100) * scale);
        int centerX = anchor.Left + (anchor.Width - width) / 2;
        int centerY = anchor.Top + (anchor.Height - height) / 2;
        LayoutIndicatorSide side = _settings.Side ?? LayoutIndicatorSide.Right;
        return side switch
        {
            LayoutIndicatorSide.Top => (centerX + offsetX, anchor.Top - height - gap + offsetY),
            LayoutIndicatorSide.Bottom => (centerX + offsetX, anchor.Bottom + gap + offsetY),
            LayoutIndicatorSide.Left => (anchor.Left - width - gap + offsetX, centerY + offsetY),
            _ => (anchor.Right + gap + offsetX, centerY + offsetY)
        };
    }

    private static bool TryGetAutomationRect(out NativeMethods.Rect rect)
    {
        try
        {
            System.Windows.Rect bounds = AutomationElement.FocusedElement.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
            {
                rect = default;
                return false;
            }
            rect = new NativeMethods.Rect
            {
                Left = (int)Math.Round(bounds.Left),
                Top = (int)Math.Round(bounds.Top),
                Right = (int)Math.Round(bounds.Right),
                Bottom = (int)Math.Round(bounds.Bottom)
            };
            return true;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException)
        {
            rect = default;
            return false;
        }
    }

    private static bool TryGetCaretRect(NativeMethods.GuiThreadInfo info, out NativeMethods.Rect rect)
    {
        var topLeft = new NativeMethods.NativePoint { X = info.CaretRect.Left, Y = info.CaretRect.Top };
        var bottomRight = new NativeMethods.NativePoint { X = info.CaretRect.Right, Y = info.CaretRect.Bottom };
        if (!NativeMethods.ClientToScreen(info.CaretWindow, ref topLeft) ||
            !NativeMethods.ClientToScreen(info.CaretWindow, ref bottomRight))
        {
            rect = default;
            return false;
        }
        rect = new NativeMethods.Rect
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = topLeft.X + 1,
            Bottom = Math.Max(topLeft.Y + 1, bottomRight.Y)
        };
        return true;
    }

    private static (string Language, string Region) GetLayout(uint threadId)
    {
        int languageId = (int)(NativeMethods.GetKeyboardLayout(threadId).ToInt64() & 0xffff);
        string language;
        try { language = CultureInfo.GetCultureInfo(languageId).TwoLetterISOLanguageName; }
        catch { language = "??"; }

        string region = language switch
        {
            "ru" => "RU", "uk" => "UA", "be" => "BY", "en" => "US", "de" => "DE",
            "fr" => "FR", "es" => "ES", "it" => "IT", "pl" => "PL", "cs" => "CZ",
            "tr" => "TR", "pt" => "PT", "ja" => "JP", "ko" => "KR", "zh" => "CN",
            _ => string.Empty
        };
        return (language, region);
    }

    public void Dispose()
    {
        _disposed = true;
        _layoutTimer.Stop();
        _moveTimer.Dispose();
        foreach (nint hook in _hooks)
            if (hook != 0) NativeMethods.UnhookWinEvent(hook);
        _overlay.Close();
    }
}
