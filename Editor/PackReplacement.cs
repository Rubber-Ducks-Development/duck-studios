using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DuckStudio;

public enum PackMode { Addon, ReplaceDlc, ReplaceCustom }

// A freemode clothing collection shipped in a game DLC pack, read from the
// pack's own YMT names so the collection names always match the local build.
public sealed record DlcCollection(string Pack, string Male, string Female)
{
    public override string ToString() => $"{Pack}  ({string.Join(" / ", new[] { Male, Female }.Where(x => x.Length > 0))})";
}

public static class PackReplacement
{
    public static readonly string[] Labels = ["Add-on (own collection)", "Replace a GTA DLC collection", "Replace a custom collection"];
    private static readonly Regex CollectionName = new("^[a-z0-9_]{2,60}$", RegexOptions.Compiled);
    private static readonly Regex YmtName = new(@"^mp_(?<gender>[mf])_freemode_01_(?<collection>(?!p_)[a-z0-9_]+)\.ymt$", RegexOptions.Compiled);

    public static bool IsReplacement(ClothingProject p) => p.PackMode != PackMode.Addon;

    // The collection this pack's clothing is built into for one PED.
    public static string Collection(ClothingProject p, string ped)
    {
        var female = ped.Contains("mp_f_");
        if (IsReplacement(p)) return female ? p.ReplaceFemaleCollection : p.ReplaceMaleCollection;
        return (female ? "mp_f_" : "mp_m_") + p.Collection;
    }

    public static List<string> Validate(ClothingProject p)
    {
        List<string> issues = [];
        if (!IsReplacement(p)) return issues;
        var peds = p.Items.Select(x => x.Ped).Distinct().ToList();
        foreach (var (ped, gender, name) in new[] { ("mp_m_freemode_01", "male", p.ReplaceMaleCollection), ("mp_f_freemode_01", "female", p.ReplaceFemaleCollection) })
        {
            if (!peds.Contains(ped)) continue;
            if (name.Length == 0) issues.Add($"Choose the {gender} collection to replace, or remove the {gender} clothing from this replacement pack.");
            else if (!CollectionName.IsMatch(name)) issues.Add($"Replaced {gender} collection '{name}' must use 2-60 lowercase letters, digits or underscores.");
            else if (name.StartsWith(gender == "male" ? "mp_f_" : "mp_m_")) issues.Add($"Replaced {gender} collection '{name}' belongs to the other gender.");
        }
        if (p.ReplaceMaleCollection.Length > 0 && p.ReplaceMaleCollection == p.ReplaceFemaleCollection)
            issues.Add("Male and female replaced collections must be different.");
        return issues;
    }

    public static string? TargetIssue(ClothingProject p, BuildTarget target) =>
        IsReplacement(p) && target is BuildTarget.AltV or BuildTarget.SinglePlayer
            ? "Replacement packs currently build for FiveM or raw game assets only. Choose one of those targets, or switch Pack mode back to Add-on."
            : null;

    // Pairs the freemode YMTs found in each pack, e.g. mpheist -> mp_m_heist / mp_f_heist.
    public static List<DlcCollection> Parse(IEnumerable<(string Archive, string Name)> hits)
    {
        var found = new List<(string Pack, string Gender, string Collection)>();
        foreach (var (archive, name) in hits)
        {
            var match = YmtName.Match(name.ToLowerInvariant());
            if (!match.Success) continue;
            var pack = Path.GetFileName(Path.GetDirectoryName(archive.Replace('/', Path.DirectorySeparatorChar)) ?? "").ToLowerInvariant();
            found.Add((pack, match.Groups["gender"].Value, match.Groups["collection"].Value));
        }
        static string Stem(string collection) => Regex.Replace(collection, "^mp_[mf]_", "");
        var result = new List<DlcCollection>();
        // Patch packs can carry updated copies of older collections; list each collection once.
        foreach (var group in found.GroupBy(x => (x.Pack, Stem(x.Collection))).OrderBy(g => g.Key.Pack.StartsWith("patch")).ThenBy(g => g.Key.Pack, StringComparer.Ordinal))
        {
            var male = group.FirstOrDefault(x => x.Gender == "m").Collection ?? "";
            var female = group.FirstOrDefault(x => x.Gender == "f").Collection ?? "";
            if (result.Any(x => x.Male == male && x.Female == female)) continue;
            result.Add(new(group.Key.Pack, male, female));
        }
        return result;
    }

    private static readonly SemaphoreSlim scanGate = new(1, 1);
    private sealed record ScanCache(string Stamp, List<DlcCollection> Collections);

