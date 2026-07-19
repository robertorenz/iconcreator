using System.IO;
using System.Text.Json;

namespace IconCreator.IO;

/// <summary>Persisted most-recently-used file list (newest first, capped).</summary>
public static class RecentFiles
{
    public const int MaxItems = 20;

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IconCreator", "recent.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<string>();
            var json = File.ReadAllText(StorePath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Promote <paramref name="path"/> to the top, de-duplicate, and trim.</summary>
    public static List<string> Add(string path)
    {
        var full = Path.GetFullPath(path);
        var list = Load();
        list.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, full);
        if (list.Count > MaxItems) list = list.GetRange(0, MaxItems);
        Save(list);
        return list;
    }

    public static List<string> Remove(string path)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save(list);
        return list;
    }

    public static void Clear() => Save(new List<string>());

    private static void Save(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list));
        }
        catch { /* recent list is best-effort */ }
    }
}
