using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Borderus.Models;
using Borderus.Native;

namespace Borderus.Rendering;

internal sealed class BorderOverlay : Window
{
    private readonly BorderRenderer _renderer = new();
    private nint _target;
    private nint _handle;
    private bool _shown;

    public BorderOverlay(nint target = default)
    {
        _target = target;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _renderer;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        long style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new nint(style));
    }

    public void ShowPrepared()
    {
        if (!_shown)
        {
            Show();
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlHwndParent, 0);
            _shown = true;
            return;
        }

        NativeMethods.ShowWindow(_handle, NativeMethods.SwShowNoActivate);
    }

    public void SetTarget(nint target) => _target = target;

    public void HideImmediately() => NativeMethods.ShowWindow(_handle, NativeMethods.SwHide);

    public void Position(NativeMethods.Rect rect, double padding)
    {
        if (_handle == 0) return;
        int pad = (int)Math.Ceiling(padding);
        nint windowAboveTarget = NativeMethods.GetWindow(_target, NativeMethods.GwHwndPrev);
        uint flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder;
        if (windowAboveTarget == _handle) flags |= NativeMethods.SwpNoZOrder;
        NativeMethods.SetWindowPos(_handle, windowAboveTarget,
            rect.Left - pad, rect.Top - pad, rect.Width + pad * 2, rect.Height + pad * 2,
            flags);
    }

    public void Render(BorderSettings settings, double dashOffset, bool active, bool elevated) =>
        _renderer.Update(settings, dashOffset, active, elevated);
}
