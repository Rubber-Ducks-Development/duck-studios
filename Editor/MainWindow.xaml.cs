using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DuckStudio;

public partial class MainWindow : Window
{
    private ClothingProject project = new();
    private readonly AppSettings settings = AppSettings.Load();
    private string? projectPath;
    private ClothingAsset? selected;
    private bool syncing;
    private bool refreshingList;
    private ModelVisual3D? activeModel;
    private string? activeSceneFolder;
    private Point3D previewCenter = new(0, 0, 0);
    private double previewExtent = 2;
    private double orbitX = 0.7, orbitY = 0.25, distance = 6;
    private Point? dragStart;
    private int previewVersion;
    private bool autoCollapsedDrawables;
    private readonly DispatcherTimer listRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer newItemHighlightTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DispatcherOperation? addedItemReveal;
    private ClothingAsset? itemToReveal;
    private readonly Dictionary<string, int> textureSelections = new(StringComparer.OrdinalIgnoreCase);
    private string? lastSyncedAssetId;
    private ClothingAsset? findingsAsset;
    private readonly TextBlock findingsScopeText = new() { Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Button showAllFindingsButton = new() { Content = "Show all items", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left };

    public MainWindow()
    {
        InitializeComponent();
        QuickFilterBox.SelectedIndex = 0;
        var findingsHeader = (StackPanel)IssueCount.Parent;
        findingsHeader.Children.Add(findingsScopeText);
        findingsHeader.Children.Add(showAllFindingsButton);
        showAllFindingsButton.Click += (_, _) => { findingsAsset = null; RefreshValidation(); };
        listRefreshTimer.Tick += (_, _) => { listRefreshTimer.Stop(); RefreshList(); };
        newItemHighlightTimer.Tick += (_, _) => { foreach (var asset in project.Items.Where(a => a.IsNew)) asset.RefreshNewHighlight(); };
        newItemHighlightTimer.Start();
        foreach (var slot in ProjectIO.Slots) SlotBox.Items.Add(ProjectIO.SlotLabel(slot));
        SyncProject();
        SyncItem();
        ApplyIcons();
        InitializeTextureReload();
        EnsureHistoryIdentity();
        MarkSaved();
        backupTimer.Tick += BackupTick;
        backupTimer.Start();
        InitializeEditing();
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => EnsureProject());
        StartUpdateChecks();
    }

    private void WindowClosed(object sender, EventArgs e) { addedItemReveal?.Abort(); backupTimer.Stop(); listRefreshTimer.Stop(); newItemHighlightTimer.Stop(); textureReloadTimer.Stop(); textureReloadClosed = true; updateCheckTimer.Stop(); ClearModel(); LaunchPendingUpdate(); }

    private void ChangeGtaFolderClick(object sender, RoutedEventArgs e) => ChooseGtaFolder();

    private bool ChooseGtaFolder()
    {
        var picker = new OpenFileDialog { Title = "Locate GTA5.exe in your GTA V installation", Filter = "Grand Theft Auto V (GTA5.exe)|GTA5.exe", CheckFileExists = true };
        if (picker.ShowDialog() != true) return false;
        settings.GtaFolderPath = Path.GetDirectoryName(picker.FileName)!;
        settings.Save();
        project.GtaFolderPath = settings.GtaFolderPath;
        GtaPathText.Text = settings.GtaFolderPath;
        return true;
    }

    private void SyncProject()
    {
        syncing = true;
        Title = $"Duck Studio - {project.Name}";
        GtaPathText.Text = File.Exists(Path.Combine(settings.GtaFolderPath, "GTA5.exe")) ? settings.GtaFolderPath : "GTA V not linked";
        NameBox.Text = project.Name;
        CollectionBox.Text = project.Collection;
        MaleYmtButton.Content = File.Exists(project.MaleYmtPath) ? "Male YMT  ✓" : "Male YMT...";
        MaleMetaButton.Content = File.Exists(project.MaleMetaPath) ? "Male META  ✓" : "Male META...";
        FemaleYmtButton.Content = File.Exists(project.FemaleYmtPath) ? "Female YMT  ✓" : "Female YMT...";
        FemaleMetaButton.Content = File.Exists(project.FemaleMetaPath) ? "Female META  ✓" : "Female META...";
        var imported = false;
        SupplementalCount.Text = imported ? "Existing metadata and stream files are preserved." : project.SupplementalYmtPaths.Count == 0 ? "No supplemental YMT files" : string.Join(" · ", project.SupplementalYmtPaths.Select(Path.GetFileName));
        BuildButton.Content = "Build project folder";
        SetButtonIcon(BuildButton, "build-project");
        BuildHeading.Text = imported ? "Copy existing resource" : "Build your pack";
        ImportedSourceText.Text = imported ? project.SourceResourcePath : "";
        ImportedSourceText.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        MetadataLabel.Text = "YMT and shop META are generated automatically when you build.";
        ImportGtaButton.IsEnabled = !imported;
        AddMaleButton.IsEnabled = !imported;
        AddFemaleButton.IsEnabled = !imported;
        RemoveItemButton.IsEnabled = !imported && selected != null;
        NameBox.IsEnabled = !imported;
        CollectionBox.IsEnabled = !imported;
        MaleYmtButton.Visibility = Visibility.Collapsed;
        MaleMetaButton.Visibility = Visibility.Collapsed;
        FemaleYmtButton.Visibility = Visibility.Collapsed;
        FemaleMetaButton.Visibility = Visibility.Collapsed;
        SupplementalButton.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        ClearSupplementalButton.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        syncing = false;
        RefreshList();
    }

