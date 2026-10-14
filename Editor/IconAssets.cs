using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuckStudio;

public static class IconAssets
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? redBulb;

    public static ImageSource StatusBulb(bool valid) => valid ? Get("lightbulb-green") : redBulb ??= CreateRedBulb();

    private static ImageSource CreateRedBulb()
    {
        var outer = Geometry.Parse("M32 5 C19.1 5 10 14.3 10 27 C10 35.4 14.2 41.3 19.7 47 H44.3 C49.8 41.3 54 35.4 54 27 C54 14.3 44.9 5 32 5 Z");
        var highlight = Geometry.Parse("M17 24 C18.6 16.8 23 12.6 29.5 11.3").GetWidenedPathGeometry(new Pen(Brushes.Black, 4.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var cutTop = new CombinedGeometry(GeometryCombineMode.Exclude, outer, highlight);
        var baseShape = Geometry.Parse("M21 52 H43 V54 C43 59 39 62 32 62 C25 62 21 59 21 54 Z");
        var color = new SolidColorBrush(Color.FromRgb(226, 64, 77));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 64, 64))));
        drawing.Children.Add(new GeometryDrawing(color, null, cutTop));
        drawing.Children.Add(new GeometryDrawing(color, null, baseShape));
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    public static ImageSource Get(string name)
    {
        if (Cache.TryGetValue(name, out var image)) return image;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/DuckStudio;component/Assets/Icons/{name}.png", UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return Cache[name] = bitmap;
    }
}
