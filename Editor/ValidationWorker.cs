using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DuckStudio;

// Native model readers and their large temporary buffers must not share the
// editor's GC heap. A cancelled or stuck reader can be terminated independently.
public static class ValidationWorker
{
    private const string Prefix = "DUCK_VALIDATION_PROGRESS:";
    private sealed record Request(ClothingProject Project, string Core);
    private sealed record Response(List<ValidationFinding>? Findings, string? Error);
    private sealed class WorkerProgress : IProgress<string>
    {
        public void Report(string message) => Console.WriteLine(Prefix + message);
    }

    public static int Execute(string requestPath, string responsePath)
    {
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath), ProjectIO.Json)
                ?? throw new InvalidDataException("Missing validation request.");
            var findings = ProjectValidation.Analyze(request.Project, request.Core, new WorkerProgress(), trimMemory: true);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(new Response(findings, null), ProjectIO.Json));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(responsePath, JsonSerializer.Serialize(new Response(null, (ex.InnerException ?? ex).Message), ProjectIO.Json));
            return 1;
        }
    }

    public static async Task<List<ValidationFinding>> AnalyzeAsync(ClothingProject project, string core,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var folder = Path.Combine(Path.GetTempPath(), "DuckStudio-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var request = Path.Combine(folder, "request.json"); var response = Path.Combine(folder, "response.json");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new Request(project, core), ProjectIO.Json), cancellation).ConfigureAwait(false);
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "DuckStudio.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = folder
            };
            foreach (var arg in new[] { "--validate-project", request, response }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the model validation worker.");
            var lastMessage = "Starting model validation";
            async Task ReadProgress()
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                    if (line.StartsWith(Prefix, StringComparison.Ordinal))
                    { lastMessage = line[Prefix.Length..]; progress?.Report(lastMessage); }
            }
            var stdout = ReadProgress(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token);
            try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                throw new IOException("Model validation timed out after two minutes. Last step: " + lastMessage);
            }
            await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(response)) throw new IOException("The model validation worker stopped unexpectedly. Last step: " + lastMessage + "\n" + errors.Trim());
            var result = JsonSerializer.Deserialize<Response>(await File.ReadAllTextAsync(response, cancellation).ConfigureAwait(false), ProjectIO.Json);
            if (process.ExitCode != 0 || result?.Findings == null)
                throw new IOException("Model validation failed: " + (result?.Error ?? errors.Trim()));
            return result.Findings;
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
