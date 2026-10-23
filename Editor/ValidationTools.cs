using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

public sealed record ValidationFinding(string Severity,string AssetId,string Item,string Problem,string Fix)
{
    public override string ToString()=>$"{Severity}  /  {Item}\n{Problem}\n{Fix}";
}
public static class ProjectValidation
{
    public static List<ValidationFinding> Analyze(ClothingProject project,string core,IProgress<string>? progress=null,CancellationToken cancellation=default,bool trimMemory=false)
    {
        cancellation.ThrowIfCancellationRequested();
        var models = new Dictionary<string, ClothingInspection>(StringComparer.OrdinalIgnoreCase);
        var textures = new Dictionary<string, CodeWalkerPreview.TextureInspection>(StringComparer.OrdinalIgnoreCase);
        var findings=new List<ValidationFinding>();
        var basic=PackBuilder.Validate(project,(asset,issues)=>{foreach(var issue in issues)findings.Add(new("Error",asset.Id,asset.Name,issue,"Select this item and attach a valid model or texture, or correct its properties."));});
        foreach(var issue in basic.Where(x=>!findings.Any(f=>f.Problem==x)))findings.Add(new("Error","","Project",issue,"Correct project settings or tattoo properties before building."));
        foreach(var group in project.Items.GroupBy(x=>(x.Ped,x.Slot,x.Drawable)).Where(g=>g.Count()>1))
            foreach(var item in group)findings.Add(new("Warning",item.Id,item.Name,"Multiple items share this drawable position.","Use Batch editing to assign distinct positions. Export order is sorted by position."));
        foreach(var group in project.Items.GroupBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            foreach(var item in group)findings.Add(new("Warning",item.Id,item.Name,"Repeated display name makes the pack harder to identify.","Rename this item or use a batch naming pattern."));
        var position = 0;
        foreach(var item in project.Items)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Validating model {++position}/{project.Items.Count}: {item.Name}");
            if(!File.Exists(item.ModelPath))continue;
            try
            {
                if (!models.TryGetValue(item.ModelPath, out var inspection)) models[item.ModelPath] = inspection = CodeWalkerPreview.InspectClothing(item.ModelPath, core);
                var model=inspection.Model;
                var embedded=inspection.EmbeddedNames;
                var auxiliary=inspection.AuxiliaryNames;
                foreach(var lod in model.Lods.Skip(1).Where(x=>x.Triangles==0))findings.Add(new("Warning",item.Id,item.Name,$"Missing {lod.Level} LOD.","Inspect the model or generate conservative LOD copies in Model optimization."));
                if(model.Lods[0].Triangles>30000)findings.Add(new("Warning",item.Id,item.Name,$"High LOD contains {model.Lods[0].Triangles:N0} triangles.","Inspect your geometry budget and lower LODs before deploying."));
                foreach(var path in item.TexturePaths.Where(File.Exists))
                {
                    cancellation.ThrowIfCancellationRequested();
                    progress?.Report($"Validating textures {position}/{project.Items.Count}: {Path.GetFileName(path)}");
                    try
                    {
                        if (!textures.TryGetValue(path, out var stats)) textures[path] = stats = CodeWalkerPreview.InspectTextureDictionary(path, core);
                        var external=stats.Names;var names=external.Concat(embedded).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        // Clothing variations may carry a custom-named diffuse override.
                        // An unambiguous color map satisfies every DiffuseSampler, just
                        // as a streamed PED variation does in CodeWalker's renderer.
                        var binding=DiffuseBinding.Select(external,model.DiffuseNames,auxiliary);
                        if(binding==null)foreach(var missing in model.DiffuseNames.Where(x=>!names.Contains(x)))findings.Add(new("Error",item.Id,item.Name,$"{Path.GetFileName(path)} cannot resolve diffuse map '{missing}'.",$"Expected by model: {missing}. Available in this YTD or embedded in the model: {(names.Count == 0 ? "none" : string.Join(", ", names.Order(StringComparer.OrdinalIgnoreCase)))}. Attach a YTD with a matching diffuse name or one unambiguous color texture; normal/specular maps cannot be used as the color override."));
                        if(stats.LargestDimension>2048)findings.Add(new("Warning",item.Id,item.Name,$"{Path.GetFileName(path)} has a {stats.LargestDimension}px texture.","Use the texture optimizer if this resolution is unnecessary."));
                        if(stats.Textures==0)findings.Add(new("Error",item.Id,item.Name,"Empty texture dictionary: "+Path.GetFileName(path),"Replace the dictionary with a valid texture."));
                    }
                    catch(OperationCanceledException) { throw; }
                    catch(Exception ex){findings.Add(new("Error",item.Id,item.Name,"Unreadable texture: "+Path.GetFileName(path)+". "+(ex.InnerException??ex).Message,"Replace or re-export the texture dictionary."));}
                }
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception ex){findings.Add(new("Error",item.Id,item.Name,"Unreadable model: "+(ex.InnerException??ex).Message,"Replace or re-export this YDD."));}
            finally
            {
                // Only enabled in the isolated worker. Collecting its temporary native
                // resource buffers cannot suspend the editor's dispatcher or rendering.
                if (trimMemory && GC.GetTotalMemory(false) > 128L * 1024 * 1024)
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            }
        }
        foreach(var outfit in project.Outfits)
            foreach(var entry in outfit.Entries.Where(x=>!string.IsNullOrEmpty(x.AssetId)))
            {
                var asset=project.Items.FirstOrDefault(x=>x.Id==entry.AssetId);
                if(asset==null||asset.Ped!=outfit.Ped||asset.Slot!=entry.Slot || entry.Texture<0 || entry.Texture>=Math.Max(1,asset.TexturePaths.Count))findings.Add(new("Warning",entry.AssetId,outfit.Name,"Outfit contains a missing or incompatible item/variation.","Open Outfit builder and update this preset."));
            }
        cancellation.ThrowIfCancellationRequested();
        return findings;
    }
}
public static partial class CodeWalkerPreview
{
    public static List<string> AuxiliaryTextureNames(string path,string coreDll)
    {
        EnsureCore(coreDll);return MaterialTextureNames(path,false);
    }

