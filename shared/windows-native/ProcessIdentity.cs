using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ProcessIdentity
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Entry
    {
        public uint Size, Usage, Pid; public UIntPtr Heap; public uint Module, Threads, ParentPid;
        public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string File;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static long Started(uint pid)
    {
        try { using (var process = Process.GetProcessById((int)pid)) return process.StartTime.ToUniversalTime().Ticks; }
        catch { return 0; }
    }
    public static bool IsPhotoshop(uint pid)
    {
        try { using (var process = Process.GetProcessById((int)pid)) return process.ProcessName.Equals("Photoshop", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    public static uint PhotoshopAncestor(uint pid)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return 0;
        var parents = new System.Collections.Generic.Dictionary<uint, uint>();
        try
        {
            var entry = new Entry(); entry.Size = (uint)Marshal.SizeOf(typeof(Entry));
            if (Process32First(snapshot, ref entry)) do { parents[entry.Pid] = entry.ParentPid; } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        for (int count = 0; pid != 0 && count < 32; count++)
        {
            if (IsPhotoshop(pid)) return pid;
            uint parent; if (!parents.TryGetValue(pid, out parent) || parent == pid) return 0;
            pid = parent;
        }
        return 0;
    }
}
