using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace PanelRecall
{
    internal static class Native
    {
        internal const string Property = "PanelRecall.WindowIdentity.v1";
        internal const uint NoSize = 1, NoZOrder = 4, NoActivate = 16, NoOwnerZOrder = 0x0200, Async = 0x4000;
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        internal delegate bool EnumProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder value, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        internal static void InitializeDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }
        internal static uint Dpi(IntPtr window)
        { try { uint value = GetDpiForWindow(window); return value == 0 ? 96 : value; } catch (EntryPointNotFoundException) { return 96; } }
        internal static string ClassName(IntPtr window)
        { var value = new StringBuilder(256); GetClassName(window, value, value.Capacity); return value.ToString(); }
        internal static bool IsFloating(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window) || GetAncestor(window, 2) != window) return false;
            if ((GetWindowLongPtr(window, -16).ToInt64() & 0x40000000L) != 0) return false;
            string name = ClassName(window);
            // Never move the PS application, a document, menus or a modal dialog.
            return name != "Photoshop" && name != "OWL.DocumentWindow" && name != "PSDocC" &&
                name != "#32770" && name != "#32768" && name != "OWL.ShadowView";
        }
        internal static uint PhotoshopHost(IntPtr window, uint pid)
        {
            var visited = new HashSet<IntPtr>();
            for (int i = 0; window != IntPtr.Zero && i < 24 && visited.Add(window); i++, window = GetWindow(window, 4))
            {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (ProcessIdentity.IsPhotoshop(owner)) return owner;
            }
            return ProcessIdentity.PhotoshopAncestor(pid);
        }
#if NET8_0_OR_GREATER
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
#endif
        internal static WindowInfo Describe(IntPtr window)
        {
            if (!IsFloating(window) || !IsWindowVisible(window)) return null;
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id || pid == 0) return null;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    var title = new StringBuilder(1024); GetWindowText(window, title, title.Capacity);
                    var module = process.MainModule;
                    if (module == null) return null;
                    string path = module.FileName;
                    uint host = PhotoshopHost(window, pid);
                    return new WindowInfo { Handle = window, Pid = pid, Started = process.StartTime.ToUniversalTime().Ticks,
                        Title = title.ToString(), Class = ClassName(window), Exe = path, HostPid = host };
                }
            }
            catch { return null; }
        }
        internal static List<WindowInfo> Windows(bool photoshopOnly)
        {
            var result = new List<WindowInfo>();
            EnumWindows(delegate(IntPtr window, IntPtr data)
            {
                var item = Describe(window);
                if (item != null && (!photoshopOnly || item.HostPid != 0)) result.Add(item);
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(WindowInfo a, WindowInfo b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.ToString(), b.ToString()); });
            return result;
        }
    }
    internal sealed class WindowInfo
    {
        public IntPtr Handle;
        public uint Pid, HostPid;
        public long Started;
        public string Title = "", Class = "", Exe = "";
        public override string ToString()
        { return (Title.Length == 0 ? UiText.T("（无标题）") : Title) + " · " + System.IO.Path.GetFileName(Exe) + " · " + Class + " · PID " + Pid; }
        internal Binding Binding()
        { return new Binding { Title = Title, Class = Class, Exe = Exe }; }
        internal bool Matches(Binding binding)
        { return binding != null && String.Equals(Exe, binding.Exe, StringComparison.OrdinalIgnoreCase) && Class == binding.Class && Title == binding.Title; }
    }
}
