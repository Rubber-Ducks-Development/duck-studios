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
        preview.Close();
        Call(window, "WindowClosed", window, EventArgs.Empty);
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
        var group = Descendants<Expander>(list).First(); group.IsExpanded = false;
        Call(window, "QueueAddedItemReveal", last); Call(window, "RevealAddedItem"); Layout(content, 1000, 650);
        check(group.IsExpanded, "Revealing an addition expands its collapsed clothing category");
        ((TextBox)window.FindName("SearchBox")).Text = "no_matching_item"; Call(window, "RefreshList");
        Call(window, "QueueAddedItemReveal", last); Call(window, "RevealAddedItem"); Layout(content, 1000, 650);
        check(list.SelectedItem == last && list.Items.Contains(last), "An active search cannot hide the final added item");
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
