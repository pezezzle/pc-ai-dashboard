param([switch]$MoveMouse, [switch]$PressTestKey)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ScreensaverNativeCheck {
    public delegate bool Callback(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] public struct InputData { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputData Data; }
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, Input[] input, int size);
    public static object[] Windows(uint process) {
        var result = new List<object>();
        EnumWindows((window, data) => {
            uint id; GetWindowThreadProcessId(window, out id);
            if (id == process) {
                var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity);
                if (text.ToString().StartsWith("PC")) {
                    Rect rect; GetWindowRect(window, out rect);
                    result.Add(new { Title=text.ToString(), Visible=IsWindowVisible(window), X=rect.Left, Y=rect.Top, Width=rect.Right-rect.Left, Height=rect.Bottom-rect.Top });
                }
            }
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
    public static void NudgeMouse(int x) {
        var input = new Input { Type=0, Data=new InputData { Mouse=new MouseInput { X=x, Flags=1 } } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1) throw new Exception("SendInput failed");
    }
    public static void TestKey() {
        // F24 supplies global keyboard input without typing text or changing focus.
        var down=new Input { Type=1, Data=new InputData { Keyboard=new KeyboardInput { Key=0x87 } } };
        var up=new Input { Type=1, Data=new InputData { Keyboard=new KeyboardInput { Key=0x87, Flags=2 } } };
        if (SendInput(2,new[] { down,up },Marshal.SizeOf(typeof(Input)))!=2) throw new Exception("Keyboard SendInput failed");
    }
}
'@
[ScreensaverNativeCheck]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
if ($MoveMouse) {
    [ScreensaverNativeCheck]::NudgeMouse(1)
    Start-Sleep -Milliseconds 350
    [ScreensaverNativeCheck]::NudgeMouse(-1)
    return
}
$appPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\app\PcAiDashboard.exe'
$dashboardProcess = Get-Process PcAiDashboard | Where-Object { $_.Path -eq $appPath } | Select-Object -First 1
if (-not $dashboardProcess) { throw 'Published dashboard is not running.' }
if ($PressTestKey) { [ScreensaverNativeCheck]::TestKey(); return }
$monitors = @([System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
    @{ Id=$_.DeviceName; X=$_.Bounds.X; Y=$_.Bounds.Y; Width=$_.Bounds.Width; Height=$_.Bounds.Height }
})
@{ Monitors=$monitors; Windows=@([ScreensaverNativeCheck]::Windows($dashboardProcess.Id)) } | ConvertTo-Json -Depth 5 -Compress
