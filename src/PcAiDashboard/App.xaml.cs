using System.IO;
using System.Text.Json;
using System.Windows;
namespace PcAiDashboard;
public partial class App : Application
{
    private System.Threading.Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        DispatcherUnhandledException += (_, args) => { AppFiles.Log("Unhandled: "+args.Exception.GetType().Name+" "+args.Exception.Message); args.Handled=true; };
        if(e.Args.Length>=2 && e.Args[0]=="--diagnostics")
        {
            using var hardware=new HardwareService(); using var ai=new AiService();
            for(int i=0;i<25;i++) { await Task.Delay(1000); if(i>3 && ai.Read().All(a=>a.Status!="Verbinden")) break; }
            hardware.Read(AppFiles.LoadSettings()); await Task.Delay(1100);
            var data=hardware.Read(AppFiles.LoadSettings());
            File.WriteAllText(e.Args[1],JsonSerializer.Serialize(new DashboardSnapshot(DateTimeOffset.Now,data.Metrics,data.Drives,ai.Read(),data.Status),AppFiles.Json)); Shutdown(); return;
        }
        instance=new Mutex(true,"Local\\PcAiDashboard",out var first);
        if(!first) { MessageBox.Show("Das Dashboard läuft bereits. Öffne es über das Symbol im Infobereich.","PC · AI Dashboard"); Shutdown(); return; }
        MainWindow=new MainWindow(); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
