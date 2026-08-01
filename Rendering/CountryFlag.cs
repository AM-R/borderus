using System.Windows;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace Borderus.Rendering;

public sealed class CountryFlag : FrameworkElement
{
    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region), typeof(string), typeof(CountryFlag),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Region
    {
        get => (string)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
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
            dc.DrawEllipse(MediaBrushes.Crimson, null, new System.Windows.Point(rect.Width / 2, rect.Height / 2),
                rect.Height * 0.27, rect.Height * 0.27);
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
        ("RU", 0) => MediaColors.White,
        ("RU", 1) => MediaColor.FromRgb(0, 57, 166),
        ("RU", 2) => MediaColor.FromRgb(213, 43, 30),
        ("UA", 0) => MediaColor.FromRgb(0, 87, 184),
        ("UA", 1) => MediaColor.FromRgb(255, 215, 0),
        ("DE", 0) => MediaColors.Black,
        ("DE", 1) => MediaColor.FromRgb(221, 0, 0),
        ("DE", 2) => MediaColor.FromRgb(255, 206, 0),
        ("FR", 0) => MediaColor.FromRgb(0, 35, 149),
        ("FR", 1) => MediaColors.White,
        ("FR", 2) => MediaColor.FromRgb(237, 41, 57),
        ("IT", 0) => MediaColor.FromRgb(0, 146, 70),
        ("IT", 1) => MediaColors.White,
        ("IT", 2) => MediaColor.FromRgb(206, 43, 55),
        ("PL", 0) => MediaColors.White,
        ("PL", 1) => MediaColor.FromRgb(220, 20, 60),
        ("BY", 0) => MediaColor.FromRgb(206, 23, 32),
        ("BY", 1) => MediaColor.FromRgb(206, 23, 32),
        ("BY", 2) => MediaColor.FromRgb(0, 124, 48),
        ("JP", 0) => MediaColors.White,
        ("US", 0) => MediaColor.FromRgb(178, 34, 52),
        _ => MediaColor.FromRgb(55, 70, 82)
    };
}
