using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PanelMirror;

internal sealed record WindowItem(nint Handle, uint ProcessId, long StartedAt, string ProcessName, string WindowClass, string Title, int PhotoshopMajor)
{
    public override string ToString() => $"{Compatibility.Display(ProcessName, PhotoshopMajor)} · {(Title.Length > 0 ? Title : UiText.T("（无标题）"))} · PID {ProcessId} · 0x{Handle:X}";
    public bool Matches(Binding b) => ProcessName.Equals(b.ProcessName, StringComparison.OrdinalIgnoreCase) && WindowClass == b.WindowClass && Title == b.Title &&
        (b.PhotoshopMajor == 0 || PhotoshopMajor == b.PhotoshopMajor);
}

internal static class Native
{
    // Only the source-only documentation harness sets a window-list filter.
    internal static Func<WindowItem, bool>? WindowFilter { get; set; }
    private static readonly Dictionary<(uint Pid, long Start), int> versions = new();
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public RECT(Rectangle r) { Left = r.Left; Top = r.Top; Right = r.Right; Bottom = r.Bottom; } }
    [StructLayout(LayoutKind.Sequential)] internal struct ThumbnailProperties
    {
        public uint Flags;
        public RECT Destination, Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
    }
    internal delegate bool EnumProc(nint hwnd, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref POINT p);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref POINT p);
    [DllImport("user32.dll")] internal static extern nint ChildWindowFromPointEx(nint parent, POINT p, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern int GetDlgCtrlID(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetProp(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetProp(nint hwnd, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint RemoveProp(nint hwnd, string name);
    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(nint thumbnail);

    internal static WindowItem? Describe(nint hwnd)
    {
        if (!IsWindow(hwnd)) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            long startedAt = process.StartTime.ToUniversalTime().Ticks;
            var title = new StringBuilder(1024); var cls = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity); GetClassName(hwnd, cls, cls.Capacity);
            int major = 0;
            if (process.ProcessName.Equals("Photoshop", StringComparison.OrdinalIgnoreCase) &&
                !versions.TryGetValue((pid, startedAt), out major))
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (path is not null) major = FileVersionInfo.GetVersionInfo(path).FileMajorPart;
                }
                catch (System.ComponentModel.Win32Exception) { /* Version is informational, not a reason to lose the window. */ }
                catch (FileNotFoundException) { }
                if (versions.Count > 512) versions.Clear();
                versions[(pid, startedAt)] = major;
            }
            return new(hwnd, pid, startedAt, process.ProcessName, cls.ToString(), title.ToString(), major);
        }
        catch (ArgumentException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    internal static List<WindowItem> Windows()
    {
        var list = new List<WindowItem>();
        EnumWindows((hwnd, _) => { if (IsWindowVisible(hwnd) && Describe(hwnd) is { } item && (WindowFilter is null || WindowFilter(item))) list.Add(item); return true; }, 0);
        return list.OrderBy(x => x.ProcessName).ThenBy(x => x.Title).ToList();
    }

    // Deliberately conservative: support only known independent PS panel hosts.
    // The documentation harness is explicitly restricted to its synthetic PID.
    internal static bool SupportedPanel(WindowItem item)
    {
        if (!PanelRecall.Native.IsFloating(item.Handle) || string.IsNullOrWhiteSpace(item.Title)) return false;
        if (WindowFilter is not null) return WindowFilter(item);
        if (item.WindowClass is not ("OWL.Floating" or "OWL.Palette" or "OWL.TabbedPalette" or "PLUGPLUG_UI_NATIVE_WINDOW_CLASS_NAME")) return false;
        return item.ProcessName.Equals("Photoshop", StringComparison.OrdinalIgnoreCase) ||
            PanelRecall.Native.PhotoshopHost(item.Handle, item.ProcessId) != 0;
    }

    internal static string ClassName(nint hwnd)
    { var value = new StringBuilder(256); GetClassName(hwnd, value, value.Capacity); return value.ToString(); }
    internal static string WindowTitle(nint hwnd)
    { var value = new StringBuilder(1024); GetWindowText(hwnd, value, value.Capacity); return value.ToString(); }

    internal static bool ClientBounds(nint hwnd, out Rectangle screen)
    {
        screen = Rectangle.Empty;
        var origin = new POINT(0, 0);
        if (!GetClientRect(hwnd, out var rect) || rect.Right <= 0 || rect.Bottom <= 0 ||
            rect.Right > 100000 || rect.Bottom > 100000 || !ClientToScreen(hwnd, ref origin)) return false;
        screen = new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom); return true;
    }

    internal static nint Pack(int x, int y) => (nint)unchecked((int)((ushort)x | ((uint)(ushort)y << 16)));
}
