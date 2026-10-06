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
    private readonly HardwareService hardware=new();
    private readonly AiService ai=new();
    private readonly CancellationTokenSource stop=new();
    private DashboardSettings settings=AppFiles.LoadSettings();
    private readonly Forms.NotifyIcon tray=new();
    private bool ready;
    private const string Origin="https://pc-ai-dashboard.local";
    public MainWindow()
    {
        InitializeComponent();
        Loaded+=OnLoaded; Closed+=OnClosed;
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.F11) { settings.Fullscreen=!settings.Fullscreen; ApplyDisplay(); AppFiles.SaveSettings(settings); } if(e.Key==Key.Escape && settings.Fullscreen) { settings.Fullscreen=false; ApplyDisplay(); } };
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        try
        {
            InitializeTray(); ApplyDisplay(); StartAquasuite(); InstallClaudeIntegration();
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(AppFiles.Root,"WebView2"));
            await Browser.EnsureCoreWebView2Async(env);
            var core=Browser.CoreWebView2;
            core.Settings.IsStatusBarEnabled=false; core.Settings.AreDefaultContextMenusEnabled=false;
            core.SetVirtualHostNameToFolderMapping("pc-ai-dashboard.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
            // A separate, non-mapped origin ensures media requests reach this handler.
            core.AddWebResourceRequestedFilter("https://pc-ai-stream.local/*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=(_,args)=>
            {
                if(args.Request.Uri!="https://pc-ai-stream.local/current") return;
                try
                {
                    if(!File.Exists(settings.LocalVideoPath)) { args.Response=core.Environment.CreateWebResourceResponse(Stream.Null,404,"Not Found",""); return; }
                    long length=new FileInfo(settings.LocalVideoPath).Length,start=0,end=length-1;
                    var range=args.Request.Headers.Contains("Range")?args.Request.Headers.GetHeader("Range"):"";
                    var match=System.Text.RegularExpressions.Regex.Match(range,"^bytes=(\\d+)-(\\d*)$"); bool partial=match.Success;
                    if(partial) { start=long.Parse(match.Groups[1].Value); if(match.Groups[2].Value.Length>0) end=Math.Min(end,long.Parse(match.Groups[2].Value)); }
                    if(start>end || start>=length) { args.Response=core.Environment.CreateWebResourceResponse(Stream.Null,416,"Range Not Satisfiable",$"Content-Range: bytes */{length}"); return; }
                    var media=new MediaSegmentStream(settings.LocalVideoPath,start,end-start+1);
                    string mime=Path.GetExtension(settings.LocalVideoPath).Equals(".webm",StringComparison.OrdinalIgnoreCase)?"video/webm":"video/mp4";
                    string headers=$"Content-Type: {mime}\r\nContent-Length: {end-start+1}\r\nAccept-Ranges: bytes\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: {Origin}";
                    if(partial) headers+=$"\r\nContent-Range: bytes {start}-{end}/{length}";
                    args.Response=core.Environment.CreateWebResourceResponse(media,partial?206:200,partial?"Partial Content":"OK",headers);
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OverflowException) { args.Response=core.Environment.CreateWebResourceResponse(Stream.Null,404,"Not Found",""); }
            };
            core.NavigationStarting+=(_,args)=> { if(!args.Uri.StartsWith(Origin+"/",StringComparison.OrdinalIgnoreCase)) args.Cancel=true; };
            core.NewWindowRequested+=(_,args)=> { args.Handled=true; };
            core.PermissionRequested+=(_,args)=> { args.State=CoreWebView2PermissionState.Deny; };
            core.WebMessageReceived+=OnMessage;
            core.ProcessFailed+=(_,args)=>AppFiles.Log("WebView2: "+args.ProcessFailedKind);
            core.Navigate(Origin+"/index.html");
            _=Task.Run(PumpSnapshots);
        }
        catch(Exception ex) { AppFiles.Log("Start fehlgeschlagen: "+ex.GetType().Name+" "+ex.Message); MessageBox.Show("Die Anzeige konnte nicht starten.\n"+ex.Message,"PC · AI Dashboard",MessageBoxButton.OK,MessageBoxImage.Error); Close(); }
    }
    private async Task PumpSnapshots()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var result=hardware.Read(settings); var snapshot=new DashboardSnapshot(DateTimeOffset.Now,result.Metrics,result.Drives,ai.Read(),result.Status);
                await Dispatcher.InvokeAsync(()=> { if(ready) Send(new {type="snapshot",data=snapshot}); });
            }catch(Exception e) { AppFiles.Log("Messwerte: "+e.GetType().Name); }
            try { await Task.Delay(1000,stop.Token); }catch(OperationCanceledException) { break; }
        }
    }
    private void Send(object value)=>Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value,AppFiles.Json));
    private void SendConfiguration()
    {
        string mediaUrl="";
        if(!string.IsNullOrWhiteSpace(settings.LocalVideoPath) && File.Exists(settings.LocalVideoPath))
        {
            mediaUrl="https://pc-ai-stream.local/current";
        }
        Send(new {type="configuration",settings,mediaUrl,displays=GetDisplays(),integration=File.Exists(Path.Combine(AppFiles.Root,"claude-statusline.cjs"))});
    }
    private async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(!e.Source.StartsWith(Origin+"/",StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var doc=JsonDocument.Parse(e.WebMessageAsJson); var r=doc.RootElement; string? type=r.GetProperty("type").GetString();
            switch(type)
            {
                case "ready": ready=true; SendConfiguration(); break;
                case "saveSettings":
                    settings=DashboardSettings.Validate(r.GetProperty("settings").Deserialize<DashboardSettings>(AppFiles.Json)??settings);
                    AppFiles.SaveSettings(settings); ApplyAutostart(); ApplyDisplay(); SendConfiguration(); break;
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
    public static List<DisplayInfo> GetDisplays()=>Forms.Screen.AllScreens.Select(s=>new DisplayInfo(s.DeviceName,$"{s.DeviceName.Replace(@"\\.\","")} · {s.Bounds.Width} × {s.Bounds.Height}",s.Bounds.Width,s.Bounds.Height,s.Bounds.X,s.Bounds.Y,s.Primary)).ToList();
    private void ApplyDisplay()
    {
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
    private void OnClosed(object? sender,EventArgs e) { stop.Cancel(); hardware.Dispose(); ai.Dispose(); tray.Dispose(); Browser.Dispose(); }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
