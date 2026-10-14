using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DuckStudio;

public partial class MainWindow
{
    private void ListRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(list, source) is ListBoxItem row) row.IsSelected = true;
    }
    private static string? Prompt(Window owner, string title, string current)
    {
        var dialog = new Window { Title=title, Owner=owner, WindowStartupLocation=WindowStartupLocation.CenterOwner, Width=410, Height=145, ResizeMode=ResizeMode.NoResize, Background=new SolidColorBrush(Color.FromRgb(51,51,53)), Foreground=Brushes.White };
        var panel=new DockPanel { Margin=new Thickness(14) }; dialog.Content=panel;
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var cancel=new Button { Content="Cancel", Width=78, Margin=new Thickness(0,8,8,0) };cancel.Click+=(_,_)=>dialog.DialogResult=false;buttons.Children.Add(cancel);
        var ok=new Button { Content="OK", Width=78, Margin=new Thickness(0,8,0,0) };ok.Click+=(_,_)=>dialog.DialogResult=true;buttons.Children.Add(ok);
        var box=new TextBox { Text=current, Height=29, Padding=new Thickness(5,3,5,3) };panel.Children.Add(box);
        dialog.Loaded+=(_,_)=>{box.Focus();box.SelectAll();};
        return dialog.ShowDialog()==true ? box.Text.Trim() : null;
    }
    private static void CopyFileTo(string source,string target)
    {
        if(Path.GetFullPath(source).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))return;
        File.Copy(source,target,true);
    }
    private static void RevealPath(string path)
    {
        if(File.Exists(path))Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{path}\""){UseShellExecute=true});
    }
    private void RenameDrawableClick(object sender,RoutedEventArgs e)
    {
        if(selected == null)return;
        var value=Prompt(this,"Rename drawable",selected.Name);
        if(!string.IsNullOrWhiteSpace(value)){selected.Name=value;SyncItem();RefreshList();}
    }
    private void ExportDrawableClick(object sender,RoutedEventArgs e)
    {
        if(selected == null)return;
        var picker=new SaveFileDialog { Filter="GTA drawable (*.ydd)|*.ydd", FileName=Path.GetFileName(selected.ModelPath) };
        if(picker.ShowDialog(this)!=true)return;
        try{CopyFileTo(selected.ModelPath,picker.FileName);}catch(Exception ex){ShowError(ex);}
    }
    private void ExportDrawableWithTexturesClick(object sender,RoutedEventArgs e)
    {
        if(selected == null)return;
        var picker=new OpenFolderDialog { Title="Export model and its texture variations" };
        if(picker.ShowDialog(this)!=true)return;
        try
        {
            foreach(var file in new[]{selected.ModelPath,selected.FirstPersonPath,selected.PhysicsPath}.Concat(selected.TexturePaths).Where(File.Exists))
                CopyFileTo(file,Path.Combine(picker.FolderName,Path.GetFileName(file)));
            StatusText.Text="Exported model and texture files to "+picker.FolderName;
        }
        catch(Exception ex){ShowError(ex);}
    }
    private void ExportDrawableXmlClick(object sender,RoutedEventArgs e)
    {
        if(selected == null)return;
        var picker=new SaveFileDialog { Filter="XML (*.xml)|*.xml", FileName=Path.GetFileName(selected.ModelPath)+".xml" };
        if(picker.ShowDialog(this)!=true)return;
        try { var core=ResolveCorePath(true);if(core==null)return;File.WriteAllText(picker.FileName,CodeWalkerPreview.ExportXmlFile(selected.ModelPath,Path.GetDirectoryName(picker.FileName)!,core)); }
        catch(Exception ex){ShowError(ex);}
    }
    private void ExportDrawableDdsClick(object sender,RoutedEventArgs e)
    {
        if(selected == null)return;
        var picker=new OpenFolderDialog { Title="Export embedded textures as DDS" };
        if(picker.ShowDialog(this)!=true)return;
        try { var core=ResolveCorePath(true);if(core==null)return;foreach(var file in selected.TexturePaths)CodeWalkerPreview.ExportXmlFile(file,picker.FolderName,core);StatusText.Text="Exported supported DDS textures"; }
        catch(Exception ex){ShowError(ex);}
    }
    private void ExportDrawablePngClick(object sender,RoutedEventArgs e)
    {
        if(selected == null)return;
        var picker=new OpenFolderDialog { Title="Export embedded textures as PNG" };
        if(picker.ShowDialog(this)!=true)return;
        try
        {
            var core=ResolveCorePath(true);if(core==null)return;var count=0;
            foreach(var file in selected.TexturePaths)
            {
                var dir=Path.Combine(picker.FolderName,Path.GetFileNameWithoutExtension(file));
                count+=CodeWalkerPreview.ExportTexturePngs(file,dir,core);
            }
            StatusText.Text=$"Exported {count} PNG textures";
        }
        catch(Exception ex){ShowError(ex);}
    }
    private void CopyGameNameClick(object sender,RoutedEventArgs e) { if(selected!=null)Clipboard.SetText(Path.GetFileNameWithoutExtension(selected.ModelPath)); }
    private void CopyDrawablePathClick(object sender,RoutedEventArgs e) { if(selected!=null)Clipboard.SetText(selected.ModelPath); }
    private void CopyCreatorInfoClick(object sender,RoutedEventArgs e)
    {
        if(selected!=null)Clipboard.SetText(JsonSerializer.Serialize(new CreatorClipboard(selected.CreatorName,selected.Website,selected.SupportUrl,selected.Notes)));
    }
    private void PasteCreatorInfoClick(object sender,RoutedEventArgs e)
    {
        if(selected==null)return;
        try
        {
            var data=JsonSerializer.Deserialize<CreatorClipboard>(Clipboard.GetText()) ?? throw new InvalidDataException("Clipboard has no creator data.");
            selected.CreatorName=data.CreatorName;selected.Website=data.Website;selected.SupportUrl=data.SupportUrl;selected.Notes=data.Notes;SyncItem();
        }
        catch(Exception ex){ShowError(ex);}
    }
    private sealed record CreatorClipboard(string CreatorName,string Website,string SupportUrl,string Notes);
    private int TextureIndex => selected != null && TextureList.SelectedIndex >= 0 && TextureList.SelectedIndex < selected.TexturePaths.Count ? TextureList.SelectedIndex : -1;
    private string? ContextTexturePath => TextureIndex >= 0 ? selected!.TexturePaths[TextureIndex] : null;
    private void RenameTextureClick(object sender,RoutedEventArgs e)
    {
        if(ContextTexturePath is not { } path)return;
        var value=Prompt(this,"Rename texture entry (display name)",selected!.TextureLabels.GetValueOrDefault(path,Path.GetFileNameWithoutExtension(path)));
        if(value!=null){selected.TextureLabels[path]=value;SyncItem();}
    }
    private async void ReplaceTextureClick(object sender,RoutedEventArgs e)
    {
        var index=TextureIndex;if(index<0)return;
        var asset=selected!;
        var picker=new OpenFileDialog { Filter=TextureImport.Filter, Title="Replace texture with YTD, photo or PSD" };
        if(picker.ShowDialog(this)!=true)return;
        await ImportTexturesAsync(asset,[picker.FileName],index);
    }
    private void RevealTextureClick(object sender,RoutedEventArgs e) { if(ContextTexturePath is { } path)RevealPath(selected!.TextureSources.TryGetValue(path,out var link)?link.SourcePath:path); }
    private void ExportTextureClick(object sender,RoutedEventArgs e)
    {
        if(ContextTexturePath is not { } path)return;
        var picker=new SaveFileDialog { Filter="GTA texture (*.ytd)|*.ytd", FileName=Path.GetFileName(path) };
        if(picker.ShowDialog(this)!=true)return;
        try{CopyFileTo(path,picker.FileName);}catch(Exception ex){ShowError(ex);}
    }
    private void ExportTextureXmlClick(object sender,RoutedEventArgs e)
    {
        if(ContextTexturePath is not { } path)return;
        var picker=new SaveFileDialog { Filter="XML (*.xml)|*.xml", FileName=Path.GetFileName(path)+".xml" };
        if(picker.ShowDialog(this)!=true)return;
        try{var core=ResolveCorePath(true);if(core==null)return;File.WriteAllText(picker.FileName,CodeWalkerPreview.ExportXmlFile(path,Path.GetDirectoryName(picker.FileName)!,core));}catch(Exception ex){ShowError(ex);}
    }
    private void ExportTextureDdsClick(object sender,RoutedEventArgs e)
    {
        if(ContextTexturePath is not { } path)return;
        var picker=new OpenFolderDialog { Title="Export embedded DDS textures" };
        if(picker.ShowDialog(this)!=true)return;
        try{var core=ResolveCorePath(true);if(core==null)return;CodeWalkerPreview.ExportXmlFile(path,picker.FolderName,core);StatusText.Text="Exported DDS textures to "+picker.FolderName;}catch(Exception ex){ShowError(ex);}
    }
    private void ExportTexturePngClick(object sender,RoutedEventArgs e)
    {
        if(ContextTexturePath is not { } path)return;
        var picker=new OpenFolderDialog { Title="Export embedded PNG textures" };
        if(picker.ShowDialog(this)!=true)return;
        try{var core=ResolveCorePath(true);if(core==null)return;var count=CodeWalkerPreview.ExportTexturePngs(path,picker.FolderName,core);StatusText.Text=$"Exported {count} PNG textures";}catch(Exception ex){ShowError(ex);}
    }
    private void CopyTextureLetterClick(object sender,RoutedEventArgs e) { if(TextureIndex>=0)Clipboard.SetText(((char)('a'+TextureIndex)).ToString()); }
    private void CopyTextureGameNameClick(object sender,RoutedEventArgs e) { if(ContextTexturePath is { } path)Clipboard.SetText(Path.GetFileNameWithoutExtension(path)); }
    private void CopyTexturePathClick(object sender,RoutedEventArgs e) { if(ContextTexturePath is { } path)Clipboard.SetText(path); }
}
