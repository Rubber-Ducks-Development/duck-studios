using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckStudio;

public sealed class ProjectBackups(string root, int retention = 20)
{
    public string Root { get; } = Path.GetFullPath(root);
    private readonly Dictionary<string, string> fingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Ticks, long Length, string Path)> copied = new(StringComparer.OrdinalIgnoreCase);

    // The caller supplies a detached snapshot, so copying never races UI edits.
    public string? Capture(ClothingProject snapshot, string identity)
    {
        var original = JsonSerializer.Serialize(snapshot, ProjectIO.Json);
        var sources = Sources(snapshot).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stamps = sources.Select(path => File.Exists(path)
            ? $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{new FileInfo(path).Length}"
            : path + "|missing");
        var fingerprint = Digest(original + string.Join('\n', stamps));
        if (fingerprints.GetValueOrDefault(identity) == fingerprint) return null;
        var folder = Path.Combine(Root, Digest(identity)[..20]);
        var projectFolder = Path.Combine(folder, "Project");
        var assetFolder = Path.Combine(folder, "Assets");
        Directory.CreateDirectory(projectFolder); Directory.CreateDirectory(assetFolder);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources.Where(File.Exists))
        {
            var info = new FileInfo(source);
            var cacheKey = folder + "|" + Path.GetFullPath(source);
            if (copied.TryGetValue(cacheKey, out var previous) && previous.Ticks == info.LastWriteTimeUtc.Ticks && previous.Length == info.Length && File.Exists(previous.Path))
            { paths[source] = previous.Path; continue; }
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
            var destination = Path.Combine(assetFolder, hash + Path.GetExtension(source).ToLowerInvariant());
            if (!File.Exists(destination))
            {
                var temp = destination + ".tmp";
                try
                {
                    using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                    File.Move(temp, destination, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            paths[source] = destination;
            copied[cacheKey] = (info.LastWriteTimeUtc.Ticks, info.Length, destination);
        }
        string CopyPath(string path) => paths.GetValueOrDefault(path, path);
        foreach (var item in snapshot.Items)
        {
            item.TextureSources = item.TextureSources.ToDictionary(x => CopyPath(x.Key), x => new TextureSourceLink { SourcePath = CopyPath(x.Value.SourcePath), TextureName = x.Value.TextureName, MaxDimension=x.Value.MaxDimension, CompressDiffuse=x.Value.CompressDiffuse }, StringComparer.OrdinalIgnoreCase);
            item.ModelPath = CopyPath(item.ModelPath);
            item.TexturePaths = item.TexturePaths.Select(CopyPath).ToList();
            item.TextureLabels = item.TextureLabels.ToDictionary(x => CopyPath(x.Key), x => x.Value, StringComparer.OrdinalIgnoreCase);
            item.FirstPersonPath = CopyPath(item.FirstPersonPath); item.PhysicsPath = CopyPath(item.PhysicsPath); item.PreviewPath = CopyPath(item.PreviewPath);
        }
        foreach(var tattoo in snapshot.Tattoos) { tattoo.SourcePath=CopyPath(tattoo.SourcePath); tattoo.TexturePath=CopyPath(tattoo.TexturePath); }
        snapshot.MaleYmtPath = CopyPath(snapshot.MaleYmtPath); snapshot.FemaleYmtPath = CopyPath(snapshot.FemaleYmtPath);
        snapshot.MaleMetaPath = CopyPath(snapshot.MaleMetaPath); snapshot.FemaleMetaPath = CopyPath(snapshot.FemaleMetaPath);
        snapshot.SupplementalYmtPaths = snapshot.SupplementalYmtPaths.Select(CopyPath).ToList();
        var name = string.Concat(snapshot.Collection.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(name)) name = "project";
        var target = Path.Combine(projectFolder, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}{ProjectFiles.Extension}");
        ProjectIO.Save(snapshot, target);
        // Keep the newest 20 recovery points, with shared, immutable assets.
        foreach (var old in ProjectFiles.EnumerateNativeProjects(projectFolder).OrderByDescending(File.GetLastWriteTimeUtc).Skip(retention)) File.Delete(old);
        var referenced = ProjectFiles.EnumerateNativeProjects(projectFolder).SelectMany(path => Sources(ProjectIO.Load(path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var orphan in Directory.GetFiles(assetFolder)) if (!referenced.Contains(orphan)) File.Delete(orphan);
        var missing = sources.Where(path => !File.Exists(path)).ToList();
        File.WriteAllText(Path.Combine(folder, "README.txt"), "Open a .duckproj (or legacy .duck / .weftproj) in Project to restore a recovery point. Assets contains shared backup copies.\nSource project: " + identity
            + (missing.Count == 0 ? "" : "\n\nThese referenced files were already missing when this backup was made:\n" + string.Join('\n', missing)));
        fingerprints[identity] = fingerprint;
        return target;
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static IEnumerable<string> Sources(ClothingProject project)
    {
        foreach (var item in project.Items)
        {
            yield return item.ModelPath; foreach (var texture in item.TexturePaths) yield return texture;
            foreach (var link in item.TextureSources.Values) yield return link.SourcePath;
            yield return item.FirstPersonPath; yield return item.PhysicsPath; yield return item.PreviewPath;
        }
        foreach(var tattoo in project.Tattoos) { yield return tattoo.SourcePath; yield return tattoo.TexturePath; }
        yield return project.MaleYmtPath; yield return project.FemaleYmtPath; yield return project.MaleMetaPath; yield return project.FemaleMetaPath;
        foreach (var path in project.SupplementalYmtPaths) yield return path;
    }
}
