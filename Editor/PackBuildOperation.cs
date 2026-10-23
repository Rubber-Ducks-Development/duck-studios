using System.IO;
using System.IO.Compression;

namespace DuckStudio;

public sealed record PackBuildResult(string Folder, string? Zip);

public static class PackBuildOperation
{
    // Clone on the caller's UI thread; all disk work belongs to the worker.
    public static Task<PackBuildResult> RunAsync(ClothingProject project, string parent,
        PackBuilder.BuildOptions options, bool createZip, IProgress<string>? progress = null,CancellationToken cancellation=default)
    {
        var snapshot = ProjectAssets.Clone(project);
        return Task.Run(() => Run(snapshot, parent, options, createZip, progress,cancellation),cancellation);
    }

    private static PackBuildResult Run(ClothingProject snapshot, string parent,
        PackBuilder.BuildOptions options, bool createZip, IProgress<string>? progress,CancellationToken cancellation)
    {
        var inputs = Path.Combine(Path.GetTempPath(), "DuckStudio-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(inputs);
            progress?.Report("Preparing independent build textures...");
            var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Independent(string path)
            {
                cancellation.ThrowIfCancellationRequested();
                if (generated.TryGetValue(path, out var existing)) return existing;
                var copy = Path.Combine(inputs, generated.Count.ToString("D4"), Path.GetFileName(path));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                if (File.Exists(path)) File.Copy(path, copy);
                generated[path] = copy;
                return copy;
            }
            foreach (var item in snapshot.Items)
            {
                var links = item.TextureSources.Where(pair => item.TexturePaths.Contains(pair.Key)).ToArray();
                foreach (var (path, link) in links)
                {
                    var copy = Independent(path);
                    for (var i = 0; i < item.TexturePaths.Count; i++)
                        if (item.TexturePaths[i] == path) item.TexturePaths[i] = copy;
                    if (item.TextureLabels.Remove(path, out var label)) item.TextureLabels[copy] = label;
                    item.TextureSources.Remove(path);
                    item.TextureSources[copy] = link;
                }
            }
            foreach (var tattoo in snapshot.Tattoos)
                tattoo.TexturePath = string.IsNullOrWhiteSpace(tattoo.TexturePath)
                    ? Path.Combine(inputs, "tattoo-" + Guid.NewGuid().ToString("N") + ".ytd") : Independent(tattoo.TexturePath);

            var folder = PackBuilder.Build(snapshot, parent, options, progress,cancellation,isolateValidation:true);
            string? zip = null;
            if (createZip)
            {
                progress?.Report("Compressing the project ZIP...");
                zip = Path.Combine(parent, snapshot.Collection + "-project.zip");
                if (File.Exists(zip)) zip = Path.Combine(parent, snapshot.Collection + "-project-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
                var temporary = Path.Combine(Path.GetFullPath(parent), ".duckstudio-zip-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                    {
                        var buffer = new byte[81920];
                        foreach (var file in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var relative = Path.GetRelativePath(folder,file).Replace('\\','/');
                            progress?.Report("Compressing: " + relative);
                            var entry = archive.CreateEntry(relative,CompressionLevel.Optimal);
                            using var input = File.OpenRead(file); using var output = entry.Open();
                            int read; while ((read=input.Read(buffer,0,buffer.Length))>0)
                            { cancellation.ThrowIfCancellationRequested(); output.Write(buffer,0,read); }
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(temporary, zip);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return new(folder, zip);
        }
        finally { if (Directory.Exists(inputs)) Directory.Delete(inputs, true); }
    }
}
