using System.Windows;
using System.Windows.Media;
using Borderus.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaPen = System.Windows.Media.Pen;

namespace Borderus.Rendering;

internal sealed class BorderRenderer : FrameworkElement
{
    private BorderProfile _profile = new();
    private double _dashOffset;
    private bool _useElevatedColor;

    public void Update(BorderSettings settings, double dashOffset, bool active, bool elevated)
    {
        _profile = active ? settings.Active : settings.Inactive;
        _dashOffset = dashOffset;
        _useElevatedColor = elevated && _profile.UseElevatedColor;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 2 || ActualHeight < 2) return;

        double thickness = Math.Clamp(_profile.Thickness, 1, 30);
        double inset = thickness / 2;
        var rect = new Rect(inset, inset, Math.Max(0, ActualWidth - thickness), Math.Max(0, ActualHeight - thickness));
        double radius = Math.Clamp(_profile.CornerRadius, 0, 40);

        if (_profile.FillStyle == BorderFillStyle.FadeEdges)
        {
            DrawFadeEdges(dc, rect, radius, thickness);
            return;
        }

        var brush = CreateBrush(rect);
        var pen = CreatePen(brush, thickness);
        DrawOutline(dc, pen, rect, radius);
    }

    private void DrawFadeEdges(DrawingContext dc, Rect rect, double radius, double thickness)
    {
        int bands = Math.Max(1, (int)Math.Ceiling(thickness));
        for (int i = 0; i < bands; i++)
        {
            double opacity = bands == 1 ? 1 : (double)i / (bands - 1);
            var baseColor = GetBaseColor();
            byte alpha = (byte)(baseColor.A * Math.Pow(opacity, 1.35));
            var color = baseColor;
            color.A = alpha;
            var bandRect = new Rect(rect.Left - thickness / 2 + i + 0.5,
                                    rect.Top - thickness / 2 + i + 0.5,
                                    Math.Max(0, rect.Width + thickness - 2 * i - 1),
                                    Math.Max(0, rect.Height + thickness - 2 * i - 1));
            DrawOutline(dc, CreatePen(new SolidColorBrush(color), 1), bandRect,
                        Math.Max(0, radius + thickness / 2 - i));
        }
    }

    private void DrawOutline(DrawingContext dc, MediaPen pen, Rect rect, double radius)
    {
        if (_profile.ShowTop && _profile.ShowRight && _profile.ShowBottom && _profile.ShowLeft)
        {
            dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
            return;
        }

        double leftInset = _profile.ShowLeft ? radius : 0;
        double rightInset = _profile.ShowRight ? radius : 0;
        double topInset = _profile.ShowTop ? radius : 0;
        double bottomInset = _profile.ShowBottom ? radius : 0;
        if (_profile.ShowTop)
            dc.DrawLine(pen, new System.Windows.Point(rect.Left + leftInset, rect.Top), new System.Windows.Point(rect.Right - rightInset, rect.Top));
        if (_profile.ShowRight)
            dc.DrawLine(pen, new System.Windows.Point(rect.Right, rect.Top + topInset), new System.Windows.Point(rect.Right, rect.Bottom - bottomInset));
        if (_profile.ShowBottom)
            dc.DrawLine(pen, new System.Windows.Point(rect.Right - rightInset, rect.Bottom), new System.Windows.Point(rect.Left + leftInset, rect.Bottom));
        if (_profile.ShowLeft)
            dc.DrawLine(pen, new System.Windows.Point(rect.Left, rect.Bottom - bottomInset), new System.Windows.Point(rect.Left, rect.Top + topInset));
        if (radius <= 0) return;
        if (_profile.ShowTop && _profile.ShowLeft)
            DrawArc(dc, pen, new System.Windows.Point(rect.Left, rect.Top + radius), new System.Windows.Point(rect.Left + radius, rect.Top), radius);
        if (_profile.ShowTop && _profile.ShowRight)
            DrawArc(dc, pen, new System.Windows.Point(rect.Right - radius, rect.Top), new System.Windows.Point(rect.Right, rect.Top + radius), radius);
        if (_profile.ShowBottom && _profile.ShowRight)
            DrawArc(dc, pen, new System.Windows.Point(rect.Right, rect.Bottom - radius), new System.Windows.Point(rect.Right - radius, rect.Bottom), radius);
        if (_profile.ShowBottom && _profile.ShowLeft)
            DrawArc(dc, pen, new System.Windows.Point(rect.Left + radius, rect.Bottom), new System.Windows.Point(rect.Left, rect.Bottom - radius), radius);
    }

    private static void DrawArc(DrawingContext dc, MediaPen pen, System.Windows.Point start, System.Windows.Point end, double radius)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private MediaBrush CreateBrush(Rect rect)
    {
        if (_profile.FillStyle == BorderFillStyle.Solid)
            return new SolidColorBrush(GetBaseColor());

        double phase = _profile.AnimateGradient ? _dashOffset * 0.08 : 0;
        double dx = Math.Cos(phase) * 0.7;
        double dy = Math.Sin(phase) * 0.7;
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new(GetBaseColor(), 0),
                new(_profile.ParsedSecondaryColor, 0.5),
                new(GetBaseColor(), 1)
            },
            new System.Windows.Point(0.5 - dx, 0.5 - dy),
            new System.Windows.Point(0.5 + dx, 0.5 + dy));
    }

    private System.Windows.Media.Color GetBaseColor() => _useElevatedColor ? _profile.ParsedElevatedColor : _profile.ParsedColor;

    private MediaPen CreatePen(MediaBrush brush, double thickness)
    {
        brush.Freeze();
        var pen = new MediaPen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            DashCap = _profile.LineStyle == BorderLineStyle.Dotted ? PenLineCap.Round : PenLineCap.Flat
        };

        pen.DashStyle = _profile.LineStyle switch
        {
            BorderLineStyle.Dashed => new DashStyle(new[] { 3d, 2d }, _dashOffset),
            BorderLineStyle.Dotted => new DashStyle(new[] { 0.05d, 1.8d }, _dashOffset),
            _ => DashStyles.Solid
        };
        return pen;
    }
}
