using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuckStudio;

static class ReportChecks
{
    public static void Run(string root, Action<bool,string> check)
    {
        var asset=new ClothingAsset{Name="11 Tops / jackets",Slot="jbib",ModelPath=Path.Combine(root,"sender-model.ydd")};
        var project=new ClothingProject{Name="Sender pack",Collection="sender_pack",OutputFolder=root,Items=[asset]};
        var findings=Enumerable.Range(0,600).Select(i=>new ValidationFinding("Error",asset.Id,asset.Name,$"variation_{i:000}.ytd is missing diffuse map 'expected_{i:000}'.","Available: different_diffuse. Attach the matching YTD.")).ToList();
        var report=BuildReport.Create(project,new BuildValidationException(findings));
        check(report.Contains("variation_000")&&report.Contains("variation_599")&&report.Contains("different_diffuse")&&report.Contains("sender-model.ydd"),"Build report preserves every finding, model path and repair guidance");
        PreviewDiagnostics.RecordBuildReport(report);
        check(PreviewDiagnostics.Report(null,null,-1).Contains("variation_599"),"Help debug report retains the full latest build failure beyond the preview event limit");
        var window=new TextReportWindow("Build report","Scroll to review all build findings. Copy or save the complete report for support.",report+"\n"+new string('x',2000)+"\nFINAL REPORT LINE","test-build-report");
        var body=(FrameworkElement)window.Content;
        foreach(var size in new[]{new Size(900,660),new Size(640,360)})
        {
            Layout(body,size);
            var text=window.ReportText;
            check(text.ViewportHeight>0&&text.ExtentHeight>text.ViewportHeight&&text.ActualHeight<size.Height,"Large reports keep a bounded scroll viewport at "+size);
            text.ScrollToEnd();Layout(body,size);
            check(text.VerticalOffset>0&&text.ExtentHeight-text.ViewportHeight-text.VerticalOffset<2,"The last build error is reachable at "+size);
            text.ScrollToHome();Layout(body,size);
            text.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent});Layout(body,size);
            if(SystemParameters.WheelScrollLines!=0)check(text.VerticalOffset>0,"Mouse wheel scrolls long read-only reports at "+size);
            text.ScrollToHorizontalOffset(1000);Layout(body,size);
            check(text.HorizontalOffset>0,"Long texture names can scroll horizontally at "+size);
            check(Children<Button>(body).Where(b=>b.Content is string s&&s is "Copy report" or "Save report..." or "Close").All(b=>{var p=b.TranslatePoint(new Point(),body);return p.Y>=0&&p.Y+b.ActualHeight<=size.Height&&p.X+b.ActualWidth<=size.Width;}),"Copy, save and close stay visible at "+size);
        }
        check(window.MaxHeight<=SystemParameters.WorkArea.Height&&window.SizeToContent==SizeToContent.Manual,"Report windows stay within the desktop work area");
        Layout(body,new Size(900,660));window.ReportText.ScrollToEnd();Layout(body,new Size(900,660));
        var bitmap=new RenderTargetBitmap(900,660,96,96,PixelFormats.Pbgra32);bitmap.Render(body);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(root,"scrollable-build-report.png")))encoder.Save(file);
        window.Close();
        var debug=new PreviewDebugWindow(()=>report);Layout((FrameworkElement)debug.Content,new Size(900,660));debug.ReportText.ScrollToVerticalOffset(400);debug.ReportText.UpdateLayout();
        var offset=debug.ReportText.VerticalOffset;
        Children<Button>((FrameworkElement)debug.Content).Single(b=>Equals(b.Content,"Refresh")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));debug.ReportText.UpdateLayout();
        check(offset>0&&Math.Abs(debug.ReportText.VerticalOffset-offset)<2,"Refreshing Help debug preserves the report scroll position");debug.Close();
    }
    static void Layout(FrameworkElement body,Size size){body.Measure(size);body.Arrange(new Rect(size));body.UpdateLayout();}
    static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in Children<T>(child))yield return nested;}
    }
}
