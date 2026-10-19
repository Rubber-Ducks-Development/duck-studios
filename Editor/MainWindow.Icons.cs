using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DuckStudio;

public partial class MainWindow
{
    private void ApplyIcons()
    {
        SetButtonIcon(ImportPackageButton, "import-package", 16);
        SetButtonIcon(ImportGtaButton, "add-item", 16);
        SetButtonIcon(AddMaleButton, "male-toolbar", 16);
        SetButtonIcon(AddFemaleButton, "female-toolbar", 16);
        SetButtonIcon(RemoveItemButton, "remove", 16);
        SetButtonIcon(ErrorListButton, "error-list");
        SetButtonIcon(SettingsFooterButton, "settings");
        SetButtonIcon(BuildFooterButton, "build-project");
        SetButtonIcon(AddTextureButton, "add-display-item");
        SetButtonIcon(RemoveTextureButton, "remove");
        SetIconOnly(MoveTextureUpButton, "move-up", "Move texture up");
        SetIconOnly(MoveTextureDownButton, "move-down", "Move texture down");
        SetButtonIcon(ViewYtdButton, "image-placeholder");
        SetButtonIcon(DetailsPreviewButton, "preview-eye");
        SetButtonIcon(ExportZipButton, "build-project");
    }

    private static void SetButtonIcon(Button button, string icon, double size = 20)
    {
        if (button.Content is not string label) return;
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var graphic = new Image
        {
            Source = IconAssets.Get(icon), Width = size, Height = size,
            Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, size == 16 ? 4 : 6, 0),
            SnapsToDevicePixels = true, UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(graphic, BitmapScalingMode.HighQuality);
        content.Children.Add(graphic);
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });
        content.Children.Add(text);
        button.Content = content;
        AutomationProperties.SetName(button, label);
    }

    private static void SetIconOnly(Button button, string icon, string label)
    {
        var graphic = new Image { Source = IconAssets.Get(icon), Width = 20, Height = 20, Stretch = Stretch.Uniform, SnapsToDevicePixels = true, UseLayoutRounding = true };
        RenderOptions.SetBitmapScalingMode(graphic, BitmapScalingMode.HighQuality);
        button.Content = graphic;
        button.Padding = new Thickness(4, 2, 4, 2);
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }
}
