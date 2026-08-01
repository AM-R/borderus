using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Borderus.Models;
using Borderus.Native;

namespace Borderus.Rendering;

internal sealed class BorderOverlay : NativeWindow, IDisposable
{
    private nint _target;
    private bool _shown;
    private bool _positioned;
    private int _lastX;
    private int _lastY;
    private int _lastWidth;
    private int _lastHeight;
    private BorderProfile _profile = new();
    private double _dashOffset;
    private bool _elevated;
    private bool _visualDirty = true;

    public BorderOverlay(nint target = default)
    {
        _target = target;
        CreateHandle(new CreateParams
        {
            Caption = string.Empty,
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
            Style = unchecked((int)0x80000000),
            ExStyle = (int)(NativeMethods.WsExLayered | NativeMethods.WsExTransparent |
                NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate)
        });
    }

    public void ShowPrepared()
    {
        NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        _shown = true;
    }

    public void SetTarget(nint target)
    {
        _target = target;
        _positioned = false;
        _visualDirty = true;
    }

    public void HideImmediately()
    {
        if (_shown) NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
        _shown = false;
        _positioned = false;
    }

    public void Position(NativeMethods.Rect rect, double padding)
    {
        if (Handle == 0) return;
        int pad = (int)Math.Ceiling(padding);
        int x = rect.Left - pad;
        int y = rect.Top - pad;
        int width = rect.Width + pad * 2;
        int height = rect.Height + pad * 2;
        if (width < 2 || height < 2) return;

        nint windowAboveTarget = NativeMethods.GetWindow(_target, NativeMethods.GwHwndPrev);
        bool sizeChanged = !_positioned || width != _lastWidth || height != _lastHeight;
        if (sizeChanged || _visualDirty)
        {
            if (!Present(x, y, width, height)) return;
            _visualDirty = false;
            if (!_shown) ShowPrepared();
        }
        else if (x != _lastX || y != _lastY)
        {
            uint flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder |
                NativeMethods.SwpNoSize | NativeMethods.SwpNoCopyBits;
            if (windowAboveTarget == Handle) flags |= NativeMethods.SwpNoZOrder;
            if (!NativeMethods.SetWindowPos(Handle, windowAboveTarget, x, y, 0, 0, flags)) return;
        }
        else if (windowAboveTarget != Handle)
        {
            uint flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder |
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize;
            if (!NativeMethods.SetWindowPos(Handle, windowAboveTarget, 0, 0, 0, 0, flags)) return;
        }
        else
        {
            return;
        }

        _lastX = x;
        _lastY = y;
        _lastWidth = width;
        _lastHeight = height;
        _positioned = true;
    }

    public void Render(BorderSettings settings, double dashOffset, bool active, bool elevated)
    {
        _profile = (active ? settings.Active : settings.Inactive).Copy();
        _dashOffset = dashOffset;
        _elevated = elevated;
        _visualDirty = true;
        if (_positioned) Present(_lastX, _lastY, _lastWidth, _lastHeight);
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (Handle != 0) DestroyHandle();
    }

    private bool Present(int x, int y, int width, int height)
    {
        uint dpi = NativeMethods.GetDpiForWindow(_target);
        double scale = dpi == 0 ? 1 : dpi / 96d;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
            BorderDrawing.Draw(dc, width / scale, height / scale, _profile, _dashOffset, _elevated);

        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var bitmapInfo = new NativeMethods.BitmapInfo
        {
            Header = new NativeMethods.BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BiRgb,
                SizeImage = (uint)pixels.Length
            }
        };
        nint sourceDc = NativeMethods.CreateCompatibleDC(0);
        if (sourceDc == 0) return false;
        nint bitmapHandle = NativeMethods.CreateDIBSection(sourceDc, ref bitmapInfo,
            NativeMethods.DibRgbColors, out nint bits, 0, 0);
        if (bitmapHandle == 0 || sourceDc == 0 || bits == 0)
        {
            if (bitmapHandle != 0) NativeMethods.DeleteObject(bitmapHandle);
            if (sourceDc != 0) NativeMethods.DeleteDC(sourceDc);
            return false;
        }
        Marshal.Copy(pixels, 0, bits, pixels.Length);

        nint oldBitmap = NativeMethods.SelectObject(sourceDc, bitmapHandle);
        try
        {
            var destination = new NativeMethods.NativePoint { X = x, Y = y };
            var size = new NativeMethods.NativeSize { Width = width, Height = height };
            var origin = new NativeMethods.NativePoint();
            var blend = new NativeMethods.BlendFunction
            {
                Operation = NativeMethods.AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AcSrcAlpha
            };
            if (!NativeMethods.UpdateLayeredWindow(Handle, 0, ref destination, ref size,
                sourceDc, ref origin, 0, ref blend, NativeMethods.UlwAlpha)) return false;
        }
        finally
        {
            NativeMethods.SelectObject(sourceDc, oldBitmap);
            NativeMethods.DeleteObject(bitmapHandle);
            NativeMethods.DeleteDC(sourceDc);
        }

        nint windowAboveTarget = NativeMethods.GetWindow(_target, NativeMethods.GwHwndPrev);
        if (windowAboveTarget != Handle)
            NativeMethods.SetWindowPos(Handle, windowAboveTarget, 0, 0, 0, 0,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder |
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize);
        return true;
    }
}
