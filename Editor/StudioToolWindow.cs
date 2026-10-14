using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuckStudio;

public class StudioToolWindow : Window
{
    public DockPanel Body { get; } = new() { Margin=new Thickness(20) };
    protected static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(190,190,193));
    public StudioToolWindow(string title,double width=980,double height=680)
    {
        Title="Duck Studio - "+title;Width=width;Height=height;MinWidth=Math.Min(720,width);MinHeight=Math.Min(460,height);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(48,48,50));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=12;
        Icon=new BitmapImage(new Uri("pack://application:,,,/DuckStudio;component/Assets/AppIconSource.png"));Body.Background=Background;Content=Body;
        Resources[typeof(TextBlock)]=new Style(typeof(TextBlock)){Setters={new Setter(TextBlock.ForegroundProperty,Brushes.White)}};
        Resources[typeof(TextBox)]=new Style(typeof(TextBox)){Setters={new Setter(TextBox.BackgroundProperty,new SolidColorBrush(Color.FromRgb(62,62,65))),new Setter(TextBox.ForegroundProperty,Brushes.White),new Setter(TextBox.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(105,105,109))),new Setter(TextBox.PaddingProperty,new Thickness(6,4,6,4))}};
        Resources[typeof(ComboBox)]=Application.Current.Resources["AnimatedComboBoxStyle"];
        var buttons=new Style(typeof(Button));buttons.Setters.Add(new Setter(Button.ForegroundProperty,Brushes.White));buttons.Setters.Add(new Setter(Button.BackgroundProperty,new SolidColorBrush(Color.FromRgb(78,80,85))));
        var chrome=new FrameworkElementFactory(typeof(Border));chrome.SetValue(Border.BorderThicknessProperty,new Thickness(1));chrome.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new(System.Windows.Data.RelativeSourceMode.TemplatedParent)});chrome.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=new(System.Windows.Data.RelativeSourceMode.TemplatedParent)});chrome.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=new(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);content.SetValue(ContentPresenter.RecognizesAccessKeyProperty,true);chrome.AppendChild(content);buttons.Setters.Add(new Setter(Button.TemplateProperty,new ControlTemplate(typeof(Button)){VisualTree=chrome}));
        var disabled=new Trigger{Property=IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(Button.OpacityProperty,.45));buttons.Triggers.Add(disabled);var focus=new Trigger{Property=IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Button.BorderBrushProperty,Brushes.Gold));buttons.Triggers.Add(focus);Resources[typeof(Button)]=buttons;
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!PanelDismissal.HasOpenDropdown(e.OriginalSource as DependencyObject)){e.Handled=true;Close();}};
        var heading=new TextBlock{Text=title,FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(heading,Dock.Top);Body.Children.Add(heading);
    }
    public static Button Action(string label)=>new(){Content=label,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0),MinHeight=31,Background=new SolidColorBrush(Color.FromRgb(78,80,85)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(111,113,118)),Cursor=Cursors.Hand};
    public static TextBlock Hint(string text)=>new(){Text=text,Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14),LineHeight=19};
    protected StackPanel Footer()
    {
        var panel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(panel,Dock.Bottom);Body.Children.Add(panel);return panel;
    }
    public static TextBox Field(Panel panel,string label,string value,Action<string>? edited=null)
    {
        panel.Children.Add(new TextBlock{Text=label,Foreground=Muted,Margin=new Thickness(0,9,0,5)});
        var field=new TextBox{Text=value,MinHeight=30};panel.Children.Add(field);
        if(edited!=null)field.TextChanged+=(_,_)=>edited(field.Text);return field;
    }
    public static ComboBox Choice(Panel panel,string label,IEnumerable<string> values,string selected,Action<string> changed)
    {
        panel.Children.Add(new TextBlock{Text=label,Foreground=Muted,Margin=new Thickness(0,9,0,5)});
        string Label(string value)=>value switch { "GENDER_MALE"=>"Male","GENDER_FEMALE"=>"Female","GENDER_DONTCARE"=>"Both",_=>System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace("ZONE_","").Replace("TATTOO_","").Replace('_',' ').ToLowerInvariant()) };
        var choice=new ComboBox{Height=30};foreach(var value in values){var item=new ComboBoxItem{Content=Label(value),Tag=value};choice.Items.Add(item);if(value==selected)choice.SelectedItem=item;}panel.Children.Add(choice);choice.SelectionChanged+=(_,_)=>{if(choice.SelectedItem is ComboBoxItem{Tag:string value})changed(value);};return choice;
    }
}