    public static Task<List<DlcCollection>> ScanAsync(string gtaFolder, CancellationToken cancellation = default,
        IProgress<string>? progress = null, bool forceRefresh = false) =>
        Task.Run(() => ScanCoreAsync(gtaFolder, cancellation, progress, forceRefresh), cancellation);

    private static async Task<List<DlcCollection>> ScanCoreAsync(string gtaFolder, CancellationToken cancellation,
        IProgress<string>? progress, bool forceRefresh)
    {
        // A closing dialog must finish stopping its reader before another one starts.
        progress?.Report("Waiting for the previous DLC scan to finish...");
        await scanGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            gtaFolder = Path.GetFullPath(gtaFolder);
            var exe = Path.Combine(gtaFolder, "GTA5.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Link GTA5.exe in Settings to list the installed DLCs.");
            var packs = Path.Combine(gtaFolder, "update", "x64", "dlcpacks");
            if (!Directory.Exists(packs)) throw new DirectoryNotFoundException("No DLC packs found at " + packs);
            var rage = Path.Combine(AppContext.BaseDirectory, "rage.exe");
            if (!File.Exists(rage)) throw new FileNotFoundException("The GTA file reader (rage.exe) is missing from the app folder.");
            progress?.Report("Checking installed DLC archives...");
            var archives = new List<string>();
            var pending = new Stack<string>(); pending.Push(packs);
            var hasLinks = (File.GetAttributes(packs) & FileAttributes.ReparsePoint) != 0;
            while (pending.TryPop(out var directory))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(path);
                    // Do not follow junctions while creating the cache inventory. The general
                    // reader may follow them, so linked installations must bypass this cache.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { hasLinks = true; continue; }
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                    else if (Path.GetExtension(path).Equals(".rpf", StringComparison.OrdinalIgnoreCase)) archives.Add(path);
                }
            }
            archives.Sort(StringComparer.OrdinalIgnoreCase);
            var inventory = new StringBuilder("DlcCollections/v1\n").Append(gtaFolder);
            foreach (var path in new[] { exe, rage }.Concat(archives))
            {
                cancellation.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                inventory.Append('\n').Append(path).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
            }
            var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory.ToString())));
            var cachePath = Path.Combine(StudioSceneService.DataRoot, "DlcCollections",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gtaFolder.ToUpperInvariant()))) + ".json");
            if (!forceRefresh && !hasLinks)
            {
                try
                {
                    var cache = JsonSerializer.Deserialize<ScanCache>(File.ReadAllText(cachePath));
                    if (cache?.Stamp == stamp && cache.Collections != null)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        progress?.Report("Loaded collections from the unchanged GTA installation.");
                        return cache.Collections;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
            if (archives.Count == 0 && !hasLinks) return [];
            var start = new ProcessStartInfo(rage) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["HOME"] = StudioSceneService.DataRoot;
            foreach (var arg in new[] { "--no-update-check", "--exe", exe, "search", packs, "*freemode_01_*.ymt", "--json" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not start rage.exe.");
            // Drain both pipes even on cancellation, so the child cannot block on a full pipe.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                var elapsed = Stopwatch.StartNew();
                var exited = process.WaitForExitAsync(cancellation);
                while (!exited.IsCompleted)
                {
                    progress?.Report((hasLinks ? "Scanning DLC archives and linked folders... " : $"Scanning {archives.Count} DLC archives and their nested packs... ") + elapsed.Elapsed.ToString(@"m\:ss"));
                    await Task.WhenAny(exited, Task.Delay(1000, cancellation)).ConfigureAwait(false);
                }
                await exited.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                throw;
            }
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Could not read the DLC packs: " + errors.Trim());
            cancellation.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(output.Trim().Length == 0 ? "[]" : output);
            var result = Parse(json.RootElement.EnumerateArray().Select(x => (x.GetProperty("archive").GetString() ?? "", x.GetProperty("name").GetString() ?? "")));
            cancellation.ThrowIfCancellationRequested();
            // The reader can skip unreadable packs with exit code zero. Do not persist partial results.
            if (!hasLinks && !errors.Contains("skipping ", StringComparison.OrdinalIgnoreCase) && !errors.Contains("failed to ", StringComparison.OrdinalIgnoreCase))
            {
                var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    File.WriteAllText(temporary, JsonSerializer.Serialize(new ScanCache(stamp, result)));
                    File.Move(temporary, cachePath, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            return result;
        }
        finally { scanGate.Release(); }
    }
}
