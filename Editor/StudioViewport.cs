using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DuckStudio;

public sealed class StudioViewport : Grid
{
    public Viewport3D Viewport { get; }=new(){ClipToBounds=true};
    private readonly PerspectiveCamera camera=new(){FieldOfView=40,NearPlaneDistance=.01};
    private ModelVisual3D? visual;
    private Point3D center;private double extent=2,orbit,distance=5,elevation=.06;private Point? drag;
    private bool invertPanning;
    public Model3D? Model=>visual?.Content;
    public StudioViewport()
    {
        Background=new SolidColorBrush(Color.FromRgb(42,44,48));ClipToBounds=true;Viewport.Camera=camera;Children.Add(Viewport);
        var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(160,166,173)));lights.Children.Add(new DirectionalLight(Colors.White,new(-1,-1,-2)));lights.Children.Add(new DirectionalLight(Color.FromRgb(115,130,148),new(1,-.5,1)));Viewport.Children.Add(new ModelVisual3D{Content=lights});
        MouseDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Left && OrbitControls.IsSurfaceInput(e.OriginalSource as DependencyObject, this)){invertPanning=AppSettings.Load().InvertPanning;drag=e.GetPosition(this);CaptureMouse();}};MouseUp+=(_,_)=>{drag=null;ReleaseMouseCapture();};
        MouseMove+=(_,e)=>{if(drag==null||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(this);orbit+=OrbitControls.HorizontalDelta(p.X-drag.Value.X,invertPanning);elevation=Math.Clamp(elevation+(p.Y-drag.Value.Y)*.008,-1.3,1.3);drag=p;UpdateCamera();};
        MouseWheel+=(_,e)=>{if(!OrbitControls.IsSurfaceInput(e.OriginalSource as DependencyObject, this))return;distance=OrbitControls.ZoomDistance(distance,e.Delta,Fit());UpdateCamera();e.Handled=true;};LostMouseCapture+=(_,_)=>drag=null;SizeChanged+=(_,_)=>{if(visual!=null)UpdateCamera();};
    }
    public void Set(NativePreviewResult result,bool preserve=false)
    {
        if(visual!=null)Viewport.Children.Remove(visual);visual=new(){Content=result.Model};Viewport.Children.Add(visual);center=result.Center;extent=result.Extent;if(!preserve){orbit=0;elevation=.06;distance=Fit();}UpdateCamera();
    }
    private double Fit()
    {
        var horizontal=camera.FieldOfView*Math.PI/360;var vertical=Math.Atan(Math.Tan(horizontal)*Math.Max(ActualHeight,1)/Math.Max(ActualWidth,1));var bounds=Model?.Bounds??Rect3D.Empty;
        if(bounds.IsEmpty)return Math.Max(extent,.2)*.85/Math.Sin(Math.Min(horizontal,vertical));
        return Math.Max(bounds.SizeX/2/Math.Tan(horizontal),bounds.SizeY/2/Math.Tan(vertical))*1.14+bounds.SizeZ/2;
    }
    private void UpdateCamera(){camera.NearPlaneDistance=OrbitControls.NearPlane(distance);camera.Position=new(center.X+distance*Math.Cos(elevation)*Math.Sin(orbit),center.Y+distance*Math.Sin(elevation),center.Z+distance*Math.Cos(elevation)*Math.Cos(orbit));camera.LookDirection=center-camera.Position;camera.UpDirection=new(0,1,0);}
    public void Front(){orbit=0;elevation=.06;distance=Fit();UpdateCamera();}
    public static BitmapSource Checker()
    {
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())for(var y=0;y<8;y++)for(var x=0;x<8;x++)
        {
            dc.DrawRectangle(new SolidColorBrush((x+y)%2==0?Color.FromRgb(205,210,216):Color.FromRgb(68,88,115)),null,new Rect(x*64,y*64,64,64));
            dc.DrawText(new FormattedText($"{(char)('A'+y)}{x+1}",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),18,Brushes.OrangeRed,1),new Point(x*64+13,y*64+20));
        }
        var bitmap=new RenderTargetBitmap(512,512,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();return bitmap;
    }
    public BitmapSource Capture(int width,int height,double angle,bool transparent)
    {
        if(Model==null)throw new InvalidOperationException("Load a model before taking screenshots.");
        var surface=new Grid{Width=width,Height=height,Background=transparent?null:new SolidColorBrush(Color.FromRgb(42,44,48))};var view=new Viewport3D{Width=width,Height=height};surface.Children.Add(view);
        var bounds=Model.Bounds;var target=new Point3D(bounds.X+bounds.SizeX/2,bounds.Y+bounds.SizeY/2,bounds.Z+bounds.SizeZ/2);var fov=40d;var horizontal=fov*Math.PI/360;var vertical=Math.Atan(Math.Tan(horizontal)*height/width);
        var span=Math.Abs(Math.Cos(angle))*bounds.SizeX+Math.Abs(Math.Sin(angle))*bounds.SizeZ;var depth=Math.Abs(Math.Cos(angle))*bounds.SizeZ+Math.Abs(Math.Sin(angle))*bounds.SizeX;
        var dist=Math.Max(span/2/Math.Tan(horizontal),bounds.SizeY/2/Math.Tan(vertical))*1.14+depth/2;
        var position=new Point3D(target.X+dist*Math.Sin(angle),target.Y+dist*.04,target.Z+dist*Math.Cos(angle));view.Camera=new PerspectiveCamera(position,target-position,new(0,1,0),fov){NearPlaneDistance=.01};
        var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(160,166,173)));lights.Children.Add(new DirectionalLight(Colors.White,new(-1,-1,-2)));view.Children.Add(new ModelVisual3D{Content=lights});view.Children.Add(new ModelVisual3D{Content=Model.CloneCurrentValue()});
        surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(surface);image.Freeze();return image;
    }
    public static void SavePng(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(path);encoder.Save(output);}
}
