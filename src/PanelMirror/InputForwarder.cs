using System.Runtime.InteropServices;

namespace PanelMirror;

// Uncertain geometry cancels the original stamped target without posting a
// coordinate-bearing mouse-up that could complete a click in a new layout.
internal sealed class InputForwarder
{
    private readonly PanelTracker tracker;
    private readonly Action<string> report;
    private readonly Dictionary<int, (nint Target, nint Flags)> keys = new();
    private nint dragTarget, hoverTarget, keyboardTarget;
    private int buttons;
    internal bool IsDragging => buttons != 0;
    internal InputForwarder(PanelTracker tracker, Action<string> report) { this.tracker = tracker; this.report = report; }

    private static (int Mask, uint Down, uint Up, uint Double, int Extra) Button(MouseButtons button) => button switch
    {
        MouseButtons.Left => (1, 0x201, 0x202, 0x203, 0),
        MouseButtons.Right => (2, 0x204, 0x205, 0x206, 0),
        MouseButtons.Middle => (16, 0x207, 0x208, 0x209, 0),
        MouseButtons.XButton1 => (32, 0x20B, 0x20C, 0x20D, 1 << 16),
        MouseButtons.XButton2 => (64, 0x20B, 0x20C, 0x20D, 2 << 16),
        _ => (0, 0, 0, 0, 0)
    };
    private static int Modifiers() => ((Control.ModifierKeys & Keys.Shift) != 0 ? 4 : 0) | ((Control.ModifierKeys & Keys.Control) != 0 ? 8 : 0);
    private bool Post(nint target, uint message, nint wParam, nint lParam, bool cleanup = false)
    {
        if (cleanup && message is not (0x1F or 0x2A3 or 0x101)) return false;
        if (!tracker.CanPost(target, cleanup)) { report(UiText.T("输入目标已失效，操作未转发。")); return false; }
        if (Native.PostMessage(target, message, wParam, lParam)) return true;
        report(UiText.F("消息投递失败（{0}）；原面板可能不接受后台消息。", Marshal.GetLastWin32Error())); return false;
    }
    private bool Mouse(nint target, uint message, int flags, Native.POINT screen, bool wheel = false)
    {
        var local = screen;
        if (!wheel && !Native.ScreenToClient(target, ref local)) { report(UiText.T("无法转换输入坐标。")); return false; }
        if (local.X < short.MinValue || local.X > short.MaxValue || local.Y < short.MinValue || local.Y > short.MaxValue)
        { report(UiText.T("输入坐标超出 Windows 鼠标消息范围，已暂停本次投递。")); return false; }
        return Post(target, message, (nint)flags, Native.Pack(local.X, local.Y));
    }
    internal void Leave()
    {
        if (hoverTarget != 0) Post(hoverTarget, 0x2A3, 0, 0, true);
        hoverTarget = 0;
    }
    private void Hover(nint target) { if (hoverTarget != target) { Leave(); hoverTarget = target; } }
    internal bool Down(MouseButtons button, int clicks, Native.POINT screen)
    {
        var spec = Button(button);
        if (spec.Mask == 0 || (buttons & spec.Mask) != 0) return false;
        nint target = IsDragging ? dragTarget : tracker.Hit(screen);
        if (target == 0 || !Native.IsWindowEnabled(target)) return false;
        Hover(target);
        int nextButtons = buttons | spec.Mask;
        if (!Mouse(target, clicks > 1 ? spec.Double : spec.Down, nextButtons | Modifiers() | spec.Extra, screen)) return false;
        buttons = nextButtons; dragTarget = target;
        if (keyboardTarget != target) ReleaseKeys();
        keyboardTarget = target;
        return true;
    }
    internal void Move(Native.POINT screen)
    {
        nint target = IsDragging ? dragTarget : tracker.Hit(screen);
        Hover(target);
        if (target != 0) Mouse(target, 0x200, buttons | Modifiers(), screen);
    }
    internal void Up(MouseButtons button, Native.POINT screen)
    {
        var spec = Button(button);
        if ((buttons & spec.Mask) == 0) return;
        buttons &= ~spec.Mask;
        bool sent = Mouse(dragTarget, spec.Up, buttons | Modifiers() | spec.Extra, screen);
        if (!sent) { Post(dragTarget, 0x1F, 0, 0, true); buttons = 0; }
        if (!IsDragging) dragTarget = 0;
    }
    internal void Wheel(Native.POINT screen, int delta, bool horizontal)
    {
        nint target = IsDragging ? dragTarget : tracker.Hit(screen);
        if (target != 0) Mouse(target, horizontal ? 0x20Eu : 0x20Au, buttons | Modifiers() | (delta << 16), screen, true);
    }
    internal void ReleasePointer()
    {
        if (buttons != 0) Post(dragTarget, 0x1F, 0, 0, true);
        buttons = 0;
        dragTarget = 0; Leave();
    }
    internal void Key(uint message, nint key, nint flags)
    {
        int vk = (int)key;
        nint target = keys.TryGetValue(vk, out var held) ? held.Target : keyboardTarget;
        if (message == 0x101)
        {
            if (!keys.Remove(vk)) return;
            if (!Post(target, message, key, flags)) Post(target, message, key, flags, true);
            return;
        }
        if (target == 0 || !tracker.CanPost(target, false) || !Native.IsWindowEnabled(target)) return;
        if (Post(target, message, key, flags) && message == 0x100) keys[vk] = (target, flags);
    }
    internal void ReleaseKeys()
    {
        foreach (var pair in keys)
        {
            uint flags = unchecked((uint)(long)pair.Value.Flags);
            flags = (flags & 0x01FF0000u) | 0xC0000001u;
            Post(pair.Value.Target, 0x101, (nint)pair.Key, (nint)unchecked((int)flags), true);
        }
        keys.Clear();
    }
    internal void Cancel() { ReleasePointer(); ReleaseKeys(); keyboardTarget = 0; }
}
