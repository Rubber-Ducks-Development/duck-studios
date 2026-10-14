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
        SetButtonIcon(ImportPackageButton, "import-package");
        SetButtonIcon(ImportGtaButton, "add-item");
        SetButtonIcon(AddMaleButton, "male-toolbar");
        SetButtonIcon(AddFemaleButton, "female-toolbar");
        SetButtonIcon(RemoveItemButton, "remove");
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

    private static void SetButtonIcon(Button button, string icon)
    {
        if (button.Content is not string label) return;
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var graphic = new Image
        {
            Source = IconAssets.Get(icon), Width = 20, Height = 20,
            Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0),
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
