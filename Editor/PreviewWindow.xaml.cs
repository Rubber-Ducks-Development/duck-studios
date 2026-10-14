using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DuckStudio;

public partial class PreviewWindow : Window
{
    private readonly ClothingAsset asset;
    private readonly AppSettings settings;
    private readonly DispatcherTimer rotationTimer;
    private ModelVisual3D? currentVisual;
    private ModelVisual3D? floorVisual;
    private ModelVisual3D? gridVisual;
    private readonly List<(GeometryModel3D Model, Material Original, Material? Back)> materials = [];
    private Point3D center = new(0, 0, 0);
    private double extent = 2, orbitX = 0, orbitY = .05, distance = 6;
    private Point? dragPoint;
    private string? currentScene;
    private int loadVersion;
    private bool pedMode;
    private bool ready;
    private CancellationTokenSource? loadCancellation;
    private readonly Stopwatch poseTime=Stopwatch.StartNew();

    public PreviewWindow(ClothingAsset selected, AppSettings appSettings)
    {
        asset = selected;
        settings = appSettings;
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !PanelDismissal.HasOpenDropdown(e.OriginalSource as DependencyObject)) { e.Handled = true; Close(); }
        };
        Title = $"Duck Studio - Clothes Preview - {asset.Name}";
        pedMode = !asset.IsProp && File.Exists(Path.Combine(settings.GtaFolderPath, "GTA5.exe"));
        PedStatusText.Text = pedMode ? "Loading the clothing on a full PED." : "Showing the selected clothing only.";
        RenderModeBox.SelectedIndex = 0;
        BackgroundBox.SelectedIndex = 0;
        PoseBox.SelectedIndex = 0;
        foreach (var path in asset.TexturePaths) TextureBox.Items.Add(Path.GetFileName(path));
        if (TextureBox.Items.Count > 0) TextureBox.SelectedIndex = 0;
        rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        rotationTimer.Tick += (_, _) => { if (AutoRotateBox.IsChecked == true && currentVisual != null) { orbitX += .006; UpdateCamera(); } };
        rotationTimer.Tick += (_, _) => { if(PlayPoseBox.IsChecked==true && PoseBox.SelectedIndex>0 && currentVisual?.Content is {} model)PreviewPoses.Apply(model,PreviewPoses.Names[PoseBox.SelectedIndex],poseTime.Elapsed.TotalSeconds); };
        rotationTimer.Start();
        asset.TexturesChanged += LinkedTextureChanged;
    }
    private void WindowLoaded(object sender, RoutedEventArgs e) { ready = true; _ = LoadAsync(); }
    private void WindowClosed(object? sender, EventArgs e)
    {
        ready = false; asset.TexturesChanged -= LinkedTextureChanged; ++loadVersion; loadCancellation?.Cancel(); rotationTimer.Stop();
        if (currentScene != null) DeleteScene(currentScene);
    }
    private void ToggleMenuClick(object sender, RoutedEventArgs e)
    {
        var opening = MenuColumn.Width.Value == 0;
        MenuColumn.Width = new GridLength(opening ? 310 : 0);
        OpenMenuButton.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        MenuPane.BeginAnimation(OpacityProperty, null);
        if (opening)
        {
            MenuPane.Opacity = 0;
            MenuPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
        if (currentVisual != null) { distance = Math.Max(distance, FitDistance() * .5); UpdateCamera(); }
    }
    private void TextureChanged(object sender, SelectionChangedEventArgs e) { if (ready) _ = LoadAsync(); }
    private void OnlyClothClick(object sender, RoutedEventArgs e) { pedMode = false; _ = LoadAsync(); }
    private void FitPedClick(object sender, RoutedEventArgs e)
    {
        if (asset.IsProp) { PedStatusText.Text = "PED fitting for props is unavailable; showing the selected prop in 3D."; OptionTabs.SelectedIndex = 1; return; }
        if (!File.Exists(Path.Combine(settings.GtaFolderPath, "GTA5.exe")))
        {
            var picker = new OpenFileDialog { Title = "Locate GTA5.exe for full PED preview", Filter = "Grand Theft Auto V (GTA5.exe)|GTA5.exe" };
            if (picker.ShowDialog(this) != true) return;
            settings.GtaFolderPath = Path.GetDirectoryName(picker.FileName)!; settings.Save();
        }
        pedMode = true; _ = LoadAsync();
    }
    private void LinkedTextureChanged(object? sender, EventArgs e)
    {
        if (!ready) return;
        var index = TextureBox.SelectedIndex;
        ready = false;
        TextureBox.Items.Clear();
        foreach (var path in asset.TexturePaths) TextureBox.Items.Add(asset.TextureLabels.GetValueOrDefault(path, Path.GetFileName(path)));
        TextureBox.SelectedIndex = asset.TexturePaths.Count > 0 ? Math.Clamp(index, 0, asset.TexturePaths.Count - 1) : -1;
        ready = true;
        _ = LoadAsync(true);
    }
    private string? TexturePath => TextureBox.SelectedIndex >= 0 && TextureBox.SelectedIndex < asset.TexturePaths.Count ? asset.TexturePaths[TextureBox.SelectedIndex] : null;
    private async Task LoadAsync(bool preserveCamera = false)
    {
        if (!File.Exists(asset.ModelPath)) { HintText.Text = "Model file is missing"; HintText.Visibility = Visibility.Visible; return; }
        var version = ++loadVersion;
        loadCancellation?.Cancel();
        var cancellationSource = new CancellationTokenSource();
        loadCancellation = cancellationSource;
        var cancellation = cancellationSource.Token;
        var rage = Path.Combine(AppContext.BaseDirectory, "rage.exe");
        if (!File.Exists(rage)) { HintText.Text = "3D renderer is missing"; HintText.Visibility = Visibility.Visible; loadCancellation = null; cancellationSource.Dispose(); return; }
        var home = Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeftStudio");
        var folder = Path.Combine(home, "scenes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        HintText.Text = pedMode ? "Loading PED and clothing from GTA V..." : "Loading clothing model...";
        HintText.Visibility = Visibility.Visible;
        PedButton.IsEnabled = false;
        try
        {
            var start = new ProcessStartInfo(rage) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["HOME"] = home;
            start.ArgumentList.Add("--no-update-check");
            if (pedMode)
            {
                await EnsureIndexAsync(rage, home, Path.Combine(settings.GtaFolderPath, "GTA5.exe"), cancellation);
                cancellation.ThrowIfCancellationRequested();
                foreach (var value in new[] { "--exe", Path.Combine(settings.GtaFolderPath, "GTA5.exe"), "ped-scene", "--ped", asset.Ped, "--override-slot", asset.Slot, "--override-ydd", asset.ModelPath, "-o", folder }) start.ArgumentList.Add(value);
                if (TexturePath is { } pedTexture) { start.ArgumentList.Add("--override-ytd"); start.ArgumentList.Add(pedTexture); }
            }
            else
            {
                foreach (var value in new[] { "asset-scene", "--ydd", asset.ModelPath, "-o", folder }) start.ArgumentList.Add(value);
                if (TexturePath is { } texture) { start.ArgumentList.Add("--ytd"); start.ArgumentList.Add(texture); }
            }
            using var process = Process.Start(start) ?? throw new IOException("Could not start the 3D renderer.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(cancellation); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                throw;
            }
            var log = await stdout + await stderr;
            if (process.ExitCode != 0) throw new IOException(log);
            if (version != loadVersion) return;
            var result = PedScenePreview.Load(folder);
            if (currentVisual != null) PreviewViewport.Children.Remove(currentVisual);
            if (currentScene != null) DeleteScene(currentScene);
            currentScene = folder;
            currentVisual = new ModelVisual3D { Content = result.Model };
            PreviewViewport.Children.Add(currentVisual);
            center = result.Center; extent = result.Extent;
            RecordMaterials(result.Model);
            PoseBox.IsEnabled=PreviewPoses.CanAnimate(result.Model);
            if(PoseBox.IsEnabled)PreviewPoses.Apply(result.Model,PreviewPoses.Names[Math.Max(0,PoseBox.SelectedIndex)],poseTime.Elapsed.TotalSeconds);
            ApplyDisplayOptions();
            if (preserveCamera) UpdateCamera(); else ResetCamera();
            UpdateModelData(result.Model);
            UpdateTextureData();
            HintText.Visibility = Visibility.Collapsed;
            PedStatusText.Text = pedMode ? "Full PED preview loaded." : "Showing the selected clothing only.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == loadVersion) { HintText.Text = "Preview unavailable"; HintText.Visibility = Visibility.Visible; PedStatusText.Text = ex.Message; }
        }
        finally
        {
            if (currentScene != folder) DeleteScene(folder);
            if (version == loadVersion) PedButton.IsEnabled = true;
            if (ReferenceEquals(loadCancellation, cancellationSource)) loadCancellation = null;
            cancellationSource.Dispose();
        }
    }
    private static async Task EnsureIndexAsync(string rage, string home, string gtaExe, CancellationToken cancellation)
    {
        Directory.CreateDirectory(home);
        var stampPath = Path.Combine(home, "renderer-index.txt");
        var stamp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rage))) + "|" + Path.GetFullPath(gtaExe) + "|" + File.GetLastWriteTimeUtc(gtaExe).Ticks;
        if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp) return;
        var start = new ProcessStartInfo(rage) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["HOME"] = home;
        foreach (var value in new[] { "--no-update-check", "--exe", gtaExe, "index", "build" }) start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the GTA indexer.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            throw;
        }
        var log = await stdout + await stderr;
        if (process.ExitCode != 0) throw new IOException("Could not index GTA V: " + log);
        File.WriteAllText(stampPath, stamp);
    }
    private static void DeleteScene(string folder) { try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private void RecordMaterials(Model3D model)
    {
        materials.Clear();
        if (model is Model3DGroup group)
            foreach (var child in group.Children.OfType<GeometryModel3D>())
                if (child.Material != null) materials.Add((child, child.Material, child.BackMaterial));
    }
    private void UpdateModelData(Model3D model)
    {
        var meshes = materials.Select(x => x.Model.Geometry).OfType<MeshGeometry3D>().ToList();
        ModelDataText.Text = $"Name: {asset.Name}\nPed: {asset.Ped}\nSlot: {ProjectIO.SlotLabel(asset.Slot)}\nModel: {Path.GetFileName(asset.ModelPath)}\n\nMeshes: {meshes.Count}\nVertices: {meshes.Sum(m => m.Positions.Count):N0}\nTriangles: {meshes.Sum(m => m.TriangleIndices.Count / 3):N0}\nModel size: {ClothingAsset.FileSize(asset.ModelPath)}";
    }
    private void UpdateTextureData()
    {
        var path = TexturePath;
        TextureDataText.Text = path == null ? "No YTD variation selected." : $"{Path.GetFileName(path)}\n{ClothingAsset.FileSize(path)}";
        TextureImage.Source = null;
        if (path == null) return;
        try
        {
            var core = Path.Combine(AppContext.BaseDirectory, "CodeWalker.Core.dll");
            TextureImage.Source = CodeWalkerPreview.LoadTextureFile(path, core);
            if (TextureImage.Source is BitmapSource bitmap) TextureDataText.Text += $"\n{bitmap.PixelWidth} × {bitmap.PixelHeight} px";
        }
        catch (Exception ex) { TextureDataText.Text += "\nTexture thumbnail unavailable: " + ex.Message; }
    }
    private void DisplayOptionChanged(object sender, EventArgs e) { if (ready) ApplyDisplayOptions(); }
    private void LightDirectionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        var angle = LightSlider.Value * Math.PI / 180;
        Sun.Direction = new Vector3D(Math.Cos(angle), -1.4, Math.Sin(angle));
    }
    private void ApplyDisplayOptions()
    {
        var targetColor = (Color)ColorConverter.ConvertFromString(BackgroundBox.SelectedIndex switch { 1 => "#242426", 2 => "#777777", _ => "#356899" });
        var surfaceBrush = new SolidColorBrush((PreviewSurface.Background as SolidColorBrush)?.Color ?? targetColor);
        PreviewSurface.Background = surfaceBrush;
        surfaceBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(160)));
        var angle = LightSlider.Value * Math.PI / 180;
        Sun.Direction = new Vector3D(Math.Cos(angle), -1.4, Math.Sin(angle));
        foreach (var entry in materials)
        {
            if (RenderModeBox.SelectedIndex == 1)
            {
                var solid = new DiffuseMaterial(Brush("#C0C8CA")); entry.Model.Material = solid; entry.Model.BackMaterial = solid;
            }
            else { entry.Model.Material = entry.Original; entry.Model.BackMaterial = entry.Back; }
        }
        if (floorVisual != null) PreviewViewport.Children.Remove(floorVisual);
        if (gridVisual != null) PreviewViewport.Children.Remove(gridVisual);
        floorVisual = FloorBox.IsChecked == true ? MakeFloor(false) : null;
        gridVisual = GridBox.IsChecked == true ? MakeFloor(true) : null;
        if (floorVisual != null) PreviewViewport.Children.Add(floorVisual);
        if (gridVisual != null) PreviewViewport.Children.Add(gridVisual);
    }
    private ModelVisual3D MakeFloor(bool lines)
    {
        var half = Math.Max(extent * .8, 1.5);
        var y = center.Y - extent * .52;
        var mesh = new MeshGeometry3D();
        void Quad(double x1,double z1,double x2,double z2)
        {
            var i=mesh.Positions.Count;
            foreach(var p in new[]{new Point3D(x1,y,z1),new Point3D(x2,y,z1),new Point3D(x2,y,z2),new Point3D(x1,y,z2)}) mesh.Positions.Add(p);
            foreach(var j in new[]{0,1,2,0,2,3})mesh.TriangleIndices.Add(i+j);
        }
        if (!lines) Quad(center.X-half,center.Z-half,center.X+half,center.Z+half);
        else
        {
            var step = half / 5; var width = step * .015;
            for(int i=-5;i<=5;i++)
            {
                var offset=i*step;
                Quad(center.X+offset-width,center.Z-half,center.X+offset+width,center.Z+half);
                Quad(center.X-half,center.Z+offset-width,center.X+half,center.Z+offset+width);
            }
        }
        var material = new DiffuseMaterial(Brush(lines ? "#B3C8D8" : "#4C6981"));
        return new ModelVisual3D { Content = new GeometryModel3D(mesh,material) { BackMaterial = material } };
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private void PoseChanged(object sender,SelectionChangedEventArgs e)
    {
        if(currentVisual?.Content is {} model && PoseBox.SelectedIndex>=0)PreviewPoses.Apply(model,PreviewPoses.Names[PoseBox.SelectedIndex],poseTime.Elapsed.TotalSeconds);
    }
    private void ResetCameraClick(object sender, RoutedEventArgs e) => ResetCamera();
    private void ResetCamera() { orbitX = 0; orbitY = .05; distance = FitDistance(); UpdateCamera(); }
    private double FitDistance()
    {
        var width=Math.Max(PreviewViewport.ActualWidth,1); var height=Math.Max(PreviewViewport.ActualHeight,1);
        var horizontal=PreviewCamera.FieldOfView*Math.PI/360; var vertical=Math.Atan(Math.Tan(horizontal)*height/width);
        return Math.Max(extent,.2)*Math.Sqrt(3)/2*1.2/Math.Sin(Math.Min(horizontal,vertical));
    }
    private void UpdateCamera()
    {
        var position=new Point3D(center.X+distance*Math.Cos(orbitY)*Math.Sin(orbitX),center.Y+distance*Math.Sin(orbitY),center.Z+distance*Math.Cos(orbitY)*Math.Cos(orbitX));
        PreviewCamera.Position=position; PreviewCamera.LookDirection=center-position; PreviewCamera.UpDirection=new Vector3D(0,1,0);
    }
    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e) { if (currentVisual != null) UpdateCamera(); }
    private void ViewportMouseDown(object sender, MouseButtonEventArgs e) { if(e.ChangedButton==MouseButton.Left){dragPoint=e.GetPosition(PreviewViewport);PreviewViewport.CaptureMouse();} }
    private void ViewportMouseUp(object sender, MouseButtonEventArgs e) { dragPoint=null;PreviewViewport.ReleaseMouseCapture(); }
    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if(dragPoint == null || e.LeftButton != MouseButtonState.Pressed)return;
        var p=e.GetPosition(PreviewViewport); orbitX+=(p.X-dragPoint.Value.X)*.008;orbitY=Math.Clamp(orbitY+(p.Y-dragPoint.Value.Y)*.008,-1.35,1.35);dragPoint=p;UpdateCamera();
    }
    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e) { distance=Math.Clamp(distance*(e.Delta>0?.86:1.16),FitDistance()*.4,FitDistance()*15);UpdateCamera();e.Handled=true; }
    private void SaveScreenshotClick(object sender, RoutedEventArgs e)
    {
        var picker=new SaveFileDialog { Filter="PNG image (*.png)|*.png", FileName=asset.Name+"-preview.png" };
        if(picker.ShowDialog(this)!=true)return;
        var width=(int)Math.Max(1,PreviewSurface.ActualWidth);var height=(int)Math.Max(1,PreviewSurface.ActualHeight);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(PreviewSurface);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(picker.FileName);encoder.Save(file);
    }
}


