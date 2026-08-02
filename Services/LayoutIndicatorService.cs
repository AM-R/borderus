using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Threading;
using Borderus.Models;
using Borderus.Native;
using Borderus.Rendering;

namespace Borderus.Services;

internal sealed class LayoutIndicatorService : IDisposable
{
    private readonly LayoutIndicatorOverlay _overlay = new();
    private readonly Dispatcher _dispatcher;
    private readonly System.Threading.Timer _moveTimer;
    private readonly System.Threading.Timer _refreshTimer;
    private readonly NativeMethods.WinEventProc _eventCallback;
    private readonly nint[] _hooks = new nint[4];
    private LayoutIndicatorSettings _settings;
    private int _refreshing;
    private int _renderQueued;
    private PendingUpdate? _pendingUpdate;
    private bool _disposed;

    public LayoutIndicatorService(Dispatcher dispatcher, BorderSettings settings)
    {
        _dispatcher = dispatcher;
        _settings = EffectiveSettings(settings);
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
        // UI Automation calls into the target process. Polling terminal controls at 60 Hz
        // can starve PowerShell's accessibility provider and make both apps feel delayed.
        _refreshTimer = new System.Threading.Timer(_ => Refresh(), null, 0, 50);
    }

    public void Apply(BorderSettings settings)
    {
        _settings = EffectiveSettings(settings);
        if (!_settings.Enabled) _overlay.HideImmediately();
        else QueueRefresh();
    }

    private static LayoutIndicatorSettings EffectiveSettings(BorderSettings settings)
    {
        LayoutIndicatorSettings effective = settings.LayoutIndicator.Copy();
        effective.Enabled = settings.Enabled && effective.Enabled;
        return effective;
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
        if (!_disposed) ThreadPool.QueueUserWorkItem(_ => Refresh());
    }

    private void Refresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            LayoutIndicatorSettings settings = _settings;
            if (!settings.Enabled)
            {
                QueueHide();
                return;
            }

