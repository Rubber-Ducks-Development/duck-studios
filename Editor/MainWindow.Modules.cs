using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DuckStudio;

public partial class MainWindow
{
    private static string DataRoot=>Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeftStudio");
    private readonly ProjectHistory history=new(Path.Combine(DataRoot,"History"));
    private readonly ApplicationUpdates updater=new();
    private readonly DispatcherTimer updateCheckTimer=new(){Interval=TimeSpan.FromHours(6)};
    private string? pendingUpdateInstaller;
    private bool updateChecking;
    private readonly Dictionary<string,(long Ticks,long Length,string TextureName)> tattooReloaded=[];
    private readonly Dictionary<string,(long Ticks,long Length,string TextureName)> tattooPending=[];
    private void EnsureHistoryIdentity(){if(string.IsNullOrWhiteSpace(project.HistoryId))project.HistoryId=Guid.NewGuid().ToString("N");}
    private async Task RecordHistoryAsync(string reason)
    {
        EnsureHistoryIdentity();var snapshot=JsonSerializer.Deserialize<ClothingProject>(ProjectState(),ProjectIO.Json)!;
        try{await history.RecordAsync(snapshot,reason);}
        catch(Exception ex){if(!textureReloadClosed)BackupStatusText.ToolTip="History checkpoint failed: "+ex.Message;}
    }
    private async void HistoryClick(object sender,RoutedEventArgs e)
    {
        EnsureHistoryIdentity();await RecordHistoryAsync("Opened history");
        var dialog=new HistoryWindow(history,project.HistoryId,RecordHistoryAsync){Owner=this};
        if(dialog.ShowDialog()!=true||dialog.RevisionToRestore is not { } revision)return;
        try
        {
            await RecordHistoryAsync("Before restoring revision");var restored=await Task.Run(()=>history.Restore(revision));
            CaptureEdit();InstallEditedProject(restored);projectPath=null;TextureImport.RestoreWorkingCopies(project);CaptureEdit();WorkspaceTabs.SelectedIndex=0;
            StatusText.Text="Restored "+revision.CreatedUtc.ToLocalTime().ToString("g")+". Save as a project to keep this working copy.";await RecordHistoryAsync("Restored revision");
        }
        catch(Exception ex){ShowError(ex);}
    }
    private async void TattoosClick(object sender,RoutedEventArgs e)
    {
        if(!EnsureProject())return;var core=ResolveCorePath(true);if(core==null)return;
        var snapshot=JsonSerializer.Deserialize<ClothingProject>(ProjectState(),ProjectIO.Json)!;
        foreach(var tattoo in snapshot.Tattoos)
        {
            var destination=TextureImport.NewDestination(tattoo.TextureName+".png");
            if(File.Exists(tattoo.TexturePath)){Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(tattoo.TexturePath,destination);}
            tattoo.TexturePath=destination;
        }
        var dialog=new TattooWindow(snapshot,settings,core){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        await RecordHistoryAsync("Before tattoo edits");project.Tattoos=dialog.Tattoos;RefreshValidation();await RecordHistoryAsync("Edited tattoos");StatusText.Text=$"{project.Tattoos.Count} tattoos in this project";
    }
    private async void OptimizersClick(object sender,RoutedEventArgs e)
    {
        var core=ResolveCorePath(true);if(core==null)return;
        var dialog=new OptimizerWindow(project,core){Owner=this};if(dialog.ShowDialog()!=true)return;
        var current=project;var snapshot=JsonSerializer.Deserialize<ClothingProject>(ProjectState(),ProjectIO.Json)!;
        await RecordHistoryAsync("Before optimization");IsEnabled=false;
        try
        {
            var limit=dialog.MaxDimension;var compress=dialog.Compress;var duplicates=dialog.RemoveDuplicates;
            StatusText.Text="Creating optimized texture copies...";
            var result=await Task.Run(()=>ProjectOptimizer.Apply(snapshot,core,limit,compress,duplicates));
            if(!ReferenceEquals(current,project))return;
            foreach(var item in project.Items)
            {
                var optimized=snapshot.Items.Single(a=>a.Id==item.Id);item.TexturePaths=optimized.TexturePaths;item.TextureLabels=optimized.TextureLabels;item.TextureSources=optimized.TextureSources;item.NotifyTexturesChanged();
            }
            SyncItem();RefreshValidation();if(selected!=null)TextureSourceUpdated(selected,null);
            await RecordHistoryAsync("Optimized textures");StatusText.Text=$"Optimized {result.Files} texture files: {result.Before/1048576d:0.00} → {result.After/1048576d:0.00} MB; {result.Removed} duplicate variations removed.";
        }
        catch(Exception ex){ShowError(ex.InnerException??ex);}
        finally{IsEnabled=true;}
    }
    private async Task ReloadTattoosAsync(string core)
    {
        var current=project;
        foreach(var tattoo in current.Tattoos.ToArray())
        {
            if(!File.Exists(tattoo.SourcePath))continue;
            var file=new FileInfo(tattoo.SourcePath);var stamp=(file.LastWriteTimeUtc.Ticks,file.Length,tattoo.TextureName);
            if(tattooReloaded.TryGetValue(tattoo.Id,out var loaded)&&loaded==stamp&&File.Exists(tattoo.TexturePath))continue;
            if(!tattooPending.TryGetValue(tattoo.Id,out var pending)||pending!=stamp){tattooPending[tattoo.Id]=stamp;continue;}
            try
            {
                await Task.Run(()=>TattooBuilder.Prepare(new ClothingProject{Tattoos=[tattoo]},core));
                if(!ReferenceEquals(current,project))return;tattooReloaded[tattoo.Id]=stamp;tattooPending.Remove(tattoo.Id);tattoo.NotifyChanged();StatusText.Text="Updated tattoo artwork: "+tattoo.Name;
            }
            catch(Exception ex){StatusText.Text="Tattoo reload will retry: "+(ex.InnerException??ex).Message;}
        }
    }
    private void StartUpdateChecks()
    {
        updateCheckTimer.Tick+=async(_,_)=>{if(settings.CheckUpdatesAutomatically)await CheckUpdatesAsync(false);};updateCheckTimer.Start();
        if(settings.CheckUpdatesAutomatically)_=CheckUpdatesAsync(false);
    }
    private async void CheckUpdatesClick(object sender,RoutedEventArgs e)=>await CheckUpdatesAsync(true);
    private void UpdateSettingsClick(object sender,RoutedEventArgs e)
    {
        var dialog=new StudioToolWindow("Update settings",720,470){Owner=this};var panel=new StackPanel();dialog.Body.Children.Add(panel);
        panel.Children.Add(StudioToolWindow.Hint("Duck Studio checks on startup and every six hours. It asks before downloading and installing a newer release. Your project is saved or backed up before the app closes for installation."));
        var automatic=new CheckBox{Content="Check for updates automatically",IsChecked=settings.CheckUpdatesAutomatically,Foreground=System.Windows.Media.Brushes.White,Margin=new Thickness(0,8,0,12)};panel.Children.Add(automatic);
        var feed=StudioToolWindow.Field(panel,"Signed update feed (HTTPS URL or local file URI)",ApplicationUpdates.ResolveFeed(settings));
        panel.Children.Add(StudioToolWindow.Hint("Use the latest.json feed published with Duck Studio releases. Local release folders can be selected too."));
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,15,0,0)};panel.Children.Add(row);
        var browse=StudioToolWindow.Action("Choose local feed");var save=StudioToolWindow.Action("Save");var cancel=StudioToolWindow.Action("Cancel");cancel.IsCancel=true;row.Children.Add(browse);row.Children.Add(save);row.Children.Add(cancel);
        browse.Click+=(_,_)=>{var file=new Microsoft.Win32.OpenFileDialog{Filter="Signed update manifest|*.json"};if(file.ShowDialog(dialog)==true)feed.Text=new Uri(file.FileName).AbsoluteUri;};
        save.Click+=(_,_)=>{try{if(!string.IsNullOrWhiteSpace(feed.Text))ApplicationUpdates.ValidateLocation(feed.Text.Trim());settings.UpdateFeedUrl=feed.Text.Trim();settings.CheckUpdatesAutomatically=automatic.IsChecked==true;settings.SkippedUpdateVersion="";settings.Save();dialog.DialogResult=true;}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Check update feed");}};cancel.Click+=(_,_)=>dialog.Close();dialog.ShowDialog();
    }
    private async Task CheckUpdatesAsync(bool manual)
    {
        if(updateChecking||closePending||textureReloadClosed)return;
        var feed=ApplicationUpdates.ResolveFeed(settings);
        if(string.IsNullOrWhiteSpace(feed)){if(manual){MessageBox.Show(this,"Choose an update feed in Help > Update settings. The release folder also includes a local signed feed.","Update feed needed");UpdateSettingsClick(this,new RoutedEventArgs());}return;}
        updateChecking=true;
        try
        {
            var release=await updater.CheckAsync(feed);if(textureReloadClosed)return;
            if(release==null){if(manual)MessageBox.Show(this,"Duck Studio is up to date ("+ApplicationUpdates.CurrentVersion+").","Duck Studio updates");return;}
            if(!manual&&settings.SkippedUpdateVersion==release.Version)return;
            var prompt=new StudioToolWindow("Update available",680,490){Owner=this};var content=new StackPanel();prompt.Body.Children.Add(content);
            content.Children.Add(new TextBlock{Text="Do you want to install the update?",FontSize=18,Margin=new Thickness(0,0,0,12)});
            content.Children.Add(StudioToolWindow.Hint($"Duck Studio {release.Version} is available. You have {ApplicationUpdates.CurrentVersion}. The app will close after a recovery checkpoint, then the installer will update this folder."));
            content.Children.Add(new TextBox{Text=release.Notes,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};content.Children.Add(row);var install=StudioToolWindow.Action("Install update");var later=StudioToolWindow.Action("Not now");later.IsCancel=true;row.Children.Add(install);row.Children.Add(later);install.Click+=(_,_)=>prompt.DialogResult=true;later.Click+=(_,_)=>prompt.DialogResult=false;
            if(prompt.ShowDialog()!=true){settings.SkippedUpdateVersion=release.Version;settings.Save();return;}
            var progress=new Progress<double>(value=>StatusText.Text=$"Downloading update {release.Version}: {value:P0}");
            pendingUpdateInstaller=await updater.DownloadAsync(release,DataRoot,progress);
            StatusText.Text="Update verified. Preparing to close and install...";Close();
        }
        catch(Exception ex){if(!textureReloadClosed){StatusText.Text="Update check failed: "+ex.Message;if(manual)MessageBox.Show(this,ex.Message,"Duck Studio update failed",MessageBoxButton.OK,MessageBoxImage.Warning);}}
        finally{updateChecking=false;}
    }
    private void LaunchPendingUpdate()
    {
        if(pendingUpdateInstaller==null)return;
        Process.Start(ApplicationUpdates.InstallerStartInfo(pendingUpdateInstaller, AppContext.BaseDirectory, Environment.ProcessId));
    }
    public void OpenRequestedProject(string path)=>OpenProjectPath(path);
}
