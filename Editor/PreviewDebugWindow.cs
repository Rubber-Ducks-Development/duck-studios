using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

public sealed class PreviewDebugWindow : StudioToolWindow
{
    public PreviewDebugWindow(Func<string> report) : base("Preview debug report", 900, 660)
    {
        var hint = Hint("Reproduce the texture problem, then refresh and save this report. It includes file names, texture bindings and renderer errors, without image or model contents. Review it before sharing.");
        DockPanel.SetDock(hint, Dock.Top); Body.Children.Add(hint);
        var row = Footer();
        var refresh = Action("Refresh"); var copy = Action("Copy report"); var save = Action("Save report...");
        row.Children.Add(refresh); row.Children.Add(copy); row.Children.Add(save);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Text = report() };
        Body.Children.Add(text);
        refresh.Click += (_, _) => text.Text = report();
        copy.Click += (_, _) => { try { Clipboard.SetText(text.Text); } catch (System.Runtime.InteropServices.ExternalException ex) { MessageBox.Show(this, ex.Message, "Could not copy report"); } };
        save.Click += (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "Debug report (*.txt)|*.txt", FileName = "DuckStudio-preview-debug-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
            if (picker.ShowDialog(this) != true) return;
            try { File.WriteAllText(picker.FileName, text.Text); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "Could not save report"); }
        };
    }
}
