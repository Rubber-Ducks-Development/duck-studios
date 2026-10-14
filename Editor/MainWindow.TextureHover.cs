using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuckStudio;

public partial class MainWindow
{
    private readonly Dictionary<string, (long Ticks, long Length, BitmapSource Bitmap)> thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object thumbnailDecodeGate = new();

    private async void TextureHoverOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not Image icon || selected == null) return;
        var row = ItemsControl.ContainerFromElement(TextureList, icon) as ListBoxItem;
        if (row == null) return;
        var index = TextureList.ItemContainerGenerator.IndexFromContainer(row);
        if (index < 0 || index >= selected.TexturePaths.Count) return;
        var path = selected.TexturePaths[index];
        var core = ResolveCorePath(false);
        var panel = new StackPanel { Width = 260 };
        var heading = new TextBlock { Text = Path.GetFileName(path), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        var status = new TextBlock { Text = "Loading texture...", Foreground = Brushes.LightGray, Margin = new Thickness(0,8,0,0) };
        panel.Children.Add(heading); panel.Children.Add(status);
        if (icon.ToolTip is not ToolTip tip) return;
        tip.Content = panel;
        try
        {
            if (core == null) throw new IOException("CodeWalker.Core.dll is missing from the app folder.");
            var info = new FileInfo(path);
            BitmapSource? bitmap;
            if (thumbnailCache.TryGetValue(path, out var cached) && cached.Ticks == info.LastWriteTimeUtc.Ticks && cached.Length == info.Length)
                bitmap = cached.Bitmap;
            else
            {
                bitmap = await Task.Run(() =>
                {
                    lock (thumbnailDecodeGate)
                    {
                        var full = CodeWalkerPreview.LoadTextureFile(path, core);
                        if (full == null) return null;
                        var opaque = PreviewTexture.ForMaterial(full, PreviewTexture.AlphaMode.Opaque);
                        var scale = Math.Min(1, 512d / Math.Max(opaque.PixelWidth, opaque.PixelHeight));
                        var small = new TransformedBitmap(opaque, new ScaleTransform(scale, scale)); small.Freeze(); return (BitmapSource)small;
                    }
                });
                if (bitmap != null)
                {
                    if (thumbnailCache.Count >= 24) thumbnailCache.Clear();
                    thumbnailCache[path] = (info.LastWriteTimeUtc.Ticks, info.Length, bitmap);
                }
            }
            if (bitmap == null) { status.Text = "No supported diffuse texture found."; return; }
            var preview = new Image { Source = bitmap, Width = 260, Height = 160, Stretch = Stretch.Uniform, Margin = new Thickness(0,10,0,0) };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
            panel.Children.Insert(1, new Border { Background = new SolidColorBrush(Color.FromRgb(38,38,38)), Child = preview });
            status.Text = ClothingAsset.FileSize(path) + (selected.TextureSources.TryGetValue(path, out var link) ? "\nLinked: " + link.SourcePath + "\nReloads when saved" : ""); status.TextWrapping = TextWrapping.Wrap;
        }
        catch (Exception ex) { status.Text = "Texture preview unavailable: " + ex.Message; status.TextWrapping = TextWrapping.Wrap; }
    }
}
