using System.Windows;
namespace DuckStudio;
public partial class App : Application { protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); if(e.Args.Length>0 && System.IO.File.Exists(e.Args[0])) { StartupUri=null; var window=new MainWindow(); window.OpenRequestedProject(e.Args[0]); MainWindow=window; window.Show(); } } }