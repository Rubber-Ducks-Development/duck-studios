using System.Windows;
using System.Windows.Input;

namespace DuckStudio;

public partial class MainWindow
{
    private CancellationTokenSource? buildCancellation;

    private void CancelCurrentBuild()
    {
        if (buildCancellation == null) return;
        StatusText.Text = "Stopping the build...";
        buildCancellation.Cancel();
    }
    private void ShowCancelBuild()
    {
        BuildButton.Content = "Cancel build";
        SetButtonIcon(BuildButton, "remove");
    }
    private void BuildPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (buildCancellation != null && !(BuildButton.IsMouseOver && e.ChangedButton == MouseButton.Left)) e.Handled = true;
    }
    private void BuildPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (buildCancellation == null) return;
        if (e.Key == Key.Escape || (Keyboard.FocusedElement == BuildButton && e.Key is Key.Enter or Key.Space)) CancelCurrentBuild();
        e.Handled = true;
    }
}
