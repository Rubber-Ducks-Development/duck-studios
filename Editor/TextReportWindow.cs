using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

public class TextReportWindow : StudioToolWindow
{
    public TextBox ReportText { get; }
    public TextReportWindow(string title, string hint, string report, string fileName, Func<string>? refresh = null) : base(title, 900, 660)
    {
        var area = SystemParameters.WorkArea;
        MaxWidth = Math.Max(320, area.Width); MaxHeight = Math.Max(240, area.Height);
        MinWidth = Math.Min(640, MaxWidth); MinHeight = Math.Min(360, MaxHeight);
        Width = Math.Min(900, MaxWidth); Height = Math.Min(660, MaxHeight);
        SizeToContent = SizeToContent.Manual;
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Body.Children.Add(grid);
        grid.Children.Add(Hint(hint));
        ReportText = new TextBox
        {
            IsReadOnly = true, IsReadOnlyCaretVisible = true, AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = report, MinHeight = 0, VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(ReportText, 1); grid.Children.Add(ReportText);
        ReportText.PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta == 0 || SystemParameters.WheelScrollLines == 0) return;
            var step = SystemParameters.WheelScrollLines < 0 ? ReportText.ViewportHeight : SystemParameters.WheelScrollLines * ReportText.FontSize * 1.35;
            ReportText.ScrollToVerticalOffset(ReportText.VerticalOffset - e.Delta / 120d * step);
            e.Handled = true;
        };
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(row, 2); grid.Children.Add(row);
        if (refresh != null)
        {
            var button = Action("Refresh"); row.Children.Add(button);
            button.Click += (_, _) => { var offset = ReportText.VerticalOffset; ReportText.Text = refresh(); ReportText.UpdateLayout(); ReportText.ScrollToVerticalOffset(offset); };
        }
        var copy = Action("Copy report"); var save = Action("Save report..."); var close = Action("Close"); close.IsCancel = true;
        row.Children.Add(copy); row.Children.Add(save); row.Children.Add(close);
        copy.Click += (_, _) => { try { Clipboard.SetText(ReportText.Text); } catch (System.Runtime.InteropServices.ExternalException ex) { MessageBox.Show(this, ex.Message, "Could not copy report"); } };
        save.Click += (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = fileName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
            if (picker.ShowDialog(this) != true) return;
            try { File.WriteAllText(picker.FileName, ReportText.Text); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "Could not save report"); }
        };
        close.Click += (_, _) => Close();
    }
}
