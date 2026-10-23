using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using DuckStudio;

static class PerformanceChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // The test apphost temporarily stands in for rage.exe in its own output folder.
    public static bool RunReaderFixture(string[] args)
    {
        var log = Environment.GetEnvironmentVariable("DUCK_STUDIO_SCAN_FIXTURE");
        if (log == null || !args.Contains("search")) return false;
        File.WriteAllText(Path.Combine(log, Environment.ProcessId + ".started"), "fixture");
        while (!File.Exists(Path.Combine(log, "release"))) Thread.Sleep(20);
        var game = Path.GetDirectoryName(args[Array.IndexOf(args, "--exe") + 1])!;
        var archive = Path.Combine(game, "update", "x64", "dlcpacks", "mpheist", "dlc.rpf");
        Console.WriteLine(JsonSerializer.Serialize(new[] {
            new { archive, name = "mp_m_freemode_01_mp_m_heist.ymt" },
            new { archive, name = "mp_f_freemode_01_mp_f_heist.ymt" }
        }));
        Console.Error.WriteLine("2 hit(s) in 1 archive(s), 0 nested");
        return true;
    }

    public static void Run(string root, Action<bool, string> check)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { Build(root, check); Worker(root, check); Scan(root, check); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    static void Worker(string root, Action<bool,string> check)
    {
        var folder=Directory.CreateDirectory(Path.Combine(root,"ValidationWorker")).FullName;
        var project=new ClothingProject { Name="Worker checks",Collection="worker_checks",OutputFolder=folder,
            Items=Enumerable.Range(0,200).Select(i=>new ClothingAsset { Name="Fixture "+i,ModelPath=Path.Combine(folder,i+".ydd"),TexturePaths=[Path.Combine(folder,i+".ytd")],Drawable=i%128 }).ToList() };
        var core=Path.Combine(AppContext.BaseDirectory,"CodeWalker.Core.dll");
        using var cancelled=new CancellationTokenSource();
        cancelled.Cancel();
        var before=ValidationWorker.AnalyzeAsync(project,core,cancellation:cancelled.Token);
        PumpUntil(()=>before.IsCompleted);
        check(before.IsCanceled,"Already-cancelled validation does not start a worker");
        using var stop=new CancellationTokenSource();
        var progress=new InlineProgress(message=>{if(message.StartsWith("Validating model "))stop.Cancel();});
        var running=ValidationWorker.AnalyzeAsync(project,core,progress,stop.Token);
        PumpUntil(()=>running.IsCompleted);
        check(running.IsCanceled,"Cancelling during model validation terminates the worker and propagates cancellation");
        var complete=ValidationWorker.AnalyzeAsync(project,core);
        PumpUntil(()=>complete.IsCompleted);
        check(complete.GetAwaiter().GetResult().Any(f=>f.Problem.StartsWith("Missing model:")),"An isolated worker returns structured model findings without opening an editor window");
        var active=PackBuildOperation.RunAsync(project,folder,new(),false,cancellation:cancelled.Token);
        PumpUntil(()=>active.IsCompleted);
        check(active.IsCanceled&&!Directory.Exists(Path.Combine(folder,"worker_checks-project")),"Cancelling a build before it starts leaves no partial export");
    }

    static void Build(string root, Action<bool, string> check)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "BackgroundBuild")).FullName;
        var texture = Path.Combine(folder, "tattoo.ytd"); File.WriteAllBytes(texture, [41, 42]);
        var metadata = Path.Combine(folder, "mp_creaturemetadata_background.ymt"); File.WriteAllBytes(metadata, [43, 44]);
        var project = new ClothingProject { Name = "Background", Collection = "background", OutputFolder = folder,
            PackMode = PackMode.ReplaceCustom, ReplaceMaleCollection = "mp_m_heist",
            SupplementalYmtPaths = [metadata], Tattoos = [new() { TextureName = "test_tattoo", TexturePath = texture }] };
        var artworkSource = Path.Combine(folder, "art.png");
        ImageSafety.Initialize();
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 4, 4)) image.Write(artworkSource);
        var convertedTexture = Path.Combine(folder, "converted.ytd"); File.WriteAllBytes(convertedTexture, [51, 52]);
        project.Tattoos.Add(new() { TextureName = "converted_tattoo", SourcePath = artworkSource, TexturePath = convertedTexture });
        var before = JsonSerializer.Serialize(project, ProjectIO.Json);
        var dispatcher = Dispatcher.CurrentDispatcher;
        var uiThread = Environment.CurrentManagedThreadId;
        var heartbeat = false;
        var workerThread = uiThread;
        using var released = new ManualResetEventSlim();
        var progress = new InlineProgress(message =>
        {
            if (message != "Preparing independent build textures...") return;
            workerThread = Environment.CurrentManagedThreadId;
            dispatcher.BeginInvoke(() => { heartbeat = true; released.Set(); });
            if (!released.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Build blocked the UI dispatcher.");
        });
        var task = PackBuildOperation.RunAsync(project, folder, new(), true, progress);
        PumpUntil(() => task.IsCompleted);
        var result = task.GetAwaiter().GetResult();
        check(heartbeat && workerThread != uiThread, "Build disk work runs off the UI thread while the dispatcher stays responsive");
        check(before == JsonSerializer.Serialize(project, ProjectIO.Json) && File.ReadAllBytes(texture).SequenceEqual(new byte[] { 41, 42 }), "Background build preserves the live project and its original texture bytes");
        var reopened = ProjectIO.Load(Path.Combine(result.Folder, "Project", "background.duckproj"));
        check(reopened.Tattoos.All(t => File.Exists(t.TexturePath)), "Background build bundles generated inputs before removing temporary files");
        check(CodeWalkerPreview.InspectTextures(reopened.Tattoos.Single(t => t.TextureName == "converted_tattoo").TexturePath,
            Path.Combine(AppContext.BaseDirectory, "CodeWalker.Core.dll")).Textures == 1
            && File.ReadAllBytes(convertedTexture).SequenceEqual(new byte[] { 51, 52 }),
            "Successful artwork conversion exports a real YTD without rewriting the editor's generated texture");
        using (var zip = ZipFile.OpenRead(result.Zip!))
            check(zip.Entries.Any(e => e.FullName.EndsWith("background.duckproj")), "Background ZIP contains the completed editable project");
        var second = PackBuildOperation.RunAsync(project, folder, new(), true);
        PumpUntil(() => second.IsCompleted);
        var rebuilt = second.GetAwaiter().GetResult();
        check(result.Zip != rebuilt.Zip && File.Exists(result.Zip) && File.Exists(rebuilt.Zip), "Rebuilding preserves an existing ZIP and publishes a separate completed archive");
        using var cancellation=new CancellationTokenSource();
        var interrupted=PackBuildOperation.RunAsync(project,folder,new(),true,new InlineProgress(message=>
        {if(message.StartsWith("Compressing:"))cancellation.Cancel();}),cancellation.Token);
        PumpUntil(()=>interrupted.IsCompleted);
        check(interrupted.IsCanceled&&!Directory.EnumerateFiles(folder,".duckstudio-zip-*").Any()&&File.Exists(result.Zip),
            "Cancelling ZIP compression removes its temporary archive and preserves the previous ZIP");

        // Real image conversion must write to the build's independent destination, even on failure.
        var artwork = Path.Combine(folder, "invalid-art.png"); File.WriteAllBytes(artwork, [1, 2]);
        project.Items.Add(new() { Name = "Invalid linked art", ModelPath = "missing.ydd", TexturePaths = [texture],
            TextureSources = new() { [texture] = new() { SourcePath = artwork } } });
        var failed = PackBuildOperation.RunAsync(project, folder, new(), false);
        PumpUntil(() => failed.IsCompleted);
        check(failed.IsFaulted && File.ReadAllBytes(texture).SequenceEqual(new byte[] { 41, 42 }) && File.Exists(Path.Combine(result.Folder, "Project", "background.duckproj")), "Failed conversion preserves the editor texture and the previous published build");
    }

    static void Scan(string root, Action<bool, string> check)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "ScanLifecycle")).FullName;
        var game = Directory.CreateDirectory(Path.Combine(folder, "Game")).FullName;
        var pack = Directory.CreateDirectory(Path.Combine(game, "update", "x64", "dlcpacks", "mpheist")).FullName;
        var archive = Path.Combine(pack, "dlc.rpf"); File.WriteAllBytes(archive, [1]);
        File.WriteAllText(Path.Combine(game, "GTA5.exe"), "not executed");
        var reader = Path.Combine(AppContext.BaseDirectory, "rage.exe");
        var backup = reader + ".fixture-" + Guid.NewGuid().ToString("N");
        var existed = File.Exists(reader);
        var previousFixture = Environment.GetEnvironmentVariable("DUCK_STUDIO_SCAN_FIXTURE");
        var previousHome = Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME");
        var home = Path.Combine(folder, "Data");
        var windows = new List<BuildWindow>();
        int[] Started() => Directory.GetFiles(folder, "*.started").Select(p => int.Parse(Path.GetFileNameWithoutExtension(p))).ToArray();
        bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
        BuildWindow Open()
        {
            var window = new BuildWindow(new() { Name = "Lifecycle", Collection = "lifecycle", OutputFolder = folder, PackMode = PackMode.ReplaceDlc }, gtaFolder: game);
            windows.Add(window); return window;
        }
        try
        {
            if (existed) File.Move(reader, backup);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "DuckStudio.Tests.exe"), reader);
            Environment.SetEnvironmentVariable("DUCK_STUDIO_SCAN_FIXTURE", folder);
            Environment.SetEnvironmentVariable("WEFT_STUDIO_DATA_HOME", home);
            var first = Open();
            PumpUntil(() => Started().Length == 1);
            var firstPid = Started().Single();
            first.Close();
            var second = Open();
            PumpUntil(() => Started().Length == 2);
            check(!Alive(firstPid) && Started().Count(Alive) == 1, "Closing and immediately reopening DLC options stops the old reader before starting another");
            var mode = (ComboBox)typeof(BuildWindow).GetField("modeBox", Private)!.GetValue(second)!;
            mode.SelectedIndex = (int)PackMode.ReplaceCustom;
            PumpUntil(() => Started().All(pid => !Alive(pid)));
            check(true, "Switching away from DLC replacement terminates its scan");
            second.Close();
            var third = Open();
            PumpUntil(() => Started().Length == 3);
            typeof(BuildWindow).GetMethod("StopScan", Private)!.Invoke(third, null);
            PumpUntil(() => Started().All(pid => !Alive(pid)));
            check(true, "Stop scan terminates the active reader");
            third.Close();

            File.WriteAllText(Path.Combine(folder, "release"), "release");
            var scan = PackReplacement.ScanAsync(game);
            PumpUntil(() => scan.IsCompleted);
            check(scan.GetAwaiter().GetResult().Single() == new DlcCollection("mpheist", "mp_m_heist", "mp_f_heist"), "Asynchronous scan parses paired collections from the reader output");
            var starts = Started().Length;
            var cached = PackReplacement.ScanAsync(game);
            PumpUntil(() => cached.IsCompleted);
            check(cached.GetAwaiter().GetResult().Count == 1 && Started().Length == starts, "Unchanged DLC inventory reuses persistent results without launching a reader");
            File.AppendAllText(archive, "updated");
            var changed = PackReplacement.ScanAsync(game);
            PumpUntil(() => changed.IsCompleted); changed.GetAwaiter().GetResult();
            check(Started().Length == starts + 1, "Changing a DLC archive invalidates the saved scan results");
            var forced = PackReplacement.ScanAsync(game, forceRefresh: true);
            PumpUntil(() => forced.IsCompleted); forced.GetAwaiter().GetResult();
            check(Started().Length == starts + 2, "Explicit refresh bypasses the DLC cache");
            foreach (var path in Directory.GetFiles(Path.Combine(home, "DlcCollections"))) File.WriteAllText(path, "{");
            var repaired = PackReplacement.ScanAsync(game);
            PumpUntil(() => repaired.IsCompleted); repaired.GetAwaiter().GetResult();
            check(Started().Length == starts + 3, "A corrupt DLC cache triggers a fresh scan instead of failing discovery");
        }
        finally
        {
            foreach (var window in windows) window.Close();
            foreach (var pid in Started())
                try { using var p = Process.GetProcessById(pid); if (!p.HasExited && p.MainModule?.FileName == reader) { p.Kill(true); p.WaitForExit(); } } catch (ArgumentException) { }
            if (File.Exists(reader)) File.Delete(reader);
            if (existed) File.Move(backup, reader);
            Environment.SetEnvironmentVariable("DUCK_STUDIO_SCAN_FIXTURE", previousFixture);
            Environment.SetEnvironmentVariable("WEFT_STUDIO_DATA_HOME", previousHome);
        }
    }

    sealed class InlineProgress(Action<string> report) : IProgress<string> { public void Report(string message) => report(message); }
    static void PumpUntil(Func<bool> done)
    {
        var elapsed = Stopwatch.StartNew();
        while (!done())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Performance regression check timed out.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
    }
}
