namespace PanelMirror;

// A live binding survives document-title changes, but not a source process restart.
// Persisted bindings still use exact matching so restart never silently chooses a different panel.
internal sealed class SourceWindow
{
    internal WindowItem Identity { get; }
    internal nint Handle => Identity.Handle;
    internal SourceWindow(WindowItem identity) => Identity = identity;

    internal bool IsAlive()
    {
        var current = Native.Describe(Handle);
        return current is not null && current.ProcessId == Identity.ProcessId && current.StartedAt == Identity.StartedAt &&
            current.ProcessName == Identity.ProcessName && current.WindowClass == Identity.WindowClass;
    }

    internal bool Owns(nint target)
    {
        if (!Native.IsWindow(Handle) || !Native.IsWindow(target)) return false;
        Native.GetWindowThreadProcessId(Handle, out uint rootPid);
        Native.GetWindowThreadProcessId(target, out uint targetPid);
        return rootPid == Identity.ProcessId && targetPid == Identity.ProcessId && (target == Handle || Native.IsChild(Handle, target));
    }

    internal bool ReadGeometry(Binding binding, out Size clientSize, out Rectangle area)
    {
        clientSize = Size.Empty; area = Rectangle.Empty;
        if (!Native.GetClientRect(Handle, out var rect)) return false;
        clientSize = new Size(rect.Right, rect.Bottom);
        var client = new Rectangle(Point.Empty, clientSize);
        area = binding.Width == 0 && binding.Height == 0 ? client : new Rectangle(binding.X, binding.Y, binding.Width, binding.Height);
        return area.Width > 0 && area.Height > 0 && client.Contains(area);
    }

    internal nint Hit(Native.POINT screen)
    {
        nint target = Handle;
        for (int depth = 0; depth < 32; depth++)
        {
            var local = screen;
            if (!Native.ScreenToClient(target, ref local)) return 0;
            nint child = Native.ChildWindowFromPointEx(target, local, 7);
            if (child == 0 || child == target) break;
            if (!Owns(child)) return 0;
            target = child;
        }
        return Owns(target) && Native.IsWindowEnabled(target) ? target : 0;
    }
}
