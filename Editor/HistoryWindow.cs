using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

public sealed class HistoryWindow : StudioToolWindow
{
    public ProjectRevision? RevisionToRestore { get; private set; }
    public HistoryWindow(ProjectHistory history,string identity,Func<string,Task> checkpoint):base("Project history")
    {
        var description=Hint("Saves, builds and automatic checkpoints appear here. Restore opens an independent working copy; the current project is captured before restoring.");DockPanel.SetDock(description,Dock.Top);Body.Children.Add(description);
        var footer=Footer();var restore=Action("Restore selected revision");var close=Action("Close");close.IsCancel=true;close.Click+=(_,_)=>Close();footer.Children.Add(restore);footer.Children.Add(close);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(400)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(18)});grid.ColumnDefinitions.Add(new());Body.Children.Add(grid);
        var revisions=new ListBox{SelectionMode=SelectionMode.Extended,Background=new SolidColorBrush(Color.FromRgb(39,39,41)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(83,83,85))};grid.Children.Add(revisions);
        var details=new StackPanel();Grid.SetColumn(details,2);grid.Children.Add(details);
        details.Children.Add(new TextBlock{Text="Revision details",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        var summary=Hint("Select a revision to review its changes.");details.Children.Add(summary);
        var changes=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=190};details.Children.Add(changes);
        var name=Field(details,"Checkpoint name","Manual checkpoint");var save=Action("Create checkpoint");save.Margin=new Thickness(0,12,0,0);save.HorizontalAlignment=HorizontalAlignment.Left;details.Children.Add(save);
        var compare=Action("Compare two selected revisions");compare.Margin=new Thickness(0,12,0,0);compare.HorizontalAlignment=HorizontalAlignment.Left;details.Children.Add(compare);
        details.Children.Add(Hint("Ctrl-click two revisions to compare settings, assets, content hashes and file sizes."));
        compare.Click+=async(_,_)=>
        {
            var chosen=revisions.SelectedItems.Cast<ProjectRevision>().OrderBy(r=>r.CreatedUtc).ToArray();
            if(chosen.Length!=2||chosen.Any(r=>!r.CanRestore)){MessageBox.Show(this,"Select two revisions with available asset copies.","Compare revisions");return;}
            compare.IsEnabled=false;try{var differences=await Task.Run(()=>ProjectComparison.Compare(ProjectIO.Load(chosen[0].SnapshotPath),ProjectIO.Load(chosen[1].SnapshotPath)));changes.Text=differences.Count==0?"These revisions have identical project settings and asset content.":string.Join("\n\n",differences);summary.Text=$"Comparison: {chosen[0].CreatedUtc.ToLocalTime():g} → {chosen[1].CreatedUtc.ToLocalTime():g}\n{differences.Count} changes";}catch(Exception ex){MessageBox.Show(this,ex.Message,"Comparison failed");}finally{compare.IsEnabled=true;}
        };
        void Refresh(){revisions.ItemsSource=history.Read(identity);if(revisions.Items.Count>0)revisions.SelectedIndex=0;else{summary.Text="No history yet. Create a checkpoint or save this project.";restore.IsEnabled=false;}}
        revisions.SelectionChanged+=(_,_)=>{if(revisions.SelectedItem is not ProjectRevision revision)return;summary.Text=revision.ProjectName+"\n"+revision.CreatedUtc.ToLocalTime().ToString("f")+"\n"+(revision.CanRestore?"Asset copies are available.":"The detailed event is retained; its recovery assets have expired.");changes.Text=string.Join("\n",revision.Changes);restore.IsEnabled=revision.CanRestore;};
        restore.Click+=(_,_)=>{if(revisions.SelectedItem is ProjectRevision revision&&revision.CanRestore){RevisionToRestore=revision;DialogResult=true;}};
        save.Click+=async(_,_)=>{save.IsEnabled=false;try{await checkpoint(string.IsNullOrWhiteSpace(name.Text)?"Manual checkpoint":name.Text.Trim());Refresh();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not create checkpoint");}finally{save.IsEnabled=true;}};
        Refresh();
    }
}
