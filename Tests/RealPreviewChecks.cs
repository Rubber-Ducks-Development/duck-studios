using System.IO;
using System.Reflection;
using System.Xml.Linq;
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
        var binary = CodeWalkerPreview.InspectClothing(model, core);
        var legacy = System.Xml.Linq.XDocument.Parse(CodeWalkerPreview.ExportXmlFile(model, "", core));
        var expected = ModelTools.Levels.Select(level =>
        {
            var meshes = legacy.Descendants(level).Descendants("Geometries").Elements("Item").ToList();
            return new LodStatistics(level.Replace("DrawableModels", ""), meshes.Sum(m => ModelTools.Rows(m).Length), meshes.Sum(m => ModelTools.Indices(m).Length / 3), meshes.Count);
        }).ToList();
        check(binary.Model.Lods.SequenceEqual(expected), "Direct binary inspection preserves all XML-derived LOD vertex, triangle and mesh counts");
        check(binary.Model.Bones == legacy.Descendants("Bones").Elements("Item").Count()
            && binary.Model.HasWeights == legacy.Descendants("Layout").Elements("BlendWeights").Any(), "Direct binary inspection preserves skeleton and skin-weight findings");
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
        BuildDiffuseVariations(folder,model,artwork,core,check);
        ClothingExportChecks.Run(root,check);
    }
    static void BuildDiffuseVariations(string folder,string model,string artwork,string core,Action<bool,string> check)
    {
        var renamed=Path.Combine(folder,"renamed-diffuse.ytd");
        var normalArtwork=Path.Combine(folder,"normal-fixture.png");
        var pixels=Enumerable.Repeat(new byte[]{255,100,20,255},64*64).SelectMany(p=>p).ToArray();
        var image=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,64*4);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(normalArtwork))png.Save(stream);
        Dictionary(renamed,[("GravityFlow#0065_Bump",normalArtwork),("GravityFlow#0065_Spec",normalArtwork),("FF1",artwork)]);
        var originalBytes=File.ReadAllBytes(renamed);
        check(CodeWalkerPreview.VariationDiffuseName(renamed,model,core)=="FF1","Custom-named FF1 variation resolves separately from bump/specular maps");
        var thumb=CodeWalkerPreview.LoadTextureFile(renamed,core,modelPath:model)!;
        var thumbPixels=new byte[thumb.PixelWidth*thumb.PixelHeight*4];thumb.CopyPixels(thumbPixels,thumb.PixelWidth*4,0);
        check(thumbPixels[2]>200&&thumbPixels[0]>130&&thumbPixels[1]<60,"Custom-named variation thumbnail displays the color artwork");
        var asset=new ClothingAsset{Name="Renamed variation",Slot="task",ModelPath=model,TexturePaths=[renamed]};
        var project=new ClothingProject{Name="Renamed build",Collection="renamed_build",OutputFolder=folder,CodeWalkerCorePath=core,Items=[asset]};
        check(!ProjectValidation.Analyze(project,core).Any(f=>f.Severity=="Error"),"Valid renamed clothing diffuse no longer produces a missing-name build error");
        PackBuilder.Build(project,folder);
        check(Directory.GetFiles(Path.Combine(folder,"renamed_build-project"),"*.ytd",SearchOption.AllDirectories).Any(p=>File.ReadAllBytes(p).SequenceEqual(originalBytes)),"Build publishes renamed YTD bytes without rewriting user assets");
        var preview=new PreviewWindow(asset,new AppSettings(),0);
        Pump((Task)typeof(PreviewWindow).GetMethod("LoadAsync",Private)!.Invoke(preview,[false])!);
        var visual=(ModelVisual3D)typeof(PreviewWindow).GetField("currentVisual",Private)!.GetValue(preview)!;
        check(TexturePixels(visual.Content).Any(p=>p.R>200&&p.B>130&&p.G<60),"Actual 3D preview renders FF1 color pixels instead of a normal/specular map");
        var report=PreviewDiagnostics.Report(null,asset,0);
        check(report.Contains("FF1")&&report.Contains("selected-ytd"),"Debug report identifies the custom diffuse actually used by the 3D preview");
        preview.Close();
        var imported=Path.Combine(folder,"renamed-photo.ytd");
        check(TextureImport.Convert(artwork,imported,core,model,renamed)=="FF1"&&CodeWalkerPreview.TextureNames(imported,core).Count==3,"Photo import replaces the custom diffuse while preserving normal/specular entries");
        check(File.ReadAllBytes(renamed).SequenceEqual(originalBytes),"Validation, preview, build and photo import preserve the original renamed YTD");
        var metadata=CodeWalkerPreview.InspectModel(model,core);
        if(!metadata.DiffuseNames.Except(CodeWalkerPreview.EmbeddedTextureNames(model,core),StringComparer.OrdinalIgnoreCase).Any())return;
        foreach(var entries in new[]{new[]{("cloth_normal",normalArtwork),("cloth_spec",normalArtwork)},new[]{("FF1",artwork),("FF2",normalArtwork)}})
        {
            var wrong=Path.Combine(folder,"unresolved-diffuse-"+entries[0].Item1+".ytd");Dictionary(wrong,entries);asset.TexturePaths=[wrong];
            BuildValidationException? error=null;try{PackBuilder.Build(project,folder);}catch(BuildValidationException ex){error=ex;}
            check(error?.Findings.Any(f=>f.Problem.Contains("cannot resolve diffuse map")&&f.Fix.Contains(entries[0].Item1))==true,"Normal-only or ambiguous dictionaries retain a useful blocking build finding");
            var failure=BuildReport.Create(project,error!);
            check(failure.Contains(Path.GetFileName(wrong))&&failure.Contains(model),"Unresolved diffuse report retains model and variation file context");
            check(Directory.GetFiles(Path.Combine(folder,"renamed_build-project"),"*.ytd",SearchOption.AllDirectories).Any(p=>File.ReadAllBytes(p).SequenceEqual(originalBytes)),"Failed validation preserves the previous valid published build");
        }
        void Dictionary(string path,(string Name,string Image)[] entries)
        {
            var assembly=Assembly.LoadFrom(core);var ytdType=assembly.GetType("CodeWalker.GameFiles.YtdFile",true)!;
            var textureType=assembly.GetType("CodeWalker.GameFiles.Texture",true)!;
            var list=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(textureType))!;
            foreach(var entry in entries)
            {
                var single=path+".single.ytd";TextureImport.Convert(entry.Image,single,core,model,textureName:entry.Name);
                var file=Activator.CreateInstance(ytdType)!;ytdType.GetMethod("Load",[typeof(byte[])])!.Invoke(file,[File.ReadAllBytes(single)]);
                var dict=ytdType.GetProperty("TextureDict")!.GetValue(file)!;
                var textures=dict.GetType().GetProperty("Textures")!.GetValue(dict)!;
                list.Add(((Array)textures.GetType().GetProperty("data_items")!.GetValue(textures)!).GetValue(0));File.Delete(single);
            }
            var result=Activator.CreateInstance(ytdType)!;var dictionary=Activator.CreateInstance(assembly.GetType("CodeWalker.GameFiles.TextureDictionary",true)!)!;
            dictionary.GetType().GetMethod("BuildFromTextureList")!.Invoke(dictionary,[list]);ytdType.GetProperty("TextureDict")!.SetValue(result,dictionary);
            File.WriteAllBytes(path,(byte[])ytdType.GetMethod("Save",Type.EmptyTypes)!.Invoke(result,null)!);
        }
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
