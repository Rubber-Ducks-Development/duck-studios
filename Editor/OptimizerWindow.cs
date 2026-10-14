using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

public sealed class OptimizerWindow : StudioToolWindow
{
    public int MaxDimension=>int.Parse((string)dimensions.SelectedItem);
    public bool Compress=>compression.IsChecked==true;
    public bool RemoveDuplicates=>duplicates.IsChecked==true;
    private readonly ComboBox dimensions;
    private readonly CheckBox compression,duplicates;
    public OptimizerWindow(ClothingProject project,string core):base("Optimizers",1040,680)
    {
        var description=Hint("Analyze texture sizes and repeated content, then create optimized copies. Original files, skin weights, model geometry, normal maps and specular maps remain available. History keeps a checkpoint before applying changes.");DockPanel.SetDock(description,Dock.Top);Body.Children.Add(description);
        var options=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(options,Dock.Top);Body.Children.Add(options);
        options.Children.Add(new TextBlock{Text="Maximum texture size",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,9,0)});
        dimensions=new ComboBox{ItemsSource=new[]{"512","1024","2048","4096","8192"},SelectedItem="2048",Width=90,Height=30};options.Children.Add(dimensions);
        compression=new CheckBox{Content="DXT5 compression and mipmaps",IsChecked=true,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(18,0,0,0)};options.Children.Add(compression);
        duplicates=new CheckBox{Content="Remove identical variations",ToolTip="Removes repeated variations and renumbers the remaining variations. Original files are kept; history can restore the previous list.",Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(18,0,0,0)};options.Children.Add(duplicates);
        var footer=Footer();var analyze=Action("Analyze");var apply=Action("Create optimized copies");var close=Action("Cancel");close.IsCancel=true;footer.Children.Add(analyze);footer.Children.Add(apply);footer.Children.Add(close);
        var results=new ListBox{Background=new SolidColorBrush(Color.FromRgb(38,38,40)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(83,83,85))};Body.Children.Add(results);
        analyze.Click+=async(_,_)=>{analyze.IsEnabled=false;results.ItemsSource=new[]{"Analyzing texture sizes and duplicate content..."};try{var limit=MaxDimension;results.ItemsSource=await Task.Run(()=>ProjectOptimizer.Analyze(project,core,limit));}catch(Exception ex){MessageBox.Show(this,(ex.InnerException??ex).Message,"Analysis failed");}finally{analyze.IsEnabled=true;}};
        apply.IsEnabled=project.Items.Any(a=>a.TexturePaths.Count>0);
        apply.Click+=(_,_)=>DialogResult=true;close.Click+=(_,_)=>DialogResult=false;
        Loaded+=(_,_)=>analyze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
