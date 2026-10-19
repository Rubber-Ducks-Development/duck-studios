using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DuckStudio;

// Optional integration check using local, user-owned game assets. No assets are distributed.
static class RealPreviewChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(string root, Action<bool, string> check)
    {
        var model = Environment.GetEnvironmentVariable("DUCK_STUDIO_QA_MODEL");
        var original = Environment.GetEnvironmentVariable("DUCK_STUDIO_QA_YTD");
        if (model == null || original == null) return;
        var folder = Directory.CreateDirectory(Path.Combine(root, "RealPreview")).FullName;
        var artwork = Path.Combine(folder, "variation-h.png");
        var pixels = Enumerable.Range(0, 64 * 64).SelectMany(i => new byte[] { 180, 20, 240, 255 }).ToArray();
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(artwork)) encoder.Save(stream);
        var alternate = Path.Combine(folder, "task_diff_000_h_uni.ytd");
        var core = Path.Combine(AppContext.BaseDirectory, "CodeWalker.Core.dll");
        TextureImport.Convert(artwork, alternate, core, model, original);
        var bitmap = CodeWalkerPreview.LoadTextureFile(alternate, core)!;
        check(bitmap.PixelWidth == 64 && bitmap.PixelHeight == 64, "Real YTD variation H decodes at its original size");
        var asset = new ClothingAsset { Name = "YTD integration fixture", Slot = "task", ModelPath = model, TexturePaths = [original, original, original, original, original, original, original, alternate] };
        var preview = new PreviewWindow(asset, new AppSettings(), 7);
        SynchronizationContext.SetSynchronizationContext(new QaContext());
        ((ColumnDefinition)preview.FindName("MenuColumn")).Width = new GridLength(310);
        var task = (Task)typeof(PreviewWindow).GetMethod("LoadAsync", Private)!.Invoke(preview, [false])!;
        Pump(task);
        var visual = (ModelVisual3D?)typeof(PreviewWindow).GetField("currentVisual", Private)!.GetValue(preview);
        check(visual?.Content != null, "Selected H YTD loads on the actual 3D clothing model");
        var hColors = TexturePixels(visual!.Content!);
        check(hColors.Any(p => p.R > 200 && p.B > 130 && p.G < 60), "3D materials use H's magenta artwork rather than A");
        var tabs = (TabControl)preview.FindName("OptionTabs"); tabs.SelectedIndex = 3;
        Save((FrameworkElement)preview.Content, Path.Combine(folder, "real-texture-h.png"));
        ((ComboBox)preview.FindName("TextureBox")).SelectedIndex = 0;
        task = (Task)typeof(PreviewWindow).GetMethod("LoadAsync", Private)!.Invoke(preview, [true])!;
        Pump(task);
        visual = (ModelVisual3D?)typeof(PreviewWindow).GetField("currentVisual", Private)!.GetValue(preview);
        check(!TexturePixels(visual!.Content!).SequenceEqual(hColors), "Switching from H to A changes actual 3D texture pixels");
        Save((FrameworkElement)preview.Content, Path.Combine(folder, "real-texture-a.png"));
        preview.Close();
        DctPhotoWorkflow(folder, model, original, artwork, core, check);
        BuildFailureReport(folder,model,artwork,core,check);
    }
    static void BuildFailureReport(string folder,string model,string artwork,string core,Action<bool,string> check)
    {
        var metadata=CodeWalkerPreview.InspectModel(model,core);
        var embedded=CodeWalkerPreview.EmbeddedTextureNames(model,core);
        if(!metadata.DiffuseNames.Except(embedded,StringComparer.OrdinalIgnoreCase).Any())return;
        var wrong=Path.Combine(folder,"wrong-diffuse.ytd");
        TextureImport.Convert(artwork,wrong,core,model,textureName:"unrelated_report_fixture");
        var asset=new ClothingAsset{Name="Report fixture",Slot="task",ModelPath=model,TexturePaths=[wrong]};
        var project=new ClothingProject{Name="Report build",Collection="report_build",OutputFolder=folder,CodeWalkerCorePath=core,Items=[asset]};
        BuildValidationException? error=null;
        try{PackBuilder.Build(project,folder);}catch(BuildValidationException ex){error=ex;}
        check(error?.Findings.Any(f=>f.Problem.Contains("missing diffuse map")&&f.Fix.Contains("unrelated_report_fixture"))==true,"Real build failure retains expected and available diffuse map names in structured findings");
        check(!Directory.Exists(Path.Combine(folder,"report_build-project")),"Invalid diffuse dictionaries remain blocked before publishing build output");
        var report=BuildReport.Create(project,error!);
        check(report.Contains("wrong-diffuse.ytd")&&report.Contains(model)&&report.Contains("unrelated_report_fixture"),"Real build failure report retains file context and available texture names for support");
    }
    static void DctPhotoWorkflow(string folder, string model, string original, string artwork, string core, Action<bool,string> check)
    {
        var path=Path.Combine(folder,"photo-workflow.dctproj"); File.WriteAllText(path,"Adjacent built-resource import fixture");
        var built=Directory.CreateDirectory(Path.Combine(folder,"clothproject_photo-workflow","photo-workflow_build")).FullName;
        File.WriteAllText(Path.Combine(built,"fxmanifest.lua"),"fx_version 'cerulean'\ngame 'gta5'");
        var stream=Directory.CreateDirectory(Path.Combine(built,"stream")).FullName;
        File.Copy(model,Path.Combine(stream,"mp_m_freemode_01^task_000_u.ydd"));
        File.Copy(original,Path.Combine(stream,"mp_m_freemode_01^task_diff_000_a_uni.ytd"));
        var project=ResourceImporter.ImportDurtyProject(path); var asset=project.Items.Single();
        var window=new MainWindow();
        typeof(MainWindow).GetField("project",Private)!.SetValue(window,project);
        typeof(MainWindow).GetField("selected",Private)!.SetValue(window,asset);
        typeof(MainWindow).GetMethod("SyncItem",Private)!.Invoke(window,null);
        var import=(Task<int>)typeof(MainWindow).GetMethod("ImportTexturesAsync",Private)!.Invoke(window,[asset,new[]{artwork},-1])!;Pump(import);
        check(import.Result==1 && ((ListBox)window.FindName("TextureList")).SelectedIndex==1,"DCT project plus dropped PNG selects the new generated YTD variation");
        var selected=new PreviewWindow(asset,new AppSettings(),import.Result, (a,files)=>(Task<int>)typeof(MainWindow).GetMethod("ImportTexturesAsync",Private)!.Invoke(window,[a,files,-1])!);
        var load=(Task)typeof(PreviewWindow).GetMethod("LoadAsync",Private)!.Invoke(selected,[false])!;Pump(load);
        var visual=(ModelVisual3D)typeof(PreviewWindow).GetField("currentVisual",Private)!.GetValue(selected)!;
        check(TexturePixels(visual.Content).Any(p=>p.R>200&&p.B>130&&p.G<60),"DCT imported clothing renders the dropped PNG's actual magenta pixels");
        var report=PreviewDiagnostics.Report(path,asset,1);
        check(report.Contains("selected-ytd")&&report.Contains(Path.GetFileName(artwork)),"DCT photo preview report records the selected artwork and resolved YTD origin");
        var secondArtwork=Path.Combine(folder,"second-photo.png");File.Copy(artwork,secondArtwork);
        typeof(PreviewWindow).GetField("ready",Private)!.SetValue(selected,true);
        var photoImport=(Task)typeof(PreviewWindow).GetMethod("ImportPhotosAsync",Private)!.Invoke(selected,[new[]{secondArtwork}])!;Pump(photoImport);
        visual=(ModelVisual3D)typeof(PreviewWindow).GetField("currentVisual",Private)!.GetValue(selected)!;
        check(((ComboBox)selected.FindName("TextureBox")).SelectedIndex==2&&TexturePixels(visual.Content).Any(p=>p.R>200&&p.B>130&&p.G<60),"From photos in an already open preview selects and renders the last imported image");
        var body=(FrameworkElement)selected.Content;Save(body,Path.Combine(folder,"dct-photo-preview.png"));
        selected.Close();typeof(MainWindow).GetMethod("WindowClosed",Private)!.Invoke(window,[window,EventArgs.Empty]);
    }
    static List<Color> TexturePixels(Model3D model)
    {
        var colors = new List<Color>();
        if (model is Model3DGroup group) foreach (var child in group.Children) colors.AddRange(TexturePixels(child));
        if (model is GeometryModel3D geometry && geometry.Material is DiffuseMaterial { Brush: ImageBrush { ImageSource: BitmapSource bitmap } })
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4) colors.Add(Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]));
        }
        return colors;
    }
    static void Pump(Task task)
    {
        var context = (QaContext)SynchronizationContext.Current!;
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            while (context.Pending.TryDequeue(out var callback)) callback.Action(callback.State);
            Thread.Sleep(10);
        }
        if (!task.IsCompleted) throw new TimeoutException("Real model preview timed out.");
        task.GetAwaiter().GetResult();
    }
    sealed class QaContext : SynchronizationContext
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Action, object? State)> Pending = new();
        public override void Post(SendOrPostCallback action, object? state) => Pending.Enqueue((action, state));
    }
    static void Save(FrameworkElement content, string path)
    {
        content.Measure(new Size(1160, 850)); content.Arrange(new Rect(0, 0, 1160, 850)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1160, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
