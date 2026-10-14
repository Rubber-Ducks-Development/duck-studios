using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DuckStudio;

public sealed class OutfitWindow : StudioToolWindow
{
    public ClothingProject Result { get; }
    private readonly StudioViewport viewport=new();
    private StudioScene? scene;
    private CancellationTokenSource? loading;
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly DispatcherTimer animation=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private readonly Stopwatch elapsed=Stopwatch.StartNew();
    private bool closed;
    public OutfitWindow(ClothingProject project,AppSettings settings,string core):base("Outfit builder",1250,840)
    {
        Result=ProjectAssets.Clone(project);
        var status=Hint("Combine project clothing and props with base-game components. Use Hide to inspect clipping. Fitting poses use your skin weights; test exact animations and tattoo projection in FiveM.");DockPanel.SetDock(status,Dock.Top);Body.Children.Add(status);
        var footer=Footer();var export=Action("Export outfits for FiveM...");var apply=Action("Save presets and close");var cancel=Action("Cancel");cancel.IsCancel=true;footer.Children.Add(export);footer.Children.Add(apply);footer.Children.Add(cancel);apply.Click+=(_,_)=>DialogResult=true;cancel.Click+=(_,_)=>Close();
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(370)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(16)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var controls=new StackPanel();grid.Children.Add(new ScrollViewer{Content=controls,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Grid.SetColumn(viewport,2);grid.Children.Add(viewport);
        controls.Children.Add(new TextBlock{Text="Saved outfit"});var presets=new ComboBox{Height=30,Margin=new Thickness(0,6,0,10)};controls.Children.Add(presets);var row=new WrapPanel();controls.Children.Add(row);var add=Action("New outfit");var remove=Action("Remove");row.Children.Add(add);row.Children.Add(remove);
        var name=Field(controls,"Outfit name","");controls.Children.Add(new TextBlock{Text="Character",Margin=new Thickness(0,9,0,6)});var gender=new ComboBox{ItemsSource=new[]{"Male","Female"},Height=30};controls.Children.Add(gender);
        controls.Children.Add(new TextBlock{Text="Fitting pose",Margin=new Thickness(0,9,0,6)});var pose=new ComboBox{ItemsSource=PreviewPoses.Names,SelectedIndex=0,Height=30};controls.Children.Add(pose);var play=new CheckBox{Content="Play animation",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,9,0,9)};controls.Children.Add(play);var load=Action("Refresh preview");controls.Children.Add(load);
        var entries=new StackPanel{Margin=new Thickness(0,16,0,0)};controls.Children.Add(entries);var tattoos=new StackPanel();controls.Children.Add(tattoos);
        var building=false;
        OutfitPreset? Current()=>presets.SelectedItem as OutfitPreset;
        void Queue(){if(building||closed)return;debounce.Stop();debounce.Start();}
        void Fill()
        {
            if(Current() is not {} outfit)return;building=true;name.Text=outfit.Name;gender.SelectedIndex=outfit.Ped.Contains("mp_f_")?1:0;entries.Children.Clear();tattoos.Children.Clear();
            foreach(var slot in ProjectIO.Slots)
            {
                var entry=outfit.Entries.FirstOrDefault(x=>x.Slot==slot);if(entry==null){entry=new(){Slot=slot,Hidden=slot.StartsWith("p_")};outfit.Entries.Add(entry);}
                var field=entry;entries.Children.Add(new TextBlock{Text=ProjectIO.SlotLabel(slot),Margin=new Thickness(0,9,0,5),Foreground=Muted});
                var choice=new ComboBox{Height=30};var baseItem=new ComboBoxItem{Content=slot.StartsWith("p_")?"No prop":"Base game",Tag="base"};var hideItem=new ComboBoxItem{Content="Hide",Tag="hide"};if(!slot.StartsWith("p_"))choice.Items.Add(baseItem);choice.Items.Add(hideItem);
                foreach(var asset in Result.Items.Where(x=>x.Ped==outfit.Ped&&x.Slot==slot)){var option=new ComboBoxItem{Content=asset.Name,Tag=asset.Id};choice.Items.Add(option);if(field.AssetId==asset.Id&&!field.Hidden)choice.SelectedItem=option;}
                if(choice.SelectedItem==null)choice.SelectedItem=field.Hidden?hideItem:slot.StartsWith("p_")?hideItem:baseItem;entries.Children.Add(choice);
                var indices=new WrapPanel{Margin=new Thickness(0,5,0,0)};entries.Children.Add(indices);indices.Children.Add(new TextBlock{Text="Base drawable",VerticalAlignment=VerticalAlignment.Center});var drawable=new TextBox{Text=field.BaseDrawable.ToString(),Width=48,Margin=new Thickness(7,0,12,0),IsEnabled=string.IsNullOrEmpty(field.AssetId)&&!field.Hidden};indices.Children.Add(drawable);indices.Children.Add(new TextBlock{Text="Texture",VerticalAlignment=VerticalAlignment.Center});var texture=new TextBox{Text=field.Texture.ToString(),Width=48,Margin=new Thickness(7,0,0,0)};indices.Children.Add(texture);
                choice.SelectionChanged+=(_,_)=>{if(building||choice.SelectedItem is not ComboBoxItem{Tag:string value})return;field.Hidden=value=="hide";field.AssetId=value is "base" or "hide"?"":value;field.Texture=0;texture.Text="0";drawable.IsEnabled=value=="base";Queue();};
                drawable.TextChanged+=(_,_)=>{if(!building&&int.TryParse(drawable.Text,out var value)&&value>=0){field.BaseDrawable=value;Queue();}};
                texture.TextChanged+=(_,_)=>{if(!building&&int.TryParse(texture.Text,out var value)&&value>=0){var asset=Result.Items.FirstOrDefault(x=>x.Id==field.AssetId);if(asset!=null&&value>=Math.Max(1,asset.TexturePaths.Count)){texture.ToolTip="This item has "+Math.Max(1,asset.TexturePaths.Count)+" variations, starting at zero.";return;}field.Texture=value;Queue();}};
            }
            tattoos.Children.Add(new TextBlock{Text="Tattoo artwork (UV approximation)",Margin=new Thickness(0,18,0,9)});
            foreach(var tattoo in Result.Tattoos.Where(t=>t.Gender=="GENDER_DONTCARE"||t.Gender==(outfit.Ped.Contains("mp_f_")?"GENDER_FEMALE":"GENDER_MALE")))
            {
                var check=new CheckBox{Content=tattoo.Name,IsChecked=outfit.TattooIds.Contains(tattoo.Id),Foreground=Brushes.White,Margin=new Thickness(0,0,0,7)};tattoos.Children.Add(check);check.Click+=(_,_)=>{if(check.IsChecked==true&&!outfit.TattooIds.Contains(tattoo.Id))outfit.TattooIds.Add(tattoo.Id);else if(check.IsChecked!=true)outfit.TattooIds.Remove(tattoo.Id);Queue();};
            }
            building=false;Queue();
        }
        void RefreshPresets(OutfitPreset? selected){building=true;presets.ItemsSource=null;presets.ItemsSource=Result.Outfits;presets.SelectedItem=selected;building=false;Fill();}
        add.Click+=(_,_)=>{var outfit=new OutfitPreset{Name="Outfit "+(Result.Outfits.Count+1),Ped=Current()?.Ped??"mp_m_freemode_01"};Result.Outfits.Add(outfit);RefreshPresets(outfit);};
        remove.Click+=(_,_)=>{if(Current() is {} outfit){Result.Outfits.Remove(outfit);RefreshPresets(Result.Outfits.FirstOrDefault());if(Current()==null){entries.Children.Clear();tattoos.Children.Clear();}}};
        presets.SelectionChanged+=(_,_)=>{if(!building)Fill();};name.TextChanged+=(_,_)=>{if(!building&&Current() is {} outfit){outfit.Name=name.Text;}};gender.SelectionChanged+=(_,_)=>{if(building||Current() is not {} outfit)return;outfit.Ped=gender.SelectedIndex==1?"mp_f_freemode_01":"mp_m_freemode_01";outfit.Entries.Clear();outfit.TattooIds.Clear();Fill();};
        async Task Load()
        {
            if(closed||Current() is not {} outfit)return;loading?.Cancel();var cancellation=new CancellationTokenSource();loading=cancellation;status.Text="Loading outfit and linked textures...";
            try
            {
                var snapshot=ProjectAssets.Clone(Result);var selected=snapshot.Outfits.First(x=>x.Id==outfit.Id);var next=await StudioSceneService.LoadOutfitAsync(snapshot,selected,settings.GtaFolderPath,core,cancellation.Token);
                if(closed||cancellation.IsCancellationRequested){next.Dispose();return;}scene?.Dispose();scene=next;ApplyTattoos(next.Result.Model,Result.Tattoos.Where(t=>outfit.TattooIds.Contains(t.Id)),core);viewport.Set(next.Result,true);viewport.Front();
                status.Text=PreviewPoses.CanAnimate(next.Result.Model)?"Outfit loaded. Drag to rotate; scroll to zoom. Fitting poses use the model's rig. Tattoo placement is a UV approximation.":"Outfit loaded. No usable skin rig was found for animation.";
                PreviewPoses.Apply(next.Result.Model,pose.SelectedItem?.ToString()??"Standing",elapsed.Elapsed.TotalSeconds);
            }
            catch(OperationCanceledException){}
            catch(Exception ex){if(!closed&&!cancellation.IsCancellationRequested)status.Text=(ex.InnerException??ex).Message;}
            finally{if(ReferenceEquals(loading,cancellation))loading=null;cancellation.Dispose();}
        }
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Load();};load.Click+=async(_,_)=>await Load();pose.SelectionChanged+=(_,_)=>{if(viewport.Model is {} model)PreviewPoses.Apply(model,pose.SelectedItem?.ToString()??"Standing",elapsed.Elapsed.TotalSeconds);};
        animation.Tick+=(_,_)=>{if(play.IsChecked==true&&viewport.Model is {} model&&pose.SelectedIndex>0)PreviewPoses.Apply(model,pose.SelectedItem!.ToString()!,elapsed.Elapsed.TotalSeconds);};animation.Start();
        foreach(var asset in project.Items)asset.TexturesChanged+=Updated;
        void Updated(object? sender,EventArgs e){if(sender is ClothingAsset asset&&Result.Items.FirstOrDefault(x=>x.Id==asset.Id) is {} copy){copy.TexturePaths=[..asset.TexturePaths];copy.TextureSources=asset.TextureSources;Queue();}}
        export.Click+=(_,_)=>{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Outfit JSON|*.json",FileName=project.Collection+"-outfits.json"};if(picker.ShowDialog(this)==true)try{System.IO.File.WriteAllText(picker.FileName,System.Text.Json.JsonSerializer.Serialize(FiveMCompanion.Outfits(Result),ProjectIO.Json));}catch(Exception ex){MessageBox.Show(this,ex.Message,"Outfit export");}};
        Closed+=(_,_)=>{closed=true;debounce.Stop();animation.Stop();loading?.Cancel();scene?.Dispose();foreach(var asset in project.Items)asset.TexturesChanged-=Updated;};
        if(Result.Outfits.Count==0)Result.Outfits.Add(new OutfitPreset());RefreshPresets(Result.Outfits[0]);
    }
    public static void ApplyTattoos(System.Windows.Media.Media3D.Model3D model,IEnumerable<TattooAsset> tattoos,string core)
    {
        foreach(var tattoo in tattoos)
        {
            var artwork=TattooWindow.ReadArtwork(tattoo,core);var target=tattoo.Zone=="ZONE_HEAD"?"head":tattoo.Zone.EndsWith("LEG")?"lowr":"uppr";
            foreach(var geometry in PreviewPoses.Geometry(model))
            {
                if((string)geometry.GetValue(PedScenePreview.SourceSlotProperty)!=target||geometry.Material is not DiffuseMaterial{Brush:ImageBrush{ImageSource:BitmapSource skin}})continue;
                var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawImage(skin,new Rect(0,0,skin.PixelWidth,skin.PixelHeight));var center=new Point(tattoo.U*skin.PixelWidth,tattoo.V*skin.PixelHeight);dc.PushTransform(new RotateTransform(tattoo.Rotation*180/Math.PI,center.X,center.Y));dc.DrawImage(artwork,new Rect(center.X-tattoo.ScaleX*skin.PixelWidth/2,center.Y-tattoo.ScaleY*skin.PixelHeight/2,tattoo.ScaleX*skin.PixelWidth,tattoo.ScaleY*skin.PixelHeight));dc.Pop();}
                var bitmap=new RenderTargetBitmap(skin.PixelWidth,skin.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();geometry.Material=geometry.BackMaterial=new DiffuseMaterial(new ImageBrush(bitmap){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill});
            }
        }
    }
}
