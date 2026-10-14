using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DuckStudio;

internal static class PanelDismissal
{
    public static DependencyObject? Parent(DependencyObject source) => source switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(source),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(source)
    };

    public static bool IsInteractive(DependencyObject? source, DependencyObject boundary)
    {
        for (var current = source; current != null && current != boundary; current = Parent(current))
        {
            if (current is ButtonBase or TextBoxBase or PasswordBox or ComboBox or ListBox or TreeView
                or MenuItem or Hyperlink or Thumb or ScrollBar or Slider or Viewport3D or TabItem)
                return true;
            if (current is FrameworkElement element && element.Cursor == Cursors.Hand) return true;
        }
        return false;
    }

    public static bool HasOpenDropdown(DependencyObject? source)
    {
        for (var current = source; current != null; current = Parent(current))
            if (current is ComboBox { IsDropDownOpen: true }) return true;
        return false;
    }
}
