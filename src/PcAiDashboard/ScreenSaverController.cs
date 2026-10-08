using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PcAiDashboard;

// One coordinator owns all monitor windows; telemetry stays in the normal window.
public sealed class ScreenSaverController : IDisposable
{
    private readonly MainWindow dashboard;
    private readonly DispatcherTimer timer;
    private readonly List<MainWindow> windows = [];
    private bool restoreDashboard;
    private bool stopping;
    private bool disposed;
    private bool sessionUnavailable;
    private long startedAt;
    private long lastStoppedAt = Environment.TickCount64;
    private uint initialInput;

    public bool IsActive => windows.Count > 0;
    public static bool ShowsDashboard(string displayId, IReadOnlyCollection<string>? selected)
        => ScreenSaverPolicy.ShowsDashboard(displayId, selected);
    public ScreenSaverController(MainWindow dashboard)
    {
        this.dashboard = dashboard;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += CheckInput;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        timer.Start();
    }

    public static bool ShouldStart(bool enabled, int minutes, uint now, uint lastInput, long sinceStop)
        => ScreenSaverPolicy.ShouldStart(enabled, minutes, now, lastInput, sinceStop);

    public void Start()
    {
        if (disposed || sessionUnavailable || IsActive) return;
        var displays = MainWindow.GetDisplays();
        if (displays.Count == 0) return;
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return;
        initialInput = input.Time;
        startedAt = Environment.TickCount64;
        restoreDashboard = dashboard.IsVisible;
        try
        {
            foreach (var display in displays)
            {
                var window = new MainWindow(dashboard, display);
                windows.Add(window);
                window.Closed += (_, _) => { if (!stopping && !disposed) Stop(); };
                window.Show();
            }
            if (IsActive) dashboard.Hide();
        }
        catch (Exception ex)
        {
            AppFiles.Log("Screensaver start failed: " + ex.GetType().Name);
            Stop();
        }
    }

    public void Broadcast(DashboardSnapshot snapshot)
    {
        foreach (var window in windows.ToArray()) window.ReceiveSnapshot(snapshot);
    }

    public void Stop(bool restore = true)
    {
        if (stopping || !IsActive) return;
        stopping = true;
        try
        {
            var closing = windows.ToArray();
            windows.Clear();
            foreach (var window in closing) if (!window.IsClosed) window.Close();
            lastStoppedAt = Environment.TickCount64;
            if (restore && restoreDashboard && !dashboard.IsClosed)
            {
                dashboard.ShowActivated = false;
                dashboard.Show();
                dashboard.ShowActivated = true;
            }
        }
        finally { stopping = false; }
    }

    private void CheckInput(object? sender, EventArgs e)
    {
        if (disposed || sessionUnavailable) return;
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return;
        if (IsActive)
        {
            // Ignore the mouse-up from the launch button, then observe input on any monitor/app.
            if (Environment.TickCount64 - startedAt < 750) initialInput = input.Time;
            else if (input.Time != initialInput) Stop();
        }
        else if (dashboard.IsReady && ShouldStart(dashboard.CurrentSettings.ScreenSaverTimerEnabled,
            dashboard.CurrentSettings.ScreenSaverIdleMinutes, unchecked((uint)Environment.TickCount),
            input.Time, Environment.TickCount64 - lastStoppedAt)) Start();
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
        => dashboard.Dispatcher.BeginInvoke(() => { if (!disposed) Stop(); });

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        => dashboard.Dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
                or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.SessionLogoff)
            {
                sessionUnavailable = true;
                Stop();
            }
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect
                or SessionSwitchReason.RemoteConnect or SessionSwitchReason.SessionLogon)
            {
                sessionUnavailable = false;
                lastStoppedAt = Environment.TickCount64;
            }
        });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Stop(false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
