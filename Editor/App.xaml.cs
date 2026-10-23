using System.Windows;
namespace DuckStudio;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 3 && e.Args[0] == "--validate-project")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(ValidationWorker.Execute(e.Args[1], e.Args[2]));
            return;
        }
        base.OnStartup(e);
        var window = new MainWindow();
        if (e.Args.Length > 0 && System.IO.File.Exists(e.Args[0])) window.OpenRequestedProject(e.Args[0]);
        MainWindow = window;
        window.Show();
    }
}
