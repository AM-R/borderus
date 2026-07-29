using System.Text.Json.Serialization;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace Borderus.Models;

public enum BorderLineStyle { Solid, Dashed, Dotted }
public enum BorderFillStyle { Solid, FadeEdges, AlongLine }
public enum AnimationDirection { Clockwise, CounterClockwise }
public enum LayoutIndicatorPlacement { Floating, Inside }
public enum LayoutIndicatorContent { FlagOnly, FlagAndCode }
public enum LayoutIndicatorAnchor { Field, Caret }
public enum LayoutIndicatorSide { Top, Right, Bottom, Left }

public sealed class LayoutIndicatorSettings
{
    public bool Enabled { get; set; }
    public double Size { get; set; } = 32;
    public double Opacity { get; set; } = 0.9;
    public LayoutIndicatorPlacement Placement { get; set; } = LayoutIndicatorPlacement.Floating;
    public LayoutIndicatorContent Content { get; set; } = LayoutIndicatorContent.FlagAndCode;
    public bool ShowContainer { get; set; } = true;
    public LayoutIndicatorAnchor Anchor { get; set; } = LayoutIndicatorAnchor.Field;
    public LayoutIndicatorSide Side { get; set; } = LayoutIndicatorSide.Right;
    public double OffsetX { get; set; } = 8;
    public double OffsetY { get; set; }

    public LayoutIndicatorSettings Copy() => (LayoutIndicatorSettings)MemberwiseClone();
}

public sealed class BorderProfile
{
    public double Thickness { get; set; } = 1;
    public string Color { get; set; } = "#FF0078D7";
    public string SecondaryColor { get; set; } = "#FF00B7C3";
    public bool UseElevatedColor { get; set; }
    public string ElevatedColor { get; set; } = "#FFFFB900";
    public BorderLineStyle LineStyle { get; set; } = BorderLineStyle.Solid;
    public BorderFillStyle FillStyle { get; set; } = BorderFillStyle.Solid;
    public bool Animate { get; set; }
    public bool AnimateGradient { get; set; }
    public AnimationDirection Direction { get; set; } = AnimationDirection.Clockwise;
    public double AnimationSpeed { get; set; } = 1;
    public double CornerRadius { get; set; } = 4;
    public bool ShowTop { get; set; } = true;
    public bool ShowRight { get; set; } = true;
    public bool ShowBottom { get; set; } = true;
    public bool ShowLeft { get; set; } = true;

    [JsonIgnore]
    public MediaColor ParsedColor => BorderSettings.ParseColor(Color, Colors.DodgerBlue);

    [JsonIgnore]
    public MediaColor ParsedSecondaryColor => BorderSettings.ParseColor(SecondaryColor, Colors.DeepSkyBlue);

    [JsonIgnore]
    public MediaColor ParsedElevatedColor => BorderSettings.ParseColor(ElevatedColor, Colors.Orange);

    public BorderProfile Copy() => (BorderProfile)MemberwiseClone();
}

public sealed class BorderSettings
{
    public bool Enabled { get; set; } = true;
    public BorderProfile Active { get; set; } = new();
    public BorderProfile Inactive { get; set; } = new()
    {
        Color = "#FF808080",
        SecondaryColor = "#FFB0B0B0",
        ElevatedColor = "#FFFF8C00"
    };
    public LayoutIndicatorSettings LayoutIndicator { get; set; } = new();

    public BorderSettings Copy() => new()
    {
        Enabled = Enabled,
        Active = Active.Copy(),
        Inactive = Inactive.Copy(),
        LayoutIndicator = LayoutIndicator.Copy()
    };

    public static MediaColor ParseColor(string? value, MediaColor fallback)
    {
        try { return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!; }
        catch { return fallback; }
    }
}
