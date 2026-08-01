using System.Collections.Concurrent;
using Borderus.Native;

namespace Borderus.Rendering;

/// <summary>
/// Pools BorderOverlay instances to limit memory usage from one WPF Window per tracked application window.
/// Maintains a pre-allocated working set and gracefully degrades by falling through to new allocations,
/// bounded by a configurable maximum to prevent unbounded memory growth.
/// </summary>
internal sealed class OverlayPool : IDisposable
{
    private readonly ConcurrentBag<BorderOverlay> _available = new();
    private int _inUse;
    private readonly int _maxSize;
    private bool _disposed;

    public OverlayPool(int maxSize = 128)
    {
        _maxSize = maxSize;
        Preallocate(Math.Min(maxSize, 16));
    }

    public BorderOverlay? Borrow(nint target)
    {
        if (_disposed) return null;
        if (_available.TryTake(out var overlay))
        {
            _inUse++;
            overlay.SetTarget(target);
            return overlay;
        }

        if (_inUse >= _maxSize) return null;
        _inUse++;
        return new BorderOverlay(target);
    }

    public void Return(BorderOverlay overlay)
    {
        if (_disposed || overlay is null) return;
        _inUse--;
        overlay.HideImmediately();
        _available.Add(overlay);
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var overlay in _available) overlay.Close();
        _available.Clear();
    }

    private void Preallocate(int count)
    {
        for (int i = 0; i < count; i++)
            _available.Add(new BorderOverlay());
    }
}
