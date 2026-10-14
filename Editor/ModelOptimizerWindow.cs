using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DuckStudio;

public sealed class ModelOptimizerWindow : StudioToolWindow
{
    public string? ReplacementPath { get; private set; }
    public ModelOptimizerWindow(ClothingAsset asset,string core):base("Model optimization - "+asset.Name,1080,760)
    {
        var hint=Hint("Inspect triangle counts and switch LOD previews. Generate creates Medium, Low and Very low LODs from High using conservative vertex clustering. It preserves High, materials, skin weights and open boundaries. Ratios are goals; seams can limit reduction. Review the result before applying.");DockPanel.SetDock(hint,Dock.Top);Body.Children.Add(hint);
        var footer=Footer();var apply=Action("Apply generated model");apply.IsEnabled=false;var cancel=Action("Cancel");cancel.IsCancel=true;footer.Children.Add(apply);footer.Children.Add(cancel);apply.Click+=(_,_)=>DialogResult=true;cancel.Click+=(_,_)=>Close();
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(310)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(16)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var fields=new StackPanel();grid.Children.Add(new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var preview=new StudioViewport();Grid.SetColumn(preview,2);grid.Children.Add(preview);
        var stats=new TextBlock{TextWrapping=TextWrapping.Wrap,LineHeight=21};fields.Children.Add(stats);fields.Children.Add(new TextBlock{Text="Preview LOD",Margin=new Thickness(0,15,0,6)});
        var lod=new ComboBox{ItemsSource=new[]{"High","Medium","Low","Very low"},SelectedIndex=0,Height=30};fields.Children.Add(lod);
        var medium=Field(fields,"Medium triangle ratio","0.65");var low=Field(fields,"Low triangle ratio","0.35");var very=Field(fields,"Very low triangle ratio","0.15");var generate=Action("Generate LOD copy");generate.Margin=new Thickness(0,15,0,0);fields.Children.Add(generate);
        var current=asset.ModelPath;
        void Refresh(){var metrics=CodeWalkerPreview.InspectModel(current,core);stats.Text=string.Join('\n',metrics.Lods)+$"\n{metrics.Bones} embedded bones\n"+(metrics.HasWeights?"Skin weights present":"Rigid / no skin weights");try{preview.Set(CodeWalkerPreview.Load(current,asset.TexturePaths.FirstOrDefault(),core,lod.SelectedIndex));preview.Front();}catch(Exception ex){hint.Text=(ex.InnerException??ex).Message;}}
        lod.SelectionChanged+=(_,_)=>Refresh();
        generate.Click+=async(_,_)=>
        {
            try
            {
                double Parse(TextBox box)=>double.Parse(box.Text,System.Globalization.CultureInfo.InvariantCulture);var ratios=new[]{Parse(medium),Parse(low),Parse(very)};var destination=Path.Combine(Path.GetDirectoryName(TextureImport.NewDestination(asset.ModelPath))!,Path.GetFileName(asset.ModelPath));generate.IsEnabled=apply.IsEnabled=false;hint.Text="Generating and verifying LOD copy...";
                var reduced=await Task.Run(()=>ModelTools.GenerateLods(asset.ModelPath,destination,core,ratios[0],ratios[1],ratios[2]));ReplacementPath=destination;current=destination;Refresh();hint.Text=$"Generated independent LOD copy; removed {reduced:N0} triangles across the lower levels. Compare each LOD before applying.";apply.IsEnabled=true;
            }
            catch(Exception ex){hint.Text=(ex.InnerException??ex).Message;}
            finally{generate.IsEnabled=true;}
        };
        Refresh();Loaded+=(_,_)=>preview.Front();
    }
}