    public static string? VariationDiffuseName(string path,string modelPath,string coreDll)
    {
        EnsureCore(coreDll);
        return DiffuseBinding.Select(TextureNames(path,coreDll),DiffuseTextureNames(modelPath),MaterialTextureNames(modelPath,false));
    }

    public static List<string> EmbeddedTextureNames(string path,string coreDll)
    {
        EnsureCore(coreDll);var type=core!.GetType("CodeWalker.GameFiles.YddFile",true)!;var file=Activator.CreateInstance(type)!;type.GetMethod("Load",[typeof(byte[])])!.Invoke(file,[File.ReadAllBytes(path)]);
        return Member(file, "Drawables") is Array drawables ? EmbeddedTextureNames(drawables) : [];
    }
    private static List<string> EmbeddedTextureNames(Array drawables)
    {
        var result=new List<string>();
        foreach(var drawable in drawables)
            if(drawable!=null && Member(drawable,"ShaderGroup") is {} shaders && Member(shaders,"TextureDictionary") is {} dict && Member(dict,"Textures") is {} entries && Member(entries,"data_items") is Array textures)
                result.AddRange(textures.Cast<object>().Select(t=>Member(t,"Name")?.ToString()??""));
        return result;
    }
}
public sealed class ValidationWindow : StudioToolWindow
{
    public ValidationWindow(ClothingProject project,string core,Action<string> select):base("Build validation",1000,710)
    {
        var hint=Hint("Check models, texture names, LODs and resource limits. Double-click a finding to select its drawable. Warnings do not prevent a build.");DockPanel.SetDock(hint,Dock.Top);Body.Children.Add(hint);
        var footer=Footer();var analyze=Action("Analyze project");var export=Action("Export report...");var close=Action("Close");close.IsCancel=true;footer.Children.Add(analyze);footer.Children.Add(export);footer.Children.Add(close);close.Click+=(_,_)=>Close();
        var list=new ListBox{Background=new SolidColorBrush(Color.FromRgb(39,39,41)),Foreground=Brushes.White,HorizontalContentAlignment=HorizontalAlignment.Stretch};Body.Children.Add(list);
        var template=new DataTemplate(typeof(ValidationFinding));var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.MarginProperty,new Thickness(7));template.VisualTree=text;list.ItemTemplate=template;
        list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is ValidationFinding finding&&!string.IsNullOrEmpty(finding.AssetId)){select(finding.AssetId);Close();}};
        List<ValidationFinding> findings=[];
        async Task Analyze(){analyze.IsEnabled=export.IsEnabled=false;hint.Text="Analyzing models and textures...";try{findings=await Task.Run(()=>ProjectValidation.Analyze(ProjectAssets.Clone(project),core));list.ItemsSource=findings;hint.Text=$"{findings.Count(x=>x.Severity=="Error")} errors / {findings.Count(x=>x.Severity=="Warning")} warnings. Double-click a finding to select its item.";}catch(Exception ex){hint.Text=ex.Message;}finally{analyze.IsEnabled=export.IsEnabled=true;}}
        analyze.Click+=async(_,_)=>await Analyze();Loaded+=async(_,_)=>await Analyze();
        export.Click+=(_,_)=>{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Text report|*.txt",FileName=project.Collection+"-validation.txt"};if(picker.ShowDialog(this)==true)File.WriteAllText(picker.FileName,string.Join("\n\n",findings));};
    }
}
