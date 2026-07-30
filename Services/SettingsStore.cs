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
            return JsonSerializer.Deserialize<BorderSettings>(File.ReadAllText(FilePath)) ?? new();
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
