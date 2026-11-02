using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PanelRecall
{
    internal sealed class RecallSession : IDisposable
    {
        private enum Phase { Ready, MovingDpi, Positioning, Present, ReturningDpi, Returning, Failed, Finished }
        internal readonly ReturnRecord Record;
        internal IntPtr Window { get { return new IntPtr(Record.Window); } }
        internal bool Finished { get { return phase == Phase.Finished; } }
        internal bool Present { get { return phase == Phase.Present; } }
        internal bool Returning { get { return phase == Phase.ReturningDpi || phase == Phase.Returning; } }
        private Phase phase;
        private IntPtr process, host;
        private long issued, deadline;
        private Rectangle expected;
        private Native.Point summonPoint;
        private bool marked;
        private static long Now { get { return Stopwatch.GetTimestamp(); } }
        private static long After(int ms) { return Now + Stopwatch.Frequency * ms / 1000; }

        private RecallSession(ReturnRecord record)
        {
            ValidateRecord(record);
            Record = record;
            process = Native.OpenProcess(0x00100000, false, record.Pid);
            if (process == IntPtr.Zero || ProcessIdentity.Started(record.Pid) != record.Started)
            { Dispose(); throw new InvalidOperationException("源进程身份无法确认，未移动窗口。"); }
            if (record.HostPid != 0 && record.HostStarted > 0 && ProcessIdentity.Started(record.HostPid) == record.HostStarted)
                host = Native.OpenProcess(0x00100000, false, record.HostPid);
        }
        private static void ValidateRecord(ReturnRecord r)
        {
            if (r == null || r.Version != 1 || r.Slot < 0 || r.Slot > 4 || r.Window == 0 || r.Cookie == 0 || r.Pid == 0 || r.Started <= 0 ||
                String.IsNullOrEmpty(r.Class) || String.IsNullOrEmpty(r.Exe) || r.Title == null || r.Width < 1 || r.Height < 1 ||
                r.Width > 100000 || r.Height > 100000 || Math.Abs((long)r.Left) > 1000000 || Math.Abs((long)r.Top) > 1000000)
                throw new InvalidOperationException("恢复记录无效，未操作任何窗口。");
        }
        internal static bool IsDefinitelyGone(ReturnRecord r)
        {
            ValidateRecord(r);
            IntPtr window = new IntPtr(r.Window);
            if (!Native.IsWindow(window)) return true;
            uint pid; Native.GetWindowThreadProcessId(window, out pid);
            if (pid != r.Pid) return true;
            long started = ProcessIdentity.Started(pid);
            return started != 0 && started != r.Started;
        }
        internal static RecallSession Recover(ReturnRecord record)
        {
            var result = new RecallSession(record);
            try
            {
                if (!result.IdentityValid()) throw new InvalidOperationException("恢复窗口的标记或进程已改变，未移动窗口。请关闭旧面板后重新绑定。");
                result.marked = true;
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        internal static RecallSession Capture(WindowInfo chosen, int slot)
        {
            var current = Native.Describe(chosen.Handle);
            if (current == null || current.Pid != chosen.Pid || current.Started != chosen.Started || current.Class != chosen.Class)
                throw new InvalidOperationException("源窗口已改变，请刷新列表后重新绑定。");
            RequireMovable(current.Handle);
            Native.Rect rect;
            if (!Native.GetWindowRect(current.Handle, out rect) || rect.Width < 1 || rect.Height < 1)
                throw new InvalidOperationException("无法读取原面板位置。");
            if (Native.GetProp(current.Handle, Native.Property) != IntPtr.Zero)
                throw new InvalidOperationException("此窗口已有召回标记，请先恢复上次会话，或关闭该面板后重新打开。");
            long cookie = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
            if (cookie == 0) cookie = 1;
            var record = new ReturnRecord { Slot = slot, Window = current.Handle.ToInt64(), Pid = current.Pid, Started = current.Started,
                Cookie = cookie, Class = current.Class, Exe = current.Exe, Title = current.Title, HostPid = current.HostPid,
                HostStarted = current.HostPid == 0 ? 0 : ProcessIdentity.Started(current.HostPid),
                Left = rect.Left, Top = rect.Top, Width = rect.Width, Height = rect.Height };
            var result = new RecallSession(record);
            try
            {
                if (!Native.SetProp(current.Handle, Native.Property, new IntPtr(cookie)))
                    throw new InvalidOperationException("无法标记面板身份，请确认与 Photoshop 使用相同权限。");
                result.marked = true;
                // Durable return point must exist BEFORE the first move.
                Storage.Write("return.json", record);
                return result;
            }
            catch
            {
                // A filesystem error can occur after a replacement committed.
                // Keep the stamp if our durable record exists, so recovery is
                // still possible even though no first move has been issued.
                bool persisted = false;
                try { var saved = Storage.Read<ReturnRecord>("return.json"); persisted = saved != null && saved.Cookie == record.Cookie && saved.Window == record.Window; }
                catch { persisted = true; }
                if (!persisted && result.IdentityValid()) Native.RemoveProp(current.Handle, Native.Property);
                result.marked = false; result.Dispose(); throw;
            }
        }
        private static void RequireMovable(IntPtr window)
        {
            if (!Native.IsFloating(window)) throw new InvalidOperationException("只能召回浮动顶层面板；请先将停靠面板拖成浮动窗口。");
            if (!Native.IsWindowVisible(window) || Native.IsIconic(window) || Native.IsZoomed(window))
                throw new InvalidOperationException("请让面板保持可见、未最小化且未最大化。");
            if (!Native.IsWindowEnabled(window)) throw new InvalidOperationException("面板被对话框阻塞，请先关闭对话框。");
        }
        private bool IdentityValid()
        {
            if (process == IntPtr.Zero || Native.WaitForSingleObject(process, 0) != 258 || !Native.IsWindow(Window)) return false;
            uint pid; Native.GetWindowThreadProcessId(Window, out pid);
            return pid == Record.Pid && Native.GetProp(Window, Native.Property) == new IntPtr(Record.Cookie) && Native.ClassName(Window) == Record.Class;
        }
        private void RequireIdentity()
        { if (!IdentityValid()) throw new InvalidOperationException("面板身份失效，未移动其他窗口；恢复记录已保留。"); }
        internal void Summon(Native.Point point)
        {
            RequireIdentity(); RequireMovable(Window);
            if (phase != Phase.Ready && phase != Phase.Present) throw new InvalidOperationException("正在定位或放回，请等待完成；失败后请先放回。");
            CanvasClickHook.Disarm(); summonPoint = point;
            // First let the destination monitor apply its own DPI. Do not
            // force an old screen's pixel dimensions onto the plugin.
            expected = new Rectangle(point.X, point.Y, 0, 0);
            Move(expected, true, true);
            phase = Phase.MovingDpi;
        }
        internal void Restore()
        {
            if (Finished) return;
            CanvasClickHook.Disarm(); RequireIdentity(); RequireMovable(Window);
            expected = OriginalOnExistingScreens();
            Move(expected, true);
            phase = Phase.ReturningDpi;
        }
        private Rectangle OriginalOnExistingScreens()
        {
            var original = new Rectangle(Record.Left, Record.Top, Record.Width, Record.Height);
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle overlap = Rectangle.Intersect(original, screen.WorkingArea);
                if (overlap.Width >= 32 && overlap.Height >= 32) return original;
            }
            Rectangle work = Screen.FromRectangle(original).WorkingArea;
            return new Rectangle(Math.Max(work.Left, Math.Min(original.Left, work.Right - original.Width)),
                Math.Max(work.Top, Math.Min(original.Top, work.Bottom - original.Height)), original.Width, original.Height);
        }
        private void Move(Rectangle bounds, bool positionOnly, bool raise = false)
        {
            RequireIdentity();
            uint flags = Native.NoActivate | Native.NoOwnerZOrder | Native.Async | (raise ? 0 : Native.NoZOrder) | (positionOnly ? Native.NoSize : 0);
            if (!Native.SetWindowPos(Window, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags))
                throw new InvalidOperationException("系统拒绝移动面板（错误 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + "），请尝试放回。");
            issued = Now; deadline = After(2500);
        }
        private bool AtPosition(Native.Rect rect)
        { return Math.Abs((long)rect.Left - expected.Left) <= 3 && Math.Abs((long)rect.Top - expected.Top) <= 3; }
        internal void EnableCanvasReturn()
        { if (phase == Phase.Present && host != IntPtr.Zero && Native.WaitForSingleObject(host, 0) == 258) CanvasClickHook.Arm(); }
#if NET8_0_OR_GREATER
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
#endif
        internal string Tick(bool autoReturn)
        {
            if (Finished || phase == Phase.Ready || phase == Phase.Failed) return null;
            RequireIdentity();
            if (phase == Phase.Present)
            {
                if (autoReturn && host != IntPtr.Zero && Native.WaitForSingleObject(host, 0) == 258 && CanvasClickHook.TakeCanvasPress(Record.HostPid, Window))
                { Restore(); return "已识别 PS 画布点击，正在放回原面板…"; }
                return null;
            }
            Native.Rect rect;
            if (!Native.GetWindowRect(Window, out rect)) throw new InvalidOperationException("无法确认面板位置，恢复记录已保留。");
            if (Now > deadline) throw new InvalidOperationException("面板未在期限内到达请求位置，不能宣称成功；请点“放回原位”重试。");
            if (!AtPosition(rect) || (Now - issued) * 1000.0 / Stopwatch.Frequency < 150) return null;
            if (phase == Phase.MovingDpi)
            {
                Rectangle work = Screen.FromPoint(new System.Drawing.Point(summonPoint.X, summonPoint.Y)).WorkingArea;
                int gap = Math.Max(8, (int)(12 * Native.Dpi(Window) / 96));
                expected = new Rectangle(Math.Max(work.Left, Math.Min(summonPoint.X + gap, work.Right - rect.Width)),
                    Math.Max(work.Top, Math.Min(summonPoint.Y + gap, work.Bottom - rect.Height)), rect.Width, rect.Height);
                Move(expected, true, true); phase = Phase.Positioning;
            }
            else if (phase == Phase.ReturningDpi)
            { Move(expected, false); phase = Phase.Returning; }
            else if (phase == Phase.Positioning)
            {
                phase = Phase.Present;
                if (autoReturn) EnableCanvasReturn();
                return "面板已召回；松键保持。Shift+Alt+" + (Record.Slot + 1) + " 放回。" +
                    (autoReturn ? "PS 画布点击可触发自动放回（识别不明确时保持）。" : "");
            }
            else if (phase == Phase.Returning && Math.Abs((long)rect.Width - expected.Width) <= 3 && Math.Abs((long)rect.Height - expected.Height) <= 3)
            {
                RequireIdentity();
                Storage.ClearReturnRecord();
                if (Native.GetProp(Window, Native.Property) == new IntPtr(Record.Cookie)) Native.RemoveProp(Window, Native.Property);
                marked = false; phase = Phase.Finished; Dispose();
                return "原面板位置和尺寸已确认恢复。";
            }
            return null;
        }
        internal void Fail() { CanvasClickHook.Disarm(); phase = Phase.Failed; }
        internal bool DiscardIfGone()
        {
            uint pid; Native.GetWindowThreadProcessId(Window, out pid);
            bool gone = !Native.IsWindow(Window) || pid != Record.Pid || (process != IntPtr.Zero && Native.WaitForSingleObject(process, 0) == 0);
            if (!gone) return false;
            CanvasClickHook.Disarm(); Storage.ClearReturnRecord(); marked = false; phase = Phase.Finished; Dispose(); return true;
        }
        public void Dispose()
        {
            // Never silently discard an outstanding return point here.
            if (marked) CanvasClickHook.Disarm();
            if (process != IntPtr.Zero) { Native.CloseHandle(process); process = IntPtr.Zero; }
            if (host != IntPtr.Zero) { Native.CloseHandle(host); host = IntPtr.Zero; }
        }
    }
}
