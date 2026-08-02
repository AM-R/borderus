using System.IO;
using System.Text.Json;
using Borderus.Models;

namespace Borderus.Services;

internal static class SettingsStore
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Borderus");
    private static readonly string FilePath = Path.Combine(AppDataDir, "settings.json");

    static SettingsStore()
    {
        Directory.CreateDirectory(AppDataDir);
    }

    public static BorderSettings Load()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            BorderSettings settings = JsonSerializer.Deserialize<BorderSettings>(json) ?? new();
            settings.Keyboard ??= new KeyboardSettings();
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(BorderSettings.BordersEnabled), out _))
                settings.BordersEnabled = settings.Enabled;
            if (!document.RootElement.TryGetProperty(nameof(BorderSettings.StartWithWindows), out _))
                settings.StartWithWindows = StartupService.IsEnabled();
            if (document.RootElement.TryGetProperty(nameof(BorderSettings.Keyboard), out JsonElement keyboard) &&
                keyboard.ValueKind == JsonValueKind.Object)
            {
                if (!keyboard.TryGetProperty(nameof(KeyboardSettings.NonCharacterRepeatDelayMs), out _))
                    settings.Keyboard.NonCharacterRepeatDelayMs = settings.Keyboard.RepeatDelayMs;
                if (!keyboard.TryGetProperty(nameof(KeyboardSettings.NonCharacterRepeatIntervalMs), out _))
                    settings.Keyboard.NonCharacterRepeatIntervalMs = settings.Keyboard.RepeatIntervalMs;
            }
            return settings;
        }
        catch
        {
            return new();
        }
    }

    public static void Save(BorderSettings settings)
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
