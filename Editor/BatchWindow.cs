using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

public static class BatchEditing
{
    public static string Name(string pattern,ClothingAsset asset,int number)=>Regex.Replace(pattern,@"\{n(?::([^}]+))?\}",m=>number.ToString(m.Groups[1].Success?m.Groups[1].Value:"0",System.Globalization.CultureInfo.InvariantCulture)).Replace("{name}",asset.Name).Replace("{slot}",asset.Slot).Replace("{gender}",asset.GenderLabel);
    public static IEnumerable<string> SortFiles(IEnumerable<string> paths)=>paths.OrderBy(p=>Regex.Replace(Path.GetFileName(p),@"\d+",m=>m.Value.PadLeft(15,'0')),StringComparer.OrdinalIgnoreCase);
    public static void ImportVariations(ClothingAsset asset,IEnumerable<string> sources,string core)
    {
        var paths=SortFiles(sources).Where(TextureImport.IsSupported).ToArray();
        if(asset.TexturePaths.Count+paths.Length>26)throw new InvalidDataException("The import would exceed 26 variations. Select fewer files or remove existing variations first.");
        var prepared=new List<(string Path,TextureSourceLink? Link,string Label)>();
        foreach(var source in paths)
        {
            if(!File.Exists(source))throw new FileNotFoundException("Image not found",source);
            if(Path.GetExtension(source).Equals(".ytd",StringComparison.OrdinalIgnoreCase))prepared.Add((source,null,Path.GetFileNameWithoutExtension(source)));
            else
            {
                var destination=TextureImport.NewDestination(source);var template=asset.TexturePaths.FirstOrDefault();var name=TextureImport.Convert(source,destination,core,asset.ModelPath,template);
                prepared.Add((destination,new(){SourcePath=source,TextureName=name},Path.GetFileNameWithoutExtension(source)));
            }
        }
        foreach(var entry in prepared){asset.TexturePaths.Add(entry.Path);asset.TextureLabels[entry.Path]=entry.Label;if(entry.Link!=null)asset.TextureSources[entry.Path]=entry.Link;}
    }
}
public sealed class BatchWindow : StudioToolWindow
{
    public ClothingProject Result { get; }
    public BatchWindow(ClothingProject project,string core,string? selectedId):base("Batch editing",1020,720)
    {
        Result=ProjectAssets.Clone(project);
        var hint=Hint("Select multiple drawables with Ctrl or Shift. Changes stay in this window until Apply changes. Image folders attach to one selected drawable in natural filename order.");DockPanel.SetDock(hint,Dock.Top);Body.Children.Add(hint);
        var footer=Footer();var apply=Action("Apply changes");var cancel=Action("Cancel");cancel.IsCancel=true;footer.Children.Add(apply);footer.Children.Add(cancel);apply.Click+=(_,_)=>DialogResult=true;cancel.Click+=(_,_)=>Close();
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(380)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(18)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var list=new ListBox{ItemsSource=Result.Items,SelectionMode=SelectionMode.Extended,Background=new SolidColorBrush(Color.FromRgb(39,39,41)),Foreground=Brushes.White};grid.Children.Add(list);list.SelectedItem=Result.Items.FirstOrDefault(x=>x.Id==selectedId);
        var fields=new StackPanel();var scroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,2);grid.Children.Add(scroll);
        var pattern=Field(fields,"Naming pattern ({name}, {slot}, {gender}, {n:000})","{slot}_{n:000}");var start=Field(fields,"First number","0");
        var rename=Action("Rename selected");rename.Margin=new Thickness(0,10,0,10);fields.Children.Add(rename);var renumber=Action("Assign drawable positions");fields.Children.Add(renumber);
        var size=new ComboBox{ItemsSource=new[]{512,1024,2048,4096,8192},SelectedItem=2048,Height=30,Margin=new Thickness(0,6,0,6)};fields.Children.Add(new TextBlock{Text="Maximum texture dimension",Margin=new Thickness(0,16,0,0)});fields.Children.Add(size);
        var compression=new CheckBox{Content="Compress diffuse maps (DXT5) and generate mipmaps",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,8,0,10)};fields.Children.Add(compression);
        var optimize=Action("Optimize selected textures");fields.Children.Add(optimize);var import=Action("Import image folder to selected drawable...");import.Margin=new Thickness(0,18,0,10);fields.Children.Add(import);
        var status=Hint("No pending batch changes.");fields.Children.Add(status);
        List<ClothingAsset> Selected(){var selection=list.SelectedItems.Cast<ClothingAsset>().ToList();if(selection.Count==0)throw new InvalidOperationException("Select one or more drawables first.");return selection;}
        void Run(Action action){try{action();list.Items.Refresh();}catch(Exception ex){MessageBox.Show(this,(ex.InnerException??ex).Message,"Batch editing");}}
        rename.Click+=(_,_)=>Run(()=>{var items=Selected();var n=int.Parse(start.Text);var names=items.Select((a,i)=>BatchEditing.Name(pattern.Text,a,n+i)).ToArray();if(names.Any(string.IsNullOrWhiteSpace))throw new InvalidDataException("Names cannot be empty.");for(var i=0;i<items.Count;i++)items[i].Name=names[i];status.Text=$"Renamed {items.Count} drawables.";});
        renumber.Click+=(_,_)=>Run(()=>{var items=Selected();var n=int.Parse(start.Text);if(n<0)throw new InvalidDataException("Positions cannot be negative.");var assignments=items.GroupBy(a=>(a.Ped,a.Slot)).SelectMany(g=>g.Select((a,i)=>(Asset:a,Number:n+i))).ToArray();if(assignments.Any(x=>Result.Items.Except(items).Any(a=>a.Ped==x.Asset.Ped&&a.Slot==x.Asset.Slot&&a.Drawable==x.Number)))throw new InvalidDataException("A position is already occupied by an unselected item. Change the first number or select the entire slot group.");foreach(var x in assignments)x.Asset.Drawable=x.Number;status.Text=$"Assigned positions to {items.Count} drawables.";});
        async Task Work(Func<List<ClothingAsset>,string> job){try{var items=Selected();var max=(int)size.SelectedItem;var compressed=compression.IsChecked==true;optimize.IsEnabled=import.IsEnabled=apply.IsEnabled=false;status.Text="Preparing batch changes...";status.Text=await Task.Run(()=>job(items));}catch(Exception ex){status.Text=(ex.InnerException??ex).Message;}finally{optimize.IsEnabled=import.IsEnabled=apply.IsEnabled=true;}}
        optimize.Click+=async(_,_)=>{var limit=(int)size.SelectedItem;var compress=compression.IsChecked==true;await Work(items=>{var report=ProjectOptimizer.Apply(new ClothingProject{Items=items},core,limit,compress,false);return $"Optimized {report.Files} textures: {report.Before/1048576d:0.00} → {report.After/1048576d:0.00} MB.";});};
        import.Click+=async(_,_)=>{try{var items=Selected();if(items.Count!=1)throw new InvalidOperationException("Select exactly one drawable for folder import.");var picker=new Microsoft.Win32.OpenFolderDialog{Title="Choose texture variation folder"};if(picker.ShowDialog(this)!=true)return;var files=Directory.GetFiles(picker.FolderName).Where(TextureImport.IsSupported).ToArray();if(files.Length==0)throw new InvalidDataException("No supported images or YTDs in this folder.");await Work(selected=>{BatchEditing.ImportVariations(selected[0],files,core);return $"Imported {files.Length} linked variations.";});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Folder import");}};
    }
}
