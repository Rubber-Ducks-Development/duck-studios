using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuckStudio;

static class EditorInteractionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(string root, Action<bool, string> check)
    {
        check(OrbitControls.HorizontalDelta(100, false) < 0 && OrbitControls.HorizontalDelta(-100, false) > 0,
            "Default orbit follows horizontal pointer direction");
        check(OrbitControls.HorizontalDelta(100, true) == -OrbitControls.HorizontalDelta(100, false), "Inverted orbit reverses direction");
        var close = 5d;
        for (int i = 0; i < 60; i++) close = OrbitControls.ZoomDistance(close, 120, 5);
        check(close > 0 && close <= .01 && OrbitControls.NearPlane(close) < close, "Zoom reaches close detail distances with a smaller near clipping plane");
        check(Math.Abs(OrbitControls.ZoomDistance(OrbitControls.ZoomDistance(5, 60, 5), 60, 5) - OrbitControls.ZoomDistance(5,120,5)) < .000001, "Partial wheel deltas produce proportional smooth zoom");
        var settings = new AppSettings { InvertPanning = true };
        settings.Save();
        check(AppSettings.Load().InvertPanning, "Inverted panning persists across settings reload");

        var male = new ClothingAsset { Name = "Male vest", ModelPath = Path.Combine(root, "mp_m_freemode_01^task_000_u.ydd"), Slot = "task" };
        var female = new ClothingAsset { Name = "Female vest", ModelPath = Path.Combine(root, "mp_f_freemode_01^task_000_u.ydd"), Slot = "task", Ped = "mp_f_freemode_01" };
        File.WriteAllBytes(male.ModelPath, [1]); File.WriteAllBytes(female.ModelPath, [1]);
        var ytd = Path.Combine(root, "elsewhere", "task_diff_000_h_uni.ytd");
        check(ClothingImport.TextureTarget([male], ytd, null, null) == male, "YTD-only import matches an existing drawable from another folder");
        check(ClothingImport.TextureTarget([male, female], ytd, null, null) == null, "Ambiguous YTD import never guesses a gender");
        check(ClothingImport.TextureTarget([male, female], ytd, female.Ped, null) == female, "Gender-specific YTD import selects only the requested gender");
        check(ClothingImport.TextureTarget([male, female], ytd, null, female) == female, "Selected drawable resolves an unnamed YTD gender");
        check(ClothingImport.TextureTarget([male, female], Path.Combine(root, "custom.YTD"), null, female) == female, "Arbitrary YTD filenames can attach to the selected drawable");
        var wrongGender = Path.Combine(root, "mp_f_freemode_01^task_diff_000_h_uni.ytd");
        check(ClothingImport.TextureTarget([male], wrongGender, null, male) == null, "Named female YTD cannot silently attach to a male drawable");
        var duplicate = new ClothingAsset { ModelPath = Path.Combine(root, "other", "task_000_u.ydd"), Slot = "task" };
        check(ClothingImport.TextureTarget([male, duplicate], ytd, null, null) == null, "Ambiguous collections require explicit texture attachment");
        check(ClothingImport.NextPosition([], male.Ped, male.Slot) == 0, "First item in a clothing type starts at position zero");
        check(ClothingImport.NextPosition([new() { Drawable = 9, Slot = "task" }, new() { Drawable = 100, Slot = "jbib" }, new() { Drawable = 90, Slot = "task", Ped = female.Ped }], male.Ped, male.Slot) == 10,
            "Next position follows the highest position for that gender and clothing type");

        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        for (int i = 0; i < 8; i++)
        {
            var texture = Path.Combine(root, $"task_diff_000_{(char)('a' + i)}_uni.ytd");
            File.WriteAllBytes(texture, [1]); male.TexturePaths.Add(texture);
        }
        var window = new MainWindow();
        var project = new ClothingProject { Name = "List checks", Collection = "list_checks", OutputFolder = root, Items = [female, male] };
        typeof(MainWindow).GetField("project", Private)!.SetValue(window, project);
        typeof(MainWindow).GetField("selected", Private)!.SetValue(window, male);
        Call(window, "RefreshList"); Call(window, "SyncItem");
        var textures = (ListBox)window.FindName("TextureList");
        using (var buildStop = new CancellationTokenSource())
        {
            typeof(MainWindow).GetField("buildCancellation", Private)!.SetValue(window, buildStop);
            typeof(MainWindow).GetField("buildBusy", Private)!.SetValue(window, true);
            Call(window, "ShowCancelBuild");
            check(window.IsEnabled && textures.IsEnabled && ((Button)window.FindName("BuildButton")).IsEnabled,
                "Build mode keeps the window and list rendering enabled with an active Cancel build button");
            var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            Call(window, "BuildPreviewMouseDown", window, mouse);
            check(mouse.Handled, "Build mode blocks editor mouse input while keeping the window enabled");
            Call(window, "BuildClick", window, new RoutedEventArgs());
            check(buildStop.IsCancellationRequested, "The build button cancels an active build without opening another dialog");
            typeof(MainWindow).GetField("buildCancellation", Private)!.SetValue(window, null);
            typeof(MainWindow).GetField("buildBusy", Private)!.SetValue(window, false);
            Call(window, "SyncProject");
        }
        var variations = (ComboBox)window.FindName("PreviewTextureBox");
        textures.SelectedIndex = 7;
        check(variations.SelectedIndex == 7, "Selecting texture H synchronizes the 3D preview selection");
        Call(window, "SyncItem");
        check(textures.SelectedIndex == 7 && variations.SelectedIndex == 7, "Refreshing item details preserves selected texture H");
        variations.SelectedIndex = 3;
        check(textures.SelectedIndex == 3, "Preview variation selection synchronizes back to the texture list");
        var list = (ListBox)window.FindName("AssetList");
        var view = (System.Windows.Data.ListCollectionView)list.ItemsSource;
        var groups = view.Groups!.Cast<System.Windows.Data.CollectionViewGroup>().ToList();
        check(groups.Count == 1 && groups[0].Items.Count == 2 && groups[0].Items.Cast<object>().All(a => a is ClothingAsset), "Male and female share a single clothing category without gender dropdowns");
        check(groups[0].Items.Cast<ClothingAsset>().SequenceEqual([male, female]), "Male items appear before female items inside the clothing list");

        var incoming = Directory.CreateDirectory(Path.Combine(root, "Incoming")).FullName;
        var import0 = Path.Combine(incoming, "task_000_u.ydd");
        var import1 = Path.Combine(incoming, "task_001_u.ydd");
        var importTexture = Path.Combine(incoming, "task_diff_000_h_uni.ytd");
        foreach (var path in new[] { import0, import1, importTexture }) File.WriteAllBytes(path, [1]);
        Call(window, "ImportPaths", new[] { import0, import1, importTexture }, null!);
        var added = project.Items.Where(a => a.ModelPath == import0 || a.ModelPath == import1).OrderBy(a => a.Drawable).ToArray();
        check(added.Select(a => a.Drawable).SequenceEqual([1, 2]), "Multi-file Add assigns consecutive positions after existing items");
        check(added.All(a => a.IsNew) && !male.IsNew && !female.IsNew, "Only newly added clothing is marked for yellow highlighting");
        check(added[0].TexturePaths.Single() == importTexture, "Renumbered imports retain textures matched by original source filename");
        check(list.Items.Cast<ClothingAsset>().SequenceEqual(new[] { male }.Concat(added).Append(female)), "New male items append below existing male items and above female items");
        Call(window, "ImportPaths", new[] { import0 }, null!);
        check(project.Items.Count == 4 && added[0].Drawable == 1, "Reimporting the same source preserves its existing position");
        list.SelectedItem = added[0];
        check(added[0].IsNew, "Programmatic selection preserves a new row's timed highlight");
        Call(window, "CopyGenderClick", window, new RoutedEventArgs());
        var copied = project.Items.Last();
        check(copied.Ped == female.Ped && copied.Drawable == 1, "Copy to other gender appends at that gender's next position");
        check(list.Items.Cast<ClothingAsset>().Last() == copied, "New female copy appears at the bottom of the clothing list");
        var resource = Directory.CreateDirectory(Path.Combine(root, "ResourceAppend")).FullName;
        var stream = Directory.CreateDirectory(Path.Combine(resource, "stream")).FullName;
        File.WriteAllText(Path.Combine(resource, "fxmanifest.lua"), "fx_version 'cerulean'\ngame 'gta5'");
        var resourceModel = Path.Combine(stream, "task_005_u.ydd");
        File.WriteAllBytes(resourceModel, [1]); File.WriteAllBytes(Path.Combine(stream, "task_diff_005_a_uni.ytd"), [1]);
        Call(window, "LoadResource", resource, false);
        check(project.Items.Last().Drawable == 3 && project.Items.Last().ModelPath == resourceModel && project.Items.Last().TexturePaths.Count == 1,
            "Resource additions append at the next position and keep their original texture links");
        var saved = Path.Combine(root, "ordered-list.duckproj"); ProjectIO.Save(project, saved);
        check(ProjectIO.Load(saved).Items.Select(a => a.Drawable).SequenceEqual(project.Items.Select(a => a.Drawable)), "Assigned positions survive project save and reopen");
        var preview = new PreviewWindow(male, settings, 7);
        check(((ComboBox)preview.FindName("TextureBox")).SelectedIndex == 7, "Separate preview opens with H instead of resetting to A");
        PreviewDebugChecks(window, preview, male, root, check);
        ((ColumnDefinition)preview.FindName("MenuColumn")).Width = new GridLength(310);
        var content = (FrameworkElement)preview.Content;
        Layout(content, 900, 740);
        var tabs = (TabControl)preview.FindName("OptionTabs");
        var positions = tabs.Items.Cast<TabItem>().Select(t => t.TranslatePoint(new Point(), tabs)).ToArray();
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i; Layout(content, 900, 740);
            check(tabs.Items.Cast<TabItem>().Select((t, j) => (t.TranslatePoint(new Point(), tabs) - positions[j]).Length < .01).All(x => x), $"Preview tab {i + 1} keeps every header in its original position");
        }
        var mainContent = (FrameworkElement)window.Content;
        Layout(mainContent, 1280, 845);
        var expanders = Descendants<Expander>(list).ToList();
        check(expanders.Count == 1, "Only the clothing category renders a collapse header");
        var toggle = Descendants<ToggleButton>(expanders[0]).First();
        var arrow = Descendants<System.Windows.Shapes.Path>(toggle).First();
        check(toggle.Background is SolidColorBrush b && b.Color.A == 0 && arrow.Stroke is SolidColorBrush c && c.Color.R > 150, "Collapse arrows render light strokes on transparent dark headers");
        expanders[0].IsExpanded = false; Layout(mainContent, 1280, 845);
        check(((FrameworkElement)expanders[0].Template.FindName("Body", expanders[0])).Visibility == Visibility.Collapsed, "Clothing category header collapses its items");
        expanders[0].IsExpanded = true; Layout(mainContent, 1280, 845);
        Save(mainContent, Path.Combine(root, "library.png"), 1280, 845);
        list.SelectedItem = added[0]; Layout(mainContent, 1280, 845);
        var newRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(added[0]);
        var rowBackground = (Border)newRow.Template.FindName("Row", newRow);
        var tint = Descendants<Border>(newRow).First(b => b.Background is SolidColorBrush c && c.Color == (Color)ColorConverter.ConvertFromString("#FFF2B3"));
        check(tint.Opacity == 1 && newRow.Foreground is SolidColorBrush text && text.Color.R < 60,
            "New selected row uses light yellow with dark, readable text");
        Save(mainContent, Path.Combine(root, "new-row-yellow.png"), 1280, 845);
        newRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        Layout(mainContent, 1280, 845);
        check(added[0].IsNew && tint.Opacity == 1, "Clicking a new row does not control its timed highlight");
        added[0].UpdateNewHighlight(TimeSpan.FromSeconds(1.9)); Layout(mainContent, 1280, 845);
        check(tint.Opacity == 1, "New item remains yellow for the first two seconds");
        added[0].UpdateNewHighlight(TimeSpan.FromSeconds(2.35)); Layout(mainContent, 1280, 845);
        check(tint.Opacity > .45 && tint.Opacity < .55, "Yellow highlight fades gradually after two seconds");
        added[0].UpdateNewHighlight(TimeSpan.FromSeconds(2.7)); Layout(mainContent, 1280, 845);
        check(!added[0].IsNew && tint.Opacity == 0 && rowBackground.Background is SolidColorBrush normal && normal.Color == (Color)ColorConverter.ConvertFromString("#62666A"),
            "Highlight returns to normal automatically without clicking");
        check(added[1].IsNew && copied.IsNew, "Each new row has its own highlight lifetime");
        Call(window, "RefreshList"); Layout(mainContent, 1280, 845);
        check(!added[0].IsNew && added[1].IsNew, "List refresh does not restart expired highlights");
        var snapshot = System.Text.Json.JsonSerializer.Serialize(project, ProjectIO.Json);
        added[1].IsNew = false;
        check(System.Text.Json.JsonSerializer.Serialize(project, ProjectIO.Json) == snapshot, "Fading the UI highlight does not change saved project data");
        ScrollAndToolbarChecks(window, project, root, check);
        Save(content, Path.Combine(root, "preview-tabs.png"), 900, 740);
        ReportChecks.Run(root,check);
        preview.Close();
        Call(window, "WindowClosed", window, EventArgs.Empty);
    }
    static void PreviewDebugChecks(MainWindow window, PreviewWindow preview, ClothingAsset asset, string root, Action<bool,string> check)
    {
        var surface = (Grid)preview.FindName("PreviewSurface");
        Layout((FrameworkElement)preview.Content, 900, 740);
        var empty = VisualTreeHelper.HitTest(surface, new Point(surface.ActualWidth - 10, 80))!.VisualHit;
        check(OrbitControls.IsSurfaceInput(empty, surface), "Empty blue preview background receives orbit and zoom input");
        var menu = (Button)preview.FindName("OpenMenuButton");
        check(!OrbitControls.IsSurfaceInput(menu, surface), "Preview menu buttons do not start camera gestures");
        var camera = (System.Windows.Media.Media3D.PerspectiveCamera)preview.FindName("PreviewCamera");
        var before = (double)typeof(PreviewWindow).GetField("distance",Private)!.GetValue(preview)!;
        surface.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.MouseWheelEvent});
        check((double)typeof(PreviewWindow).GetField("distance",Private)!.GetValue(preview)! < before, "Scrolling empty blue preview background zooms the camera");
        for (int i=0; i<60; i++) surface.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.MouseWheelEvent});
        var zoomed=(double)typeof(PreviewWindow).GetField("distance",Private)!.GetValue(preview)!;
        check(zoomed < .02 && camera.NearPlaneDistance < zoomed, "Standalone preview zooms much closer than the previous fitted-distance limit");
        typeof(PreviewWindow).GetField("currentVisual",Private)!.SetValue(preview,new System.Windows.Media.Media3D.ModelVisual3D());
        Call(preview,"ToggleMenuClick",preview,new RoutedEventArgs());
        check(Math.Abs((double)typeof(PreviewWindow).GetField("distance",Private)!.GetValue(preview)!-zoomed)<.000001,"Opening preview options preserves close zoom");
        typeof(PreviewWindow).GetField("currentVisual",Private)!.SetValue(preview,null);
        var embedded=(Grid)window.FindName("ModelSurface");
        typeof(MainWindow).GetField("activeModel",Private)!.SetValue(window,new System.Windows.Media.Media3D.ModelVisual3D());
        for (int i=0; i<60; i++) embedded.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.MouseWheelEvent});
        var embeddedZoom=(double)typeof(MainWindow).GetField("distance",Private)!.GetValue(window)!;
        check(embeddedZoom<.02&&((System.Windows.Media.Media3D.PerspectiveCamera)window.FindName("ModelCamera")).NearPlaneDistance<embeddedZoom,"Embedded preview shares close zoom and clipping behavior");
        Call(window,"ViewportSizeChanged",window,null!);
        check(Math.Abs((double)typeof(MainWindow).GetField("distance",Private)!.GetValue(window)!-embeddedZoom)<.000001,"Resizing the main preview preserves close zoom");
        typeof(MainWindow).GetField("activeModel",Private)!.SetValue(window,null);
        check(((ComboBoxItem)((ComboBox)preview.FindName("BackgroundBox")).Items[0]).Content.ToString()=="Duck Blue", "Background option is branded Duck Blue");
        var linked = asset.TexturePaths[7]; asset.TextureSources[linked] = new TextureSourceLink { SourcePath=Path.Combine(root,"chosen.png"), TextureName="chosen_diffuse" };
        var id = PreviewDiagnostics.Begin(asset,linked,"regression");
        var folder = Directory.CreateDirectory(Path.Combine(root,"DebugScene")).FullName;
        File.WriteAllText(Path.Combine(folder,"scene.json"), "{\"meshes\":[{\"slot\":\"task\",\"texture_name\":\"chosen_diffuse\",\"shader_diffuse\":\"base_diffuse\",\"texture_origin\":\"selected-ytd\",\"texture\":\"slot9_chosen.png\"}]}");
        PreviewDiagnostics.Complete(id,folder,"test renderer completed");
        var report = PreviewDiagnostics.Report(Path.Combine(root,"uniform.dctproj"),asset,7);
        check(report.Contains("SELECTED h") && report.Contains("chosen.png") && report.Contains("chosen_diffuse") && report.Contains("selected-ytd") && report.Contains("uniform.dctproj"), "Debug report identifies project, chosen H artwork and actual scene texture origin");
        var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        check(!report.Contains(profile,StringComparison.OrdinalIgnoreCase), "Debug reports redact the Windows profile directory");
        for(var i=0;i<45;i++)PreviewDiagnostics.Record("bounded-event-"+i+"!");
        report=PreviewDiagnostics.Report(null,null,-1);
        check(!report.Contains("bounded-event-0!")&&report.Contains("bounded-event-44!"), "Preview diagnostic history remains bounded");
        var dialog=new PreviewDebugWindow(()=>report);
        Layout((FrameworkElement)dialog.Content,900,660); Save((FrameworkElement)dialog.Content,Path.Combine(root,"preview-debug-report.png"),900,660);dialog.Close();
        var photos=new PreviewWindow(asset,new AppSettings(),7,(_,_)=>Task.FromResult(7));
        check(((Button)photos.FindName("SurfacePhotosButton")).IsEnabled&&((Button)photos.FindName("TexturePhotosButton")).IsEnabled,"Photo import is available on the preview surface and Textures data tab");
        photos.Close();
    }
    static void ScrollAndToolbarChecks(MainWindow window, ClothingProject project, string root, Action<bool, string> check)
    {
        var content = (FrameworkElement)window.Content;
        var list = (ListBox)window.FindName("AssetList");
        var bulk = Directory.CreateDirectory(Path.Combine(root, "BulkPants")).FullName;
        var paths = Enumerable.Range(100, 60).Select(n => Path.Combine(bulk, $"lowr_{n:000}_u.ydd")).ToArray();
        foreach (var path in paths) File.WriteAllBytes(path, [1]);
        var oldSelection = list.SelectedItem;
        Call(window, "ImportPaths", paths, null!);
        var last = project.Items.Single(a => a.ModelPath == paths.Last());
        check(ReferenceEquals(typeof(MainWindow).GetField("itemToReveal", Private)!.GetValue(window), last) && ReferenceEquals(list.SelectedItem, oldSelection),
            "Bulk Add queues only the last item without selecting intermediate rows");
        Layout(content, 1000, 650); Call(window, "RevealAddedItem"); Layout(content, 1000, 650);
        var scroller = Descendants<ScrollViewer>(list).First();
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(last);
        var position = row.TranslatePoint(new Point(), scroller);
        check(list.SelectedItem == last && scroller.VerticalOffset > 0 && position.Y >= -1 && position.Y + row.ActualHeight <= scroller.ActualHeight + 1,
            "Bulk Add scrolls the final new row completely into view");
        var firstOperationTarget = project.Items.First();
        Call(window, "QueueAddedItemReveal", firstOperationTarget);
        var firstOperation = (System.Windows.Threading.DispatcherOperation)typeof(MainWindow).GetField("addedItemReveal", Private)!.GetValue(window)!;
        Call(window, "QueueAddedItemReveal", last);
        check(firstOperation.Status == System.Windows.Threading.DispatcherOperationStatus.Aborted && ReferenceEquals(typeof(MainWindow).GetField("itemToReveal", Private)!.GetValue(window), last),
            "Repeated Add requests coalesce into a single final scroll");
        Call(window, "RevealAddedItem"); Layout(content, 1000, 650);
        var group = Descendants<Expander>(list).Single(g=>((System.Windows.Data.CollectionViewGroup)g.DataContext).Items.OfType<ClothingAsset>().Any(a=>a.Slot==last.Slot)); group.IsExpanded = false;
        Call(window, "QueueAddedItemReveal", last); Call(window, "RevealAddedItem"); Layout(content, 1000, 650);
        check(!group.IsExpanded, "Revealing an addition preserves its collapsed category and scrolls to the header");
        ((TextBox)window.FindName("SearchBox")).Text = "no_matching_item"; Call(window, "RefreshList");
        Call(window, "QueueAddedItemReveal", last); Call(window, "RevealAddedItem"); Layout(content, 1000, 650);
        check(list.SelectedItem == last && list.Items.Contains(last), "An active search cannot hide the final added item");
        ClothingGroupStateChecks(window,project,root,check);
        var toolbar = (WrapPanel)window.FindName("ClothingActions");
        var left = (ColumnDefinition)window.FindName("LeftColumn");
        foreach (var width in new[] { 500, 360, 260 })
        {
            left.Width = new GridLength(width); Layout(content, 1000, 650);
            check(toolbar.Children.Cast<Button>().All(button =>
            {
                var p = button.TranslatePoint(new Point(), toolbar);
                var label = ((StackPanel)button.Content).Children.OfType<TextBlock>().Single();
                return p.X >= 0 && p.X + button.ActualWidth <= toolbar.ActualWidth + 1 && p.Y + button.ActualHeight <= toolbar.ActualHeight + 1 && label.ActualWidth >= label.DesiredSize.Width - 1;
            }), $"All clothing action buttons and labels fit a {width}px list pane");
            Save(content, Path.Combine(root, $"toolbar-{width}.png"), 1000, 650);
        }
        left.Width = new GridLength(500); Layout(content, 1280, 845);
        check(SlotIcons.For("lowr").Bounds.Width > 10 && SlotIcons.For("lowr").Bounds.Height >= 18 && !ReferenceEquals(SlotIcons.For("lowr"), SlotIcons.For("jbib")), "Pants use a dedicated full-height trousers outline");
        Save(content, Path.Combine(root, "bulk-pants-revealed.png"), 1280, 845);
    }
    static void ClothingGroupStateChecks(MainWindow window,ClothingProject project,string root,Action<bool,string> check)
    {
        var content=(FrameworkElement)window.Content;var list=(ListBox)window.FindName("AssetList");
        var folder=Directory.CreateDirectory(Path.Combine(root,"GroupState")).FullName;
        foreach(var slot in new[]{"berd","jbib"})
        {
            var path=Path.Combine(folder,$"{slot}_000_u.ydd");File.WriteAllBytes(path,[1]);
            Call(window,"ImportPaths",new[]{path},null!);
        }
        Layout(content,1000,650);
        Expander Group(string slot)=>Descendants<Expander>(list).Single(g=>((System.Windows.Data.CollectionViewGroup)g.DataContext).Items.OfType<ClothingAsset>().Any(a=>a.Slot==slot));
        Group("berd").IsExpanded=false;Group("jbib").IsExpanded=false;Group("lowr").IsExpanded=true;
        var beforeTitle=project.Items.First(a=>a.Slot=="berd").GroupTitle;
        var added=Path.Combine(folder,"berd_001_u.ydd");File.WriteAllBytes(added,[1]);
        Call(window,"ImportPaths",new[]{added},null!);Layout(content,1000,650);Call(window,"RevealAddedItem");Layout(content,1000,650);
        check(!Group("berd").IsExpanded&&!Group("jbib").IsExpanded&&Group("lowr").IsExpanded&&project.Items.First(a=>a.Slot=="berd").GroupTitle!=beforeTitle,"Adding a mask preserves all group states even when header counts change");
        check(((ClothingAsset)list.SelectedItem).ModelPath==added,"A closed group still selects the final added item without reopening");
        var tops=Enumerable.Range(1,12).Select(i=>Path.Combine(folder,$"jbib_{i:000}_u.ydd")).ToArray();foreach(var path in tops)File.WriteAllBytes(path,[1]);
        Call(window,"ImportPaths",tops,null!);Layout(content,1000,650);Call(window,"RevealAddedItem");Layout(content,1000,650);
        check(!Group("berd").IsExpanded&&!Group("jbib").IsExpanded&&Group("lowr").IsExpanded&&((ClothingAsset)list.SelectedItem).ModelPath==tops.Last(),"Bulk additions keep closed masks and tops closed and reveal only the last destination");
        var scroller=Descendants<ScrollViewer>(list).First();var header=Group("jbib");var position=header.TranslatePoint(new Point(),scroller);
        check(position.Y>=-1&&position.Y+header.ActualHeight<=scroller.ActualHeight+1,"Adding into a closed category brings its header fully into view");
        ((TextBox)window.FindName("SearchBox")).Text="no_matching_group";Call(window,"RefreshList");Layout(content,1000,650);
        ((TextBox)window.FindName("SearchBox")).Text="";Call(window,"RefreshList");Layout(content,1000,650);
        check(!Group("berd").IsExpanded&&!Group("jbib").IsExpanded&&Group("lowr").IsExpanded,"Search refreshes preserve category expansion choices");
        var saved=System.Text.Json.JsonSerializer.Serialize(project,ProjectIO.Json);
        Group("berd").IsExpanded=true;Call(window,"RefreshList");Layout(content,1000,650);
        check(Group("berd").IsExpanded&&!Group("jbib").IsExpanded,"Manually reopening a category persists through the next refresh");
        check(System.Text.Json.JsonSerializer.Serialize(project,ProjectIO.Json)==saved,"Category expansion state stays out of project saves and undo data");
        Save(content,Path.Combine(root,"preserved-group-states.png"),1000,650);
    }
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    static void Layout(FrameworkElement element, int w, int h) { element.Measure(new Size(w, h)); element.Arrange(new Rect(0, 0, w, h)); element.UpdateLayout(); }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    static void Save(FrameworkElement content, string path, int w, int h)
    {
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
