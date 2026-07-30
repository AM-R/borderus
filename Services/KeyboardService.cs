using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using Borderus.Models;
using Borderus.Native;

namespace Borderus.Services;

internal sealed class KeyboardService : IDisposable
{
    private const nuint SyntheticMarker = 0x424F5244;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private readonly Dictionary<KeySound, SoundPlayer> _players = new();
    private readonly object _repeatLock = new();
    private CancellationTokenSource? _repeatCancellation;
    private KeyboardSettings _settings;
    private nint _hook;
    private int _heldKey;
    private bool _disposed;

    public KeyboardService(BorderSettings settings)
    {
        _settings = settings.Keyboard.Copy();
        _keyboardCallback = OnKeyboardEvent;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, 0, 0);
    }

    public void Apply(BorderSettings settings) => _settings = settings.Keyboard.Copy();

    public void Preview(KeySound sound)
    {
        if (sound != KeySound.None) Play(sound);
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        if (_disposed || code < 0) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        NativeMethods.LowLevelKeyboardInput input = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(data);
        if ((input.Flags & NativeMethods.LlkhfInjected) != 0 && input.ExtraInfo == SyntheticMarker)
            return NativeMethods.CallNextHookEx(_hook, code, message, data);

        int key = (int)input.VirtualKey;
        bool keyDown = message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown;
        bool keyUp = message == NativeMethods.WmKeyUp || message == NativeMethods.WmSysKeyUp;
        if (IsCharacterKey(key) && keyDown)
        {
            if (Interlocked.CompareExchange(ref _heldKey, key, 0) == 0)
            {
                PlayCurrentLayoutSound();
                StartRepeat(key);
            }
            // Suppress Windows' own repeats. The first physical key-down still reaches the target.
            else if (Volatile.Read(ref _heldKey) == key)
                return 1;
        }
        else if (keyUp && Interlocked.CompareExchange(ref _heldKey, 0, key) == key)
            StopRepeat();
        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    private void StartRepeat(int key)
    {
        StopRepeat();
        var cancellation = new CancellationTokenSource();
        lock (_repeatLock) _repeatCancellation = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Math.Clamp(_settings.RepeatDelayMs, 10, 1000), cancellation.Token);
                while (!cancellation.IsCancellationRequested && Volatile.Read(ref _heldKey) == key)
                {
                    NativeMethods.keybd_event((byte)key, 0, 0, SyntheticMarker);
                    NativeMethods.keybd_event((byte)key, 0, NativeMethods.KeyeventfKeyup, SyntheticMarker);
                    PlayCurrentLayoutSound();
                    await Task.Delay(Math.Clamp(_settings.RepeatIntervalMs, 5, 250), cancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StopRepeat()
    {
        CancellationTokenSource? cancellation;
        lock (_repeatLock)
        {
            cancellation = _repeatCancellation;
            _repeatCancellation = null;
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void PlayCurrentLayoutSound()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        int language = (int)(NativeMethods.GetKeyboardLayout(threadId).ToInt64() & 0xffff);
        Play(language == 0x0419 ? _settings.RussianSound : _settings.EnglishSound);
    }

    private void Play(KeySound sound)
    {
        switch (sound)
        {
            case KeySound.None: return;
            case KeySound.SystemAsterisk: SystemSounds.Asterisk.Play(); return;
            case KeySound.SystemBeep: SystemSounds.Beep.Play(); return;
            case KeySound.SystemExclamation: SystemSounds.Exclamation.Play(); return;
            case KeySound.SystemHand: SystemSounds.Hand.Play(); return;
        }
        SoundPlayer player = GetPlayer(sound);
        player.Stop();
        player.Play();
    }

    private SoundPlayer GetPlayer(KeySound sound)
    {
        if (_players.TryGetValue(sound, out SoundPlayer? player)) return player;
        player = new SoundPlayer(new MemoryStream(CreateWave(sound)));
        player.Load();
        _players.Add(sound, player);
        return player;
    }

    private static bool IsCharacterKey(int key) =>
        key is >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xE2 || key == 0x20;

    private static byte[] CreateWave(KeySound sound)
    {
        (double frequency, int duration, double volume) = sound switch
        {
            KeySound.Click => (1250, 28, 0.65),
            KeySound.Mechanical => (760, 42, 0.75),
            _ => (980, 25, 0.50)
        };
        const int sampleRate = 22050;
        int samples = sampleRate * duration / 1000;
        using var stream = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray()); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8.ToArray()); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double envelope = Math.Pow(1d - i / (double)samples, 3);
            double sample = Math.Sin(2 * Math.PI * frequency * i / sampleRate) * envelope * volume;
            writer.Write((short)(sample * short.MaxValue));
        }
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRepeat();
        if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
        foreach (SoundPlayer player in _players.Values) player.Dispose();
        _players.Clear();
    }
}
