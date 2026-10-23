using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DuckStudio;

public partial class MainWindow
{
    private readonly DispatcherTimer backupTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly ProjectBackups backups = new(Path.Combine(
        Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeftStudio"), "Backups"));
    private string recoverySession = "Unsaved-" + Guid.NewGuid().ToString("N");
    private readonly System.Threading.SemaphoreSlim backupGate = new(1, 1);
    private string savedProjectState = "";
    private bool closeApproved, closePending;

    private string ProjectState() => JsonSerializer.Serialize(project, ProjectIO.Json);
    private void MarkSaved() => savedProjectState = ProjectState();
    private async Task BackupAsync()
    {
        if (string.IsNullOrWhiteSpace(project.Name) && project.Items.Count == 0) return;
        var snapshot = JsonSerializer.Deserialize<ClothingProject>(ProjectState(), ProjectIO.Json)!;
        var identity = projectPath == null ? recoverySession : Path.GetFullPath(projectPath);
        await backupGate.WaitAsync();
        try
        {
            var path = await Task.Run(() => backups.Capture(snapshot, identity));
            if (path != null) { BackupStatusText.Text = "Backup " + DateTime.Now.ToString("h:mm tt"); await RecordHistoryAsync("Automatic checkpoint"); }
            BackupStatusText.ToolTip = "Automatic backups every minute. Open Options > Open automatic backups to restore a .duckproj (legacy .duck and .weftproj backups also open).";
        }
        catch (Exception ex)
        {
            BackupStatusText.Text = "Backup failed"; BackupStatusText.ToolTip = ex.Message;
            throw;
        }
        finally { backupGate.Release(); }
    }
    private async void BackupTick(object? sender, EventArgs e)
    {
        if (closePending || buildBusy || !IsEnabled) return;
        try { await BackupAsync(); } catch (Exception) { /* Failure remains visible in the footer. */ }
    }
    private void OpenBackupsClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(backups.Root); Process.Start(new ProcessStartInfo(backups.Root) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private bool SaveProject(bool saveAs = false)
    {
        var target = !saveAs && projectPath != null && ProjectFiles.IsCurrentProject(projectPath) ? projectPath : null;
        if (target == null)
        {
            var suggested = projectPath == null ? project.Collection + ProjectFiles.Extension : ProjectFiles.SuggestedSavePath(projectPath);
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = ProjectFiles.SaveFilter, DefaultExt = ProjectFiles.Extension,
                AddExtension = true, OverwritePrompt = true, FileName = suggested };
            dialog.FileOk += (_, e) =>
            {
                if (ProjectFiles.IsCurrentProject(dialog.FileName)) return;
                e.Cancel = true;
                MessageBox.Show(this, "Save Duck Studio projects with the .duckproj extension.", "Duck Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            if (dialog.ShowDialog(this) != true) return false;
            target = dialog.FileName;
        }
        try
        {
            ProjectIO.Save(project, target); projectPath = target; MarkSaved(); settings.RememberProject(target);
            StatusText.Text = $"Saved {Path.GetFileName(target)}"; _ = BackupAfterSaveAsync(); _ = RecordHistoryAsync("Saved project"); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    private async Task BackupAfterSaveAsync() { try { await BackupAsync(); } catch (Exception) { } }

    private bool ConfirmProjectSwitch()
    {
        if (ProjectState()==savedProjectState || (project.Items.Count==0 && project.Tattoos.Count==0 && string.IsNullOrWhiteSpace(project.Name))) return true;
        var choice=MessageBox.Show(this,"Save changes before switching projects?", "Unsaved project",MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
        if(choice==MessageBoxResult.Cancel)return false;
        if(choice==MessageBoxResult.Yes)return SaveProject();
        try { var snapshot=System.Text.Json.JsonSerializer.Deserialize<ClothingProject>(ProjectState(),ProjectIO.Json)!;backups.Capture(snapshot,projectPath??recoverySession);return true; }
        catch(Exception ex){ShowError(ex);return false;}
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closeApproved) return;
        e.Cancel = true;
        if (buildBusy) { StatusText.Text = "Wait for the build or close its options dialog before closing Duck Studio."; return; }
        if (closePending) return;
        closePending = true;
        try
        {
            var dirty = ProjectState() != savedProjectState;
            var choice = pendingUpdateInstaller!=null && !dirty ? 2 : ConfirmClose(dirty);
            if (choice == 0) return;
            IsEnabled = false;
            while (textureImportBusy || textureReloadBusy) await Task.Delay(50);
            if (choice == 1 && !SaveProject()) return;
            try { await BackupAsync(); }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, "The automatic backup could not be completed. Unsaved changes may not be recoverable.\n\n" + ex.Message + "\n\nClose anyway?",
                    "Backup failed", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            }
            closeApproved = true; Close();
        }
        finally { if (!closeApproved) { IsEnabled = true; pendingUpdateInstaller = null; } closePending = false; }
    }

    // Returns 0 for cancel, 1 for save and close, 2 for close with recovery copy.
    private int ConfirmClose(bool dirty)
    {
        var result = 0;
        var dialog = new Window { Title = "Close Duck Studio?", Owner = this, Icon = Icon, Width = 490, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(48,48,48)), Foreground = Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Are you sure you want to close?", FontSize = 17, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = dirty ? "You have unsaved changes. A recovery backup will be created before closing." : "A recovery backup will be checked before closing.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0,12,0,18) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        void Add(string text, int value, bool cancel = false)
        {
            var button = new Button { Content = text, Padding = new Thickness(12,6,12,6), Margin = new Thickness(6,0,0,0), IsCancel = cancel };
            button.Click += (_, _) => { result = value; dialog.Close(); }; buttons.Children.Add(button);
            if (cancel) dialog.Loaded += (_, _) => button.Focus();
        }
        if (dirty) Add("Save and close", 1);
        Add(dirty ? "Close without saving" : "Close", 2); Add("Cancel", 0, true);
        dialog.ShowDialog(); return result;
    }
}
