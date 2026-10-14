using System.Windows;
using System.Windows.Input;

namespace DuckStudio;

public partial class MainWindow
{
    private bool DismissAuxiliaryContent()
    {
        if (WorkspaceTabs.SelectedIndex == 0 && DetailsTabs.SelectedIndex <= 0 && TextureViewer.Visibility == Visibility.Visible)
        {
            TextureViewer.Visibility = Visibility.Collapsed;
            TextureImage.Source = null;
            return true;
        }
        if (WorkspaceTabs.SelectedIndex > 0)
        {
            TextureViewer.Visibility = Visibility.Collapsed;
            TextureImage.Source = null;
            if (WorkspaceTabs.SelectedIndex == 1) ClearModel();
            WorkspaceTabs.SelectedIndex = 0;
            AssetList.Focus();
            return true;
        }
        if (DetailsTabs.SelectedIndex > 0)
        {
            TextureViewer.Visibility = Visibility.Collapsed;
            TextureImage.Source = null;
            DetailsTabs.SelectedIndex = 0;
            return true;
        }
        return false;
    }

    private void WorkspacePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || PanelDismissal.IsInteractive(e.OriginalSource as DependencyObject, WorkspaceTabs)) return;
        if (DismissAuxiliaryContent()) e.Handled = true;
    }
}
