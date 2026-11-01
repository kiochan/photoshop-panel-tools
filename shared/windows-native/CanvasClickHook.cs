using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Observe primary-button presses without swallowing or replaying them. The
// low-level callback only snapshots a bounded queue; classification runs on
// the helper loop, never in Photoshop or on the input-hook thread.
internal static class CanvasClickHook
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    {
        public Point Point;
        public uint Mouse, Flags, Time;
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }
    // Object identity is the arm generation; stale clicks cannot hide a HUD
    // that was hidden/reopened/rebound after the press was captured.
    private sealed class Scope { public uint ArmedAt; }
    private sealed class Press
    {
        public Scope Scope;
        public Point Point;
        public IntPtr Window;
        public long Time;
    }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static readonly HookProc callback = OnMouse;
    private static readonly ManualResetEvent ready = new ManualResetEvent(false);
    private static readonly object sync = new object();
    private static readonly Queue<Press> presses = new Queue<Press>();
    private static Scope scope;
    private static Thread thread;
    private static uint threadId;
    private static IntPtr hook;
    private static int stopping;
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetTickCount();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);

    public static bool Install()
    {
        try
        {
            thread = new Thread(delegate()
            {
                try
                {
                    threadId = GetCurrentThreadId();
                    Message message;
                    PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // create queue before Stop can post
                    if (Interlocked.CompareExchange(ref stopping, 0, 0) != 0) return;
                    hook = SetWindowsHookEx(14, callback, IntPtr.Zero, 0);
                    ready.Set();
                    if (hook == IntPtr.Zero) return;
                    while (Interlocked.CompareExchange(ref stopping, 0, 0) == 0 && GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                    { TranslateMessage(ref message); DispatchMessage(ref message); }
                }
                catch { /* Report capability=false; keyboard/HUD operation can continue. */ }
                finally
                {
                    if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                    ready.Set();
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.WaitOne(1200)) { Stop(); return false; }
            return hook != IntPtr.Zero;
        }
        catch { Stop(); return false; }
    }
    public static void Arm()
    {
        if (hook == IntPtr.Zero) return;
        lock (sync) presses.Clear();
        Interlocked.Exchange(ref scope, new Scope { ArmedAt = GetTickCount() });
    }
    public static void Disarm()
    {
        Interlocked.Exchange(ref scope, null);
        lock (sync) presses.Clear();
    }
    public static void Stop()
    {
        Disarm();
        Interlocked.Exchange(ref stopping, 1);
        if (threadId != 0) PostThreadMessage(threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        if (thread != null && thread.IsAlive) thread.Join(800);
    }
    private static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            Scope armed = Interlocked.CompareExchange(ref scope, null, null);
            if (code == 0 && wParam.ToInt64() == 0x0201 && armed != null)
            {
                var data = (MouseData)Marshal.PtrToStructure(lParam, typeof(MouseData));
                if (unchecked((int)(data.Time - armed.ArmedAt)) < 0 ||
                    unchecked((uint)(GetTickCount() - data.Time)) > 400) return CallNextHookEx(hook, code, wParam, lParam);
                // Do not discard injected/promoted mouse events: tablet drivers
                // may use those. No pressure, pointer or WinTab data is altered.
                var press = new Press { Scope = armed, Point = data.Point, Window = WindowFromPoint(data.Point), Time = Stopwatch.GetTimestamp() };
                lock (sync)
                {
                    while (presses.Count >= 32) presses.Dequeue();
                    presses.Enqueue(press);
                }
            }
        }
        catch { /* Fail open and always continue the input chain. */ }
        return CallNextHookEx(hook, code, wParam, lParam);
    }
    public static bool TakeCanvasPress(uint hostProcess, IntPtr hud)
    {
        if (hostProcess == 0 || hud == IntPtr.Zero) return false;
        Press[] batch;
        lock (sync) { batch = presses.ToArray(); presses.Clear(); }
        Scope armed = Interlocked.CompareExchange(ref scope, null, null);
        foreach (Press press in batch)
        {
            double age = (Stopwatch.GetTimestamp() - press.Time) * 1000.0 / Stopwatch.Frequency;
            if (armed == null || press.Scope != armed || age < 0 || age > 400) continue;
            if (IsCanvas(press, hostProcess, hud)) return true;
        }
        return false;
    }
    private static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(128);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
    private static IntPtr Parent(IntPtr window)
    {
        // Unlike GetParent, GA_PARENT never substitutes a popup's owner.
        IntPtr parent = GetAncestor(window, 1);
        return parent == GetDesktopWindow() ? IntPtr.Zero : parent;
    }
    private static bool IsCanvas(Press press, uint hostProcess, IntPtr hud)
    {
        IntPtr leaf = press.Window;
        // Re-hit the ORIGINAL press point, not the cursor's later position.
        // A changed/covered window makes this observation inconclusive.
        if (leaf == IntPtr.Zero || WindowFromPoint(press.Point) != leaf || leaf == hud || IsChild(hud, leaf)) return false;
        uint process; GetWindowThreadProcessId(leaf, out process);
        if (process != hostProcess || !IsWindowVisible(leaf) || !IsWindowEnabled(leaf)) return false;
        // PSViewC alone is NOT sufficient: native palettes also use this class.
        // Require a document container in the real parent chain, never an
        // owner-chain, foreground-process or document-title substring fallback.
        if (ClassName(leaf) != "PSViewC") return false;
        Rect client; Point local = press.Point;
        if (!GetClientRect(leaf, out client) || !ScreenToClient(leaf, ref local) ||
            local.X < client.Left || local.X >= client.Right || local.Y < client.Top || local.Y >= client.Bottom) return false;
        bool document = false;
        IntPtr cursor = leaf;
        for (int depth = 0; cursor != IntPtr.Zero && depth < 32; depth++, cursor = Parent(cursor))
        {
            GetWindowThreadProcessId(cursor, out process);
            if (process != hostProcess || cursor == hud) return false;
            string name = ClassName(cursor);
            if (name.Length == 0) return false;
            if (name == "OWL.Dock" || name == "OWL.ControlBarContainer" || name == "OWL.Floating" ||
                name == "OWL.Palette" || name == "OWL.TabbedPalette" || name == "OWL.ShadowView" ||
                name == "#32770" || name == "#32768" || name == "PLUGPLUG_UI_NATIVE_WINDOW_CLASS_NAME") return false;
            if (name == "OWL.DocumentWindow" || name == "PSDocC") document = true;
        }
        // In unknown Photoshop layouts leave the HUD open. Never generalize
        // a Photoshop-process click into a canvas click.
        IntPtr root = GetAncestor(leaf, 2);
        return cursor == IntPtr.Zero && document && root != IntPtr.Zero && IsWindowEnabled(root);
    }
}
