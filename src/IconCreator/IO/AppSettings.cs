using System.IO;
using System.Text.Json;
using IconCreator.Localization;

namespace IconCreator.IO;

/// <summary>Small persisted app preferences (currently just the UI language).</summary>
public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.English;

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IconCreator", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath)) ?? new AppSettings();
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(this));
        }
        catch { /* best effort */ }
    }
}
