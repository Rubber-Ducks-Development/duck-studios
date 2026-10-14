using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuckStudio;

public static class ProductScreenshots
{
    public static string SafeName(string value)=>string.Concat(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');
    public static BitmapSource ContactSheet(IReadOnlyList<(BitmapSource Image,string Caption)> tiles)
    {
        const int columns=4,cellWidth=300,cellHeight=365;var rows=(tiles.Count+columns-1)/columns;
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(39,41,45)),null,new Rect(0,0,columns*cellWidth,rows*cellHeight));
            for(var i=0;i<tiles.Count;i++)
            {
                var x=(i%columns)*cellWidth;var y=(i/columns)*cellHeight;var image=tiles[i].Image;var scale=Math.Min(280d/image.PixelWidth,310d/image.PixelHeight);var w=image.PixelWidth*scale;var h=image.PixelHeight*scale;dc.DrawImage(image,new Rect(x+(cellWidth-w)/2,y+(310-h)/2+10,w,h));
                var text=new FormattedText(tiles[i].Caption,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.White,1){MaxTextWidth=280,MaxTextHeight=38,Trimming=TextTrimming.CharacterEllipsis};dc.DrawText(text,new Point(x+10,y+323));
            }
        }
        var bitmap=new RenderTargetBitmap(columns*cellWidth,Math.Max(1,rows*cellHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();return bitmap;
    }
}
public sealed class ScreenshotWindow : StudioToolWindow
{
    private CancellationTokenSource? cancellation;
    public ScreenshotWindow(ClothingProject project,ClothingAsset? selected,AppSettings settings,string core):base("Product screenshots",1020,770)
    {
        var hint=Hint("Export front, back and side PNGs with fixed studio lighting. Transparent images contain the rendered model only. Contact sheets include filenames and variation labels. Each export creates a new folder.");DockPanel.SetDock(hint,Dock.Top);Body.Children.Add(hint);
        var footer=Footer();var export=Action("Export screenshots...");var close=Action("Close");close.IsCancel=true;footer.Children.Add(export);footer.Children.Add(close);close.Click+=(_,_)=>Close();
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(310)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(18)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var fields=new StackPanel();grid.Children.Add(fields);var preview=new StudioViewport();Grid.SetColumn(preview,2);grid.Children.Add(preview);
        fields.Children.Add(new TextBlock{Text="Capture",Margin=new Thickness(0,0,0,5)});var mode=new ComboBox{ItemsSource=new[]{"Selected drawable","All project drawables","Saved outfit"},SelectedIndex=selected==null?1:0,Height=30};fields.Children.Add(mode);
        fields.Children.Add(new TextBlock{Text="Outfit preset",Margin=new Thickness(0,10,0,5)});var outfit=new ComboBox{ItemsSource=project.Outfits,SelectedIndex=project.Outfits.Count>0?0:-1,Height=30};fields.Children.Add(outfit);
        fields.Children.Add(new TextBlock{Text="Image resolution",Margin=new Thickness(0,10,0,5)});var size=new ComboBox{ItemsSource=new[]{512,1024,2048},SelectedItem=1024,Height=30};fields.Children.Add(size);
        var transparent=new CheckBox{Content="Transparent PNG background",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,14,0,10)};fields.Children.Add(transparent);
        var variations=new CheckBox{Content="Capture every texture variation",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,0,0,10)};fields.Children.Add(variations);
        var views=new CheckBox{Content="Front, back, left and right views",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,0,0,10)};fields.Children.Add(views);
        var sheet=new CheckBox{Content="Create contact sheets",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,0,0,10)};fields.Children.Add(sheet);
        var progress=Hint("Ready to export.");fields.Children.Add(progress);var cancel=Action("Cancel export");cancel.IsEnabled=false;fields.Children.Add(cancel);cancel.Click+=(_,_)=>cancellation?.Cancel();Closed+=(_,_)=>cancellation?.Cancel();
        export.Click+=async(_,_)=>
        {
            var picker=new Microsoft.Win32.OpenFolderDialog{Title="Choose product screenshot output folder"};if(picker.ShowDialog(this)!=true)return;
            var selection=mode.SelectedIndex;var allVariations=variations.IsChecked==true;var fourViews=views.IsChecked==true;var alpha=transparent.IsChecked==true;var contact=sheet.IsChecked==true;var resolution=(int)size.SelectedItem;var preset=outfit.SelectedItem as OutfitPreset;
            if(selection==0&&selected==null){progress.Text="Select a drawable in the main window first.";return;}if(selection==2&&preset==null){progress.Text="Save an outfit preset first.";return;}
            var folder=Path.Combine(picker.FolderName,"DuckStudio-photos-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..5]);Directory.CreateDirectory(folder);cancellation=new();var token=cancellation.Token;export.IsEnabled=false;cancel.IsEnabled=true;
            var tiles=new List<(BitmapSource Image,string Caption)>();int captured=0,sheets=0;
            void Capture(string name)
            {
                foreach(var (angle,label) in fourViews?new[]{(0d,"front"),(Math.PI,"back"),(-Math.PI/2,"left"),(Math.PI/2,"right")}:new[]{(0d,"front")})
                {
                    token.ThrowIfCancellationRequested();var image=preview.Capture(resolution,resolution,angle,alpha);var safe=ProductScreenshots.SafeName(name);if(string.IsNullOrWhiteSpace(safe))safe="item";var file=$"{++captured:0000}-{safe}-{label}.png";StudioViewport.SavePng(image,Path.Combine(folder,file));tiles.Add((image,name+" / "+label));
                    if(contact&&tiles.Count==24){StudioViewport.SavePng(ProductScreenshots.ContactSheet(tiles),Path.Combine(folder,$"contact-sheet-{++sheets:000}.png"));tiles.Clear();}
                }
            }
            try
            {
                if(selection==2)
                {
                    progress.Text="Loading outfit...";using var scene=await StudioSceneService.LoadOutfitAsync(ProjectAssets.Clone(project),preset!,settings.GtaFolderPath,core,token);OutfitWindow.ApplyTattoos(scene.Result.Model,project.Tattoos.Where(t=>preset!.TattooIds.Contains(t.Id)),core);preview.Set(scene.Result);Capture(preset!.Name);
                }
                else
                {
                    var items=selection==0?new[]{selected!}:project.Items.ToArray();
                    foreach(var asset in items)
                    {
                        var count=allVariations?Math.Max(1,asset.TexturePaths.Count):1;
                        for(var i=0;i<count;i++)
                        {
                            token.ThrowIfCancellationRequested();progress.Text=$"Rendering {asset.Name} / variation {i+1} of {count}";var rage=Path.Combine(AppContext.BaseDirectory,"rage.exe");var sceneFolder=Path.Combine(StudioSceneService.DataRoot,"scenes",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sceneFolder);
                            try{var args=new List<string>{"asset-scene","--ydd",asset.ModelPath,"-o",sceneFolder};if(asset.TexturePaths.ElementAtOrDefault(i) is {} texture)args.AddRange(["--ytd",texture]);await StudioSceneService.RunAsync(rage,StudioSceneService.DataRoot,args,token);token.ThrowIfCancellationRequested();preview.Set(PedScenePreview.Load(sceneFolder));Capture(asset.Name+"-"+(char)('a'+i));}
                            finally{try{Directory.Delete(sceneFolder,true);}catch(IOException){}}
                            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
                if(contact&&tiles.Count>0)StudioViewport.SavePng(ProductScreenshots.ContactSheet(tiles),Path.Combine(folder,$"contact-sheet-{++sheets:000}.png"));progress.Text=$"Exported {captured} views and {sheets} contact sheets.\n{folder}";
            }
            catch(OperationCanceledException){progress.Text=$"Export canceled. {captured} completed images remain in {folder}.";}
            catch(Exception ex){progress.Text=(ex.InnerException??ex).Message;}
            finally{export.IsEnabled=true;cancel.IsEnabled=false;cancellation.Dispose();cancellation=null;}
        };
    }
}