    private void RefreshList()
    {
        listRefreshTimer.Stop();
        RefreshValidation();
        var query = SearchBox.Text?.Trim() ?? "";
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var quickFilter = QuickFilterBox.SelectedIndex;
        foreach(var group in project.Items.GroupBy(x => x.Group))
            foreach(var item in group) item.GroupTitle=$"{group.Key} ({group.Count(x => x.Ped == "mp_m_freemode_01")}M {group.Count(x => x.Ped == "mp_f_freemode_01")}F)";
        var current = selected;
        refreshingList = true;
        var visible = project.Items.Where(x => MatchesSearch(x, tokens) && MatchesQuickFilter(x, quickFilter)).OrderBy(x=>Array.IndexOf(ProjectIO.Slots,x.Slot)).ThenBy(x=>x.Ped == "mp_f_freemode_01" ? 1 : 0).ThenBy(x=>x.Drawable).ToList();
        var view = new System.Windows.Data.ListCollectionView(visible);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ClothingAsset.GroupTitle)));
        AssetList.ItemsSource = view;
        if (current != null) AssetList.SelectedItem = current;
        refreshingList = false;
        CountText.Text = visible.Count == project.Items.Count
            ? $"{project.Items.Count} drawable{(project.Items.Count == 1 ? "" : "s")}" : $"{visible.Count} of {project.Items.Count} drawables";
        EmptyListText.Visibility = AssetList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void QuickFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetList != null) QueueListRefresh();
    }
    private static bool MatchesQuickFilter(ClothingAsset asset, int filter) => filter switch
    {
        1 => asset.HasValidationIssues,
        2 => asset.Ped == "mp_m_freemode_01",
        3 => asset.Ped == "mp_f_freemode_01",
        4 => !asset.IsProp,
        5 => asset.IsProp,
        _ => true
    };
    private void QueueListRefresh()
    {
        listRefreshTimer.Stop();
        listRefreshTimer.Start();
    }

    private static bool MatchesSearch(ClothingAsset asset, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return true;
        var searchable = string.Join(' ', asset.Name, asset.Group, asset.Slot, asset.Ped,
            asset.Drawable.ToString(CultureInfo.InvariantCulture), Path.GetFileName(asset.ModelPath),
            asset.IsProp ? "prop" : "component", asset.Ped == "mp_f_freemode_01" ? "female" : "male",
            string.Join(' ', asset.TexturePaths.Select(Path.GetFileName)));
        return tokens.All(token => token.Equals("male", StringComparison.OrdinalIgnoreCase)
            ? asset.Ped == "mp_m_freemode_01"
            : token.Equals("female", StringComparison.OrdinalIgnoreCase)
                ? asset.Ped == "mp_f_freemode_01"
                : searchable.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshValidation()
    {
        List<string>? focusedIssues = null;
        var issues = PackBuilder.Validate(project, (asset, assetIssues) =>
        {
            asset.SetValidationStatus(assetIssues);
            if (ReferenceEquals(asset, findingsAsset)) focusedIssues = [..assetIssues];
        });
        if (findingsAsset != null && focusedIssues != null)
        {
            findingsScopeText.Text = $"Showing findings for {findingsAsset.Name}";
            findingsScopeText.Visibility = Visibility.Visible;
            showAllFindingsButton.Visibility = Visibility.Visible;
            IssueCount.Text = $"  {focusedIssues.Count} issue{(focusedIssues.Count == 1 ? "" : "s")}";
            ValidationText.Text = focusedIssues.Count == 0 ? "No issues found for this drawable." : string.Join("\n", focusedIssues.Select(x => "•  " + x));
        }
        else
        {
            findingsAsset = null;
            findingsScopeText.Visibility = Visibility.Collapsed;
            showAllFindingsButton.Visibility = Visibility.Collapsed;
            IssueCount.Text = $"  {issues.Count} issue{(issues.Count == 1 ? "" : "s")}";
            ValidationText.Text = issues.Count == 0 ? "Ready to generate YMT, shop META, manifest and streaming assets." : string.Join("\n", issues.Select(x => "•  " + x));
        }
        StatusText.Text = issues.Count == 0 ? $"{project.Items.Count} drawables  /  ready to build" : $"{project.Items.Count} drawables  /  {issues.Count} validation issue{(issues.Count == 1 ? "" : "s")}";
    }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmProjectSwitch()) return;
        var next = new ClothingProject { Name = "", Collection = "" };
        if (new SettingsWindow(next, settings, true) { Owner = this }.ShowDialog() != true) return;
        recoverySession = "Unsaved-" + Guid.NewGuid().ToString("N");
        project = next; projectPath = null; EnsureHistoryIdentity(); selected = null; ClearModel(); SyncProject(); SyncItem(); WorkspaceTabs.SelectedIndex = 0; ResetEdits();
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ProjectFiles.OpenFilter };
        if (dialog.ShowDialog() != true) return;
        try { OpenProjectPath(dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OpenProjectPath(string path)
    {
        if (!ConfirmProjectSwitch()) return;
        if (ProjectFiles.IsDurtyProject(path))
        {
            LoadResource(path, durtyProject: true);
        }
        else
        {
            project = ProjectIO.Load(path); TextureImport.RestoreWorkingCopies(project); projectPath = path; selected = project.Items.FirstOrDefault(); ClearModel(); SyncProject(); SyncItem(); WorkspaceTabs.SelectedIndex = 0; MarkSaved();
        }
        ResetEdits();
        settings.RememberProject(path);
        _ = BackupAfterSaveAsync();
        _ = RecordHistoryAsync("Opened project");
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        SaveProject();
    }

    private void SaveAsClick(object sender, RoutedEventArgs e)
    {
        SaveProject(true);
    }

    private void RecentProjectsOpened(object sender, RoutedEventArgs e)
    {
        RecentProjectsMenu.Items.Clear();
        foreach (var path in settings.RecentProjects.Where(File.Exists))
        {
            var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path, Tag = path };
            item.Click += (_, _) => { try { OpenProjectPath(path); } catch (Exception ex) { ShowError(ex); } };
            RecentProjectsMenu.Items.Add(item);
        }
        if (RecentProjectsMenu.Items.Count == 0) RecentProjectsMenu.Items.Add(new MenuItem { Header = "No recent projects", IsEnabled = false });
    }

    private void OpenAddMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void ImportClick(object sender, RoutedEventArgs e) => ImportFiles(null);
    private void AddMaleClick(object sender, RoutedEventArgs e) => ImportFiles("mp_m_freemode_01");
    private void AddFemaleClick(object sender, RoutedEventArgs e) => ImportFiles("mp_f_freemode_01");
    private void ImportFolderClick(object sender, RoutedEventArgs e) => ImportFolder(null);
    private void ImportMaleFolderClick(object sender, RoutedEventArgs e) => ImportFolder("mp_m_freemode_01");
    private void ImportFemaleFolderClick(object sender, RoutedEventArgs e) => ImportFolder("mp_f_freemode_01");

    private void ImportFolder(string? requiredPed)
    {
        if (!EnsureProject()) return;
        var dialog = new OpenFolderDialog { Title = "Select a folder containing GTA clothing files" };
        if (dialog.ShowDialog() != true) return;
        ImportPaths(Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories), requiredPed);
    }

    private void ImportFiles(string? requiredPed)
    {
        if (!EnsureProject()) return;
        var dialog = new OpenFileDialog { Filter = "GTA clothing files (*.ydd;*.ytd)|*.ydd;*.ytd", Multiselect = true, Title = requiredPed == null ? "Add clothing files" : $"Add {requiredPed} clothing files" };
        if (dialog.ShowDialog() != true) return;
        ImportPaths(dialog.FileNames, requiredPed);
    }

    private void ImportPaths(IEnumerable<string> paths, string? requiredPed)
    {
        if (!EnsureProject()) return;
        var files = paths.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
        var models = files.Where(x => x.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase)).ToList();
        var textures = files.Where(x => x.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)).ToList();
        ClothingAsset? lastAdded = null;
        foreach (var path in models.Where(x=>!ProjectIO.IsAlternative(x)))
        {
            if (!ProjectIO.TryParseDrawable(path, out var slot, out var number)) continue;
            var ped = requiredPed ?? ProjectIO.InferPed(path);
            var asset = project.Items.FirstOrDefault(x => x.Ped == ped && x.Slot == slot && ProjectIO.TryParseDrawable(x.ModelPath,out var originalSlot,out var originalNumber) && originalSlot==slot && originalNumber==number && ProjectIO.SourceCollection(x.ModelPath)==ProjectIO.SourceCollection(path));
            if (asset == null) { asset = new ClothingAsset { Name = ProjectIO.SlotLabel(slot).Split('[','(')[0].Trim(), Ped = ped, Slot = slot }; ClothingImport.Append(project.Items, asset); lastAdded = asset; }
            asset.ModelPath = path; asset.GeneratedLods=false;
            var basePath=Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path));
            if(File.Exists(basePath+"_1.ydd"))asset.FirstPersonPath=basePath+"_1.ydd";
            if(File.Exists(basePath+".yld"))asset.PhysicsPath=basePath+".yld";
        }
        var unmatched = new List<string>();
        ClothingAsset? textureTarget = null;
        int addedTextures = 0;
        foreach (var path in textures)
        {
            var asset = ClothingImport.TextureTarget(project.Items, path, requiredPed, models.Count == 0 ? selected : null);
            if (asset == null) { unmatched.Add(Path.GetFileName(path) + ": no unambiguous matching drawable. Select its drawable and use Add texture."); continue; }
            textureTarget = asset;
            if (asset.TexturePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            if (asset.TexturePaths.Count >= 26) { unmatched.Add(Path.GetFileName(path) + ": drawable already has 26 variations."); continue; }
            asset.TexturePaths.Add(path);
            asset.NotifyTexturesChanged();
            addedTextures++;
        }
        RefreshList();
        if (models.Count == 0 && textureTarget != null) AssetList.SelectedItem = textureTarget;
        if (AssetList.Items.Count > 0 && AssetList.SelectedIndex < 0) AssetList.SelectedIndex = 0;
        selected = AssetList.SelectedItem as ClothingAsset; SyncItem();
        QueueAddedItemReveal(lastAdded ?? (models.Count == 0 ? textureTarget : null));
        StatusText.Text = $"Imported {models.Count} model file(s) and {addedTextures} YTD texture(s).";
        if (unmatched.Count > 0) MessageBox.Show(this, string.Join("\n", unmatched), "YTD import", MessageBoxButton.OK, MessageBoxImage.Information);
        else if (models.Count == 0 && textures.Count == 0) MessageBox.Show("No YDD models or YTD textures were found.", "Duck Studio");
    }

    private void OpenResourceClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureProject()) return;
        var dialog = new OpenFolderDialog { Title = "Select a FiveM resource folder" };
        if (dialog.ShowDialog() != true) return;
        try { LoadResource(dialog.FolderName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OpenDctClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureProject()) return;
        var dialog = new OpenFileDialog { Filter = "Durty Cloth project (*.dctproj)|*.dctproj" };
        if (dialog.ShowDialog() != true) return;
        try { OpenProjectPath(dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadResource(string path, bool durtyProject = false)
    {
        if (!EnsureProject()) return;
        var importedProject = durtyProject ? ResourceImporter.ImportDurtyProject(path) : ResourceImporter.Import(path);
        foreach (var asset in importedProject.Items) ClothingImport.Append(project.Items, asset);
        project.SourceResourcePath = "";
        projectPath = null;
        selected = null;
        ClearModel();
        SyncProject();
        SyncItem();
        if (AssetList.Items.Count > 0) AssetList.SelectedIndex = 0;
        selected = AssetList.SelectedItem as ClothingAsset; SyncItem();
        WorkspaceTabs.SelectedIndex = 0;
        QueueAddedItemReveal(importedProject.Items.LastOrDefault());
    }

    private void QueueAddedItemReveal(ClothingAsset? asset)
    {
        if (asset == null) return;
        itemToReveal = asset;
        addedItemReveal?.Abort();
        addedItemReveal = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RevealAddedItem));
    }

    private void RevealAddedItem()
    {
        addedItemReveal?.Abort();
        addedItemReveal = null;
        var asset = itemToReveal;
        itemToReveal = null;
        if (asset == null || !project.Items.Contains(asset)) return;
        if (!AssetList.Items.Contains(asset))
        {
            SearchBox.Text = "";
            QuickFilterBox.SelectedIndex = 0;
            RefreshList();
        }
        AssetList.SelectedItem = asset;
        AssetList.UpdateLayout();
        if (AssetList.ItemContainerGenerator.ContainerFromItem(asset) is DependencyObject row)
            for (var parent = PanelDismissal.Parent(row); parent != null && parent != AssetList; parent = PanelDismissal.Parent(parent))
                if (parent is Expander group) group.IsExpanded = true;
        AssetList.UpdateLayout();
        AssetList.ScrollIntoView(asset);
    }

    private void ImportPreviewClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) { MessageBox.Show("Select a drawable first.", "Duck Studio"); return; }
        var dialog = new OpenFileDialog { Filter = "Wavefront OBJ (*.obj)|*.obj" };
        if (dialog.ShowDialog() != true) return;
        selected.PreviewPath = dialog.FileName;
        LoadModel(dialog.FileName);
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder != null) SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (AssetList != null) QueueListRefresh();
    }
    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;

        project.Items.Remove(selected); selected = null; ClearModel(); RefreshList(); SyncItem();
    }

    private void AssetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingList) return;
        if (selected != null) textureSelections[selected.Id] = TextureList.SelectedIndex;
        selected = AssetList.SelectedItem as ClothingAsset;
        SyncItem();
        if (selected != null && File.Exists(selected.PreviewPath)) LoadModel(selected.PreviewPath); else { ClearModel(); if (WorkspaceTabs.SelectedIndex == 1 && selected != null) _ = TryAssetPreview(false); }
    }

    private void SyncItem()
    {
        if (selected != null && lastSyncedAssetId == selected.Id)
            textureSelections[selected.Id] = TextureList.SelectedIndex;
        lastSyncedAssetId = selected?.Id;
        syncing = true;
        ItemNameBox.Text = selected?.Name ?? "";
        PedBox.SelectedIndex = selected?.Ped == "mp_f_freemode_01" ? 1 : 0;
        PopulateSlotBox(selected?.IsProp == true);
        SlotBox.SelectedItem = ProjectIO.SlotLabel(selected?.Slot ?? "jbib");
        DrawableBox.Text = selected?.Drawable.ToString() ?? "";
        TextureList.Items.Clear();
        if (selected != null) for (var i=0;i<selected.TexturePaths.Count;i++)
        {
            var path=selected.TexturePaths[i];
            TextureList.Items.Add($"{(char)('a'+i)}   {selected.TextureLabels.GetValueOrDefault(path,Path.GetFileNameWithoutExtension(path))}   {ClothingAsset.FileSize(path)}" + (selected.TextureSources.TryGetValue(path,out var sourceLink) ? $"   [linked {Path.GetExtension(sourceLink.SourcePath).TrimStart('.').ToUpperInvariant()}]" : ""));
        }
        if (selected != null && selected.TexturePaths.Count > 0)
            TextureList.SelectedIndex = Math.Clamp(textureSelections.GetValueOrDefault(selected.Id), 0, selected.TexturePaths.Count - 1);
        SelectedSummary.Text = selected == null ? "Select a drawable to inspect" : $"{selected.Ped}  /  {selected.Slot}  /  {selected.Drawable:000}";
        var imported = false;
        EmptyItemText.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        DetailContent.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
        DetailsPreviewButton.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
        ViewYtdButton.Visibility = selected?.TexturePaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemoveItemButton.IsEnabled = selected != null;
        ItemEditor.Visibility = selected != null && !imported ? Visibility.Visible : Visibility.Collapsed;
        ImportedItemPanel.Visibility = selected != null && imported ? Visibility.Visible : Visibility.Collapsed;
        ImportedTextureList.Items.Clear();
        if (selected != null && imported)
        {
            ImportedPedText.Text = selected.Ped;
            ImportedFileText.Text = Path.GetFileName(selected.ModelPath);
            foreach (var path in selected.TexturePaths) ImportedTextureList.Items.Add($"▧     {(char)('a'+selected.TexturePaths.IndexOf(path))}   {selected.Name}   {ClothingAsset.FileSize(path)}");
        }
        PreviewModelPath.Text = selected == null ? "No item selected" : Path.GetFileName(selected.ModelPath);
        PreviewTextureCount.Text = (selected?.TexturePaths.Count ?? 0).ToString();
        PreviewTextureBox.Items.Clear();
        if (selected != null)
            foreach (var path in selected.TexturePaths) PreviewTextureBox.Items.Add(Path.GetFileName(path));
        PreviewTextureBox.SelectedIndex = PreviewTextureBox.Items.Count > 0 ? TextureList.SelectedIndex : -1;
        DetailsPreviewButton.IsEnabled = selected != null;
        TextureImage.Source = null;
        TextureViewer.Visibility = Visibility.Collapsed;
        var editable = selected != null;
        ItemNameBox.IsEnabled = editable;
        PedBox.IsEnabled = editable;
        SlotBox.IsEnabled = editable;
        DrawableBox.IsEnabled = editable;
        FirstPersonText.Text = selected?.FirstPersonPath is {Length:>0} fp ? Path.GetFileName(fp) : "Select a file";
        PhysicsText.Text = selected?.PhysicsPath is {Length:>0} ph ? Path.GetFileName(ph) : "Select a file";
        AudioBox.Text = selected?.Audio ?? "none";
        FlagsBox.Text = (selected?.BehaviourFlags ?? 0).ToString();
        RestrictionBox.Text = selected?.RestrictionTags ?? "";
        ShopBox.Text = selected?.Shop ?? "CLO_SHOP_NONE";
        CreatorBox.Text = selected?.CreatorName ?? "";
        WebsiteBox.Text = selected?.Website ?? "";
        SupportBox.Text = selected?.SupportUrl ?? "";
        NotesBox.Text = selected?.Notes ?? "";
        TextureCount.Text = $"Textures {selected?.TexturePaths.Count ?? 0}/26";
        ClothTypeBox.SelectedIndex = selected?.IsProp == true ? 1 : 0;
        syncing = false;
    }

    private bool EnsureProject()
    {
        if (PackBuilder.IsConfigured(project)) return true;
        var result = new SettingsWindow(project,settings,true) { Owner=this }.ShowDialog() == true;
        SyncProject(); SyncItem();
        return result && PackBuilder.IsConfigured(project);
    }
    private void CloseTextureViewer(object sender, MouseButtonEventArgs e) => TextureViewer.Visibility = Visibility.Collapsed;
    private void ExtraFieldChanged(object sender, EventArgs e)
    {
        if(syncing || selected == null || NotesBox == null) return;
        selected.Audio=AudioBox.Text.Trim();
        if(uint.TryParse(FlagsBox.Text,out var flags))selected.BehaviourFlags=flags;
        selected.RestrictionTags=RestrictionBox.Text; selected.Shop=ShopBox.Text.Trim();
        selected.CreatorName=CreatorBox.Text;selected.Website=WebsiteBox.Text;selected.SupportUrl=SupportBox.Text;selected.Notes=NotesBox.Text;
    }
    private void PopulateSlotBox(bool props)
    {
        SlotBox.Items.Clear();
        foreach(var slot in ProjectIO.Slots.Where(slot=>slot.StartsWith("p_")==props))
            SlotBox.Items.Add(ProjectIO.SlotLabel(slot));
    }

    private void ClothTypeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(syncing || selected==null) return;
        if((ClothTypeBox.SelectedIndex==1)!=selected.IsProp) { selected.Slot=ClothTypeBox.SelectedIndex==1?"p_head":"jbib";SyncItem();RefreshList(); }
    }
    private void BrowseAssetFile(object sender,RoutedEventArgs e)
    {
        if(selected==null)return;
        var physics=(sender as Button)?.Tag?.ToString()=="physics";
        var dialog=CreateAssetFileDialog(physics);
        if(dialog.ShowDialog(this)!=true)return;
        if(physics)selected.PhysicsPath=dialog.FileName;else selected.FirstPersonPath=dialog.FileName;
        SyncItem();RefreshValidation();
    }
    private OpenFileDialog CreateAssetFileDialog(bool physics)
    {
        var current=selected==null ? "" : physics ? selected.PhysicsPath : selected.FirstPersonPath;
        var source=File.Exists(current) ? current : selected?.ModelPath ?? "";
        var folder=Path.GetDirectoryName(source);
        if(!Directory.Exists(folder))folder=project.OutputFolder;
        return new OpenFileDialog
        {
            Title=physics ? "Select cloth physics (.yld)" : "Select first-person model (.ydd)",
            Filter=physics ? "Cloth physics (*.yld)|*.yld" : "First-person model (*.ydd)|*.ydd",
            CheckFileExists=true,
            Multiselect=false,
            InitialDirectory=Directory.Exists(folder) ? folder : "",
            FileName=File.Exists(current) ? Path.GetFileName(current) : ""
        };
    }
    private void ClearAssetFile(object sender,RoutedEventArgs e) { if(selected==null)return;if((sender as Button)?.Tag?.ToString()=="physics")selected.PhysicsPath="";else selected.FirstPersonPath="";SyncItem();RefreshValidation(); }
    private void MoveTexture(object sender,RoutedEventArgs e)
    {
        if(selected==null)return;int i=TextureList.SelectedIndex,j=i+((sender as Button)?.Tag?.ToString()=="up"?-1:1);
        if(i<0||j<0||j>=selected.TexturePaths.Count)return;
        (selected.TexturePaths[i],selected.TexturePaths[j])=(selected.TexturePaths[j],selected.TexturePaths[i]);selected.NotifyTexturesChanged();SyncItem();TextureList.SelectedIndex=j;
    }
    private void CopyGenderClick(object sender,RoutedEventArgs e)
    {
        if(selected==null)return;
        var copy=System.Text.Json.JsonSerializer.Deserialize<ClothingAsset>(System.Text.Json.JsonSerializer.Serialize(selected))!;
        copy.Id=Guid.NewGuid().ToString("N");copy.Ped=copy.Ped.Contains("mp_f_")?"mp_m_freemode_01":"mp_f_freemode_01";
        ClothingImport.Append(project.Items,copy);selected=copy;RefreshList();SyncItem();QueueAddedItemReveal(copy);
    }
    private void ReplaceClick(object sender,RoutedEventArgs e) {if(selected==null)return;var d=new OpenFileDialog{Filter="Drawable (*.ydd)|*.ydd"};if(d.ShowDialog(this)==true){selected.ModelPath=d.FileName;selected.GeneratedLods=false;ClearModel();RefreshList();SyncItem();}}
    private void CopyNameClick(object sender,RoutedEventArgs e) {if(selected!=null)Clipboard.SetText(selected.Name);}
    private void RevealClick(object sender,RoutedEventArgs e) {if(selected!=null&&File.Exists(selected.ModelPath))Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{selected.ModelPath}\""){UseShellExecute=true});}

    private void ProjectFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (syncing || NameBox == null || CollectionBox == null) return;
        project.Name = NameBox.Text.Trim(); project.Collection = CollectionBox.Text.Trim(); Title = $"Duck Studio - {project.Name}"; RefreshValidation();
    }

    private void ItemFieldChanged(object sender, EventArgs e)
    {
        if (syncing || selected == null || PedBox == null || SlotBox == null) return;
        selected.Name = ItemNameBox.Text.Trim();
        selected.Ped = PedBox.SelectedIndex == 1 ? "mp_f_freemode_01" : "mp_m_freemode_01";
        selected.Slot = ProjectIO.Slots.FirstOrDefault(slot => ProjectIO.SlotLabel(slot) == (SlotBox.SelectedItem as string)) ?? selected.Slot;
        if (int.TryParse(DrawableBox.Text, out var number)) selected.Drawable = number;
        SelectedSummary.Text = $"{selected.Ped}  /  {selected.Slot}  /  {selected.Drawable:000}";
        QueueListRefresh();
    }

    private async void AttachTextureClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var dialog = new OpenFileDialog { Filter = TextureImport.Filter, Multiselect = true, Title = "Add textures or linked images" };
        if (dialog.ShowDialog(this) != true) return;
        await ImportTexturesAsync(selected, dialog.FileNames, -1);
    }

    private void DetachTextureClick(object sender, RoutedEventArgs e)
    {
        if (selected == null || TextureList.SelectedIndex < 0) return;
        var removed=selected.TexturePaths[TextureList.SelectedIndex];
        selected.TexturePaths.RemoveAt(TextureList.SelectedIndex); selected.TextureLabels.Remove(removed); selected.TextureSources.Remove(removed); selected.NotifyTexturesChanged(); SyncItem(); RefreshValidation();
    }

    private void ChooseMetadata(Action<string> assign, string extension)
    {
        var dialog = new OpenFileDialog { Filter = $"{extension.ToUpperInvariant()} file (*.{extension})|*.{extension}" };
        if (dialog.ShowDialog() != true) return;
        assign(dialog.FileName); SyncProject();
    }
    private void MaleYmtClick(object sender, RoutedEventArgs e) => ChooseMetadata(x => project.MaleYmtPath = x, "ymt");
    private void MaleMetaClick(object sender, RoutedEventArgs e) => ChooseMetadata(x => project.MaleMetaPath = x, "meta");
    private void FemaleYmtClick(object sender, RoutedEventArgs e) => ChooseMetadata(x => project.FemaleYmtPath = x, "ymt");
    private void FemaleMetaClick(object sender, RoutedEventArgs e) => ChooseMetadata(x => project.FemaleMetaPath = x, "meta");

    private void SupplementalClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "YMT files (*.ymt)|*.ymt", Multiselect = true, Title = "Choose all supplemental YMT files" };
        if (dialog.ShowDialog() != true) return;
        project.SupplementalYmtPaths = dialog.FileNames.ToList();
        SyncProject();
    }

    private void ClearSupplementalClick(object sender, RoutedEventArgs e)
    {
        project.SupplementalYmtPaths.Clear();
        SyncProject();
    }

    private void BuildClick(object sender, RoutedEventArgs e) => BuildProject(false);

    private void BuildProject(bool zipDefault)
    {
        if (!EnsureProject()) return;
        var dialog = new BuildWindow(project,zipDefault) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        project.Collection = dialog.Collection; project.BuildTarget=dialog.Options.Target;
        project.OutputFolder = dialog.OutputFolder;
        SyncProject();
        var issues = PackBuilder.Validate(project);
        if (issues.Count > 0) { MessageBox.Show(string.Join("\n", issues), "Resolve validation issues", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try
        {
            var path = PackBuilder.Build(project, dialog.OutputFolder, dialog.Options);
            _ = RecordHistoryAsync("Built " + BuildTargets.Labels[(int)dialog.Options.Target]);
            string zipNotice = "";
            if(dialog.CreateZip)
            {
                var zipPath=Path.Combine(dialog.OutputFolder,project.Collection+"-project.zip");
                if(File.Exists(zipPath)) zipPath=Path.Combine(dialog.OutputFolder,project.Collection+"-project-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip");
                ZipFile.CreateFromDirectory(path,zipPath,CompressionLevel.Optimal,false);
                zipNotice="\nZIP: "+zipPath;
            }
            MessageBox.Show($"Project exported at:\n{path}\n\nSee the build target README for deployment instructions.{zipNotice}", "Duck Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportProjectClick(object sender, RoutedEventArgs e) => BuildClick(sender,e);

    private void ExportZipClick(object sender, RoutedEventArgs e) => BuildProject(true);

    private void WorkspaceTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, WorkspaceTabs)) return;
        if (LeftColumn != null && WorkspaceTabs != null)
        {
            if ((WorkspaceTabs.SelectedIndex == 1 || WorkspaceTabs.SelectedIndex == 3) && ActualWidth > 0 && ActualWidth < 1200 && LeftColumn.Width.Value > 0)
            {
                SetDrawablesVisible(false);
                autoCollapsedDrawables = true;
            }
            else if (WorkspaceTabs.SelectedIndex != 1 && WorkspaceTabs.SelectedIndex != 3 && autoCollapsedDrawables)
            {
                SetDrawablesVisible(true);
                autoCollapsedDrawables = false;
            }
        }
        if (WorkspaceTabs?.SelectedIndex != 1 || selected == null || activeModel != null) return;
        _ = TryAssetPreview(false);
    }

    private async void NativePreviewClick(object sender, RoutedEventArgs e) => await TryAssetPreview(true);

    private async void RenderPedClick(object sender, RoutedEventArgs e) => await RenderPedAsync(false);

    private async Task RenderPedAsync(bool preserveCamera)
    {
        var version = ++previewVersion;
        if (selected == null || !File.Exists(selected.ModelPath)) { MessageBox.Show("Select clothing in Library first.", "Duck Studio"); return; }
        if (selected.Slot.StartsWith("p_", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("PED component preview currently supports clothing slots, not props.", "Duck Studio"); return; }
        if (!File.Exists(Path.Combine(settings.GtaFolderPath, "GTA5.exe")) && !ChooseGtaFolder()) return;
        var game = settings.GtaFolderPath;
        var rage = Path.Combine(AppContext.BaseDirectory, "rage.exe");
        if (!File.Exists(rage)) { MessageBox.Show("The PED renderer (rage.exe) is missing from the app folder.", "Duck Studio"); return; }
        project.GtaFolderPath = game;
        var dataHome = Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeftStudio");
        var output = Path.Combine(dataHome, "scenes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo(rage) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["HOME"] = dataHome;
        foreach (var arg in new[] { "--no-update-check", "--exe", Path.Combine(game, "GTA5.exe"), "ped-scene", "--ped", selected.Ped, "--override-slot", selected.Slot, "--override-ydd", selected.ModelPath, "-o", output }) start.ArgumentList.Add(arg);
        if (SelectedTexturePath() is { } ytd) { start.ArgumentList.Add("--override-ytd"); start.ArgumentList.Add(ytd); }
        RenderPedButton.IsEnabled = false;
        ClearModel();
        PreviewHint.Text = "Loading 3D PED from your GTA V installation...";
        PreviewHint.Visibility = Visibility.Visible;
        try
        {
            PreviewHint.Text = "Indexing GTA V textures for 3D preview...";
            await EnsureRendererIndexAsync(rage, dataHome, Path.Combine(game, "GTA5.exe"));
            PreviewHint.Text = "Loading 3D PED from your GTA V installation...";
            using var process = Process.Start(start) ?? throw new IOException("Could not start the PED renderer.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var log = await stdout + await stderr;
            if (process.ExitCode != 0) throw new IOException(log);
            if (version != previewVersion) return;
            var scene = PedScenePreview.Load(output);
            activeModel = new ModelVisual3D { Content = scene.Model };
            ModelViewport.Children.Add(activeModel);
            activeSceneFolder = output;
            previewCenter = scene.Center;
            activePreviewPed = true;
            if (preserveCamera) UpdateCamera(); else { orbitX = 0; orbitY = .05; ResetPreviewCamera(scene.Extent); }
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { if (version == previewVersion) { PreviewHint.Text = "PED preview unavailable"; ShowError(ex); } }
        finally { RenderPedButton.IsEnabled = true; if (activeSceneFolder != output) DeleteScene(output); }
    }

    private static async Task EnsureRendererIndexAsync(string rage, string dataHome, string gtaExe)
    {
        Directory.CreateDirectory(dataHome);
        var stampPath = Path.Combine(dataHome, "renderer-index.txt");
        var stamp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rage))) + "|" +
            Path.GetFullPath(gtaExe) + "|" + File.GetLastWriteTimeUtc(gtaExe).Ticks;
        if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp) return;

        var start = new ProcessStartInfo(rage) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["HOME"] = dataHome;
        foreach (var arg in new[] { "--no-update-check", "--exe", gtaExe, "index", "build" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the GTA indexer.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var log = await stdout + await stderr;
        if (process.ExitCode != 0) throw new IOException("Could not index GTA V textures: " + log);
        File.WriteAllText(stampPath, stamp);
    }

    private void PreviewTextureChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || selected == null) return;
        TextureList.SelectedIndex = PreviewTextureBox.SelectedIndex;
        if (WorkspaceTabs.SelectedIndex == 1)
        {
            if (activePreviewPed) _ = RenderPedAsync(true);
            else _ = TryAssetPreview(false, true);
        }
    }

    private void TextureSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || selected == null) return;
        textureSelections[selected.Id] = TextureList.SelectedIndex;
        if (PreviewTextureBox.SelectedIndex != TextureList.SelectedIndex)
            PreviewTextureBox.SelectedIndex = TextureList.SelectedIndex;
    }

    private void TextureDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || selected == null ||
            ItemsControl.ContainerFromElement(TextureList, e.OriginalSource as DependencyObject) is not ListBoxItem row) return;
        TextureList.SelectedIndex = TextureList.ItemContainerGenerator.IndexFromContainer(row);
        PreviewTabClick(sender, e);
        e.Handled = true;
    }

    private string? SelectedTexturePath()
    {
        var index = WorkspaceTabs.SelectedIndex == 0
            ? TextureList.SelectedIndex
            : PreviewTextureBox.SelectedIndex;
        if (index < 0) index = PreviewTextureBox.SelectedIndex;
        return selected != null && index >= 0 && index < selected.TexturePaths.Count
            ? selected.TexturePaths[index] : null;
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && !PanelDismissal.HasOpenDropdown(e.OriginalSource as DependencyObject))
        {
            if (DismissAuxiliaryContent()) e.Handled = true;
            return;
        }
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) != 0 && (key == Key.Z || key == Key.Y) && e.OriginalSource is not TextBox)
        {
            if(key==Key.Y || (modifiers&ModifierKeys.Shift)!=0)RedoClick(sender,new RoutedEventArgs());else UndoClick(sender,new RoutedEventArgs());e.Handled=true;return;
        }
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        var alt = (modifiers & ModifierKeys.Alt) != 0;
        var textures = TextureList.IsKeyboardFocusWithin;
        var drawables = AssetList.IsKeyboardFocusWithin;
        if (!control && !shift && !alt && (textures || drawables))
        {
            if (key == Key.F2) { if (textures) RenameTextureClick(sender, new RoutedEventArgs()); else RenameDrawableClick(sender, new RoutedEventArgs()); e.Handled = true; }
            else if (key == Key.Delete) { if (textures) DetachTextureClick(sender, new RoutedEventArgs()); else RemoveClick(sender, new RoutedEventArgs()); e.Handled = true; }
            else if (key == Key.Return) { PreviewTabClick(sender, new RoutedEventArgs()); e.Handled = true; }
            if (e.Handled) return;
        }
        if (!control) return;
        if (key == Key.S) { if (shift) SaveAsClick(sender, new RoutedEventArgs()); else SaveClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.N && shift) { NewClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.O && shift) { OpenClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (key == Key.H && (textures || drawables)) { if (textures) ReplaceTextureClick(sender, new RoutedEventArgs()); else ReplaceClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.E && (textures || drawables)) { if (textures) RevealTextureClick(sender, new RoutedEventArgs()); else RevealClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.C && shift && drawables) { CopyNameClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.C && alt && drawables) { CopyGameNameClick(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (key == Key.P && shift && drawables) { CopyDrawablePathClick(sender, new RoutedEventArgs()); e.Handled = true; }
    }

    private void DetailsTabClick(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedIndex = 0;
    private void PreviewTabClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) { MessageBox.Show("Select a drawable to preview.", "Duck Studio"); return; }
        new PreviewWindow(selected, settings, TextureList.SelectedIndex) { Owner = this }.Show();
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(project, settings) { Owner = this };
        dialog.ShowDialog();
        SyncProject();
        SyncItem();
    }
    private void ErrorListClick(object sender, RoutedEventArgs e) { findingsAsset = null; RefreshValidation(); WorkspaceTabs.SelectedIndex = 3; }
    private void BuildTabClick(object sender, RoutedEventArgs e) { findingsAsset = null; RefreshValidation(); WorkspaceTabs.SelectedIndex = 3; }
    private void StatusIconClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as Image)?.DataContext is not ClothingAsset asset) return;
        AssetList.SelectedItem = asset;
        ShowFindingsFor(asset);
        e.Handled = true;
    }
    private void ShowSelectedFindingsClick(object sender, RoutedEventArgs e)
    {
        if (selected != null) ShowFindingsFor(selected);
    }
    private void ShowFindingsFor(ClothingAsset asset)
    {
        findingsAsset = asset;
        RefreshValidation();
        WorkspaceTabs.SelectedIndex = 3;
    }
    private void ValidateProjectClick(object sender, RoutedEventArgs e) { RefreshValidation(); WorkspaceTabs.SelectedIndex = 3; }
    private void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show("Duck Studio is an open-source clothing pack workspace for GTA V and FiveM. It uses your own GTA V installation for 3D ped previews.\n\nDuck Studio is not affiliated with Durty Cloth Tool or Rockstar Games.", "About Duck Studio", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ToggleDrawablesClick(object sender, RoutedEventArgs e)
    {
        autoCollapsedDrawables = false;
        SetDrawablesVisible(LeftColumn.Width.Value == 0);
    }

    private void SetDrawablesVisible(bool visible)
    {
        LeftColumn.Width = new GridLength(visible ? 500 : 0);
        DrawablesPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DrawablesSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ExpandDrawablesButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RemoveDuplicatesClick(object sender, RoutedEventArgs e)
    {

        var duplicates = project.Items.GroupBy(x => $"{x.Ped}/{x.Slot}/{x.Drawable}", StringComparer.OrdinalIgnoreCase).SelectMany(g => g.Skip(1)).ToList();
        if (duplicates.Count == 0) { StatusText.Text = "No duplicate drawable entries found"; return; }
        if (MessageBox.Show($"Remove {duplicates.Count} duplicate drawable entries from this project? Source files will not be deleted.", "Remove duplicates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var item in duplicates) project.Items.Remove(item);
        if (selected != null && duplicates.Contains(selected)) selected = null;
        RefreshList(); SyncItem();
    }

    private string? ResolveCorePath(bool prompt)
    {
        var corePath = Path.Combine(AppContext.BaseDirectory, "CodeWalker.Core.dll");
        if (!File.Exists(corePath)) corePath = project.CodeWalkerCorePath;
        if (!File.Exists(corePath) && File.Exists(project.CodeWalkerPath)) corePath = Path.Combine(Path.GetDirectoryName(project.CodeWalkerPath)!, "CodeWalker.Core.dll");
        if (File.Exists(corePath)) return corePath;
        if (!prompt) return null;
        var dialog = new OpenFileDialog { Filter = "CodeWalker Core (CodeWalker.Core.dll)|CodeWalker.Core.dll", Title = "Select your installed CodeWalker.Core.dll" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private async Task TryAssetPreview(bool showErrors, bool preserveCamera = false)
    {
        if (selected == null || !File.Exists(selected.ModelPath)) return;
        var version = ++previewVersion;
        var rage = Path.Combine(AppContext.BaseDirectory, "rage.exe");
        if (!File.Exists(rage)) { PreviewHint.Text = "The 3D renderer is missing"; return; }
        var dataHome = Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeftStudio");
        var output = Path.Combine(dataHome, "scenes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo(rage) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["HOME"] = dataHome;
        foreach (var arg in new[] { "--no-update-check", "asset-scene", "--ydd", selected.ModelPath, "-o", output }) start.ArgumentList.Add(arg);
        if (SelectedTexturePath() is { } ytd) { start.ArgumentList.Add("--ytd"); start.ArgumentList.Add(ytd); }
        PreviewHint.Text = "Loading item in 3D...";
        PreviewHint.Visibility = Visibility.Visible;
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Could not start the asset renderer.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var log = await stdout + await stderr;
            if (process.ExitCode != 0) throw new IOException(log);
            if (version != previewVersion) return;
            var result = PedScenePreview.Load(output);
            ClearModel();
            activeModel = new ModelVisual3D { Content = result.Model };
            ModelViewport.Children.Add(activeModel);
            activeSceneFolder = output;
            previewCenter = result.Center;
            activePreviewPed = false;
            if (preserveCamera) UpdateCamera(); else ResetPreviewCamera(result.Extent);
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (version == previewVersion)
            {
                PreviewHint.Text = "Item preview unavailable";
                if (showErrors) ShowError(ex);
            }
        }
        finally { if (activeSceneFolder != output) DeleteScene(output); }
    }

    private static void DeleteScene(string folder)
    {
        try { Directory.Delete(folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void OpenYtdClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTexturePath() is not { } ytd) { MessageBox.Show("Select clothing with a YTD variation first.", "Duck Studio"); return; }
        var corePath = ResolveCorePath(true);
        if (corePath == null) return;
        try
        {
            Cursor = Cursors.Wait;
            project.CodeWalkerCorePath = corePath;
            var bitmap = CodeWalkerPreview.LoadTextureFile(ytd, corePath) ?? throw new NotSupportedException("This YTD texture format is not supported yet.");
            TextureImage.Source = bitmap;
            TextureViewer.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { ShowError(ex.InnerException ?? ex); }
        finally { Cursor = Cursors.Arrow; }
    }

    private void ClearModel()
    {
        if (activeModel != null) ModelViewport.Children.Remove(activeModel);
        activeModel = null; previewCenter = new Point3D(0, 0, 0); PreviewHint.Visibility = Visibility.Visible;
        if (activeSceneFolder != null) { DeleteScene(activeSceneFolder); activeSceneFolder = null; }
    }

    private void LoadModel(string path)
    {
        try
        {
            ClearModel();
            var mesh = ObjPreview.Load(path);
            var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(115, 191, 178)));
            activeModel = new ModelVisual3D { Content = new GeometryModel3D(mesh, material) { BackMaterial = material } };
            ModelViewport.Children.Add(activeModel); PreviewHint.Visibility = Visibility.Collapsed;
            previewCenter = ObjPreview.LastCenter;
            ResetPreviewCamera(ObjPreview.LastExtent);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateCamera()
    {
        var target = previewCenter;
        var x = target.X + distance * Math.Cos(orbitY) * Math.Sin(orbitX);
        var y = target.Y + distance * Math.Sin(orbitY);
        var z = target.Z + distance * Math.Cos(orbitY) * Math.Cos(orbitX);
        ModelCamera.Position = new Point3D(x, y, z);
        ModelCamera.LookDirection = target - ModelCamera.Position;
        ModelCamera.UpDirection = new Vector3D(0, 1, 0);
    }
    private double FitPreviewDistance()
    {
        // WPF's FieldOfView is horizontal. On a tall or narrow preview panel,
        // the vertical angle is smaller and needs more distance to fit the mesh.
        var width = Math.Max(ModelViewport.ActualWidth, 1);
        var height = Math.Max(ModelViewport.ActualHeight, 1);
        var horizontal = ModelCamera.FieldOfView * Math.PI / 360;
        var vertical = Math.Atan(Math.Tan(horizontal) * height / width);
        var radius = Math.Max(previewExtent, .1) * Math.Sqrt(3) / 2;
        return radius * 1.2 / Math.Sin(Math.Min(horizontal, vertical));
    }
    private void ResetPreviewCamera(double extent)
    {
        previewExtent = extent;
        orbitX = 0;
        orbitY = .05;
        distance = FitPreviewDistance();
        UpdateCamera();
    }
    private void ResetPreviewClick(object sender, RoutedEventArgs e)
    {
        if (activeModel != null) ResetPreviewCamera(previewExtent);
    }
    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (activeModel == null) return;
        distance = Math.Max(distance, FitPreviewDistance() * .45);
        UpdateCamera();
    }
    private void ViewportMouseDown(object sender, MouseButtonEventArgs e) { if(e.ChangedButton == MouseButton.Left) { dragStart = e.GetPosition(ModelViewport); ModelViewport.CaptureMouse(); } }
    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (dragStart == null || e.LeftButton != MouseButtonState.Pressed) { dragStart = null; ModelViewport.ReleaseMouseCapture(); return; }
        var point = e.GetPosition(ModelViewport); orbitX += OrbitControls.HorizontalDelta(point.X - dragStart.Value.X, settings.InvertPanning); orbitY = Math.Clamp(orbitY + (point.Y - dragStart.Value.Y) * .008, -1.35, 1.35); dragStart = point; UpdateCamera();
    }
    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (activeModel == null) return;
        var fit = FitPreviewDistance();
        distance = Math.Clamp(distance * (e.Delta > 0 ? .86 : 1.16), fit * .45, fit * 15);
        UpdateCamera();
        e.Handled = true;
    }
    private void ShowError(Exception ex) => MessageBox.Show(ex.Message, "Duck Studio", MessageBoxButton.OK, MessageBoxImage.Error);
}

internal static class ObjPreview
{
    public static Point3D LastCenter { get; private set; } = new(0, 0, 0);
    public static double LastExtent { get; private set; } = 2;
    public static MeshGeometry3D Load(string path)
    {
        List<Point3D> vertices = [];
        List<Point> uvs = [];
        var mesh = new MeshGeometry3D();
        foreach (var raw in File.ReadLines(path))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (parts[0] == "v" && parts.Length >= 4) vertices.Add(new Point3D(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            if (parts[0] == "vt" && parts.Length >= 3) uvs.Add(new Point(Parse(parts[1]), 1 - Parse(parts[2])));
            if (parts[0] == "f" && parts.Length >= 4)
            {
                for (var i = 2; i < parts.Length - 1; i++)
                    foreach (var token in new[] { parts[1], parts[i], parts[i + 1] })
                    {
                        var ids = token.Split('/');
                        var vi = Index(ids[0], vertices.Count);
                        if (vi < 0 || vi >= vertices.Count) throw new InvalidDataException("OBJ face references a missing vertex.");
                        mesh.Positions.Add(vertices[vi]);
                        var ti = ids.Length > 1 && ids[1] != "" ? Index(ids[1], uvs.Count) : -1;
                        mesh.TextureCoordinates.Add(ti >= 0 && ti < uvs.Count ? uvs[ti] : new Point());
                        mesh.TriangleIndices.Add(mesh.Positions.Count - 1);
                    }
            }
        }
        if (mesh.Positions.Count == 0) throw new InvalidDataException("No triangles found in OBJ.");
        var bounds = mesh.Bounds;
        LastCenter = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        LastExtent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        return mesh;
    }
    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static int Index(string text, int count) { var n = int.Parse(text, CultureInfo.InvariantCulture); return n > 0 ? n - 1 : count + n; }
}
