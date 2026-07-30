using Borderus.Models;
using Borderus.Native;

namespace Borderus.Services;

internal sealed class KeyboardBacklightService : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private BacklightSettings _settings;
    private bool _disposed;

    public KeyboardBacklightService(BorderSettings settings)
    {
        _settings = settings.Backlight.Copy();
        _timer = new System.Threading.Timer(_ => Trigger(), null, Timeout.Infinite, Timeout.Infinite);
        Schedule();
    }

    public void Apply(BorderSettings settings)
    {
        _settings = settings.Backlight.Copy();
        Schedule();
    }

    public void Test(BacklightKeepAliveMethod method) => Trigger(method);

    private void Schedule()
    {
        if (_disposed || !_settings.Enabled)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        int interval = Math.Clamp(_settings.IntervalSeconds, 5, 120) * 1000;
        _timer.Change(interval, interval);
    }

    private void Trigger() => Trigger(_settings.Method);

    private static void Trigger(BacklightKeepAliveMethod method)
    {
        byte key = method switch
        {
            BacklightKeepAliveMethod.Shift => 0xA0,
            BacklightKeepAliveMethod.ScrollLock => 0x91,
            _ => 0x7E
        };
        Tap(key);
        // Restore Scroll Lock immediately so the user's lock state is unchanged.
        if (method == BacklightKeepAliveMethod.ScrollLock) Tap(key);
    }

    private static void Tap(byte key)
    {
        NativeMethods.keybd_event(key, 0, 0, 0);
        NativeMethods.keybd_event(key, 0, NativeMethods.KeyeventfKeyup, 0);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
