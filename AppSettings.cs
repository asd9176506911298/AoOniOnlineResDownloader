using AoOniOnlineResDownloader;
using System.Text.Json;

public static class AppSettings
{
    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoOniOnlineResDownloader",
        "settings.json");

    public static AppLanguage Language { get; set; } = AppLanguage.Japanese;

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            string json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Language", out var el) &&
                el.ValueKind == JsonValueKind.String &&
                Enum.TryParse<AppLanguage>(el.GetString(), true, out var lang))
            {
                Language = lang;
            }
        }
        catch
        {
            // ignore corrupt / missing settings
        }
    }

    public static void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(new { Language = Language.ToString() },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignore write errors
        }
    }
}