using System.IO;

namespace DuckStudio;

public sealed class TextureLiveReload
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (long Ticks, long Length)> loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Ticks, long Length)> pending = new(StringComparer.OrdinalIgnoreCase);
    public void Remember(string destination, string source)
    {
        var info = new FileInfo(source);
        loaded[destination] = (info.LastWriteTimeUtc.Ticks, info.Length);
        pending.Remove(destination);
    }
    public async Task RefreshAsync(ClothingProject project, string core, Action<ClothingAsset, string?> notify, bool force = false)
    {
        await gate.WaitAsync();
        try
        {
            // Snapshot bindings on the UI thread; only the decoder runs off-thread.
            var bindings = project.Items.SelectMany(a => a.TextureSources.Where(p => a.TexturePaths.Contains(p.Key)).Select(p => (Asset: a, Destination: p.Key, Link: p.Value))).ToList();
            var destinations = bindings.Select(b => b.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in loaded.Keys.Where(k => !destinations.Contains(k)).ToArray()) { loaded.Remove(stale); pending.Remove(stale); }
            var errors = new List<string>();
            foreach (var binding in bindings)
            {
                var source = binding.Link.SourcePath;
                try
                {
                    var info = new FileInfo(source);
                    if (!info.Exists) throw new FileNotFoundException("Linked image is missing: " + source);
                    var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
                    if (!force && loaded.TryGetValue(binding.Destination, out var prior) && prior == stamp && File.Exists(binding.Destination)) continue;
                    // Editors often write in several steps or atomically replace a file.
                    // Wait for two stable samples; a failed read retries on the next tick.
                    if (!force && loaded.ContainsKey(binding.Destination) && (!pending.TryGetValue(binding.Destination, out var observed) || observed != stamp)) { pending[binding.Destination] = stamp; continue; }
                    var name = await Task.Run(() => TextureImport.Convert(source, binding.Destination, core, binding.Asset.ModelPath, binding.Destination, binding.Link.TextureName, binding.Link.MaxDimension, binding.Link.CompressDiffuse));
                    if (!binding.Asset.TextureSources.TryGetValue(binding.Destination, out var current) || !ReferenceEquals(current, binding.Link)) continue;
                    binding.Link.TextureName = name;
                    loaded[binding.Destination] = stamp; pending.Remove(binding.Destination);
                    binding.Asset.NotifyTexturesChanged(); notify(binding.Asset, null);
                }
                catch (Exception ex)
                {
                    var message = "Could not reload " + Path.GetFileName(source) + ": " + (ex.InnerException ?? ex).Message;
                    errors.Add(message); notify(binding.Asset, message);
                }
            }
            if (force && errors.Count > 0) throw new IOException(string.Join("\n", errors));
        }
        finally { gate.Release(); }
    }
}
