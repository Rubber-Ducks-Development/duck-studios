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
