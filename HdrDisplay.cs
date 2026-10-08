using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// Native CCD bridge. No registry changes, mode changes, or keyboard simulation.
// Packet definitions follow Microsoft's wingdi.h. HDR and WCG are distinguished
// using GET_ADVANCED_COLOR_INFO_2 wherever the running OS supports that packet.
public static class HdrDisplay
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int MODE_INFO_SIZE = 64;
    private static readonly object Sync = new object();

    public sealed class DisplayInfo
    {
        public string Name { get; set; }
        public string DeviceName { get; set; }
        public string MonitorDevicePath { get; set; }
        public string AdapterKey { get; set; }
        public uint AdapterLow { get; set; }
        public int AdapterHigh { get; set; }
        public uint TargetId { get; set; }
        public uint SourceId { get; set; }
        public bool HdrSupported { get; set; }
        public bool UserEnabled { get; set; }
        public bool ActiveHdr { get; set; }
        public bool UsesInfo2 { get; set; }
        public bool LimitedByPolicy { get; set; }
        public bool WideColorEnforced { get; set; }
        public uint Flags { get; set; }
        public uint ColorEncoding { get; set; }
        public uint BitsPerColorChannel { get; set; }
        public uint ActiveColorMode { get; set; }
        public string Mode { get; set; }
        public string DetectionApi { get; set; }
        public int QueryStatus { get; set; }
        public int Query2Status { get; set; }
        public int TargetNameStatus { get; set; }
        public int SourceNameStatus { get; set; }
        public string Key { get { return AdapterKey + ":" + TargetId; } }
    }

    public sealed class SetResult
    {
        public string Name { get; set; }
        public string DeviceName { get; set; }
        public string MonitorDevicePath { get; set; }
        public string Key { get; set; }
        public bool Requested { get; set; }
        public bool BeforeUserEnabled { get; set; }
        public bool BeforeActiveHdr { get; set; }
        public bool AfterUserEnabled { get; set; }
        public bool AfterActiveHdr { get; set; }
        public string Api { get; set; }
        public int Status { get; set; }
        public bool Changed { get; set; }
        public bool Verified { get; set; }
        public string Error { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HEADER
    {
        public uint Type;
        public uint Size;
        public LUID AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SOURCE_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TARGET_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public RATIONAL RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO
    {
        public SOURCE_INFO Source;
        public TARGET_INFO Target;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_NAME
    {
        public HEADER Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufacturerId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_NAME
    {
        public HEADER Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string GdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COLOR_INFO
    {
        public HEADER Header;
        public uint Flags;
        public uint Encoding;
        public uint BitsPerChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COLOR_INFO_2
    {
        public HEADER Header;
        public uint Flags;
        public uint Encoding;
        public uint BitsPerChannel;
        public uint ActiveMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SET_STATE { public HEADER Header; public uint Value; }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr pathArray,
        ref uint modes, IntPtr modeArray, IntPtr topologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int NativeGetDeviceInfo(IntPtr packet);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
    private static extern int NativeSetDeviceInfo(IntPtr packet);

    static HdrDisplay()
    {
        CheckSize(typeof(HEADER), 20);
        CheckSize(typeof(SOURCE_INFO), 20);
        CheckSize(typeof(TARGET_INFO), 48);
        CheckSize(typeof(PATH_INFO), 72);
        CheckSize(typeof(COLOR_INFO), 32);
        CheckSize(typeof(COLOR_INFO_2), 36);
        CheckSize(typeof(SET_STATE), 24);
        CheckSize(typeof(TARGET_NAME), 420);
        CheckSize(typeof(SOURCE_NAME), 84);
    }

    private static void CheckSize(Type type, int expected)
    {
        int actual = Marshal.SizeOf(type);
        if (actual != expected)
            throw new InvalidOperationException(type.Name + " native layout is " + actual + ", expected " + expected);
    }

    private static HEADER Header(uint type, Type packetType, LUID adapter, uint id)
    {
        HEADER h = new HEADER();
        h.Type = type;
        h.Size = (uint)Marshal.SizeOf(packetType);
        h.AdapterId = adapter;
        h.Id = id;
        return h;
    }

    private static int GetPacket<T>(ref T packet) where T : struct
    {
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));
        try
        {
            Marshal.StructureToPtr(packet, memory, false);
            int result = NativeGetDeviceInfo(memory);
            if (result == 0)
                packet = (T)Marshal.PtrToStructure(memory, typeof(T));
            return result;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static int SetPacket(SET_STATE packet)
    {
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SET_STATE)));
        try
        {
            Marshal.StructureToPtr(packet, memory, false);
            return NativeSetDeviceInfo(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static PATH_INFO[] Paths()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            uint count, modeCount;
            int status = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out count, out modeCount);
            if (status != 0) throw new Win32Exception(status, "GetDisplayConfigBufferSizes failed");
            if (count == 0) return new PATH_INFO[0];
            int pathSize = Marshal.SizeOf(typeof(PATH_INFO));
            IntPtr paths = Marshal.AllocHGlobal(checked((int)count * pathSize));
            // DISPLAYCONFIG_MODE_INFO is a 16-byte header + 48-byte native union.
            // Its largest union member is DISPLAYCONFIG_TARGET_MODE. Both x86/x64 use 64 bytes.
            IntPtr modes = Marshal.AllocHGlobal(Math.Max(MODE_INFO_SIZE, checked((int)modeCount * MODE_INFO_SIZE)));
            try
            {
                status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref count, paths, ref modeCount, modes, IntPtr.Zero);
                if (status == ERROR_INSUFFICIENT_BUFFER) continue;
                if (status != 0) throw new Win32Exception(status, "QueryDisplayConfig failed");
                PATH_INFO[] result = new PATH_INFO[count];
                for (int i = 0; i < count; i++)
                    result[i] = (PATH_INFO)Marshal.PtrToStructure(IntPtr.Add(paths, i * pathSize), typeof(PATH_INFO));
                return result;
            }
            finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
        }
        throw new Win32Exception(ERROR_INSUFFICIENT_BUFFER, "Display topology changed repeatedly while enumerating");
    }

    private static void ReadColor(DisplayInfo display)
    {
        LUID adapter = new LUID(); adapter.LowPart = display.AdapterLow; adapter.HighPart = display.AdapterHigh;
        COLOR_INFO_2 info2 = new COLOR_INFO_2();
        info2.Header = Header(15, typeof(COLOR_INFO_2), adapter, display.TargetId);
        int status2 = GetPacket(ref info2);
        display.Query2Status = status2;
        if (status2 == 0)
        {
            display.QueryStatus = 0;
            display.UsesInfo2 = true;
            display.Flags = info2.Flags;
            display.HdrSupported = (info2.Flags & 16) != 0;
            display.UserEnabled = (info2.Flags & 32) != 0;
            display.ActiveHdr = info2.ActiveMode == 2;
            display.LimitedByPolicy = (info2.Flags & 8) != 0;
            display.WideColorEnforced = false;
            display.ColorEncoding = info2.Encoding;
            display.BitsPerColorChannel = info2.BitsPerChannel;
            display.ActiveColorMode = info2.ActiveMode;
            display.Mode = info2.ActiveMode == 2 ? "HDR" : (info2.ActiveMode == 1 ? "WCG" : "SDR");
            display.DetectionApi = "GET_ADVANCED_COLOR_INFO_2 (15)";
            return;
        }

        COLOR_INFO info = new COLOR_INFO();
        info.Header = Header(9, typeof(COLOR_INFO), adapter, display.TargetId);
        int status = GetPacket(ref info);
        display.QueryStatus = status;
        display.UsesInfo2 = false;
        display.DetectionApi = "GET_ADVANCED_COLOR_INFO (9), legacy inferred HDR";
        if (status == 0)
        {
            display.Flags = info.Flags;
            display.WideColorEnforced = (info.Flags & 4) != 0;
            // Legacy packets expose Advanced Color rather than explicit HDR. Exclude
            // wideColorEnforced displays rather than toggling a known WCG-only path.
            display.HdrSupported = (info.Flags & 1) != 0 && !display.WideColorEnforced;
            display.UserEnabled = (info.Flags & 2) != 0;
            display.ActiveHdr = display.UserEnabled && display.HdrSupported;
            display.LimitedByPolicy = (info.Flags & 8) != 0;
            display.ColorEncoding = info.Encoding;
            display.BitsPerColorChannel = info.BitsPerChannel;
            display.ActiveColorMode = display.ActiveHdr ? 2u : (display.WideColorEnforced ? 1u : 0u);
            display.Mode = display.ActiveHdr ? "HDR (legacy inferred)" : (display.WideColorEnforced ? "WCG (legacy)" : "SDR (legacy)");
        }
        else
        {
            display.HdrSupported = false;
            display.UserEnabled = false;
            display.ActiveHdr = false;
            display.Mode = "Unknown: " + new Win32Exception(status).Message;
        }
    }

    public static DisplayInfo[] Enumerate()
    {
        lock (Sync)
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PATH_INFO path in Paths())
            {
                string adapterKey = unchecked((uint)path.Target.AdapterId.HighPart).ToString("X8") + path.Target.AdapterId.LowPart.ToString("X8");
                string key = adapterKey + ":" + path.Target.Id;
                if (!seen.Add(key)) continue;

                TARGET_NAME target = new TARGET_NAME();
                target.Header = Header(2, typeof(TARGET_NAME), path.Target.AdapterId, path.Target.Id);
                target.FriendlyName = ""; target.DevicePath = "";
                int targetStatus = GetPacket(ref target);
                SOURCE_NAME source = new SOURCE_NAME();
                source.Header = Header(1, typeof(SOURCE_NAME), path.Source.AdapterId, path.Source.Id);
                source.GdiDeviceName = "";
                int sourceStatus = GetPacket(ref source);

                DisplayInfo display = new DisplayInfo();
                display.Name = targetStatus == 0 && !String.IsNullOrEmpty(target.FriendlyName) ? target.FriendlyName : "Display " + path.Target.Id;
                display.DeviceName = sourceStatus == 0 ? source.GdiDeviceName : "";
                display.MonitorDevicePath = targetStatus == 0 ? target.DevicePath : "";
                display.AdapterKey = adapterKey;
                display.AdapterLow = path.Target.AdapterId.LowPart;
                display.AdapterHigh = path.Target.AdapterId.HighPart;
                display.TargetId = path.Target.Id;
                display.SourceId = path.Source.Id;
                display.TargetNameStatus = targetStatus;
                display.SourceNameStatus = sourceStatus;
                ReadColor(display);
                displays.Add(display);
            }
            return displays.ToArray();
        }
    }

    private static bool Selected(DisplayInfo display, string[] selectors)
    {
        if (selectors == null || selectors.Length == 0) return true;
        foreach (string selector in selectors)
        {
            if (String.IsNullOrEmpty(selector)) continue;
            if (String.Equals(selector, display.MonitorDevicePath, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(selector, display.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(selector, display.Name, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(selector, display.Key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static SetResult[] Set(bool enabled) { return Set(enabled, null); }

    // Selectors are exact device paths, GDI device names, friendly names, or Key values.
    // Null/empty selects all active HDR-capable displays. Never enables WCG as a side action.
    public static SetResult[] Set(bool enabled, string[] targetSelectors)
    {
        lock (Sync)
        {
            List<SetResult> results = new List<SetResult>();
            foreach (DisplayInfo display in Enumerate())
            {
                if (display.QueryStatus != 0 || !display.HdrSupported || !Selected(display, targetSelectors)) continue;
                SetResult result = new SetResult();
                result.Name = display.Name; result.DeviceName = display.DeviceName;
                result.MonitorDevicePath = display.MonitorDevicePath; result.Key = display.Key;
                result.Requested = enabled;
                result.BeforeUserEnabled = display.UserEnabled; result.BeforeActiveHdr = display.ActiveHdr;
                result.AfterUserEnabled = display.UserEnabled; result.AfterActiveHdr = display.ActiveHdr;

                if (display.UserEnabled == enabled && display.ActiveHdr == enabled)
                {
                    result.Api = "No change needed";
                    result.Verified = true;
                    results.Add(result);
                    continue;
                }

                LUID adapter = new LUID(); adapter.LowPart = display.AdapterLow; adapter.HighPart = display.AdapterHigh;
                SET_STATE packet = new SET_STATE();
                uint packetType = display.UsesInfo2 ? 16u : 10u;
                packet.Header = Header(packetType, typeof(SET_STATE), adapter, display.TargetId);
                packet.Value = enabled ? 1u : 0u;
                result.Api = packetType == 16 ? "SET_HDR_STATE (16)" : "SET_ADVANCED_COLOR_STATE (10), legacy";
                int status = SetPacket(packet);
                // Only unsupported-packet errors warrant a legacy fallback. Access/session
                // failures are returned as failures, rather than retried through another setter.
                if (packetType == 16 && (status == 50 || status == 87 || status == 120 || status == 1))
                {
                    packet.Header.Type = 10;
                    status = SetPacket(packet);
                    result.Api = "SET_ADVANCED_COLOR_STATE (10), fallback after SET_HDR_STATE unsupported";
                }
                result.Status = status;
                result.Changed = status == 0;
                if (status != 0)
                {
                    result.Error = new Win32Exception(status).Message;
                    results.Add(result);
                    continue;
                }

                // Display changes may become visible asynchronously. Verify the actual
                // HDR mode, not only the user preference. No blind second toggle occurs.
                Stopwatch timer = Stopwatch.StartNew();
                do
                {
                    Thread.Sleep(150);
                    ReadColor(display);
                    result.AfterUserEnabled = display.UserEnabled;
                    result.AfterActiveHdr = display.ActiveHdr;
                    if (display.QueryStatus == 0 && display.UserEnabled == enabled && display.ActiveHdr == enabled)
                    {
                        result.Verified = true;
                        break;
                    }
                } while (timer.ElapsedMilliseconds < 4500);
                if (!result.Verified)
                    result.Error = display.QueryStatus != 0 ? "State verification failed: " + new Win32Exception(display.QueryStatus).Message :
                        "HDR preference set, but active HDR mode did not match within 4.5 seconds; policy or driver may limit activation.";
                results.Add(result);
            }
            return results.ToArray();
        }
    }
}
