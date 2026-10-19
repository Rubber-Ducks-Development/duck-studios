using System.IO;
using System.Text;
using System.Text.Json;

namespace DuckStudio;

public static class PreviewDiagnostics
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Events = new();
    public static void Record(string message)
    {
        lock (Gate)
        {
            Events.Enqueue(DateTime.UtcNow.ToString("O") + " " + Redact(message.Length > 12000 ? message[..12000] + " [truncated]" : message));
            while (Events.Count > 40) Events.Dequeue();
        }
    }
    public static string Begin(ClothingAsset asset, string? texture, string mode)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        Record($"[{id}] Preview requested ({mode})\nItem: {asset.Name}; PED: {asset.Ped}; slot: {asset.Slot}\nModel: {asset.ModelPath}\nSelected YTD: {texture ?? "none"}\nLinked image: {(texture != null && asset.TextureSources.TryGetValue(texture, out var link) ? link.SourcePath + "; diffuse: " + link.TextureName : "none")}");
        return id;
    }
    public static void Complete(string id, string scene, string rendererOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(scene, "scene.json")));
            var meshes = document.RootElement.GetProperty("meshes");
            string Value(JsonElement mesh, string name) => mesh.TryGetProperty(name, out var value) ? value.ToString() : "unreported";
            var bindings = meshes.EnumerateArray().Select(m => $"slot={Value(m,"slot")}; shader diffuse={Value(m,"shader_diffuse")}; resolved={Value(m,"texture_name")}; origin={Value(m,"texture_origin")}; image={Value(m,"texture")}").Distinct().Take(80);
            Record($"[{id}] Loaded {meshes.GetArrayLength()} meshes\n" + string.Join("\n", bindings) + "\nRenderer: " + rendererOutput);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { Record($"[{id}] Could not read scene diagnostics: {ex.Message}"); }
    }
    public static string Report(string? projectPath, ClothingAsset? asset, int index)
    {
        var report = new StringBuilder("Duck Studio preview debug report\n");
        report.AppendLine("Version: " + ApplicationUpdates.CurrentVersion).AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
        report.AppendLine("OS: " + Environment.OSVersion).AppendLine("Project: " + (projectPath ?? "Unsaved / imported project"));
        report.AppendLine("Renderer: " + Describe(Path.Combine(AppContext.BaseDirectory, "rage.exe")));
        if (asset != null)
        {
            report.AppendLine($"Selected item: {asset.Name}; PED: {asset.Ped}; slot: {asset.Slot}; variation index: {index}");
            report.AppendLine("Model: " + Describe(asset.ModelPath));
            for (int i = 0; i < asset.TexturePaths.Count; i++)
            {
                var path = asset.TexturePaths[i];
                report.AppendLine($"{(i == index ? "> SELECTED" : " ")} {(char)('a' + i)}: {Describe(path)}");
                if (asset.TextureSources.TryGetValue(path, out var link)) report.AppendLine("  Artwork: " + Describe(link.SourcePath) + "; diffuse: " + link.TextureName);
            }
        }
        else report.AppendLine("No clothing selected.");
        report.AppendLine().AppendLine("Recent preview and import events (current session, newest at bottom):");
        lock (Gate) foreach (var entry in Events) report.AppendLine(entry);
        return Redact(report.ToString());
    }
    private static string Describe(string path)
    {
        try { var file = new FileInfo(path); return file.Exists ? $"{path}; {file.Length} bytes; modified {file.LastWriteTimeUtc:O}" : path + "; MISSING"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return path + "; " + ex.Message; }
    }
    private static string Redact(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? value : value.Replace(profile, "<USERPROFILE>", StringComparison.OrdinalIgnoreCase);
    }
}
