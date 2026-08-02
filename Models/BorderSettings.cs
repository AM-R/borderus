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
public enum LayoutIndicatorHorizontalSide { Right, Left }
public enum LayoutIndicatorInputMode { Everywhere, TextInputOnly }
public enum LayoutIndicatorSide { Top, Right, Bottom, Left }
public enum KeySound { None, Soft, Click, Mechanical, SystemAsterisk, SystemBeep, SystemExclamation, SystemHand, Custom }
public sealed class KeyboardSettings
{
    public bool RepeatEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public int RepeatDelayMs { get; set; } = 200;
    public int RepeatIntervalMs { get; set; } = 20;
    public int NonCharacterRepeatDelayMs { get; set; } = 200;
    public int NonCharacterRepeatIntervalMs { get; set; } = 20;
    public KeySound RussianSound { get; set; }
    public KeySound EnglishSound { get; set; }
    public string RussianSoundFile { get; set; } = string.Empty;
    public string EnglishSoundFile { get; set; } = string.Empty;

    public KeyboardSettings Copy() => new()
    {
        RepeatEnabled = RepeatEnabled,
        SoundEnabled = SoundEnabled,
        RepeatDelayMs = RepeatDelayMs,
        RepeatIntervalMs = RepeatIntervalMs,
        NonCharacterRepeatDelayMs = NonCharacterRepeatDelayMs,
        NonCharacterRepeatIntervalMs = NonCharacterRepeatIntervalMs,
        RussianSound = RussianSound,
        EnglishSound = EnglishSound,
        RussianSoundFile = RussianSoundFile,
        EnglishSoundFile = EnglishSoundFile
    };
}

public sealed class LayoutIndicatorSettings
{
    public bool Enabled { get; set; } = true;
    public double Size { get; set; } = 20;
    public double Opacity { get; set; } = 0.75;
    public LayoutIndicatorPlacement Placement { get; set; } = LayoutIndicatorPlacement.Floating;
    public LayoutIndicatorContent Content { get; set; } = LayoutIndicatorContent.FlagOnly;
    public LayoutIndicatorInputMode InputMode { get; set; } = LayoutIndicatorInputMode.TextInputOnly;
    public bool ShowContainer { get; set; }
    public LayoutIndicatorAnchor Anchor { get; set; } = LayoutIndicatorAnchor.Caret;
    public LayoutIndicatorHorizontalSide DefaultSide { get; set; } = LayoutIndicatorHorizontalSide.Right;
    public LayoutIndicatorHorizontalSide WebSide { get; set; } = LayoutIndicatorHorizontalSide.Right;
    public LayoutIndicatorSide? Side { get; set; }
    public double OffsetX { get; set; } = 8;
    public double OffsetY { get; set; }

    public LayoutIndicatorSettings Copy() => new()
    {
        Enabled = Enabled,
        Size = Size,
        Opacity = Opacity,
        Placement = Placement,
        Content = Content,
        InputMode = InputMode,
        ShowContainer = ShowContainer,
        Anchor = Anchor,
        DefaultSide = DefaultSide,
        WebSide = WebSide,
        Side = Side,
        OffsetX = OffsetX,
        OffsetY = OffsetY
    };
}

public sealed class BorderProfile
{
    public double Thickness { get; set; } = 1;
    public double Padding { get; set; } = -1;
    public string Color { get; set; } = "#0078D7";
    public string SecondaryColor { get; set; } = "#FF00B7C3";
    public bool UseElevatedColor { get; set; } = true;
    public string ElevatedColor { get; set; } = "#FF0000";
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

    public BorderProfile Copy() => new()
    {
        Thickness = Thickness,
        Padding = Padding,
        Color = Color,
        SecondaryColor = SecondaryColor,
        UseElevatedColor = UseElevatedColor,
        ElevatedColor = ElevatedColor,
        LineStyle = LineStyle,
        FillStyle = FillStyle,
        Animate = Animate,
        AnimateGradient = AnimateGradient,
        Direction = Direction,
        AnimationSpeed = AnimationSpeed,
        CornerRadius = CornerRadius,
        ShowTop = ShowTop,
        ShowRight = ShowRight,
        ShowBottom = ShowBottom,
        ShowLeft = ShowLeft
    };
}

public sealed class BorderSettings
{
    public bool Enabled { get; set; } = true;
    public bool BordersEnabled { get; set; } = true;
    public bool ShowInFullscreen { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public string Language { get; set; } = "ru";
    public BorderProfile Active { get; set; } = new();
    public BorderProfile Inactive { get; set; } = new()
    {
        Color = "#808080",
        SecondaryColor = "#FFB0B0B0",
        ElevatedColor = "#EE8300"
    };
    public LayoutIndicatorSettings LayoutIndicator { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();

    public BorderSettings Copy() => new()
    {
        Enabled = Enabled,
        BordersEnabled = BordersEnabled,
        ShowInFullscreen = ShowInFullscreen,
        StartWithWindows = StartWithWindows,
        Language = Language,
        Active = Active.Copy(),
        Inactive = Inactive.Copy(),
        LayoutIndicator = LayoutIndicator.Copy(),
        Keyboard = Keyboard.Copy()
    };

    public static MediaColor ParseColor(string? value, MediaColor fallback)
    {
        try { return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!; }
        catch { return fallback; }
    }
}
