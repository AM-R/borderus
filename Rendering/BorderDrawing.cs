using System.Windows;
using System.Windows.Media;
using Borderus.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaPen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Borderus.Rendering;

internal static class BorderDrawing
{
    public static void Draw(DrawingContext dc, double width, double height, BorderProfile profile,
        double dashOffset, bool elevated)
    {
        if (width < 2 || height < 2) return;

        double thickness = Math.Clamp(profile.Thickness, 1, 30);
        double inset = thickness / 2;
        var rect = new Rect(inset, inset, Math.Max(0, width - thickness), Math.Max(0, height - thickness));
        double radius = Math.Clamp(profile.CornerRadius, 0, 40);
        bool useElevatedColor = elevated && profile.UseElevatedColor;

        if (profile.FillStyle == BorderFillStyle.FadeEdges)
        {
            DrawFadeEdges(dc, rect, radius, thickness, profile, dashOffset, useElevatedColor);
            return;
        }

        var brush = CreateBrush(rect, profile, dashOffset, useElevatedColor);
        DrawOutline(dc, CreatePen(brush, thickness, profile, dashOffset), rect, radius, profile);
    }

    private static void DrawFadeEdges(DrawingContext dc, Rect rect, double radius, double thickness,
        BorderProfile profile, double dashOffset, bool useElevatedColor)
    {
        int bands = Math.Max(1, (int)Math.Ceiling(thickness));
        for (int i = 0; i < bands; i++)
        {
            double opacity = bands == 1 ? 1 : (double)i / (bands - 1);
            var baseColor = GetBaseColor(profile, useElevatedColor);
            byte alpha = (byte)(baseColor.A * Math.Pow(opacity, 1.35));
            var color = baseColor;
            color.A = alpha;
            var bandRect = new Rect(rect.Left - thickness / 2 + i + 0.5,
                                    rect.Top - thickness / 2 + i + 0.5,
                                    Math.Max(0, rect.Width + thickness - 2 * i - 1),
                                    Math.Max(0, rect.Height + thickness - 2 * i - 1));
            DrawOutline(dc, CreatePen(new SolidColorBrush(color), 1, profile, dashOffset), bandRect,
                        Math.Max(0, radius + thickness / 2 - i), profile);
        }
    }

    private static void DrawOutline(DrawingContext dc, MediaPen pen, Rect rect, double radius,
        BorderProfile profile)
    {
        if (profile.ShowTop && profile.ShowRight && profile.ShowBottom && profile.ShowLeft)
        {
            dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
            return;
        }

        double leftInset = profile.ShowLeft ? radius : 0;
        double rightInset = profile.ShowRight ? radius : 0;
        double topInset = profile.ShowTop ? radius : 0;
        double bottomInset = profile.ShowBottom ? radius : 0;
        if (profile.ShowTop)
            dc.DrawLine(pen, new Point(rect.Left + leftInset, rect.Top), new Point(rect.Right - rightInset, rect.Top));
        if (profile.ShowRight)
            dc.DrawLine(pen, new Point(rect.Right, rect.Top + topInset), new Point(rect.Right, rect.Bottom - bottomInset));
        if (profile.ShowBottom)
            dc.DrawLine(pen, new Point(rect.Right - rightInset, rect.Bottom), new Point(rect.Left + leftInset, rect.Bottom));
        if (profile.ShowLeft)
            dc.DrawLine(pen, new Point(rect.Left, rect.Bottom - bottomInset), new Point(rect.Left, rect.Top + topInset));
        if (radius <= 0) return;
        if (profile.ShowTop && profile.ShowLeft)
            DrawArc(dc, pen, new Point(rect.Left, rect.Top + radius), new Point(rect.Left + radius, rect.Top), radius);
        if (profile.ShowTop && profile.ShowRight)
            DrawArc(dc, pen, new Point(rect.Right - radius, rect.Top), new Point(rect.Right, rect.Top + radius), radius);
        if (profile.ShowBottom && profile.ShowRight)
            DrawArc(dc, pen, new Point(rect.Right, rect.Bottom - radius), new Point(rect.Right - radius, rect.Bottom), radius);
        if (profile.ShowBottom && profile.ShowLeft)
            DrawArc(dc, pen, new Point(rect.Left + radius, rect.Bottom), new Point(rect.Left, rect.Bottom - radius), radius);
    }

    private static void DrawArc(DrawingContext dc, MediaPen pen, Point start, Point end, double radius)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static MediaBrush CreateBrush(Rect rect, BorderProfile profile, double dashOffset,
        bool useElevatedColor)
    {
        if (profile.FillStyle == BorderFillStyle.Solid)
            return new SolidColorBrush(GetBaseColor(profile, useElevatedColor));

        double phase = profile.AnimateGradient ? dashOffset * 0.08 : 0;
        double dx = Math.Cos(phase) * 0.7;
        double dy = Math.Sin(phase) * 0.7;
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new(GetBaseColor(profile, useElevatedColor), 0),
                new(profile.ParsedSecondaryColor, 0.5),
                new(GetBaseColor(profile, useElevatedColor), 1)
            },
            new Point(0.5 - dx, 0.5 - dy),
            new Point(0.5 + dx, 0.5 + dy));
    }

    private static System.Windows.Media.Color GetBaseColor(BorderProfile profile, bool useElevatedColor) =>
        useElevatedColor ? profile.ParsedElevatedColor : profile.ParsedColor;

    private static MediaPen CreatePen(MediaBrush brush, double thickness, BorderProfile profile,
        double dashOffset)
    {
        brush.Freeze();
        var pen = new MediaPen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            DashCap = profile.LineStyle == BorderLineStyle.Dotted ? PenLineCap.Round : PenLineCap.Flat
        };

        pen.DashStyle = profile.LineStyle switch
        {
            BorderLineStyle.Dashed => new DashStyle(new[] { 3d, 2d }, dashOffset),
            BorderLineStyle.Dotted => new DashStyle(new[] { 0.05d, 1.8d }, dashOffset),
            _ => DashStyles.Solid
        };
        return pen;
    }
}
