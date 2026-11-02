using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PanelMirror;

internal sealed class MirrorForm : Form
{
    internal SourceWindow Source { get; }
    private readonly Binding binding;
    private readonly InputForwarder input;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly Label caption = new() { AutoSize = false, Height = 30, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label note = new() { Height = 36, Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleLeft };
    private nint thumbnail;
    private Rectangle sourceRect, destination;
    private Size sourceSize;
    private bool available, sourceAlive, closing;
    private string? inputError;
    internal string DisplayStatus { get; private set; } = "正在打开镜像";
    internal event Action<Size>? SizeRemembered;
    private Size? rememberedSize;

    internal MirrorForm(SourceWindow source, Binding binding, int slot, string returnHint, Action<string> report)
    {
        Source = source; this.binding = binding;
        input = new InputForwarder(source, text => { inputError = text; note.Text = "  " + text; report(text); });
        Text = $"面板镜像 {slot} · {binding.Title}";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(28, 28, 30); ForeColor = Color.WhiteSmoke;
        TopMost = true; ShowInTaskbar = false; KeyPreview = true;
        MinimumSize = new Size(280, 180); ClientSize = new Size(660, 460);
        caption.Text = $"  槽位 {slot} · {returnHint} · 原面板保持原位";
        note.Text = "  输入转发原型；笔输入按鼠标处理";
        Controls.Add(caption); Controls.Add(note);
        SetStyle(ControlStyles.StandardDoubleClick, true);
        timer.Tick += (_, _) => RefreshThumbnail();
        Resize += (_, _) => { CancelInput(); RefreshThumbnail(); };
        DpiChanged += (_, _) => { CancelInput(); BeginInvoke((Action)RefreshThumbnail); };
        Deactivate += (_, _) => CancelInput();
        ResizeEnd += (_, _) => RememberSize();
        FormClosing += (_, _) => { CancelInput(); closing = true; timer.Stop(); if (rememberedSize is { } size) SizeRemembered?.Invoke(size); };
        FormClosed += (_, _) => { timer.Dispose(); UnregisterThumbnail(); };
    }
    protected override bool ShowWithoutActivation => true;

    internal void Summon()
    {
        CancelInput(); inputError = null;
        if (!Native.GetCursorPos(out var p)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var work = Screen.FromPoint(new Point(p.X, p.Y)).WorkingArea;
        if (!Visible)
        {
            // Let WinForms establish this window's target-monitor DPI before
            // applying logical saved dimensions (avoid scaling them twice).
            StartPosition = FormStartPosition.Manual;
            Location = new Point(p.X, p.Y);
            Show();
            double dpi = DeviceDpi / 96.0;
            if (binding.MirrorWidth > 0 && binding.MirrorHeight > 0)
                ClientSize = new Size((int)Math.Round(binding.MirrorWidth * dpi), (int)Math.Round(binding.MirrorHeight * dpi));
            else if (Source.ReadGeometry(binding, out _, out var area))
            {
                double scale = Math.Min(1, Math.Min(work.Width * .7 / area.Width, work.Height * .7 / area.Height));
                ClientSize = new Size(Math.Max(280, (int)(area.Width * scale)), Math.Max(180, (int)(area.Height * scale) + caption.Height + note.Height));
            }
        }
        int width = Math.Min(Width, work.Width), height = Math.Min(Height, work.Height);
        int offset = Math.Max(12, (int)Math.Round(18.0 * DeviceDpi / 96));
        Bounds = new Rectangle(Math.Clamp(p.X + offset, work.Left, work.Right - width), Math.Clamp(p.Y + offset, work.Top, work.Bottom - height), width, height);
        if (!Visible) Show();
        if (thumbnail == 0)
        {
            int hr = Native.DwmRegisterThumbnail(Handle, Source.Handle, out thumbnail);
            if (hr < 0) { UnregisterThumbnail(); Marshal.ThrowExceptionForHR(hr); }
        }
        RefreshThumbnail(); timer.Start();
    }
    private void RememberSize()
    {
        if (WindowState != FormWindowState.Normal || !Visible) return;
        rememberedSize = new Size(Math.Clamp((int)Math.Round(ClientSize.Width * 96.0 / DeviceDpi), 1, 10000),
            Math.Clamp((int)Math.Round(ClientSize.Height * 96.0 / DeviceDpi), 1, 10000));
        SizeRemembered?.Invoke(rememberedSize.Value);
    }
    private void UnregisterThumbnail()
    {
        if (thumbnail != 0) { Native.DwmUnregisterThumbnail(thumbnail); thumbnail = 0; }
    }
    private void RefreshThumbnail()
    {
        if (closing || IsDisposed || thumbnail == 0) return;
        sourceAlive = Source.IsAlive();
        if (!sourceAlive) { Pause("源窗口已退出，请重新绑定", true); return; }
        if (!Native.IsWindowVisible(Source.Handle) || Native.IsIconic(Source.Handle)) { Pause("源窗口已隐藏／最小化"); return; }
        if (!Native.IsWindowEnabled(Source.Handle)) { Pause("源窗口暂不可操作，请先处理原窗口中的对话框"); return; }
        if (!Source.ReadGeometry(binding, out var size, out var area)) { Pause("裁剪区域超出源客户区，请重新配置"); return; }
        if (sourceRect != area || sourceSize != size) CancelInput();
        sourceRect = area; sourceSize = size;
        var space = Rectangle.FromLTRB(0, caption.Bottom, ClientSize.Width, note.Top);
        if (space.Width <= 0 || space.Height <= 0) { Pause("镜像窗口太小"); return; }
        double scale = Math.Min((double)space.Width / area.Width, (double)space.Height / area.Height);
        int w = Math.Max(1, (int)(area.Width * scale)), h = Math.Max(1, (int)(area.Height * scale));
        var nextDestination = new Rectangle(space.Left + (space.Width - w) / 2, space.Top + (space.Height - h) / 2, w, h);
        if (destination != nextDestination) CancelInput();
        destination = nextDestination;
        var props = new Native.ThumbnailProperties { Flags = 0x1F, Source = new Native.RECT(area), Destination = new Native.RECT(destination), Opacity = 255, Visible = true, ClientOnly = true };
        int result = Native.DwmUpdateThumbnailProperties(thumbnail, ref props);
        if (result < 0) { Pause($"镜像更新失败：0x{result:X8}"); return; }
        available = true;
        DisplayStatus = inputError is null ? "镜像已打开 · 输入接受情况需手动确认" : inputError;
        note.Text = inputError is null ? "  实时镜像 · 笔按鼠标处理 · 不移动系统指针" : "  " + inputError;
    }
    private void Pause(string reason, bool destroyed = false)
    {
        CancelInput(); available = false;
        var props = new Native.ThumbnailProperties { Flags = 8, Visible = false };
        if (thumbnail != 0) Native.DwmUpdateThumbnailProperties(thumbnail, ref props);
        note.Text = "  " + reason;
        DisplayStatus = "镜像暂停：" + reason;
        if (destroyed) { timer.Stop(); UnregisterThumbnail(); }
    }
    private bool CanForward()
    {
        if (!available || !sourceAlive || closing) return false;
        if (!Source.Owns(Source.Handle) || !Native.IsWindowVisible(Source.Handle) || Native.IsIconic(Source.Handle) ||
            !Native.IsWindowEnabled(Source.Handle) || !Source.ReadGeometry(binding, out var size, out var area) ||
            size != sourceSize || area != sourceRect)
        { Pause("源窗口状态或尺寸已改变，等待更新画面"); return false; }
        return true;
    }
    private bool Map(Point point, bool outside, out Native.POINT screen)
    {
        screen = default;
        if (!CanForward() || (!outside && !destination.Contains(point)) || destination.Width == 0 || destination.Height == 0) return false;
        screen = new Native.POINT(sourceRect.X + (int)Math.Floor((double)(point.X - destination.X) * sourceRect.Width / destination.Width),
            sourceRect.Y + (int)Math.Floor((double)(point.Y - destination.Y) * sourceRect.Height / destination.Height));
        return Native.ClientToScreen(Source.Handle, ref screen);
    }
    private void CancelInput()
    {
        input?.Cancel();
        if (Capture) Capture = false;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Map(e.Location, input.IsDragging, out var screen) && input.Down(e.Button, e.Clicks, screen)) Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Map(e.Location, input.IsDragging, out var screen)) input.Move(screen);
        else if (!input.IsDragging) input.Leave();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e); if (!input.IsDragging) input.Leave();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Native.POINT? screen = Map(e.Location, true, out var point) ? point : null;
        input.Up(e.Button, screen);
        if (!input.IsDragging) Capture = false;
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Map(e.Location, false, out var screen)) input.Wheel(screen, e.Delta, false);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e); if (!Capture) input?.ReleasePointer();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && !binding.Keyboard) { Close(); return true; }
        if (binding.Keyboard && CanForward() && msg.Msg == 0x100) return false;
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override bool IsInputKey(Keys keyData) => binding.Keyboard || base.IsInputKey(keyData);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x20E && input is not null)
        {
            long coordinates = (long)m.LParam;
            var point = PointToClient(new Point(unchecked((short)(coordinates & 0xFFFF)), unchecked((short)((coordinates >> 16) & 0xFFFF))));
            if (Map(point, false, out var screen)) input.Wheel(screen, unchecked((short)(((long)m.WParam >> 16) & 0xFFFF)), true);
            return;
        }
        if (binding is not null && binding.Keyboard && input is not null &&
            (m.Msg == 0x100 || m.Msg == 0x101 || m.Msg == 0x102) && CanForward())
        {
            input.Key((uint)m.Msg, m.WParam, m.LParam); return;
        }
        base.WndProc(ref m);
    }
}
