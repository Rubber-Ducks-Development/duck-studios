using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Input;

namespace DuckStudio;

public sealed class UvCanvas : FrameworkElement
{
    public BitmapSource? Texture { get; set; }
    public List<MeshGeometry3D> Meshes { get; set; }=[];
    public int SelectedMesh { get; set; }=-1;
    public int[] SelectedTriangle { get; set; }=[];
    public bool Wireframe { get; set; }=true;
    protected override void OnRender(DrawingContext dc)
    {
        var width=ActualWidth;var height=ActualHeight;dc.PushClip(new RectangleGeometry(new Rect(0,0,width,height)));dc.DrawRectangle(Brushes.DimGray,null,new Rect(0,0,width,height));
        if(Texture!=null)dc.DrawImage(Texture,new Rect(0,0,width,height));
        if(Wireframe)for(var m=0;m<Meshes.Count;m++)
        {
            var mesh=Meshes[m];var pen=new Pen(m==SelectedMesh?Brushes.Orange:Brushes.DeepSkyBlue,m==SelectedMesh?1.4:.65);pen.Freeze();
            for(var i=0;i+2<mesh.TriangleIndices.Count;i+=3)
            {
                var vertices=new[]{mesh.TriangleIndices[i],mesh.TriangleIndices[i+1],mesh.TriangleIndices[i+2]};if(vertices.Any(v=>v>=mesh.TextureCoordinates.Count))continue;
                for(var j=0;j<3;j++){var a=mesh.TextureCoordinates[vertices[j]];var b=mesh.TextureCoordinates[vertices[(j+1)%3]];dc.DrawLine(pen,new Point(a.X*width,a.Y*height),new Point(b.X*width,b.Y*height));}
            }
        }
        if(SelectedMesh>=0&&SelectedMesh<Meshes.Count&&SelectedTriangle.Length==3)
        {
            var mesh=Meshes[SelectedMesh];var shape=new StreamGeometry();using(var ctx=shape.Open()){var uv=SelectedTriangle.Select(v=>mesh.TextureCoordinates[v]).Select(p=>new Point(p.X*width,p.Y*height)).ToArray();ctx.BeginFigure(uv[0],true,true);ctx.PolyLineTo(uv.Skip(1).ToArray(),true,false);}dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150,255,220,0)),new Pen(Brushes.Yellow,2),shape);
        }
        dc.Pop();
    }
}
public sealed class UvWindow : StudioToolWindow
{
    public UvWindow(ClothingAsset asset,string core):base("UV tools - "+asset.Name,1220,790)
    {
        var hint=Hint("Click the model to locate a triangle on its UV atlas. Select a surface to highlight its UVs. Checker labels reveal stretching and flipped islands. Export saves the actual texture dimensions.");DockPanel.SetDock(hint,Dock.Top);Body.Children.Add(hint);
        var footer=Footer();var export=Action("Export UV overlay...");var close=Action("Close");close.IsCancel=true;footer.Children.Add(export);footer.Children.Add(close);close.Click+=(_,_)=>Close();
        var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(toolbar,Dock.Top);Body.Children.Add(toolbar);
        var textures=new ComboBox{Width=290,Height=30,ItemsSource=asset.TexturePaths.Select(p=>System.IO.Path.GetFileName(p)).ToList(),SelectedIndex=asset.TexturePaths.Count>0?0:-1};toolbar.Children.Add(textures);
        var check=new CheckBox{Content="Checker texture",Foreground=Brushes.White,Margin=new Thickness(14,6,12,0)};var wire=new CheckBox{Content="UV wireframe",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,6,12,0)};toolbar.Children.Add(check);toolbar.Children.Add(wire);
        var surfaces=new ComboBox{Width=220,Height=30};toolbar.Children.Add(surfaces);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(12)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var canvas=new UvCanvas{Width=1024,Height=1024};var box=new Viewbox{Child=canvas,Stretch=Stretch.Uniform};grid.Children.Add(box);var preview=new StudioViewport();Grid.SetColumn(preview,2);grid.Children.Add(preview);
        NativePreviewResult result=CodeWalkerPreview.Load(asset.ModelPath,asset.TexturePaths.FirstOrDefault(),core);preview.Set(result);
        var geometry=PreviewPoses.Geometry(result.Model).ToList();canvas.Meshes=geometry.Select(g=>(MeshGeometry3D)g.Geometry).ToList();surfaces.ItemsSource=new[]{"All surfaces"}.Concat(geometry.Select((_,i)=>"Surface "+(i+1))).ToList();surfaces.SelectedIndex=0;
        var originals=geometry.Select(g=>g.Material).ToArray();BitmapSource? texture=null;
        void Refresh()
        {
            texture=textures.SelectedIndex>=0?CodeWalkerPreview.LoadTextureFile(asset.TexturePaths[textures.SelectedIndex],core,modelPath:asset.ModelPath):null;canvas.Texture=check.IsChecked==true?StudioViewport.Checker():texture;canvas.Height=canvas.Texture!=null?1024d*canvas.Texture.PixelHeight/canvas.Texture.PixelWidth:1024;canvas.Wireframe=wire.IsChecked==true;canvas.SelectedMesh=surfaces.SelectedIndex-1;
            for(var i=0;i<geometry.Count;i++)
            {
                var material=texture==null?originals[i]:new DiffuseMaterial(new ImageBrush(PreviewTexture.ForMaterial(texture,PreviewTexture.AlphaMode.Opaque)){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill});
                if(check.IsChecked==true)material=new DiffuseMaterial(new ImageBrush(canvas.Texture){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill});
                if(i==canvas.SelectedMesh){var group=new MaterialGroup();group.Children.Add(material);group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(50,255,175,0))));material=group;}
                geometry[i].Material=geometry[i].BackMaterial=material;
            }
            canvas.InvalidateVisual();
        }
        textures.SelectionChanged+=(_,_)=>Refresh();surfaces.SelectionChanged+=(_,_)=>Refresh();check.Checked+=(_,_)=>Refresh();check.Unchecked+=(_,_)=>Refresh();wire.Checked+=(_,_)=>Refresh();wire.Unchecked+=(_,_)=>Refresh();
        preview.Viewport.PreviewMouseDown+=(_,e)=>
        {
            if(e.ChangedButton!=MouseButton.Left)return;
            VisualTreeHelper.HitTest(preview.Viewport,null,r=>{if(r is RayMeshGeometry3DHitTestResult hit&&hit.ModelHit is GeometryModel3D model){var index=geometry.IndexOf(model);if(index>=0){surfaces.SelectedIndex=index+1;canvas.SelectedTriangle=[hit.VertexIndex1,hit.VertexIndex2,hit.VertexIndex3];canvas.InvalidateVisual();var uv=hit.MeshHit.TextureCoordinates[hit.VertexIndex1];hint.Text=$"Surface {index+1} selected. Triangle begins at UV {uv.X:0.000}, {uv.Y:0.000}.";}return HitTestResultBehavior.Stop;}return HitTestResultBehavior.Continue;},new PointHitTestParameters(e.GetPosition(preview.Viewport)));
        };
        export.Click+=(_,_)=>
        {
            var picker=new Microsoft.Win32.SaveFileDialog{Filter="PNG image|*.png",FileName=asset.Name+"-uv.png"};if(picker.ShowDialog(this)!=true)return;
            var width=texture?.PixelWidth??2048;var height=texture?.PixelHeight??2048;var copy=new UvCanvas{Width=width,Height=height,Texture=canvas.Texture,Meshes=canvas.Meshes,SelectedMesh=canvas.SelectedMesh,Wireframe=true};copy.Measure(new Size(width,height));copy.Arrange(new Rect(0,0,width,height));copy.UpdateLayout();var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(copy);StudioViewport.SavePng(image,picker.FileName);
        };
        Refresh();
    }
}