            nint foreground = NativeMethods.GetForegroundWindow();
            uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
            var info = new NativeMethods.GuiThreadInfo { Size = Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
            if (threadId == 0)
            {
                QueueHide();
                return;
            }

            bool hasGuiInfo = NativeMethods.GetGUIThreadInfo(threadId, ref info);
            NativeMethods.Rect caretRect = default;
            bool hasCaret = hasGuiInfo && info.CaretWindow != 0 && TryGetCaretRect(info, out caretRect);
            bool hasAutomationField = false;
            bool hasAutomationCaret = false;
            bool automationRejectsText = false;
            NativeMethods.Rect automationCaret = default;
            NativeMethods.Rect automationField = default;
            if (!hasCaret || settings.Anchor == LayoutIndicatorAnchor.Field)
                hasAutomationCaret = TryGetAutomationRects(out automationCaret, out automationField,
                    out hasAutomationField, out automationRejectsText,
                    settings.InputMode == LayoutIndicatorInputMode.TextInputOnly);
            if (hasAutomationCaret)
            {
                caretRect = automationCaret;
                hasCaret = true;
            }
            NativeMethods.Rect fallbackField = default;
            if (!hasCaret && !hasAutomationField &&
                (!automationRejectsText || NativeMethods.IsStandaloneConsoleWindow(foreground)))
            {
                nint fallbackWindow = hasGuiInfo && info.FocusWindow != 0 ? info.FocusWindow : foreground;
                bool hasFallback = NativeMethods.GetWindowRect(fallbackWindow, out fallbackField) &&
                    fallbackField.Width >= 40 && fallbackField.Height >= 14;
                if (!hasFallback && fallbackWindow != foreground)
                    hasFallback = NativeMethods.GetWindowRect(foreground, out fallbackField) &&
                        fallbackField.Width >= 40 && fallbackField.Height >= 14;
                if (hasFallback)
                    hasAutomationField = true;
            }
            if (!hasCaret && !hasAutomationField)
            {
                QueueHide();
                return;
            }

            NativeMethods.Rect anchorRect = hasCaret ? caretRect :
                automationField.Width > 0 ? automationField : fallbackField;
            if (settings.Anchor == LayoutIndicatorAnchor.Field)
            {
                if (hasAutomationField)
                    anchorRect = automationField.Width > 0 ? automationField : fallbackField;
                else if (hasGuiInfo && info.FocusWindow != 0 && info.FocusWindow != foreground &&
                    NativeMethods.GetWindowRect(info.FocusWindow, out var focusRect) &&
                    focusRect.Width >= 40 && focusRect.Height >= 14)
                    anchorRect = focusRect;
            }

            uint dpi = NativeMethods.GetDpiForWindow(foreground);
            double scale = dpi == 0 ? 1 : dpi / 96d;
            double size = Math.Clamp(settings.Size, 10, 80);
            double widthFactor = settings.Content == LayoutIndicatorContent.FlagOnly ? 1.45 : 1.75;
            int width = (int)Math.Ceiling(size * widthFactor * scale);
            int height = (int)Math.Ceiling(size * scale);
            LayoutIndicatorHorizontalSide defaultSide = IsWebApplication(processId)
                ? settings.WebSide : settings.DefaultSide;
            (int x, int y) = Position(anchorRect, width, height, scale, settings, defaultSide);
            if (!hasCaret && settings.Anchor == LayoutIndicatorAnchor.Caret &&
                NativeMethods.GetWindowRect(foreground, out var foregroundRect))
            {
                x = Math.Clamp(x, foregroundRect.Left, Math.Max(foregroundRect.Left, foregroundRect.Right - width));
                y = Math.Clamp(y, foregroundRect.Top, Math.Max(foregroundRect.Top, foregroundRect.Bottom - height));
            }

            (string language, string region) = GetLayout(foreground);
            if (foreground != NativeMethods.GetForegroundWindow()) return;
            QueueUpdate(new PendingUpdate(region, language, settings, x, y, scale));
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void QueueUpdate(PendingUpdate update)
    {
        _pendingUpdate = update;
        if (Interlocked.Exchange(ref _renderQueued, 1) != 0) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            PendingUpdate? latest = _pendingUpdate;
            if (!_disposed && _settings.Enabled && latest is not null)
                _overlay.Update(latest.Region, latest.Language, latest.Settings,
                    latest.X, latest.Y, latest.Scale);
        });
    }

    private void QueueHide() => _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
    {
        if (!_disposed) _overlay.HideImmediately();
    });

    private static (int X, int Y) Position(NativeMethods.Rect anchor, int width, int height, double scale,
        LayoutIndicatorSettings settings, LayoutIndicatorHorizontalSide horizontalSide)
    {
        int gap = (int)Math.Ceiling(6 * scale);
        int offsetX = (int)Math.Round(Math.Clamp(settings.OffsetX, -100, 100) * scale);
        int offsetY = (int)Math.Round(Math.Clamp(settings.OffsetY, -100, 100) * scale);
        int centerX = anchor.Left + (anchor.Width - width) / 2;
        int centerY = anchor.Top + (anchor.Height - height) / 2;
        LayoutIndicatorSide defaultSide = horizontalSide == LayoutIndicatorHorizontalSide.Left
            ? LayoutIndicatorSide.Left : LayoutIndicatorSide.Right;
        LayoutIndicatorSide side = settings.Side ?? defaultSide;
        return side switch
        {
            LayoutIndicatorSide.Top => (centerX + offsetX, anchor.Top - height - gap + offsetY),
            LayoutIndicatorSide.Bottom => (centerX + offsetX, anchor.Bottom + gap + offsetY),
            LayoutIndicatorSide.Left => (anchor.Left - width - gap + offsetX, centerY + offsetY),
            _ => (anchor.Right + gap + offsetX, centerY + offsetY)
        };
    }

    private static bool IsWebApplication(uint processId)
    {
        try
        {
            string name = Process.GetProcessById((int)processId).ProcessName;
            return name.Equals("chrome", StringComparison.OrdinalIgnoreCase)
                || name.Equals("msedge", StringComparison.OrdinalIgnoreCase)
                || name.Equals("firefox", StringComparison.OrdinalIgnoreCase)
                || name.Equals("brave", StringComparison.OrdinalIgnoreCase)
                || name.Equals("opera", StringComparison.OrdinalIgnoreCase)
                || name.Equals("vivaldi", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool TryGetAutomationRects(out NativeMethods.Rect caretRect,
        out NativeMethods.Rect fieldRect, out bool hasField, out bool rejectsText, bool textOnly)
    {
        caretRect = default;
        fieldRect = default;
        hasField = false;
        rejectsText = false;
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            bool acceptsText = focused.Current.IsEnabled && focused.Current.ControlType == ControlType.Edit;
            if (acceptsText && focused.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                acceptsText = !((ValuePattern)valuePattern).Current.IsReadOnly;
            rejectsText = textOnly && !acceptsText;
            if (rejectsText) return false;

            System.Windows.Rect fieldBounds = focused.Current.BoundingRectangle;
            if (!fieldBounds.IsEmpty)
            {
                fieldRect = ToNativeRect(fieldBounds);
                hasField = fieldRect.Width >= 1 && fieldRect.Height >= 1;
            }

            AutomationElement? textElement = focused;
            for (int depth = 0; depth < 8 && textElement is not null; depth++)
            {
                if (textElement.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject) &&
                    TryGetTextCaret((TextPattern)patternObject, out caretRect))
                    return true;
                textElement = TreeWalker.RawViewWalker.GetParent(textElement);
            }
            return false;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetTextCaret(TextPattern pattern, out NativeMethods.Rect caretRect)
    {
        caretRect = default;
        TextPatternRange[] selection = pattern.GetSelection();
        if (selection.Length == 0) return false;

        TextPatternRange range = selection[^1];
        System.Windows.Rect[] bounds = range.GetBoundingRectangles();
        bool caretAtLeft = true;
        if (bounds.Length < 4)
        {
            range = range.Clone();
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) == 0)
            {
                caretAtLeft = false;
                if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) == 0)
                    return false;
            }
            bounds = range.GetBoundingRectangles();
        }
        if (bounds.Length < 4) return false;

        System.Windows.Rect boundsRect = bounds[^1];
        double x = caretAtLeft ? boundsRect.Left : boundsRect.Right;
        caretRect = new NativeMethods.Rect
        {
            Left = (int)Math.Round(x),
            Top = (int)Math.Round(boundsRect.Top),
            Right = (int)Math.Round(x) + 1,
            Bottom = (int)Math.Round(boundsRect.Bottom)
        };
        return true;
    }

    private sealed record PendingUpdate(string Region, string Language,
        LayoutIndicatorSettings Settings, int X, int Y, double Scale);

    private static NativeMethods.Rect ToNativeRect(System.Windows.Rect rect) => new()
    {
        Left = (int)Math.Round(rect.Left),
        Top = (int)Math.Round(rect.Top),
        Right = (int)Math.Round(rect.Right),
        Bottom = (int)Math.Round(rect.Bottom)
    };

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

    private static (string Language, string Region) GetLayout(nint foreground)
    {
        int languageId = NativeMethods.GetKeyboardLanguageId(foreground);
        string language;
        try { language = CultureInfo.GetCultureInfo(languageId).TwoLetterISOLanguageName; }
        catch { language = "??"; }

        string region = language switch
        {
            "ru" => "RU",
            "uk" => "UA",
            "be" => "BY",
            "en" => "US",
            "de" => "DE",
            "fr" => "FR",
            "es" => "ES",
            "it" => "IT",
            "pl" => "PL",
            "cs" => "CZ",
            "tr" => "TR",
            "pt" => "PT",
            "ja" => "JP",
            "ko" => "KR",
            "zh" => "CN",
            _ => string.Empty
        };
        return (language, region);
    }

    public void Dispose()
    {
        _disposed = true;
        _moveTimer.Dispose();
        _refreshTimer.Dispose();
        foreach (nint hook in _hooks)
            if (hook != 0) NativeMethods.UnhookWinEvent(hook);
        _overlay.Close();
    }
}
