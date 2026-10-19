using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.Win32;

namespace DuckStudio;

public sealed class TattooPedWindow : StudioToolWindow
{
    private readonly TattooAsset tattoo;
    private readonly AppSettings settings;
    private readonly string core;
    private readonly Viewport3D viewport=new();
    private readonly TextBlock status=Hint("Loading PED skin UV preview...");
    private ModelVisual3D? visual;
    private Model3DGroup? original;
    private string? scene;
    private Point3D center;
    private double orbit,tilt=.08,distance=3.2;
    private Point? pointer;
    private bool hideClothes=true,closed;
    private readonly CancellationTokenSource cancellation=new();
    public TattooPedWindow(TattooAsset selected,AppSettings appSettings,string coreDll):base("Tattoo skin UV preview",1000,850)
    {
        tattoo=selected;settings=appSettings;core=coreDll;
        DockPanel.SetDock(status,Dock.Bottom);Body.Children.Add(status);
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(toolbar,Dock.Top);Body.Children.Add(toolbar);
        var hide=new CheckBox{Content="Hide clothing over skin",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,0,18,0),VerticalAlignment=VerticalAlignment.Center};toolbar.Children.Add(hide);hide.Click+=(_,_)=>{hideClothes=hide.IsChecked==true;ApplyTattoo();};
        var fit=Action("Fit PED");toolbar.Children.Add(fit);fit.Click+=(_,_)=>{distance=3.2;tilt=.08;orbit=0;Camera();};
        var border=new Border{Background=new SolidColorBrush(Color.FromRgb(53,104,153)),Child=viewport};Body.Children.Add(border);
        var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(180,180,180)));lights.Children.Add(new DirectionalLight(Color.FromRgb(185,185,185),new Vector3D(-1,-1.4,-1)));viewport.Children.Add(new ModelVisual3D{Content=lights});
        border.MouseLeftButtonDown+=(_,e)=>{pointer=e.GetPosition(viewport);border.CaptureMouse();};border.MouseLeftButtonUp+=(_,_)=>{pointer=null;border.ReleaseMouseCapture();};
        border.MouseMove+=(_,e)=>{if(pointer==null||e.LeftButton!=MouseButtonState.Pressed)return;var point=e.GetPosition(viewport);orbit+=OrbitControls.HorizontalDelta(point.X-pointer.Value.X, settings.InvertPanning, .01);tilt=Math.Clamp(tilt+(point.Y-pointer.Value.Y)*.007,-1.4,1.4);pointer=point;Camera();};
        border.MouseWheel+=(_,e)=>{distance=OrbitControls.ZoomDistance(distance,e.Delta,3.2);Camera();};
        tattoo.Changed+=Changed;Loaded+=async(_,_)=>await LoadAsync();
        Closed+=(_,_)=>{closed=true;cancellation.Cancel();tattoo.Changed-=Changed;if(scene!=null)try{Directory.Delete(scene,true);}catch(IOException){}};
    }
    private void Changed(object? sender,EventArgs e)=>ApplyTattoo();
    private async Task LoadAsync()
    {
        try
        {
            if(!File.Exists(Path.Combine(settings.GtaFolderPath,"GTA5.exe")))
            {
                var picker=new OpenFileDialog{Title="Locate GTA5.exe for tattoo skin preview",Filter="Grand Theft Auto V|GTA5.exe"};if(picker.ShowDialog(this)!=true){status.Text="Choose a GTA V installation to preview on a PED.";return;}settings.GtaFolderPath=Path.GetDirectoryName(picker.FileName)!;settings.Save();
            }
            var home=Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeftStudio");
            scene=Path.Combine(home,"scenes",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scene);
            var rage=Path.Combine(AppContext.BaseDirectory,"rage.exe");var gta=Path.Combine(settings.GtaFolderPath,"GTA5.exe");
            async Task Run(params string[] args)
            {
                var start=new ProcessStartInfo(rage){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.Environment["HOME"]=home;
                foreach(var arg in args)start.ArgumentList.Add(arg);
                using var process=Process.Start(start)??throw new IOException("Could not start the PED renderer.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                using var registration=cancellation.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
                await process.WaitForExitAsync(cancellation.Token);var log=await output+await error;if(process.ExitCode!=0)throw new IOException(log);
            }
            if(!Directory.Exists(Path.Combine(home,".rage-cli","index"))){status.Text="Indexing GTA V for the skin preview...";await Run("--no-update-check","--exe",gta,"index","build");}
            var female=tattoo.Gender=="GENDER_FEMALE";
            await Run("--no-update-check","--exe",gta,"ped-scene","--ped",female?"mp_f_freemode_01":"mp_m_freemode_01","--component","uppr=15","--component",female?"lowr=15":"lowr=14","-o",scene);
            if(closed)return;var model=PedScenePreview.Load(scene);original=((Model3DGroup)model.Model).CloneCurrentValue();center=model.Center;ApplyTattoo();Camera();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!closed)status.Text="Skin preview unavailable: "+(ex.InnerException??ex).Message;}
    }
    private void ApplyTattoo()
    {
        if(original==null||closed)return;
        try
        {
            var artwork=TattooWindow.ReadArtwork(tattoo,core);var model=original.CloneCurrentValue();var target=tattoo.Zone=="ZONE_HEAD"?"head":tattoo.Zone.EndsWith("LEG")?"lowr":"uppr";
            foreach(var geometry in model.Children.OfType<GeometryModel3D>().ToArray())
            {
                var slot=(string)geometry.GetValue(PedScenePreview.SourceSlotProperty);
                if(hideClothes&&slot is "jbib" or "task" or "accs" or "teef" or "decl"){model.Children.Remove(geometry);continue;}
                if(slot!=target||geometry.Material is not DiffuseMaterial{Brush:ImageBrush{ImageSource:BitmapSource skin}})continue;
                var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())
                {
                    context.DrawImage(skin,new Rect(0,0,skin.PixelWidth,skin.PixelHeight));
                    var center=new Point(tattoo.U*skin.PixelWidth,tattoo.V*skin.PixelHeight);context.PushTransform(new RotateTransform(tattoo.Rotation*180/Math.PI,center.X,center.Y));context.DrawImage(artwork,new Rect(center.X-tattoo.ScaleX*skin.PixelWidth/2,center.Y-tattoo.ScaleY*skin.PixelHeight/2,Math.Max(.1,tattoo.ScaleX*skin.PixelWidth),Math.Max(.1,tattoo.ScaleY*skin.PixelHeight)));context.Pop();
                }
                var bitmap=new RenderTargetBitmap(skin.PixelWidth,skin.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();
                var material=new DiffuseMaterial(new ImageBrush(bitmap){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill});geometry.Material=geometry.BackMaterial=material;
            }
            if(visual!=null)viewport.Children.Remove(visual);visual=new ModelVisual3D{Content=model};viewport.Children.Add(visual);
            status.Text="Drag to rotate; scroll to zoom. This previews the artwork on the skin UV atlas. Verify the game's decoration projection when testing the built tattoo pack.";
        }
        catch(Exception ex){status.Text="Tattoo preview could not refresh: "+ex.Message;}
    }
    private void Camera(){var offset=new Vector3D(distance*Math.Cos(tilt)*Math.Sin(orbit),distance*Math.Sin(tilt),distance*Math.Cos(tilt)*Math.Cos(orbit));viewport.Camera=new PerspectiveCamera(center+offset,-offset,new Vector3D(0,1,0),45){NearPlaneDistance=OrbitControls.NearPlane(distance)};}
}
