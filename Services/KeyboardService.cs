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
    private readonly Dictionary<string, SoundPlayer> _filePlayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _pressedKeys = new();
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

    public void Apply(BorderSettings settings)
    {
        _settings = settings.Keyboard.Copy();
        if (_settings.RepeatEnabled) return;
        Interlocked.Exchange(ref _heldKey, 0);
        StopRepeat();
    }

    public void Preview(KeySound sound, string? customFile = null)
    {
        if (_settings.SoundEnabled && sound != KeySound.None) Play(sound, customFile);
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        if (_disposed || code < 0) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        NativeMethods.LowLevelKeyboardInput input = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(data);
        if (input.ExtraInfo == SyntheticMarker)
            return NativeMethods.CallNextHookEx(_hook, code, message, data);

        int key = (int)input.VirtualKey;
        bool keyDown = message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown;
        bool keyUp = message == NativeMethods.WmKeyUp || message == NativeMethods.WmSysKeyUp;
        bool firstKeyDown = keyDown && _pressedKeys.Add(key);
        if (keyUp) _pressedKeys.Remove(key);
        if (firstKeyDown) TrackStandaloneConsoleLayoutSwitch(key);

        if (_settings.RepeatEnabled && IsRepeatableKey(key) && keyDown && !IsSystemShortcut())
        {
            if (Interlocked.CompareExchange(ref _heldKey, key, 0) == 0)
            {
                if (IsCharacterKey(key)) PlayCurrentLayoutSound();
                StartRepeat(key);
            }
            // Suppress Windows' own repeats. The first physical key-down still reaches the target.
            else if (Volatile.Read(ref _heldKey) == key)
                return 1;
        }
        else if (_settings.RepeatEnabled && keyUp && Interlocked.CompareExchange(ref _heldKey, 0, key) == key)
            StopRepeat();
        else if (!_settings.RepeatEnabled && firstKeyDown && IsCharacterKey(key))
            PlayCurrentLayoutSound();
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
                    if (IsCharacterKey(key)) PlayCurrentLayoutSound();
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
        if (!_settings.SoundEnabled) return;
        nint foreground = NativeMethods.GetForegroundWindow();
        int language = NativeMethods.GetKeyboardLanguageId(foreground);
        bool russian = language == 0x0419;
        Play(russian ? _settings.RussianSound : _settings.EnglishSound,
            russian ? _settings.RussianSoundFile : _settings.EnglishSoundFile);
    }

    private void Play(KeySound sound, string? customFile = null)
    {
        switch (sound)
        {
            case KeySound.None: return;
            case KeySound.SystemAsterisk: SystemSounds.Asterisk.Play(); return;
            case KeySound.SystemBeep: SystemSounds.Beep.Play(); return;
            case KeySound.SystemExclamation: SystemSounds.Exclamation.Play(); return;
            case KeySound.SystemHand: SystemSounds.Hand.Play(); return;
            case KeySound.Custom:
                if (!string.IsNullOrWhiteSpace(customFile) && File.Exists(customFile))
                {
                    try
                    {
                        if (!_filePlayers.TryGetValue(customFile, out SoundPlayer? customPlayer))
                        {
                            customPlayer = new SoundPlayer(customFile);
                            customPlayer.Load();
                            _filePlayers.Add(customFile, customPlayer);
                        }
                        customPlayer.Stop();
                        customPlayer.Play();
                    }
                    catch (InvalidOperationException) { }
                    catch (IOException) { }
                }
                return;
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

    private static bool IsRepeatableKey(int key) => IsCharacterKey(key) ||
        key is 0x08 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2E;

    private void TrackStandaloneConsoleLayoutSwitch(int key)
    {
        bool shift = IsTrackedPressed(0x10, 0xA0, 0xA1);
        bool control = IsTrackedPressed(0x11, 0xA2, 0xA3);
        bool alt = IsTrackedPressed(0x12, 0xA4, 0xA5);
        bool windows = _pressedKeys.Contains(0x5B) || _pressedKeys.Contains(0x5C);
        bool winSpace = key == 0x20 && windows;
        bool modifierSwitch = shift && (control || alt) &&
            (IsKey(key, 0x10, 0xA0, 0xA1) || IsKey(key, 0x11, 0xA2, 0xA3) ||
             IsKey(key, 0x12, 0xA4, 0xA5));
        if (!winSpace && !modifierSwitch) return;

        NativeMethods.CycleStandaloneConsoleKeyboardLayout(
            NativeMethods.GetForegroundWindow(), winSpace && shift);
    }

    private bool IsTrackedPressed(int generic, int left, int right) =>
        _pressedKeys.Contains(generic) || _pressedKeys.Contains(left) || _pressedKeys.Contains(right);

    private static bool IsKey(int key, int generic, int left, int right) =>
        key == generic || key == left || key == right;

    private static bool IsSystemShortcut() =>
        IsPressed(0x11) || IsPressed(0x12) || IsPressed(0x5B) || IsPressed(0x5C);

    private static bool IsPressed(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

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
        foreach (SoundPlayer player in _filePlayers.Values) player.Dispose();
        _players.Clear();
        _filePlayers.Clear();
    }
}
