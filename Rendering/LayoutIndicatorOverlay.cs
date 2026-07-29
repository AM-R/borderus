using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Borderus.Native;
using Borderus.Models;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace Borderus.Rendering;

internal sealed class LayoutIndicatorOverlay : Window
{
    private readonly TextBlock _label;
    private readonly CountryFlag _flag;
    private readonly Border _container;
    private readonly nint _handle;
    private bool _shown;
    private bool _visible;
    private string _visualKey = string.Empty;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastWidth;
    private int _lastHeight;

    public LayoutIndicatorOverlay()
    {
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _flag = new CountryFlag { Margin = new Thickness(5, 0, 3, 0) };
        _label = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        content.Children.Add(_flag);
        content.Children.Add(_label);
        _container = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(232, 24, 24, 24)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = content
        };
        Content = _container;

        _handle = new WindowInteropHelper(this).EnsureHandle();
        long style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new nint(style));
    }

    public void Update(string region, string language, LayoutIndicatorSettings settings, int x, int y, double dpiScale)
    {
        double logicalSize = Math.Clamp(settings.Size, 10, 80);
        bool showCode = settings.Content == LayoutIndicatorContent.FlagAndCode;
        double logicalWidth = logicalSize * (showCode ? 1.75 : 1.45);
        string visualKey = $"{region}|{language}|{logicalSize}|{settings.Opacity}|{showCode}|{settings.ShowContainer}";
        if (_visualKey != visualKey)
        {
            _visualKey = visualKey;
            Width = logicalWidth;
            Height = logicalSize;
            Opacity = Math.Clamp(settings.Opacity, 0.2, 1);
            _label.FontSize = Math.Max(7, logicalSize * 0.38);
            _label.Text = language.ToUpperInvariant();
            _label.Visibility = showCode ? Visibility.Visible : Visibility.Collapsed;
            _flag.Width = logicalSize * 0.72;
            _flag.Height = logicalSize * 0.46;
            _flag.Margin = settings.ShowContainer ? new Thickness(5, 0, showCode ? 3 : 5, 0) : new Thickness(0);
            _flag.Region = region;
            _container.Background = settings.ShowContainer
                ? new SolidColorBrush(MediaColor.FromArgb(232, 24, 24, 24))
                : MediaBrushes.Transparent;
            _container.BorderBrush = settings.ShowContainer
                ? new SolidColorBrush(MediaColor.FromRgb(0, 120, 215))
                : MediaBrushes.Transparent;
            _container.BorderThickness = settings.ShowContainer ? new Thickness(1) : new Thickness(0);
        }

        if (!_shown)
        {
            Show();
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlHwndParent, 0);
            _shown = true;
        }

        int width = (int)Math.Ceiling(logicalWidth * dpiScale);
        int height = (int)Math.Ceiling(logicalSize * dpiScale);
        if (_visible && Math.Abs(x - _lastX) <= 1) x = _lastX;
        if (_visible && Math.Abs(y - _lastY) <= 1) y = _lastY;
        if (!_visible || x != _lastX || y != _lastY || width != _lastWidth || height != _lastHeight)
        {
            NativeMethods.SetWindowPos(_handle, new nint(-1), x, y, width, height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            _lastX = x;
            _lastY = y;
            _lastWidth = width;
            _lastHeight = height;
            _visible = true;
        }
    }

    public void HideImmediately()
    {
        if (_shown && _visible)
        {
            NativeMethods.ShowWindow(_handle, NativeMethods.SwHide);
            _visible = false;
        }
    }

    private sealed class CountryFlag : FrameworkElement
    {
        private string _region = string.Empty;
        public string Region
        {
            get => _region;
            set { _region = value; InvalidateVisual(); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(new SolidColorBrush(ColorFor(Region, 0)), null, rect);
            if (Region is "RU" or "DE" or "BY")
            {
                double h = rect.Height / 3;
                dc.DrawRectangle(new SolidColorBrush(ColorFor(Region, 1)), null, new Rect(0, h, rect.Width, h));
                dc.DrawRectangle(new SolidColorBrush(ColorFor(Region, 2)), null, new Rect(0, h * 2, rect.Width, h));
            }
            else if (Region is "UA" or "PL")
            {
                dc.DrawRectangle(new SolidColorBrush(ColorFor(Region, 1)), null,
                    new Rect(0, rect.Height / 2, rect.Width, rect.Height / 2));
            }
            else if (Region is "FR" or "IT")
            {
                double w = rect.Width / 3;
                dc.DrawRectangle(new SolidColorBrush(ColorFor(Region, 1)), null, new Rect(w, 0, w, rect.Height));
                dc.DrawRectangle(new SolidColorBrush(ColorFor(Region, 2)), null, new Rect(w * 2, 0, w, rect.Height));
            }
            else if (Region == "JP")
            {
                dc.DrawEllipse(MediaBrushes.Crimson, null, new System.Windows.Point(rect.Width / 2, rect.Height / 2), rect.Height * 0.27, rect.Height * 0.27);
            }
            else if (Region == "US")
            {
                double stripe = rect.Height / 7;
                for (int i = 1; i < 7; i += 2)
                    dc.DrawRectangle(MediaBrushes.White, null, new Rect(0, i * stripe, rect.Width, stripe));
                dc.DrawRectangle(MediaBrushes.MidnightBlue, null, new Rect(0, 0, rect.Width * 0.45, rect.Height * 0.55));
            }
            dc.DrawRectangle(null, new System.Windows.Media.Pen(new SolidColorBrush(MediaColor.FromArgb(100, 255, 255, 255)), 1), rect);
        }

        private static MediaColor ColorFor(string region, int part) => (region, part) switch
        {
            ("RU", 0) => MediaColors.White, ("RU", 1) => MediaColor.FromRgb(0, 57, 166), ("RU", 2) => MediaColor.FromRgb(213, 43, 30),
            ("UA", 0) => MediaColor.FromRgb(0, 87, 184), ("UA", 1) => MediaColor.FromRgb(255, 215, 0),
            ("DE", 0) => MediaColors.Black, ("DE", 1) => MediaColor.FromRgb(221, 0, 0), ("DE", 2) => MediaColor.FromRgb(255, 206, 0),
            ("FR", 0) => MediaColor.FromRgb(0, 35, 149), ("FR", 1) => MediaColors.White, ("FR", 2) => MediaColor.FromRgb(237, 41, 57),
            ("IT", 0) => MediaColor.FromRgb(0, 146, 70), ("IT", 1) => MediaColors.White, ("IT", 2) => MediaColor.FromRgb(206, 43, 55),
            ("PL", 0) => MediaColors.White, ("PL", 1) => MediaColor.FromRgb(220, 20, 60),
            ("BY", 0) => MediaColor.FromRgb(206, 23, 32), ("BY", 1) => MediaColor.FromRgb(206, 23, 32), ("BY", 2) => MediaColor.FromRgb(0, 124, 48),
            ("JP", 0) => MediaColors.White,
            ("US", 0) => MediaColor.FromRgb(178, 34, 52),
            _ => MediaColor.FromRgb(55, 70, 82)
        };
    }
}
