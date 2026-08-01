using System.Windows;
using System.Windows.Media;
using Borderus.Models;

namespace Borderus.Rendering;

internal sealed class BorderRenderer : FrameworkElement
{
    private BorderProfile _profile = new();
    private double _dashOffset;
    private bool _elevated;

    public void Update(BorderSettings settings, double dashOffset, bool active, bool elevated)
    {
        _profile = active ? settings.Active : settings.Inactive;
        _dashOffset = dashOffset;
        _elevated = elevated;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BorderDrawing.Draw(dc, ActualWidth, ActualHeight, _profile, _dashOffset, _elevated);
    }
}
