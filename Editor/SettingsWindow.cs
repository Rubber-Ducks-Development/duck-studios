using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckStudio;

internal sealed class SettingsWindow : Window
{
    private readonly ClothingProject project;
    private readonly AppSettings settings;
    private readonly TextBox nameBox;
    private readonly TextBox authorBox;
    private readonly TextBox versionBox;
    private readonly TextBox descriptionBox;
    private readonly TextBox collectionBox;
    private readonly TextBox outputBox;
    private readonly TextBox gtaBox;
    private readonly ListBox sections;
    private readonly StackPanel generalPanel;
    private readonly StackPanel assetsPanel;
    private readonly StackPanel validationPanel;
    private string maleYmt;
    private string maleMeta;
    private string femaleYmt;
    private string femaleMeta;
    private List<string> supplemental;

    private static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(48, 48, 48));
    private static readonly Brush Secondary = new SolidColorBrush(Color.FromRgb(59, 59, 61));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(237, 237, 237));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(177, 177, 179));

    public SettingsWindow(ClothingProject project, AppSettings settings, bool create = false)
    {
        this.project = project;
        this.settings = settings;
        maleYmt = project.MaleYmtPath;
        maleMeta = project.MaleMetaPath;
        femaleYmt = project.FemaleYmtPath;
        femaleMeta = project.FemaleMetaPath;
        supplemental = [.. project.SupplementalYmtPaths];
        Title = create ? "Duck Studio - Create project" : "Duck Studio - Project settings";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/DuckStudio;component/Assets/AppIconSource.png"));
        Width = 1010;
        Height = 700;
        MinWidth = 750;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Surface;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var sidebar = new DockPanel { Background = Secondary };
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);
        var searchHost = new Grid { Margin = new Thickness(8) };
        var search = new TextBox { Padding = new Thickness(6, 4, 6, 4), Background = Surface, Foreground = Text, BorderBrush = Muted, ToolTip = "Search settings sections" };
        searchHost.Children.Add(search);
        var searchHint = new TextBlock { Text = "Search Settings", Foreground = Muted, FontStyle = FontStyles.Italic, Margin = new Thickness(7, 5, 0, 0), IsHitTestVisible = false };
        searchHost.Children.Add(searchHint);
        DockPanel.SetDock(searchHost, Dock.Top);
        sidebar.Children.Add(searchHost);
        sections = new ListBox { Background = Secondary, Foreground = Text, BorderThickness = new Thickness(0), Margin = new Thickness(5, 0, 5, 0) };
        foreach (var title in new[] { "General", "Assets and Metadata", "Warnings and Validations" }) sections.Items.Add(title);
        sidebar.Children.Add(sections);

        var content = new Grid { Background = Surface };
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        generalPanel = Section(content);
        assetsPanel = Section(content);
        validationPanel = Section(content);

        Heading(generalPanel, "Project:");
        nameBox = Field(generalPanel, "Name *", project.Name, "Project name used for this workspace.");
        var pair = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        generalPanel.Children.Add(pair);
        var authorPanel = new StackPanel();
        Grid.SetColumn(authorPanel, 0);
        pair.Children.Add(authorPanel);
        authorBox = Field(authorPanel, "Author (optional)", project.Author, null);
        var versionPanel = new StackPanel();
        Grid.SetColumn(versionPanel, 2);
        pair.Children.Add(versionPanel);
        versionBox = Field(versionPanel, "Version (optional)", project.Version, null);
        descriptionBox = Field(generalPanel, "Description (optional)", project.Description, "Stored with the project and added to the resource manifest.", 64);
        collectionBox = Field(generalPanel, "DLC Name *", project.Collection, "Lowercase letters, digits, and underscores; used for resource filenames.");
        outputBox = Field(generalPanel, "Output Folder *", project.OutputFolder, "Parent folder for the three-folder project export.");
        var browseOutput = ActionButton("Select folder...");
        browseOutput.Margin = new Thickness(0, -8, 0, 16);
        browseOutput.HorizontalAlignment = HorizontalAlignment.Left;
        browseOutput.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose the output folder" };
            if (dialog.ShowDialog(this) == true) outputBox.Text = dialog.FolderName;
        };
        generalPanel.Children.Add(browseOutput);
        Heading(generalPanel, "GTA V:");
        gtaBox = Field(generalPanel, "GTA5.exe", File.Exists(Path.Combine(settings.GtaFolderPath, "GTA5.exe")) ? Path.Combine(settings.GtaFolderPath, "GTA5.exe") : "", "Used only to read your local game assets for 3D PED previews.");
        var browseGame = ActionButton("Link GTA5.exe...");
        browseGame.HorizontalAlignment = HorizontalAlignment.Left;
        browseGame.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Locate GTA5.exe", Filter = "Grand Theft Auto V (GTA5.exe)|GTA5.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) gtaBox.Text = dialog.FileName;
        };
        generalPanel.Children.Add(browseGame);

        Heading(assetsPanel, "Build output:");
        Help(assetsPanel, "Build project generates binary YMT files for each gender, ShopPedApparel META files, fxmanifest.lua, and all clothing / texture files. You do not need to attach metadata. First-person models and cloth physics are included when selected on the drawable.");
        var extras = new TextBlock { Text = supplemental.Count == 0 ? "No supplemental YMT files" : string.Join(", ", supplemental.Select(Path.GetFileName)), Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 7) };
        assetsPanel.Children.Add(extras);
        var extrasRow = new StackPanel { Orientation = Orientation.Horizontal };
        var chooseExtras = ActionButton("Choose supplemental YMT...");
        chooseExtras.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose supplemental YMT files", Filter = "YMT files (*.ymt)|*.ymt", Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            supplemental = [.. dialog.FileNames];
            extras.Text = string.Join(", ", supplemental.Select(Path.GetFileName));
        };
        extrasRow.Children.Add(chooseExtras);
        var clearExtras = ActionButton("Clear");
        clearExtras.Margin = new Thickness(7, 0, 0, 0);
        clearExtras.Click += (_, _) => { supplemental.Clear(); extras.Text = "No supplemental YMT files"; };
        extrasRow.Children.Add(clearExtras);
        assetsPanel.Children.Add(extrasRow);
        

        Heading(validationPanel, "Warnings and Validations:");
        var issues = PackBuilder.Validate(project);
        Help(validationPanel, issues.Count == 0 ? "No current validation issues." : string.Join(Environment.NewLine, issues.Select(x => "- " + x)));

        var footer = new Border { Background = Secondary, BorderBrush = Muted, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 8, 10, 8) };
        Grid.SetRow(footer, 1);
        Grid.SetColumnSpan(footer, 2);
        root.Children.Add(footer);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Child = actions;
        var cancel = ActionButton("Cancel");
        cancel.IsCancel = true;
        cancel.MinWidth = 90;
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel);
        var save = ActionButton(create ? "Create project" : "Save settings");
        save.MinWidth = 120;
        save.Margin = new Thickness(7, 0, 0, 0);
        save.Click += Save;
        actions.Children.Add(save);

        sections.SelectionChanged += (_, _) => ShowSection();
        search.TextChanged += (_, _) =>
        {
            searchHint.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
            var current = sections.SelectedItem?.ToString();
            sections.Items.Clear();
            foreach (var title in new[] { "General", "Assets and Metadata", "Warnings and Validations" }.Where(x => x.Contains(search.Text, StringComparison.OrdinalIgnoreCase))) sections.Items.Add(title);
            sections.SelectedItem = sections.Items.Contains(current) ? current : sections.Items.Count > 0 ? sections.Items[0] : null;
        };
        sections.SelectedIndex = 0;

    }

    private static StackPanel Section(Grid parent)
    {
        var panel = new StackPanel { Margin = new Thickness(20, 16, 24, 22), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        parent.Children.Add(scroll);
        panel.Tag = scroll;
        return panel;
    }

    private void ShowSection()
    {
        foreach (var panel in new[] { generalPanel, assetsPanel, validationPanel }) ((ScrollViewer)panel.Tag).Visibility = Visibility.Collapsed;
        var target = sections.SelectedItem?.ToString() switch
        {
            "General" => generalPanel,
            "Assets and Metadata" => assetsPanel,
            "Warnings and Validations" => validationPanel,
            _ => null
        };
        if (target != null) ((ScrollViewer)target.Tag).Visibility = Visibility.Visible;
    }

    private static void Heading(Panel panel, string value) => panel.Children.Add(new TextBlock { Text = value, Foreground = Text, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 12) });

    private static void Help(Panel panel, string value) => panel.Children.Add(new TextBlock { Text = value, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 5, 0, 15) });

    private static TextBox Field(Panel panel, string label, string value, string? hint, double height = 0)
    {
        panel.Children.Add(new TextBlock { Text = label, Foreground = Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 5) });
        var input = new TextBox { Text = value, Foreground = Text, Background = Surface, BorderBrush = Muted, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(5, 0, 0, hint == null ? 17 : 0) };
        if (height > 0) { input.Height = height; input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        panel.Children.Add(input);
        if (hint != null) Help(panel, hint);
        return input;
    }

    private static Button ActionButton(string title) => new() { Content = title, Foreground = Text, Background = Secondary, BorderBrush = Muted, Padding = new Thickness(11, 5, 11, 5) };

    private void FilePicker(Panel panel, string label, Func<string> get, Action<string> set, string filter)
    {
        panel.Children.Add(new TextBlock { Text = label, Foreground = Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var path = new TextBlock { Text = string.IsNullOrWhiteSpace(get()) ? "Not selected" : get(), Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 12, 0) };
        row.Children.Add(path);
        var browse = ActionButton("Browse...");
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            set(dialog.FileName);
            path.Text = dialog.FileName;
        };
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        panel.Children.Add(row);
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(nameBox.Text) || !System.Text.RegularExpressions.Regex.IsMatch(collectionBox.Text.Trim(), "^[a-z][a-z0-9_]{1,30}$"))
        {
            MessageBox.Show(this, "Enter a project name and a DLC name with 2-31 lowercase letters, digits or underscores, starting with a letter.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!string.IsNullOrWhiteSpace(gtaBox.Text) && (!File.Exists(gtaBox.Text) || !Path.GetFileName(gtaBox.Text).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Select a valid GTA5.exe installation.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Directory.Exists(outputBox.Text.Trim()))
        {
            MessageBox.Show(this, "Choose an existing output folder before creating or importing clothing.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        {
            project.Name = nameBox.Text.Trim();
            project.Author = authorBox.Text.Trim();
            project.Version = versionBox.Text.Trim();
            project.Description = descriptionBox.Text.Trim();
            project.Collection = collectionBox.Text.Trim();
            project.MaleYmtPath = maleYmt;
            project.MaleMetaPath = maleMeta;
            project.FemaleYmtPath = femaleYmt;
            project.FemaleMetaPath = femaleMeta;
            project.SupplementalYmtPaths = [.. supplemental];
        }
        project.OutputFolder = outputBox.Text.Trim();
        settings.GtaFolderPath = string.IsNullOrWhiteSpace(gtaBox.Text) ? "" : Path.GetDirectoryName(gtaBox.Text)!;
        project.GtaFolderPath = settings.GtaFolderPath;
        settings.Save();
        DialogResult = true;
    }
}

