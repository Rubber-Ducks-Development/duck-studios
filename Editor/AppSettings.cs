using System.IO;
using System.Text.Json;

namespace DuckStudio;

public sealed class AppSettings
{
    public string GtaFolderPath { get; set; } = "";
    public List<string> RecentProjects { get; set; } = [];
    public bool CheckUpdatesAutomatically { get; set; } = true;
    public string UpdateFeedUrl { get; set; } = "";
    public string SkippedUpdateVersion { get; set; } = "";

    private static string SettingsPath
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeftStudio");
            return Path.Combine(root, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }

    public void RememberProject(string path)
    {
        RecentProjects.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, path);
        if (RecentProjects.Count > 10) RecentProjects.RemoveRange(10, RecentProjects.Count - 10);
        Save();
    }
}

