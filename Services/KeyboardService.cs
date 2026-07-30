using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using Borderus.Models;
using Borderus.Native;

namespace Borderus.Services;

internal sealed class KeyboardService : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private readonly Dictionary<KeySound, SoundPlayer> _players = new();
    private KeyboardSettings _settings;
    private nint _hook;
    private int _systemRepeatDelay;
    private bool _repeatDelayChanged;
    private bool _disposed;

    public KeyboardService(BorderSettings settings)
    {
        _settings = settings.Keyboard.Copy();
        _keyboardCallback = OnKeyboardEvent;
        int delay = 0;
        if (NativeMethods.SystemParametersInfo(NativeMethods.SpiGetKeyboardDelay, 0, ref delay, 0))
            _systemRepeatDelay = delay;
        ApplyRepeatDelay();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, 0, 0);
    }

    public void Apply(BorderSettings settings)
    {
        KeyboardRepeatDelay previous = _settings.RepeatDelay;
        _settings = settings.Keyboard.Copy();
        if (previous != _settings.RepeatDelay) ApplyRepeatDelay();
    }

    private void ApplyRepeatDelay()
    {
        int value = _settings.RepeatDelay switch
        {
            KeyboardRepeatDelay.Short => 0,
            KeyboardRepeatDelay.Medium => 1,
            KeyboardRepeatDelay.Long => 2,
            _ => _systemRepeatDelay
        };
        if (_settings.RepeatDelay == KeyboardRepeatDelay.System && !_repeatDelayChanged) return;
        NativeMethods.SystemParametersInfo(NativeMethods.SpiSetKeyboardDelay, (uint)value, ref value,
            NativeMethods.SpifUpdateIniFile | NativeMethods.SpifSendChange);
        _repeatDelayChanged = _settings.RepeatDelay != KeyboardRepeatDelay.System;
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        if (!_disposed && code >= 0 && (message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown))
        {
            int virtualKey = Marshal.ReadInt32(data);
            if (IsCharacterKey(virtualKey)) PlayCurrentLayoutSound();
        }
        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    private void PlayCurrentLayoutSound()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        int language = (int)(NativeMethods.GetKeyboardLayout(threadId).ToInt64() & 0xffff);
        KeySound sound = language == 0x0419 ? _settings.RussianSound : _settings.EnglishSound;
        if (sound == KeySound.None) return;
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
            KeySound.Click => (1250, 22, 0.20),
            KeySound.Mechanical => (760, 34, 0.24),
            _ => (980, 18, 0.13)
        };
        const int sampleRate = 22050;
        int samples = sampleRate * duration / 1000;
        using var stream = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(samples * 2);
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
        if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
        if (_repeatDelayChanged)
        {
            int value = _systemRepeatDelay;
            NativeMethods.SystemParametersInfo(NativeMethods.SpiSetKeyboardDelay, (uint)value, ref value,
                NativeMethods.SpifUpdateIniFile | NativeMethods.SpifSendChange);
        }
        foreach (SoundPlayer player in _players.Values) player.Dispose();
        _players.Clear();
    }
}
