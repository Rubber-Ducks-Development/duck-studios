using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DuckStudio;

public partial class MainWindow
{
    private readonly EditJournal edits=new(Path.Combine(DataRoot,"Undo",Guid.NewGuid().ToString("N")));
    private readonly DispatcherTimer editTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    private bool editRestoring;
    private void InitializeEditing()
    {
        edits.Reset(project);editTimer.Tick+=(_,_)=>CaptureEdit();editTimer.Start();
        PreviewMouseDown+=(_,e)=>{if(!PanelDismissal.IsInteractive(e.OriginalSource as DependencyObject,this))return;CaptureEdit();};
        Closed+=(_,_)=>editTimer.Stop();
    }
    private void CaptureEdit()
    {
        if(editRestoring||syncing||textureImportBusy||textureReloadBusy||closePending||buildBusy||!IsEnabled)return;
        try{edits.Observe(project);UndoMenu.IsEnabled=edits.CanUndo;RedoMenu.IsEnabled=edits.CanRedo;}
        catch(Exception ex){StatusText.ToolTip="Undo checkpoint could not be captured: "+ex.Message;}
    }
    private void ResetEdits(){edits.Reset(project);UndoMenu.IsEnabled=RedoMenu.IsEnabled=false;}
    private void InstallEditedProject(ClothingProject snapshot)
    {
        var id=selected?.Id;editRestoring=true;
        try
        {
            // Keep live preview subscriptions attached to the same asset objects.
            for(var i=0;i<snapshot.Items.Count;i++)
            {
                var incoming=snapshot.Items[i];var existing=project.Items.FirstOrDefault(x=>x.Id==incoming.Id);if(existing==null)continue;
                foreach(var property in typeof(ClothingAsset).GetProperties().Where(p=>p.CanWrite))property.SetValue(existing,property.GetValue(incoming));snapshot.Items[i]=existing;
            }
            project=snapshot;selected=project.Items.FirstOrDefault(x=>x.Id==id)??project.Items.FirstOrDefault();ClearModel();SyncProject();SyncItem();
            foreach(var asset in project.Items)asset.NotifyTexturesChanged();
        }
        finally{editRestoring=false;}
    }
    private void UndoClick(object sender,RoutedEventArgs e)
    {
        if(textureImportBusy||textureReloadBusy){StatusText.Text="Wait for the texture operation to finish before undoing.";return;}
        try{CaptureEdit();if(!edits.CanUndo)return;InstallEditedProject(edits.Undo());UndoMenu.IsEnabled=edits.CanUndo;RedoMenu.IsEnabled=edits.CanRedo;StatusText.Text="Undid project edit. Restored assets are independent working copies.";}
        catch(Exception ex){ShowError(ex);}
    }
    private void RedoClick(object sender,RoutedEventArgs e)
    {
        try{CaptureEdit();if(!edits.CanRedo)return;InstallEditedProject(edits.Redo());UndoMenu.IsEnabled=edits.CanUndo;RedoMenu.IsEnabled=edits.CanRedo;StatusText.Text="Redid project edit.";}
        catch(Exception ex){ShowError(ex);}
    }
    private void UvToolsClick(object sender,RoutedEventArgs e)
    {
        if(selected==null){StatusText.Text="Select a drawable to inspect its UV atlas.";return;}var core=ResolveCorePath(true);if(core==null)return;
        try{new UvWindow(selected,core){Owner=this}.ShowDialog();}catch(Exception ex){ShowError(ex.InnerException??ex);}
    }
    private void OutfitBuilderClick(object sender,RoutedEventArgs e)
    {
        var core=ResolveCorePath(true);if(core==null)return;CaptureEdit();
        try{var dialog=new OutfitWindow(project,settings,core){Owner=this};if(dialog.ShowDialog()==true){project.Outfits=dialog.Result.Outfits;CaptureEdit();_ = RecordHistoryAsync("Edited outfit presets");StatusText.Text=$"Saved {project.Outfits.Count} outfit presets.";}}
        catch(Exception ex){ShowError(ex.InnerException??ex);}
    }
    private void BatchEditingClick(object sender,RoutedEventArgs e)
    {
        var core=ResolveCorePath(true);if(core==null)return;CaptureEdit();
        try{var dialog=new BatchWindow(project,core,selected?.Id){Owner=this};if(dialog.ShowDialog()==true){InstallEditedProject(dialog.Result);CaptureEdit();_ = RecordHistoryAsync("Applied batch edits");}}
        catch(Exception ex){ShowError(ex.InnerException??ex);}
    }
    private void DeepValidationClick(object sender,RoutedEventArgs e)
    {
        var core=ResolveCorePath(true);if(core==null)return;
        new ValidationWindow(project,core,id=>{selected=project.Items.FirstOrDefault(x=>x.Id==id);SearchBox.Text="";QuickFilterBox.SelectedIndex=0;RefreshList();SyncItem();WorkspaceTabs.SelectedIndex=0;}){Owner=this}.ShowDialog();
    }
    private void ModelOptimizationClick(object sender,RoutedEventArgs e)
    {
        if(selected==null){StatusText.Text="Select a drawable to inspect or generate LODs.";return;}var core=ResolveCorePath(true);if(core==null)return;CaptureEdit();
        try{var dialog=new ModelOptimizerWindow(selected,core){Owner=this};if(dialog.ShowDialog()==true&&dialog.ReplacementPath is {} model){selected.ModelPath=model;selected.GeneratedLods=true;selected.NotifyTexturesChanged();ClearModel();SyncItem();RefreshList();CaptureEdit();_ = RecordHistoryAsync("Generated model LODs");}}
        catch(Exception ex){ShowError(ex.InnerException??ex);}
    }
    private void ProductScreenshotsClick(object sender,RoutedEventArgs e)
    {
        var core=ResolveCorePath(true);if(core==null)return;new ScreenshotWindow(project,selected,settings,core){Owner=this}.ShowDialog();
    }
    private void CompanionClick(object sender,RoutedEventArgs e)
    {
        var picker=new Microsoft.Win32.OpenFolderDialog{Title="Export FiveM test companion"};if(picker.ShowDialog(this)!=true)return;
        try{FiveMCompanion.Export(project,picker.FolderName);StatusText.Text="Exported duckstudio_test. Build the clothing pack, ensure both resources, then run /duckstudio.";}
        catch(Exception ex){ShowError(ex);}
    }
}
