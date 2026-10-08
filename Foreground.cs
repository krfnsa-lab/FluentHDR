using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class HdrForeground
{
    public sealed class Snapshot
    {
        public string ProcessName { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
        public bool IsFullscreen { get; set; }
        public int ProcessId { get; set; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h, ref MonitorInfo info);
    public static Snapshot Read()
    {
        Snapshot s = new Snapshot { ProcessName = "", Path = "", Title = "" };
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return s;
        uint pid; GetWindowThreadProcessId(window, out pid); s.ProcessId = (int)pid;
        try {
            using (Process p = Process.GetProcessById((int)pid)) {
                s.ProcessName = p.ProcessName;
                try { s.Path = p.MainModule.FileName; } catch { }
            }
        } catch { return s; }
        StringBuilder title = new StringBuilder(1024); GetWindowText(window, title, title.Capacity); s.Title = title.ToString();
        Rect rect; MonitorInfo mi = new MonitorInfo(); mi.Size = Marshal.SizeOf(typeof(MonitorInfo));
        if (GetWindowRect(window, out rect) && GetMonitorInfo(MonitorFromWindow(window, 2), ref mi)) {
            s.IsFullscreen = Math.Abs(rect.Left - mi.Monitor.Left) <= 2 && Math.Abs(rect.Top - mi.Monitor.Top) <= 2
                && Math.Abs(rect.Right - mi.Monitor.Right) <= 2 && Math.Abs(rect.Bottom - mi.Monitor.Bottom) <= 2;
        }
        return s;
    }
}
