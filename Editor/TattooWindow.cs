using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using Microsoft.Win32;

namespace DuckStudio;

public sealed class TattooWindow : StudioToolWindow
{
    private readonly ClothingProject project;
    private readonly AppSettings settings;
    private readonly string core;
    private readonly ListBox list=new(){Background=new SolidColorBrush(Color.FromRgb(39,39,41)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(85,85,88)),AllowDrop=true};
    private readonly StackPanel fields=new();
    private readonly Canvas placement=new(){Width=420,Height=420,Background=new SolidColorBrush(Color.FromRgb(65,67,70)),ClipToBounds=true};
    private readonly TextBlock info=Hint("Drop PNG, JPG, PSD or YTD files into the tattoo list.");
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Dictionary<string,(long Ticks,long Length)> stamps=[];
    private Point? dragging;
    private Image? overlay;
    public List<TattooAsset> Tattoos=>project.Tattoos;
    private TattooAsset? Selected=>list.SelectedItem as TattooAsset;

    public TattooWindow(ClothingProject detached,AppSettings appSettings,string coreDll):base("Tattoos",1190,790)
    {
        project=detached;settings=appSettings;core=coreDll;
        var hint=Hint("Create male, female or shared tattoos. Import artwork, choose a body zone, and adjust its UV placement. Source images stay linked; saves refresh the preview. Tattoo metadata and textures are included in the selected build target.");DockPanel.SetDock(hint,Dock.Top);Body.Children.Add(hint);
        var footer=Footer();var apply=Action("Apply tattoo changes");var cancel=Action("Cancel");cancel.IsCancel=true;footer.Children.Add(apply);footer.Children.Add(cancel);
        apply.Click+=(_,_)=>{var problems=TattooBuilder.Validate(project);if(problems.Count>0){MessageBox.Show(this,string.Join("\n",problems),"Check tattoo settings");return;}DialogResult=true;};cancel.Click+=(_,_)=>DialogResult=false;
        var grid=new Grid();foreach(var width in new[]{220d,16d,270d,20d})grid.ColumnDefinitions.Add(new(){Width=new GridLength(width)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var left=new DockPanel();grid.Children.Add(left);
        var buttons=new WrapPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(buttons,Dock.Top);left.Children.Add(buttons);
        var add=Action("Add image");var remove=Action("Remove");buttons.Children.Add(add);buttons.Children.Add(remove);left.Children.Add(list);
        var scroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,2);grid.Children.Add(scroll);
        var preview=new StackPanel();Grid.SetColumn(preview,4);grid.Children.Add(preview);preview.Children.Add(new TextBlock{Text="UV placement preview",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        preview.Children.Add(new Viewbox{Child=placement,Stretch=Stretch.Uniform,MaxHeight=390});preview.Children.Add(info);
        var model=Action("UV preview on PED");model.HorizontalAlignment=HorizontalAlignment.Left;preview.Children.Add(model);
        model.Click+=(_,_)=>{if(Selected is { } tattoo)new TattooPedWindow(tattoo,settings,core){Owner=this}.Show();};
        add.Click+=(_,_)=>{var dialog=new OpenFileDialog{Filter=TextureImport.Filter,Multiselect=true,Title="Add tattoo artwork"};if(dialog.ShowDialog(this)==true)AddFiles(dialog.FileNames);};
        remove.Click+=(_,_)=>{if(Selected is { } tattoo){project.Tattoos.Remove(tattoo);RefreshList();}};
        list.SelectionChanged+=(_,_)=>EditSelected();
        list.PreviewDragOver+=(_,e)=>{e.Effects=e.Data.GetData(DataFormats.FileDrop) is string[] files&&files.All(TextureImport.IsSupported)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        list.PreviewDrop+=(_,e)=>{e.Handled=true;if(e.Data.GetData(DataFormats.FileDrop) is not string[] files)return;var row=e.OriginalSource is DependencyObject source?ItemsControl.ContainerFromElement(list,source) as ListBoxItem:null;if(row?.DataContext is TattooAsset tattoo&&files.Length==1){tattoo.SourcePath=files[0];tattoo.NotifyChanged();EditSelected();}else AddFiles(files);};
        placement.MouseLeftButtonDown+=(_,e)=>{if(Selected==null)return;dragging=e.GetPosition(placement);placement.CaptureMouse();};
        placement.MouseMove+=(_,e)=>{if(dragging==null||Selected is not { } tattoo||e.LeftButton!=MouseButtonState.Pressed)return;var point=e.GetPosition(placement);tattoo.U+=(point.X-dragging.Value.X)/420;tattoo.V+=(point.Y-dragging.Value.Y)/420;dragging=point;UpdatePreview();tattoo.NotifyChanged();};
        placement.MouseLeftButtonUp+=(_,_)=>{dragging=null;placement.ReleaseMouseCapture();EditSelected();};
        timer.Tick+=(_,_)=>{if(Selected is not { } tattoo||!File.Exists(tattoo.SourcePath))return;var file=new FileInfo(tattoo.SourcePath);var stamp=(file.LastWriteTimeUtc.Ticks,file.Length);if(stamps.GetValueOrDefault(tattoo.SourcePath)!=stamp){stamps[tattoo.SourcePath]=stamp;UpdatePreview();tattoo.NotifyChanged();}};
        timer.Start();Closed+=(_,_)=>timer.Stop();RefreshList();
    }
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach(var path in paths.Where(p=>File.Exists(p)&&TextureImport.IsSupported(p)))
        {
            var name=Path.GetFileNameWithoutExtension(path);var slug=TattooBuilder.Slug(project.Collection+"_"+name);if(slug.Length>55)slug=slug[..55];if(slug.Length<2)slug="tattoo";
            var unique=slug;int number=2;while(project.Tattoos.Any(t=>t.TextureName==unique))unique=slug+"_"+number++;
            project.Tattoos.Add(new(){Name=name,TextureName=unique,SourcePath=Path.GetFullPath(path)});
        }
        RefreshList();if(list.Items.Count>0)list.SelectedIndex=list.Items.Count-1;
    }
    private void RefreshList(){var selected=Selected;list.ItemsSource=null;list.ItemsSource=project.Tattoos;list.SelectedItem=selected;if(list.SelectedIndex<0&&list.Items.Count>0)list.SelectedIndex=0;EditSelected();}
    private void EditSelected()
    {
        fields.Children.Clear();if(Selected is not { } tattoo){UpdatePreview();return;}
        void Changed(){tattoo.NotifyChanged();UpdatePreview();}
        Field(fields,"Name",tattoo.Name,s=>tattoo.Name=s);
        Field(fields,"Texture / overlay name",tattoo.TextureName,s=>tattoo.TextureName=s.Trim());
        Choice(fields,"Gender",TattooBuilder.Genders,tattoo.Gender,s=>{tattoo.Gender=s;Changed();});
        Choice(fields,"Body zone",TattooBuilder.Zones,tattoo.Zone,s=>{tattoo.Zone=s;Changed();});
        Choice(fields,"Facing",TattooBuilder.Facings,tattoo.Facing,s=>tattoo.Facing=s);
        void Numeric(string label,double value,Action<double> set)=>Field(fields,label,value.ToString("0.########",CultureInfo.InvariantCulture),s=>{if(double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)){set(number);Changed();}});
        Numeric("UV center X",tattoo.U,x=>tattoo.U=x);Numeric("UV center Y",tattoo.V,x=>tattoo.V=x);
        Numeric("Scale X",tattoo.ScaleX,x=>tattoo.ScaleX=x);Numeric("Scale Y",tattoo.ScaleY,x=>tattoo.ScaleY=x);Numeric("Rotation (radians)",tattoo.Rotation,x=>tattoo.Rotation=x);
        Field(fields,"Shop price",tattoo.Cost.ToString(),s=>{if(int.TryParse(s,out var n))tattoo.Cost=n;});
        Field(fields,"Update group (optional)",tattoo.UpdateGroup,s=>tattoo.UpdateGroup=s.Trim());
        var replace=Action("Replace linked artwork");replace.Margin=new Thickness(0,15,0,0);fields.Children.Add(replace);replace.Click+=(_,_)=>{var dialog=new OpenFileDialog{Filter=TextureImport.Filter};if(dialog.ShowDialog(this)==true){tattoo.SourcePath=dialog.FileName;EditSelected();tattoo.NotifyChanged();}};
        UpdatePreview();
    }
    internal static BitmapSource ReadArtwork(TattooAsset tattoo,string core)
    {
        var path=File.Exists(tattoo.SourcePath)?tattoo.SourcePath:tattoo.TexturePath;
        if(Path.GetExtension(path).Equals(".ytd",StringComparison.OrdinalIgnoreCase))return CodeWalkerPreview.LoadTextureFile(path,core)??throw new InvalidDataException("Tattoo YTD has no supported image.");
        using var image=new MagickImage(path,new MagickReadSettings{FrameIndex=0,FrameCount=1});image.Depth=8;
        using var stream=new MemoryStream(image.ToByteArray(MagickFormat.Png));var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    private void UpdatePreview()
    {
        placement.Children.Clear();
        for(int n=0;n<=10;n++)
        {
            placement.Children.Add(new System.Windows.Shapes.Line{X1=n*42,Y1=0,X2=n*42,Y2=420,Stroke=new SolidColorBrush(Color.FromRgb(88,91,95)),StrokeThickness=n is 0 or 10?2:1});
            placement.Children.Add(new System.Windows.Shapes.Line{X1=0,Y1=n*42,X2=420,Y2=n*42,Stroke=new SolidColorBrush(Color.FromRgb(88,91,95)),StrokeThickness=n is 0 or 10?2:1});
        }
        if(Selected is not { } tattoo){info.Text="Drop artwork into the list or choose Add image.";return;}
        try
        {
            var bitmap=ReadArtwork(tattoo,core);overlay=new Image{Source=bitmap,Width=Math.Max(.1,tattoo.ScaleX*420),Height=Math.Max(.1,tattoo.ScaleY*420),Stretch=Stretch.Fill,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(tattoo.Rotation*180/Math.PI)};Canvas.SetLeft(overlay,tattoo.U*420-overlay.Width/2);Canvas.SetTop(overlay,tattoo.V*420-overlay.Height/2);placement.Children.Add(overlay);
            info.Text=$"{bitmap.PixelWidth} × {bitmap.PixelHeight} px • {tattoo.Zone.Replace("ZONE_","").Replace('_',' ')}\nDrag the artwork to move its UV center. The grid spans UV 0-1.\nLinked: {Path.GetFileName(tattoo.SourcePath)}";info.ToolTip=tattoo.SourcePath;
        }
        catch(Exception ex){info.Text="Artwork preview unavailable: "+(ex.InnerException??ex).Message;}
    }
}
