using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DuckStudio;

public partial class MainWindow
{
    private readonly DispatcherTimer textureReloadTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextureLiveReload textureReload = new();
    private bool textureImportBusy, textureReloadBusy, textureReloadClosed, activePreviewPed;

    private void InitializeTextureReload()
    {
        textureReloadTimer.Tick += async (_, _) =>
        {
            if (textureReloadBusy || textureImportBusy || textureReloadClosed || closePending) return;
            var core = ResolveCorePath(false);
            if (core == null) return;
            textureReloadBusy = true;
            var current = project;
            try { await textureReload.RefreshAsync(current, core, (asset, error) => { if (!textureReloadClosed && ReferenceEquals(current, project)) TextureSourceUpdated(asset, error); }); if(!textureReloadClosed && ReferenceEquals(current,project)) await ReloadTattoosAsync(core); }
            catch (Exception ex) { if (!textureReloadClosed) StatusText.Text = "Texture reload failed: " + ex.Message; }
            finally { textureReloadBusy = false; }
        };
        textureReloadTimer.Start();
    }

    private void TextureSourceUpdated(ClothingAsset asset, string? error)
    {
        if (error != null) { StatusText.Text = error; StatusText.ToolTip = error; return; }
        thumbnailCache.Clear();
        StatusText.Text = "Updated linked texture for " + asset.Name; StatusText.ToolTip = null;
        RefreshValidation();
        if (!ReferenceEquals(selected, asset)) return;
        var hadPreview = activeModel != null && WorkspaceTabs.SelectedIndex == 1;
        SyncItem();
        if (TextureViewer.Visibility == Visibility.Visible && SelectedTexturePath() is { } path && ResolveCorePath(false) is { } core)
        {
            try { TextureImage.Source = CodeWalkerPreview.LoadTextureFile(path, core); }
            catch (Exception ex) { StatusText.Text = "Texture preview reload failed: " + ex.Message; }
        }
        if (hadPreview || WorkspaceTabs.SelectedIndex == 1)
        {
            if (activePreviewPed) _ = RenderPedAsync(true);
            else _ = TryAssetPreview(false, true);
        }
    }

    private void TextureDragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
        e.Effects = selected != null && !textureImportBusy && files is { Length: > 0 } && files.All(p => File.Exists(p) && TextureImport.IsSupported(p)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void TextureDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (selected == null || textureImportBusy || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var source = e.OriginalSource as DependencyObject;
        var row = source == null ? null : ItemsControl.ContainerFromElement(TextureList, source) as ListBoxItem;
        var index = row == null ? -1 : TextureList.ItemContainerGenerator.IndexFromContainer(row);
        await ImportTexturesAsync(selected, files, index);
    }

    private async Task ImportTexturesAsync(ClothingAsset asset, IEnumerable<string> files, int replacementIndex)
    {
        if (textureImportBusy) return;
        var current = project;
        var failures = new List<string>();
        int lastIndex = -1, imported = 0;
        textureImportBusy = true;
        AddTextureButton.IsEnabled = RemoveTextureButton.IsEnabled = false;
        try
        {
            foreach (var sourcePath in files)
            {
                var source = Path.GetFullPath(sourcePath);
                var index = replacementIndex;
                replacementIndex = -1;
                try
                {
                    if (!File.Exists(source) || !TextureImport.IsSupported(source)) throw new InvalidDataException("Unsupported image or missing file.");
                    if (index < 0 && asset.TexturePaths.Count >= 26) throw new InvalidDataException("This drawable already has 26 variations. Drop onto a row to replace one.");
                    if (index < 0 && (asset.TexturePaths.Contains(source, StringComparer.OrdinalIgnoreCase) || asset.TextureSources.Values.Any(l => l.SourcePath.Equals(source, StringComparison.OrdinalIgnoreCase)))) continue;
                    var old = index >= 0 && index < asset.TexturePaths.Count ? asset.TexturePaths[index] : null;
                    var texture = source;
                    TextureSourceLink? link = null;
                    if (!Path.GetExtension(source).Equals(".ytd", StringComparison.OrdinalIgnoreCase))
                    {
                        var core = ResolveCorePath(true);
                        if (core == null) break;
                        texture = TextureImport.NewDestination(source);
                        // Retain the original skin suffix when replacing a variation.
                        if (old != null) texture = Path.Combine(Path.GetDirectoryName(texture)!, Path.GetFileName(old));
                        var template = old ?? asset.TexturePaths.FirstOrDefault(File.Exists);
                        var requested = old != null && asset.TextureSources.TryGetValue(old, out var previous) ? previous.TextureName : null;
                        StatusText.Text = "Importing " + Path.GetFileName(source) + "...";
                        var name = await Task.Run(() => TextureImport.Convert(source, texture, core, asset.ModelPath, template, requested));
                        link = new TextureSourceLink { SourcePath = source, TextureName = name };
                    }
                    if (!ReferenceEquals(current, project) || !current.Items.Contains(asset)) break;
                    if (old != null)
                    {
                        asset.TexturePaths[index] = texture;
                        asset.TextureSources.Remove(old);
                        if (asset.TextureLabels.Remove(old, out var label)) asset.TextureLabels[texture] = label;
                        lastIndex = index;
                    }
                    else { asset.TexturePaths.Add(texture); lastIndex = asset.TexturePaths.Count - 1; }
                    if (link != null)
                    {
                        asset.TextureSources[texture] = link;
                        asset.TextureLabels.TryAdd(texture, Path.GetFileNameWithoutExtension(source));
                        textureReload.Remember(texture, source);
                    }
                    imported++;
                }
                catch (Exception ex) { failures.Add(Path.GetFileName(source) + ": " + (ex.InnerException ?? ex).Message); }
            }
            if (imported > 0 && ReferenceEquals(current, project))
            {
                if (ReferenceEquals(selected, asset) && lastIndex >= 0) textureSelections[asset.Id] = lastIndex;
                asset.NotifyTexturesChanged(); TextureSourceUpdated(asset, null);
                if (ReferenceEquals(selected, asset)) { TextureList.SelectedIndex = lastIndex; PreviewTextureBox.SelectedIndex = lastIndex; }
                StatusText.Text = "Imported " + imported + " texture" + (imported == 1 ? "" : "s") + ". Linked images update automatically when saved.";
            }
            if (failures.Count > 0) MessageBox.Show(this, string.Join("\n", failures), "Texture import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { textureImportBusy = false; AddTextureButton.IsEnabled = RemoveTextureButton.IsEnabled = selected != null; }
    }
}
