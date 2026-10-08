using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace PcAiDashboard;

public partial class MainWindow : Window
{
    private readonly HardwareService hardware;
    private readonly AiService ai;
    private readonly CancellationTokenSource stop=new();
    private DashboardSettings settings;
    private readonly Forms.NotifyIcon tray=new();
    private bool ready;
    private const string Origin="https://pc-ai-dashboard.local";
    private readonly LocalVideoServer videoServer;
    private readonly MainWindow? dashboardOwner;
    private readonly DisplayInfo? screenSaverDisplay;
    private readonly ScreenSaverController? screenSaver;
    private DashboardSnapshot? lastSnapshot;
    public bool IsClosed { get; private set; }
    public bool IsReady => ready && !IsClosed;
    public DashboardSettings CurrentSettings => settings;
    public MainWindow() : this(null, null) { }
    internal MainWindow(MainWindow? owner, DisplayInfo? display)
    {
        dashboardOwner=owner; screenSaverDisplay=display;
        hardware=owner?.hardware??new(); ai=owner?.ai??new(new WindowsAiPlatform());
        videoServer=owner?.videoServer??new(Origin);
        settings=owner?.settings??AppFiles.LoadSettings();
        InitializeComponent();
        if(owner==null) screenSaver=new(this);
        else { ShowInTaskbar=false; ShowActivated=false; Cursor=System.Windows.Input.Cursors.None; Title="PC · AI Dashboard · Bildschirmschoner"; }
        Loaded+=OnLoaded; Closed+=OnClosed;
        PreviewKeyDown+=(_,e)=> { if(dashboardOwner!=null) { dashboardOwner.screenSaver?.Stop(); e.Handled=true; return; } if(e.Key==Key.F11) { settings.Fullscreen=!settings.Fullscreen; ApplyDisplay(); AppFiles.SaveSettings(settings); } if(e.Key==Key.Escape && settings.Fullscreen) { settings.Fullscreen=false; ApplyDisplay(); } };
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(IsClosed || Browser.CoreWebView2!=null) return;
        try
        {
            ApplyDisplay();
            if(dashboardOwner==null) { InitializeTray(); StartAquasuite(); InstallClaudeIntegration(); }
            var options=new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments="--autoplay-policy=no-user-gesture-required" };
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(AppFiles.Root,"WebView2"),options);
            if(IsClosed) return;
            await Browser.EnsureCoreWebView2Async(env);
            if(IsClosed) return;
            var core=Browser.CoreWebView2!;
            core.Settings.IsStatusBarEnabled=false; core.Settings.AreDefaultContextMenusEnabled=false;
            core.SetVirtualHostNameToFolderMapping("pc-ai-dashboard.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=(_,args)=> { if(!args.Uri.StartsWith(Origin+"/",StringComparison.OrdinalIgnoreCase)) args.Cancel=true; };
            core.NewWindowRequested+=(_,args)=> { args.Handled=true; };
            core.PermissionRequested+=(_,args)=> { args.State=CoreWebView2PermissionState.Deny; };
            core.WebMessageReceived+=OnMessage;
            core.ProcessFailed+=(_,args)=>AppFiles.Log("WebView2: "+args.ProcessFailedKind);
            core.Navigate(Origin+"/index.html");
            if(dashboardOwner==null) _=Task.Run(PumpSnapshots);
        }
        catch(Exception ex) { if(IsClosed) return; AppFiles.Log("Start fehlgeschlagen: "+ex.GetType().Name+" "+ex.Message); if(dashboardOwner==null) MessageBox.Show("Die Anzeige konnte nicht starten.\n"+ex.Message,"PC · AI Dashboard",MessageBoxButton.OK,MessageBoxImage.Error); Close(); }
    }
    private async Task PumpSnapshots()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var result=hardware.Read(settings); var snapshot=new DashboardSnapshot(DateTimeOffset.Now,result.Metrics,result.Drives,ai.Read(),result.Status);
                await Dispatcher.InvokeAsync(()=> { ReceiveSnapshot(snapshot); screenSaver?.Broadcast(snapshot); });
            }catch(Exception e) { AppFiles.Log("Messwerte: "+e.GetType().Name); }
            try { await Task.Delay(1000,stop.Token); }catch(OperationCanceledException) { break; }
        }
    }
    internal void ReceiveSnapshot(DashboardSnapshot snapshot)
    {
        lastSnapshot=snapshot;
        if(IsReady) Send(new {type="snapshot",data=snapshot});
    }
    private void Send(object value) { if(!IsClosed) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value,AppFiles.Json)); }
    private void SendConfiguration()
    {
        if(dashboardOwner==null) videoServer.Configure(settings.LocalVideoPath);
        string mediaUrl=File.Exists(settings.LocalVideoPath)?videoServer.Url:"";
        var effectiveSettings=settings;
        if(dashboardOwner!=null)
        {
            effectiveSettings=JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(settings,AppFiles.Json),AppFiles.Json)!;
            effectiveSettings.Muted=true;
        }
        bool showDashboard=screenSaverDisplay==null || ScreenSaverController.ShowsDashboard(screenSaverDisplay.Id,settings.ScreenSaverDashboardDisplayIds);
        Send(new {type="configuration",settings=effectiveSettings,mediaUrl,screenSaver=dashboardOwner!=null,showDashboard,displayId=screenSaverDisplay?.Id,displays=GetDisplays(),integration=File.Exists(Path.Combine(AppFiles.Root,"claude-statusline.cjs"))});
    }
    private async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(!e.Source.StartsWith(Origin+"/",StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var doc=JsonDocument.Parse(e.WebMessageAsJson); var r=doc.RootElement; string? type=r.GetProperty("type").GetString();
            if(dashboardOwner!=null && type!="ready") return;
            switch(type)
            {
                case "ready": ready=true; SendConfiguration(); if(dashboardOwner?.lastSnapshot is { } snapshot) ReceiveSnapshot(snapshot); break;
                case "startScreenSaver": screenSaver?.Start(); break;
                case "stopScreenSaver": screenSaver?.Stop(); break;
                case "saveSettings":
                    var previous=settings;
                    settings=DashboardSettings.Validate(r.GetProperty("settings").Deserialize<DashboardSettings>(AppFiles.Json)??settings);
                    AppFiles.SaveSettings(settings);
                    if(previous.Autostart!=settings.Autostart) ApplyAutostart();
                    if(previous.DisplayId!=settings.DisplayId || previous.Fullscreen!=settings.Fullscreen || previous.AlwaysOnTop!=settings.AlwaysOnTop) ApplyDisplay();
                    SendConfiguration(); break;
                case "pickVideo":
                    var picker=new OpenFileDialog { Title="Hintergrundvideo auswählen",Filter="Videos (*.mp4;*.webm;*.m4v)|*.mp4;*.webm;*.m4v" };
                    if(picker.ShowDialog(this)==true) { settings.LocalVideoPath=picker.FileName; settings.BackgroundMode="local"; AppFiles.SaveSettings(settings); SendConfiguration(); } break;
                case "exit": Close(); break;
                case "windowed": settings.Fullscreen=false; ApplyDisplay(); SendConfiguration(); break;
                case "fullscreen": settings.Fullscreen=true; ApplyDisplay(); SendConfiguration(); break;
                case "minimize": Hide(); break;
                case "refresh": SendConfiguration(); break;
                case "openData": Process.Start(new ProcessStartInfo("explorer.exe",AppFiles.Root) { UseShellExecute=true }); break;
                case "capture":
                    var path=Path.Combine(AppFiles.Root,"dashboard.png"); using(var stream=File.Create(path)) await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream); Send(new {type="notice",message="Screenshot gespeichert: "+path}); break;
            }
        }
        catch(Exception ex) { AppFiles.Log("Bedienung: "+ex.GetType().Name); Send(new {type="notice",message="Einstellung konnte nicht übernommen werden."}); }
    }
    public static List<DisplayInfo> GetDisplays()=>Forms.Screen.AllScreens.Select(s=>new DisplayInfo(s.DeviceName,$"Bildschirm {s.DeviceName.Replace(@"\\.\DISPLAY","")} · {s.Bounds.Width} × {s.Bounds.Height} · {(s.Bounds.Height>s.Bounds.Width?"Hochformat":"Querformat")}{(s.Primary?" · Hauptbildschirm":"")}",s.Bounds.Width,s.Bounds.Height,s.Bounds.X,s.Bounds.Y,s.Primary)).ToList();
    private void ApplyDisplay()
    {
        if(screenSaverDisplay is { } display)
        {
            WindowState=WindowState.Normal; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            MinWidth=0; MinHeight=0; Topmost=true;
            var handle=new WindowInteropHelper(this).Handle;
            if(handle!=IntPtr.Zero) SetWindowPos(handle,IntPtr.Zero,display.X,display.Y,display.Width,display.Height,0x0004|0x0010);
            return;
        }
        var displays=GetDisplays(); var selected=displays.FirstOrDefault(d=>d.Id==settings.DisplayId)??displays.OrderBy(d=>(long)d.Width*d.Height).First(); settings.DisplayId=selected.Id;
        WindowState=WindowState.Normal; WindowStyle=settings.Fullscreen?WindowStyle.None:WindowStyle.SingleBorderWindow; ResizeMode=settings.Fullscreen?ResizeMode.NoResize:ResizeMode.CanResize;
        Topmost=settings.AlwaysOnTop;
        var h=new WindowInteropHelper(this).Handle;
        if(h!=IntPtr.Zero) SetWindowPos(h,IntPtr.Zero,selected.X+(settings.Fullscreen?0:30),selected.Y+(settings.Fullscreen?0:30),settings.Fullscreen?selected.Width:Math.Min(1100,selected.Width-60),settings.Fullscreen?selected.Height:Math.Min(680,selected.Height-60),0x0004|0x0010);
    }
    private void InitializeTray()
    {
        tray.Icon=System.Drawing.SystemIcons.Application; tray.Text="PC · AI Dashboard"; tray.Visible=true;
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Anzeige öffnen",null,(_,_)=>Dispatcher.Invoke(()=> { Show(); Activate(); }));
        menu.Items.Add("Einstellungen",null,async (_,_)=> { Show(); Activate(); if(ready) await Browser.CoreWebView2.ExecuteScriptAsync("openSettings()"); });
        menu.Items.Add("Beenden",null,(_,_)=>Dispatcher.Invoke(Close)); tray.ContextMenuStrip=menu; tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(()=> {Show();Activate();});
    }
    private void ApplyAutostart()
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(settings.Autostart) key.SetValue("PcAiDashboard","\""+Environment.ProcessPath+"\""); else key.DeleteValue("PcAiDashboard",false);
    }
    private void StartAquasuite()
    {
        if(!settings.StartAquasuite || Process.GetProcessesByName("aquasuite").Length>0) return;
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"aquasuite","aquasuite.exe");
        if(File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute=true,WindowStyle=ProcessWindowStyle.Minimized });
    }
    private static void InstallClaudeIntegration()
    {
        try
        {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude"); if(!Directory.Exists(folder)) return;
            var file=Path.Combine(folder,"settings.json"); var json=File.Exists(file)?JsonNode.Parse(File.ReadAllText(file))?.AsObject()??new JsonObject():new JsonObject();
            if(json["statusLine"]!=null) return;
            var target=Path.Combine(AppFiles.Root,"claude-statusline.cjs"); File.Copy(Path.Combine(AppContext.BaseDirectory,"Web","integrations","claude-statusline.cjs"),target,true);
            json["statusLine"]=new JsonObject { ["type"]="command",["command"]="node \""+target.Replace('\\','/')+"\"" };
            if(File.Exists(file)) File.Copy(file,Path.Combine(AppFiles.Root,"claude-settings.before-dashboard.json"),false);
            File.WriteAllText(file+".tmp",json.ToJsonString(AppFiles.Json)); File.Move(file+".tmp",file,true);
        }
        catch(Exception e) { AppFiles.Log("Claude-Kontextanbindung: "+e.GetType().Name); }
    }
    private void OnClosed(object? sender,EventArgs e)
    {
        IsClosed=true; ready=false; stop.Cancel(); screenSaver?.Dispose();
        if(dashboardOwner==null) { videoServer.Dispose(); hardware.Dispose(); ai.Dispose(); }
        tray.Dispose(); Browser.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
