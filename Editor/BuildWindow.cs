using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DuckStudio;

public sealed class BuildWindow : Window
{
    private readonly TextBox collectionBox;
    private readonly TextBox outputBox;
    private readonly CheckBox groupedBox;
    private readonly CheckBox zipBox;
    private readonly ComboBox platform;
    public string Collection => collectionBox.Text.Trim();
    public string OutputFolder => outputBox.Text.Trim();
    public bool CreateZip => zipBox.IsChecked == true;
    public PackBuilder.BuildOptions Options => new(groupedBox.IsChecked == true, (BuildTarget)platform.SelectedIndex);

    public BuildWindow(ClothingProject project, bool zipDefault = false)
    {
        Title = "Duck Studio - Build Project";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/DuckStudio;component/Assets/AppIconSource.png"));
        Width = 510; Height = 590; MinWidth = 480; MinHeight = 530;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = Brush("#303032"); Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var build = Button("Build project", 125);
        build.Background = Brush("#5379A5");
        build.Click += (_, _) =>
        {
            if (!Regex.IsMatch(Collection, "^[a-z][a-z0-9_]{1,30}$"))
            { MessageBox.Show(this, "Collection name must be 2 to 31 lowercase letters, digits or underscores and start with a letter.", "Check collection name"); collectionBox!.Focus(); return; }
            if (!Directory.Exists(OutputFolder))
            { MessageBox.Show(this, "Choose an existing output folder.", "Check output folder"); outputBox!.Focus(); return; }
            DialogResult = true;
        };
        DockPanel.SetDock(build, Dock.Right); footer.Children.Add(build);
        var cancel = Button("Cancel", 90); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 9, 0); cancel.Click += (_, _) => DialogResult = false;
        DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var body = new StackPanel(); scroll.Content = body;
        body.Children.Add(new TextBlock { Text = "Build Project", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        body.Children.Add(Label("Target resource type"));
        platform = new ComboBox { Height = 30, Margin = new Thickness(0, 6, 0, 17), Background = Brush("#4B4B4D"), Foreground = Brushes.White };
        foreach(var label in BuildTargets.Labels) platform.Items.Add(label); platform.SelectedIndex=(int)project.BuildTarget; body.Children.Add(platform);
        body.Children.Add(Label("Clothes collection name"));
        collectionBox = Box(project.Collection); collectionBox.Margin = new Thickness(0, 6, 0, 18); body.Children.Add(collectionBox);
        body.Children.Add(Label("Extra options"));
        groupedBox = Check("Create stream subfolders for each drawable type", true); body.Children.Add(groupedBox);
        zipBox = Check("Also create a ZIP of the three-folder project", zipDefault); body.Children.Add(zipBox);
        body.Children.Add(new TextBlock { Text = "The normal export stays unzipped. ZIP is optional.", Foreground = Brush("#BDBDBD"), FontSize = 11, Margin = new Thickness(25, 1, 0, 18) });
        body.Children.Add(Label("Build output folder"));
        var browseRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var browse = Button("…", 30); browse.Margin = new Thickness(6, 0, 0, 0);
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = "Select parent folder for project export" }; if (picker.ShowDialog(this) == true) outputBox!.Text = picker.FolderName; };
        DockPanel.SetDock(browse, Dock.Right); browseRow.Children.Add(browse);
        outputBox = Box(project.OutputFolder); browseRow.Children.Add(outputBox); body.Children.Add(browseRow);
        body.Children.Add(new TextBlock { Text = "Output: <folder>/<collection>-project/", Foreground = Brush("#BDBDBD"), FontSize = 11, Margin = new Thickness(0, 7, 0, 17) });
        var result = new Border { Background = Brush("#373B3F"), Padding = new Thickness(12), BorderBrush = Brush("#555B60"), BorderThickness = new Thickness(1) };
        result.Child = new TextBlock { Text = "FiveM/: Ready to deploy server resource\nProject/: Editable project and build details\nAssets/: Source files for reopening and rebuilding", TextWrapping = TextWrapping.Wrap, LineHeight = 22, Foreground = Brush("#DDE4E7") };
        body.Children.Add(result);
        void DescribeTarget()
        {
            var selected=(BuildTarget)Math.Max(0,platform.SelectedIndex);groupedBox.IsEnabled=selected==BuildTarget.FiveM;
            ((TextBlock)result.Child).Text=BuildTargets.OutputFolder(selected)+"/: "+(selected==BuildTarget.SinglePlayer?"DLC archive and installation steps":selected==BuildTarget.RawAssets?"Game assets and metadata": "Ready to deploy server resource")+"\nProject/: Editable project and build details\nAssets/: Source files for reopening and rebuilding";
        }
        platform.SelectionChanged+=(_,_)=>DescribeTarget();DescribeTarget();
    }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private static TextBox Box(string value) => new() { Text = value, Height = 30, Padding = new Thickness(6, 4, 6, 4), Background = Brush("#414143"), Foreground = Brushes.White, BorderBrush = Brush("#707073") };
    private static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(2, 10, 0, 4), Foreground = Brushes.White };
    private static Button Button(string text, double width) => new() { Content = text, Width = width, Height = 31, Background = Brush("#55565A"), Foreground = Brushes.White, BorderBrush = Brush("#77777B"), Padding = new Thickness(5, 2, 5, 2) };
}

