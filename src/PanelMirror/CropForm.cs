namespace PanelMirror;

// DWM displays the existing window. No screenshot files, source activation,
// pointer movement or input forwarding are used by the crop editor.
internal sealed class CropForm : Form
{
    private readonly SourceWindow source;
    private readonly Panel preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 25, 28) };
    private readonly Label description = new() { Dock = DockStyle.Top, Height = 48 };
    private readonly Button accept = new() { Text = UiText.T("使用此区域"), AutoSize = true, Enabled = false };
    // A separate owned transparent window keeps the selection above the DWM
    // thumbnail. Ordinary client painting can be covered by DWM composition.
    private readonly CropOutline outline = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Size sourceSize;
    private nint thumbnail;
    private Rectangle destination;
    private Point anchor;
    private bool dragging;
    private bool previewReady;
    internal Rectangle Selection { get; private set; }

    internal CropForm(SourceWindow source, Rectangle initial)
    {
        this.source = source;
        if (!source.IsAlive() || !source.ReadGeometry(new Binding(), out sourceSize, out _))
            throw new InvalidOperationException(UiText.T("无法读取源窗口客户区。"));
        Selection = new Rectangle(Point.Empty, sourceSize).Contains(initial) && initial.Width > 0 && initial.Height > 0
            ? initial : new Rectangle(Point.Empty, sourceSize);
        Text = UiText.T("可视化裁剪 · 在预览中拖框选择区域");
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(900, 650); MinimumSize = new Size(500, 360);
        StartPosition = FormStartPosition.CenterParent;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, AutoSize = true };
        var reset = new Button { Text = UiText.T("整个客户区"), AutoSize = true };
        var cancel = new Button { Text = UiText.T("取消"), AutoSize = true, DialogResult = DialogResult.Cancel };
        reset.Click += (_, _) => { Selection = new Rectangle(Point.Empty, sourceSize); UpdatePreview(); };
        accept.Click += (_, _) =>
        {
            if (previewReady && Selection.Width > 0 && Selection.Height > 0 && ValidSource())
            { DialogResult = DialogResult.OK; Close(); }
        };
        actions.Controls.AddRange(new Control[] { accept, reset, cancel });
        Controls.Add(preview); Controls.Add(description); Controls.Add(actions);
        CancelButton = cancel;
        preview.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !previewReady || !destination.Contains(e.Location) || !ValidSource()) return;
            anchor = ToSource(e.Location); dragging = true; preview.Capture = true;
            Selection = Rectangle.Empty; UpdateEdges();
        };
        preview.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = ToSource(e.Location);
            Selection = Rectangle.FromLTRB(Math.Min(anchor.X, p.X), Math.Min(anchor.Y, p.Y), Math.Max(anchor.X, p.X), Math.Max(anchor.Y, p.Y));
            UpdateEdges();
        };
        preview.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { dragging = false; preview.Capture = false; UpdateEdges(); } };
        preview.MouseCaptureChanged += (_, _) => { if (!preview.Capture) dragging = false; };
        Resize += (_, _) => { dragging = false; preview.Capture = false; UpdatePreview(); };
        Move += (_, _) => UpdatePreview();
        DpiChanged += (_, _) => BeginInvoke((Action)UpdatePreview);
        Shown += (_, _) =>
        {
            int hr = Native.DwmRegisterThumbnail(Handle, source.Handle, out thumbnail);
            if (hr < 0) { description.Text = UiText.F("无法打开预览（0x{0:X8}），请取消后使用数字裁剪。", hr); return; }
            UpdatePreview(); timer.Start();
        };
        timer.Tick += (_, _) => UpdatePreview();
        FormClosed += (_, _) => { timer.Dispose(); outline.Dispose(); if (thumbnail != 0) Native.DwmUnregisterThumbnail(thumbnail); };
    }

    private bool ValidSource()
    {
        bool valid = source.IsAlive() && Native.IsWindowVisible(source.Handle) && !Native.IsIconic(source.Handle) &&
            source.ReadGeometry(new Binding(), out var size, out _) && size == sourceSize;
        if (!valid)
        {
            previewReady = false; accept.Enabled = false; dragging = false; preview.Capture = false;
            outline.Hide();
            description.Text = UiText.T("源窗口已隐藏、退出或改变尺寸。请取消并重新打开裁剪。");
        }
        return valid;
    }

    private Point ToSource(Point p) => new(
        Math.Clamp((int)Math.Round((p.X - destination.X) * (double)sourceSize.Width / destination.Width), 0, sourceSize.Width),
        Math.Clamp((int)Math.Round((p.Y - destination.Y) * (double)sourceSize.Height / destination.Height), 0, sourceSize.Height));

    private void UpdatePreview()
    {
        if (thumbnail == 0 || IsDisposed) return;
        if (!Visible || WindowState == FormWindowState.Minimized) { outline.Hide(); return; }
        if (!ValidSource())
        {
            var hidden = new Native.ThumbnailProperties { Flags = 8, Visible = false };
            Native.DwmUpdateThumbnailProperties(thumbnail, ref hidden); return;
        }
        double scale = Math.Min((double)Math.Max(1, preview.Width) / sourceSize.Width, (double)Math.Max(1, preview.Height) / sourceSize.Height);
        var size = new Size(Math.Max(1, (int)(sourceSize.Width * scale)), Math.Max(1, (int)(sourceSize.Height * scale)));
        destination = new Rectangle((preview.Width - size.Width) / 2, (preview.Height - size.Height) / 2, size.Width, size.Height);
        var origin = PointToClient(preview.PointToScreen(destination.Location));
        var props = new Native.ThumbnailProperties { Flags = 0x1F, Source = new Native.RECT(new Rectangle(Point.Empty, sourceSize)),
            Destination = new Native.RECT(new Rectangle(origin, destination.Size)), Opacity = 255, Visible = true, ClientOnly = true };
        int hr = Native.DwmUpdateThumbnailProperties(thumbnail, ref props);
        previewReady = hr >= 0;
        if (!previewReady)
        {
            accept.Enabled = false; dragging = false; preview.Capture = false; outline.Hide();
            description.Text = UiText.F("预览更新失败（0x{0:X8}）。请取消后重试。", hr); return;
        }
        UpdateEdges();
    }

    private void UpdateEdges()
    {
        bool valid = previewReady && Selection.Width > 0 && Selection.Height > 0 && ValidSource();
        accept.Enabled = valid;
        if (!valid) { outline.Hide(); return; }
        int left = destination.Left + (int)((long)Selection.Left * destination.Width / sourceSize.Width);
        int top = destination.Top + (int)((long)Selection.Top * destination.Height / sourceSize.Height);
        int right = destination.Left + (int)((long)Selection.Right * destination.Width / sourceSize.Width);
        int bottom = destination.Top + (int)((long)Selection.Bottom * destination.Height / sourceSize.Height);
        outline.Bounds = preview.RectangleToScreen(preview.ClientRectangle);
        outline.Selection = Rectangle.FromLTRB(left, top, Math.Max(left + 2, right - 1), Math.Max(top + 2, bottom - 1));
        if (!outline.Visible) outline.Show(this);
        outline.Invalidate();
        description.Text = UiText.F("拖框选择需要召回的面板；此处不向源窗口发送操作。\nX {0} · Y {1} · 宽 {2} · 高 {3}；确认后仍需保存绑定。", Selection.X, Selection.Y, Selection.Width, Selection.Height);
    }

    private sealed class CropOutline : Form
    {
        internal Rectangle Selection;
        internal CropOutline()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x20; // NOACTIVATE | TRANSPARENT: input stays in the crop editor.
                return value;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.Lime, 2);
            e.Graphics.DrawRectangle(pen, Selection);
        }
        protected override void WndProc(ref Message message)
        {
            // Hit-test transparency keeps the outline enabled (required by
            // WinForms Show) while all selection input reaches the editor.
            if (message.Msg == 0x84) { message.Result = (nint)(-1); return; }
            base.WndProc(ref message);
        }
    }
}
