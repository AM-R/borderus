using System.IO;
using System.Text.Json;
using Borderus.Models;

namespace Borderus.Services;

internal static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");

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
