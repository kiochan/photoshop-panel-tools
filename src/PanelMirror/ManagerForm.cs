namespace PanelMirror;

internal sealed class ManagerForm : Form
{
    private Settings settings = new();
    private readonly ComboBox slot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 680 };
    private readonly NumericUpDown[] crop = Enumerable.Range(0, 4).Select(_ => new NumericUpDown { Minimum = 0, Maximum = 100000, Width = 90 }).ToArray();
    private readonly CheckBox keyboard = new() { Text = UiText.T("尝试转发键盘输入（实验性，需要先点击镜像中的控件）"), AutoSize = true };
    private readonly Label binding = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly MirrorForm?[] mirrors = new MirrorForm?[5];
    private readonly SourceWindow?[] sources = new SourceWindow?[5];
    private readonly HashSet<int> registered = new();
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 480 };
    private readonly HotkeyBox summonKey = new(), returnKey = new();
    private readonly CheckBox separateKeys = new() { Text = UiText.T("分别设置召唤键和放回键"), AutoSize = true };
    private readonly RealPanelController real = new();
    private readonly System.Windows.Forms.Timer moveTimer = new() { Interval = 60 };
    private bool closeAfterRestore, leaveRecovery, shuttingDown;
    private readonly System.Windows.Forms.Timer picker = new() { Interval = 100 };
    private long pickDeadline;
    private bool pickRegion, configurationReadable = true;
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Photoshop Panel Tools" };
    private readonly EventWaitHandle wake = new(false, EventResetMode.AutoReset, Program.WakeEvent);
    private readonly Label[] slotStates = Enumerable.Range(0, 5).Select(_ => new Label { AutoSize = true, MaximumSize = new Size(700, 0), Cursor = Cursors.Hand }).ToArray();
    private readonly string?[] slotErrors = new string?[5];
    private bool exitRequested, keysPaused, editingCrop;
    private long nextStatus;
    private string? realError;
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };

    public ManagerForm()
    {
        Text = UiText.T("Panel Mirror · 通用面板召回配置");
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 820); MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        Controls.Add(layout);
        var languages = new FlowLayoutPanel { AutoSize = true };
        languages.Controls.Add(new Label { Text = "Language / 语言", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        language.Items.AddRange(new object[] { "System / 跟随系统", "简体中文", "English" });
        languages.Controls.Add(language);
        languages.Controls.Add(Button(UiText.T("保存语言（重启生效）"), SaveLanguage));
        layout.Controls.Add(languages);
        layout.SizeChanged += (_, _) => FitLayout(layout);
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(UiText.T("打开配置"), null, (_, _) => ShowConfiguration());
        var pause = new ToolStripMenuItem(UiText.T("暂停快捷键")) { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => { keysPaused = pause.Checked; RegisterKeys(); UpdateSlotStates(); };
        trayMenu.Items.Add(pause);
        trayMenu.Items.Add(UiText.T("关闭所有镜像"), null, (_, _) => { for (int i = 0; i < 5; i++) HideMirror(i); UpdateSlotStates(); });
        trayMenu.Items.Add(UiText.T("放回真实面板"), null, (_, _) => RestoreReal());
        trayMenu.Items.Add(UiText.T("退出（先尝试放回）"), null, (_, _) => RequestExit(false));
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => ShowConfiguration();
        tray.Visible = true;
        layout.Controls.Add(new Label { Text = UiText.T("每个面板独立选择召回方式与快捷键；默认 Alt+1～5 按一次召唤，再按一次关闭／放回。"), AutoSize = true });
        layout.Controls.Add(new Label { Text = Compatibility.Targets, AutoSize = true });
        layout.Controls.Add(new Label { Text = UiText.T("槽位状态（点击切换配置）；关闭配置窗口后继续在系统托盘运行。"), AutoSize = true });
        for (int i = 0; i < slotStates.Length; i++)
        {
            int index = i;
            slotStates[i].Click += (_, _) => slot.SelectedIndex = index;
            layout.Controls.Add(slotStates[i]);
        }
        var row = new FlowLayoutPanel { AutoSize = true };
        slot.Items.AddRange(new object[] { UiText.T("槽位 1"), UiText.T("槽位 2"), UiText.T("槽位 3"), UiText.T("槽位 4"), UiText.T("槽位 5") });
        row.Controls.Add(slot);
        row.Controls.Add(Button(UiText.T("刷新窗口列表"), RefreshWindows));
        row.Controls.Add(Button(UiText.T("3 秒后拾取窗口"), () => StartPick(false)));
        row.Controls.Add(Button(UiText.T("3 秒后拾取控件区域"), () => StartPick(true)));
        layout.Controls.Add(row); layout.Controls.Add(windows);
        mode.Items.AddRange(new object[] { UiText.T("镜像＋输入转发（原面板保持原位）"), UiText.T("移动真实浮动面板（用完恢复原位置）") });
        mode.SelectedIndex = 0;
        mode.SelectedIndexChanged += (_, _) =>
        {
            bool mirrorMode = mode.SelectedIndex == (int)SummonMode.Mirror;
            foreach (var value in crop) value.Enabled = mirrorMode;
            keyboard.Enabled = mirrorMode;
        };
        layout.Controls.Add(mode);
        var cropButton = Button(UiText.T("预览并拖框裁剪…"), EditCrop);
        layout.Controls.Add(cropButton);
        mode.SelectedIndexChanged += (_, _) => cropButton.Enabled = mode.SelectedIndex == (int)SummonMode.Mirror;
        layout.Controls.Add(separateKeys);
        var shortcuts = new FlowLayoutPanel { AutoSize = true };
        shortcuts.Controls.Add(new Label { Text = UiText.T("召唤 / 切换"), AutoSize = true }); shortcuts.Controls.Add(summonKey);
        shortcuts.Controls.Add(new Label { Text = UiText.T("独立放回"), AutoSize = true }); shortcuts.Controls.Add(returnKey);
        shortcuts.Controls.Add(Button(UiText.T("恢复本槽默认键"), () =>
        {
            if (slot.SelectedIndex < 0) return;
            separateKeys.Checked = false; summonKey.Set(Hotkey.Default(slot.SelectedIndex)); returnKey.Set(Hotkey.Default(slot.SelectedIndex, true));
        }));
        layout.Controls.Add(shortcuts);
        layout.Controls.Add(new Label { Text = UiText.T("点击输入框后直接按组合键，Tab 切换输入框。检查后保存生效；录入时暂停本工具的全局快捷键。"), AutoSize = true });
        separateKeys.CheckedChanged += (_, _) => returnKey.Enabled = separateKeys.Checked;
        returnKey.Enabled = false;
        foreach (var box in new[] { summonKey, returnKey })
        {
            box.Rejected = Report;
            box.Enter += (_, _) => UnregisterKeys();
            box.Leave += (_, _) => BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) RegisterKeys(); }));
        }
        layout.Controls.Add(new Label { Text = UiText.T("每槽独立保存。移动模式需要先手动浮动，不使用裁剪／输入转发；修改绑定前须先放回。"), AutoSize = true });
        layout.Controls.Add(new Label { Text = UiText.T("可选择浮动面板；停靠面板请选择 Photoshop 主窗口，再填写客户区裁剪坐标。"), AutoSize = true });
        var crops = new FlowLayoutPanel { AutoSize = true };
        string[] names = { "X", "Y", UiText.T("宽"), UiText.T("高") };
        for (int i = 0; i < 4; i++) { crops.Controls.Add(new Label { Text = names[i], AutoSize = true, Padding = new Padding(0, 5, 0, 0) }); crops.Controls.Add(crop[i]); }
        layout.Controls.Add(crops);
        layout.Controls.Add(new Label { Text = UiText.T("坐标以源窗口客户区为基准，单位为像素；宽、高都为 0 表示整个客户区。"), AutoSize = true });
        layout.Controls.Add(keyboard);
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.Add(Button(UiText.T("绑定所选窗口并保存"), SaveBinding));
        actions.Controls.Add(Button(UiText.T("召回面板"), () => ShowMirror(slot.SelectedIndex)));
        actions.Controls.Add(Button(UiText.T("关闭 / 放回"), () => CloseSlot(slot.SelectedIndex)));
        actions.Controls.Add(Button(UiText.T("清除此槽"), ClearBinding));
        layout.Controls.Add(actions); layout.Controls.Add(binding); layout.Controls.Add(status);
        var recovery = new FlowLayoutPanel { AutoSize = true };
        recovery.Controls.Add(Button(UiText.T("恢复上次移动的真实面板"), RestoreReal));
        recovery.Controls.Add(Button(UiText.T("退出（先尝试放回）"), () => RequestExit(false)));
        recovery.Controls.Add(Button(UiText.T("仅退出，保留恢复记录"), () => RequestExit(true)));
        layout.Controls.Add(recovery);
        layout.Controls.Add(new Label { Text = UiText.T("原型边界：只镜像源客户区；外部菜单、弹窗不会自动跟随。\n源窗口隐藏、最小化或被对话框阻塞时暂停输入。后台消息是否被目标接受，仍需实测。\n数位笔目前按鼠标点击／拖动处理，不转发压感与倾斜。"), AutoSize = true, MaximumSize = new Size(700, 0) });
        slot.SelectedIndexChanged += (_, _) => LoadSlot();
        Shown += (_, _) =>
        {
            try { settings = Settings.Load(); }
            catch (Exception e) { configurationReadable = false; Report(UiText.T("读取配置失败，已禁止保存以保护原文件：") + e.Message); }
            language.SelectedIndex = settings.Language == "zh-CN" ? 1 : settings.Language == "en" ? 2 : 0;
            FitLayout(layout);
            RefreshWindows(); slot.SelectedIndex = 0; RegisterKeys();
            try { if (real.HasRecovery) Report(UiText.T("发现未处理的真实面板恢复记录；请先点击恢复。镜像模式仍可使用。")); }
            catch (Exception e) { Report(UiText.T("恢复记录读取失败，移动模式已阻止新召回：") + e.Message); }
            moveTimer.Start();
            UpdateSlotStates();
        };
        picker.Tick += (_, _) => FinishPick();
        moveTimer.Tick += (_, _) =>
        {
            if (wake.WaitOne(0)) ShowConfiguration();
            if (Environment.TickCount64 >= nextStatus) { nextStatus = Environment.TickCount64 + 1000; UpdateSlotStates(); }
            try
            {
                string? message = real.Tick();
                if (!real.Busy) realError = null;
                if (message is not null) Report(real.Present ? UiText.T("面板已召回；") + ReturnHint(real.ActiveSlot) : message);
                if (closeAfterRestore && !real.Busy) { closeAfterRestore = false; Close(); }
            }
            catch (Exception e) { closeAfterRestore = false; realError = e.Message; NotifyFailure(UiText.T("真实面板处理失败，恢复记录已保留：") + e.Message); }
        };
        FormClosing += (_, e) =>
        {
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
            { e.Cancel = true; CancelPick(); Hide(); return; }
            if (!leaveRecovery && real.Busy)
            {
                e.Cancel = true;
                try { real.Restore(); closeAfterRestore = real.Busy; }
                catch (Exception error) { closeAfterRestore = false; realError = error.Message; NotifyFailure(UiText.T("放回失败，可重试或仅退出保留记录：") + error.Message); }
                if (!real.Busy) BeginInvoke((Action)Close);
                return;
            }
            shuttingDown = true; picker.Stop(); moveTimer.Stop();
            for (int i = 0; i < 5; i++) HideMirror(i);
            foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
        };
        FormClosed += (_, _) => { tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); wake.Dispose(); picker.Dispose(); moveTimer.Dispose(); real.Dispose(); };
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action(); return button;
    }
    private void Report(string text) => status.Text = text;
    private static void FitLayout(FlowLayoutPanel layout)
    {
        int width = Math.Max(200, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        foreach (Control child in layout.Controls)
            if (child is Label || child is FlowLayoutPanel) child.MaximumSize = new Size(width, 0);
    }
    private void SaveLanguage()
    {
        if (!configurationReadable || language.SelectedIndex < 0) return;
        string previous = settings.Language;
        settings.Language = new[] { "auto", "zh-CN", "en" }[language.SelectedIndex];
        try { settings.Save(); Report(UiText.T("语言已保存；退出并重新打开工具后生效。")); }
        catch (Exception e) { settings.Language = previous; Report(UiText.T("保存失败：") + e.Message); }
    }
    private void ShowConfiguration()
    {
        Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }
    private void RequestExit(bool preserveRecovery)
    { exitRequested = true; leaveRecovery = preserveRecovery; Close(); }
    private void RestoreReal()
    {
        try { real.Restore(); realError = null; }
        catch (Exception e) { realError = e.Message; NotifyFailure(UiText.T("无法恢复真实面板：") + e.Message); }
        UpdateSlotStates();
    }
    private void NotifyFailure(string text)
    {
        Report(text);
        tray.BalloonTipTitle = UiText.T("Panel Tools · 操作未完成");
        tray.BalloonTipText = text.Length > 240 ? text[..240] : text;
        tray.ShowBalloonTip(4000);
    }
    private void EditCrop()
    {
        if (mode.SelectedIndex != (int)SummonMode.Mirror) return;
        if (windows.SelectedItem is not WindowItem selected) { Report(UiText.T("请先选择或拾取源窗口。")); return; }
        editingCrop = true; UnregisterKeys();
        string? feedback = null;
        try
        {
            using var editor = new CropForm(new SourceWindow(selected), new Rectangle((int)crop[0].Value, (int)crop[1].Value, (int)crop[2].Value, (int)crop[3].Value));
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            var area = editor.Selection;
            if (area.Right > 100000 || area.Bottom > 100000) throw new InvalidOperationException(UiText.T("区域超出配置范围。"));
            int[] values = { area.X, area.Y, area.Width, area.Height };
            for (int i = 0; i < values.Length; i++) crop[i].Value = values[i];
            feedback = UiText.T("裁剪区域已填入；请点击“绑定所选窗口并保存”生效。");
        }
        catch (Exception e) { feedback = UiText.T("无法编辑裁剪：") + e.Message; }
        finally
        {
            editingCrop = false; RegisterKeys();
            if (feedback is not null) Report(feedback);
        }
    }
    private void RememberMirrorSize(int index, Binding owner, Size size)
    {
        // A closing mirror must never write into a newly replaced binding.
        if (!configurationReadable || !ReferenceEquals(settings.Slots[index], owner)) return;
        if (owner.MirrorWidth == size.Width && owner.MirrorHeight == size.Height) return;
        int oldWidth = owner.MirrorWidth, oldHeight = owner.MirrorHeight;
        owner.MirrorWidth = size.Width; owner.MirrorHeight = size.Height;
        try { settings.Save(); }
        catch (Exception e) { owner.MirrorWidth = oldWidth; owner.MirrorHeight = oldHeight; Report(UiText.T("镜像尺寸保存失败：") + e.Message); }
    }
    private void UpdateSlotStates()
    {
        try
        {
            var candidates = Native.Windows();
            PanelRecall.ReturnRecord? recovery = null;
            bool recoveryUnreadable = false;
            try { recovery = PanelRecall.Storage.Read<PanelRecall.ReturnRecord>("return.json"); }
            catch { recoveryUnreadable = true; }
            for (int i = 0; i < 5; i++)
            {
                var b = settings.Slots[i];
                string state;
                if (!configurationReadable) state = UiText.T("配置读取失败 · 已保护原文件");
                else if (!b.Configured) state = UiText.T("未绑定");
                else if (real.ActiveSlot == i) state = realError is not null ? UiText.T("移动/放回失败 · 恢复记录保留：") + realError : real.Present ? UiText.T("真实面板已召回") : UiText.T("正在处理真实面板 · 恢复记录保留");
                else if (recovery?.Slot == i) state = UiText.T("有待处理的恢复记录");
                else if (slotErrors[i] is { } error) state = error;
                else if (mirrors[i] is { IsDisposed: false, Visible: true } mirror) state = mirror.DisplayStatus;
                else
                {
                    var live = sources[i];
                    bool alive = live is not null && live.IsAlive();
                    var matches = alive ? new[] { live!.Identity } : candidates.Where(w => w.Matches(b)).ToArray();
                    state = matches.Length == 0 ? UiText.T("源窗口离线或标题已改变") : matches.Length > 1 ? UiText.T("匹配不唯一 · 请重新绑定") :
                        !Native.IsWindowVisible(matches[0].Handle) || Native.IsIconic(matches[0].Handle) ? UiText.T("源窗口已隐藏/最小化") : UiText.T("待召回");
                }
                if (b.Configured)
                {
                    bool complete = registered.Contains(100 + i * 2) && (!b.SeparateReturnKey || registered.Contains(101 + i * 2));
                    state += keysPaused ? UiText.T(" · 快捷键已暂停") : editingCrop || summonKey.ContainsFocus || returnKey.ContainsFocus ? UiText.T(" · 快捷键录入/裁剪中暂停") : !complete ? UiText.T(" · 快捷键未注册或冲突") : " · " + b.SummonShortcut(i);
                    if (b.Mode == SummonMode.Move && recoveryUnreadable) state += UiText.T(" · 恢复记录不可读，禁止新移动");
                }
                slotStates[i].Text = $"{i + 1} · {(b.Configured ? b.Title : UiText.T("空槽位"))} · {state}";
            }
        }
        catch (Exception e) { Report(UiText.T("刷新槽位状态失败：") + e.Message); }
    }
    private void StartPick(bool region)
    {
        pickRegion = region; pickDeadline = Environment.TickCount64 + 3000;
        slot.Enabled = false; picker.Start();
        Report(UiText.T("请将指针移到目标面板；3 秒后只读取窗口，不点击、不移动原面板。Esc 取消。拾取后需手动保存。"));
    }
    private void CancelPick()
    { picker.Stop(); slot.Enabled = true; }
    private void FinishPick()
    {
        if (Environment.TickCount64 < pickDeadline) return;
        CancelPick();
        try
        {
            if (!Native.GetCursorPos(out var point)) throw new InvalidOperationException(UiText.T("无法读取指针位置。"));
            nint leaf = Native.WindowFromPoint(point), root = Native.GetAncestor(leaf, 2);
            if (leaf == 0 || root == 0 || Native.Describe(root) is not { } item ||
                !Native.IsWindowVisible(root) || Native.IsIconic(root))
                throw new InvalidOperationException(UiText.T("未找到可用的源顶层窗口，请避开本工具和镜像窗口。"));
            Rectangle area = Rectangle.Empty;
            if (pickRegion)
            {
                if (!Native.GetClientRect(leaf, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
                    throw new InvalidOperationException(UiText.T("控件没有可用客户区，请改用拾取窗口并手动裁剪。"));
                var origin = new Native.POINT(0, 0);
                if (!Native.ClientToScreen(leaf, ref origin) || !Native.ScreenToClient(root, ref origin) ||
                    !Native.GetClientRect(root, out var client)) throw new InvalidOperationException(UiText.T("无法转换控件区域。"));
                area = new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
                if (!new Rectangle(0, 0, client.Right, client.Bottom).Contains(area) ||
                    area.X < 0 || area.Y < 0 || area.Right > 100000 || area.Bottom > 100000)
                    throw new InvalidOperationException(UiText.T("控件区域超出源客户区，请手动裁剪。"));
            }
            RefreshWindows();
            windows.SelectedItem = windows.Items.Cast<WindowItem>().SingleOrDefault(w => w.Handle == item.Handle && w.ProcessId == item.ProcessId && w.StartedAt == item.StartedAt);
            if (windows.SelectedItem is null) throw new InvalidOperationException(UiText.T("源窗口在拾取时已变化，请重新拾取。"));
            int[] values = { area.X, area.Y, area.Width, area.Height };
            for (int i = 0; i < 4; i++) crop[i].Value = values[i];
            Report(pickRegion ? UiText.T("已填入指针命中控件的客户区；可能只是一枚按钮，请检查并调整裁剪后保存。原布局变化后需重新拾取。") : UiText.T("已选中源顶层窗口，裁剪已重置；请检查后绑定保存。"));
        }
        catch (Exception e) { Report(UiText.T("拾取失败：") + e.Message); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (picker.Enabled && keyData == Keys.Escape) { CancelPick(); Report(UiText.T("已取消拾取。")); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void RefreshWindows()
    {
        windows.DataSource = Native.Windows();
        if (slot.SelectedIndex >= 0) SelectBoundWindow();
    }
    private void SelectBoundWindow()
    {
        var b = settings.Slots[slot.SelectedIndex];
        if (sources[slot.SelectedIndex] is { } live && live.IsAlive())
        {
            windows.SelectedItem = windows.Items.Cast<WindowItem>().SingleOrDefault(x => x.Handle == live.Handle);
            return;
        }
        var items = windows.Items.Cast<WindowItem>().Where(x => x.Matches(b)).ToArray();
        windows.SelectedItem = items.Length == 1 ? items[0] : null;
    }
    private void LoadSlot()
    {
        var b = settings.Slots[slot.SelectedIndex];
        int[] values = { b.X, b.Y, b.Width, b.Height };
        for (int i = 0; i < 4; i++) crop[i].Value = Math.Clamp(values[i], 0, 100000);
        keyboard.Checked = b.Keyboard;
        separateKeys.Checked = b.SeparateReturnKey;
        summonKey.Set(b.SummonShortcut(slot.SelectedIndex)); returnKey.Set(b.ReturnShortcut(slot.SelectedIndex));
        mode.SelectedIndex = (int)b.Mode;
        binding.Text = b.Configured ? UiText.F("当前绑定：{0} · {1} · {2} · {3}", (b.Mode == SummonMode.Move ? UiText.T("移动真实面板") : UiText.T("镜像＋转发")), Compatibility.Display(b.ProcessName, b.PhotoshopMajor), b.Title, b.WindowClass) : UiText.T("当前槽位尚未绑定。");
        SelectBoundWindow();
    }
    private void SaveBinding()
    {
        CancelPick();
        if (!configurationReadable) { Report(UiText.T("配置读取失败，禁止覆盖原文件；请先修复或备份配置后重新启动。")); return; }
        if (windows.SelectedItem is not WindowItem w) { Report(UiText.T("请从列表中选择明确的源窗口。")); return; }
        if (mode.SelectedIndex == (int)SummonMode.Mirror && (crop[2].Value == 0) != (crop[3].Value == 0)) { Report(UiText.T("宽、高需要同时填写，或同时为 0。")); return; }
        int i = slot.SelectedIndex;
        if (real.ActiveSlot == i) { Report(UiText.T("请先放回此槽位的真实面板，再修改绑定或切换方式。")); return; }
        var previous = settings.Slots[i];
        if (Native.Describe(w.Handle) is not { } current || current.ProcessId != w.ProcessId || current.StartedAt != w.StartedAt)
        { Report(UiText.T("列表中的源窗口已退出或改变，请刷新后重新选择。")); return; }
        w = current;
        if (mode.SelectedIndex == (int)SummonMode.Mirror && !new SourceWindow(w).ReadGeometry(new Binding {
            X = (int)crop[0].Value, Y = (int)crop[1].Value, Width = (int)crop[2].Value, Height = (int)crop[3].Value }, out _, out _))
        { Report(UiText.T("裁剪区域超出源窗口客户区，请重新预览裁剪后保存。")); return; }
        if (mode.SelectedIndex == (int)SummonMode.Move && !PanelRecall.Native.IsFloating(w.Handle))
        { Report(UiText.T("移动模式只能绑定真实浮动面板。停靠面板可选择镜像方式，或先手动拖成浮动窗口。")); return; }
        settings.Slots[i] = new Binding { Mode = (SummonMode)mode.SelectedIndex, ProcessName = w.ProcessName, WindowClass = w.WindowClass, Title = w.Title, PhotoshopMajor = w.PhotoshopMajor,
            SeparateReturnKey = separateKeys.Checked, SummonKey = summonKey.Value.Copy(), ReturnKey = returnKey.Value.Copy(),
            X = (int)crop[0].Value, Y = (int)crop[1].Value,
            Width = mode.SelectedIndex == (int)SummonMode.Move ? 0 : (int)crop[2].Value,
            Height = mode.SelectedIndex == (int)SummonMode.Move ? 0 : (int)crop[3].Value, Keyboard = keyboard.Checked,
            MirrorWidth = previous.MirrorWidth, MirrorHeight = previous.MirrorHeight };
        var conflicts = ShortcutConflicts();
        if (conflicts.Count > 0) { settings.Slots[i] = previous; Report(UiText.T("未保存：") + string.Join("、", conflicts)); return; }
        try { settings.Save(); }
        catch (Exception e) { settings.Slots[i] = previous; Report(UiText.T("保存失败：") + e.Message); return; }
        HideMirror(i); slotErrors[i] = null; sources[i] = new SourceWindow(w); LoadSlot(); RegisterKeys(); UpdateSlotStates();
    }
    private void ClearBinding()
    {
        CancelPick();
        if (!configurationReadable) { Report(UiText.T("配置读取失败，禁止覆盖原文件；请先修复或备份配置后重新启动。")); return; }
        int i = slot.SelectedIndex;
        if (real.ActiveSlot == i) { Report(UiText.T("请先放回此槽位的真实面板，再清除绑定。")); return; }
        var previous = settings.Slots[i]; settings.Slots[i] = new();
        try { settings.Save(); }
        catch (Exception e) { settings.Slots[i] = previous; Report(UiText.T("保存失败：") + e.Message); return; }
        HideMirror(i); slotErrors[i] = null; sources[i] = null; LoadSlot(); RegisterKeys(); UpdateSlotStates();
    }
    private void UnregisterKeys()
    {
        foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
        registered.Clear();
    }
    private List<string> ShortcutConflicts()
    {
        var used = new Dictionary<(uint, int), string>();
        var failures = new List<string>();
        for (int i = 0; i < settings.Slots.Length; i++)
        {
            var b = settings.Slots[i]; if (!b.Configured) continue;
            for (int close = 0; close < (b.SeparateReturnKey ? 2 : 1); close++)
            {
                var key = close == 0 ? b.SummonShortcut(i) : b.ReturnShortcut(i);
                string name = UiText.F("槽位 {0} {1}", i + 1, (close == 0 ? UiText.T("召唤／切换") : UiText.T("放回")));
                if (!key.Valid) { failures.Add(name + UiText.T("快捷键无效")); continue; }
                if (used.TryGetValue((key.Modifiers, key.Key), out var previous)) failures.Add(UiText.F("{0} 与 {1} 重复使用 {2}；相同键请使用单键切换", previous, name, key));
                else used.Add((key.Modifiers, key.Key), name);
            }
        }
        return failures;
    }
    private void RegisterKeys()
    {
        UnregisterKeys();
        if (shuttingDown) return;
        if (keysPaused || editingCrop) { Report(keysPaused ? UiText.T("全局快捷键已暂停；可从托盘恢复。") : UiText.T("裁剪时暂停全局快捷键。")); return; }
        if (summonKey.ContainsFocus || returnKey.ContainsFocus) return;
        var failures = ShortcutConflicts();
        if (failures.Count > 0) { Report(UiText.T("快捷键未注册：") + string.Join("、", failures)); return; }
        for (int i = 0; i < 5; i++)
        {
            if (!settings.Slots[i].Configured) continue;
            var b = settings.Slots[i];
            for (int close = 0; close < (b.SeparateReturnKey ? 2 : 1); close++)
            {
                int id = 100 + i * 2 + close;
                var key = close == 0 ? b.SummonShortcut(i) : b.ReturnShortcut(i);
                if (Native.RegisterHotKey(Handle, id, 0x4000 | key.Modifiers, (uint)key.Key)) registered.Add(id);
                else failures.Add(UiText.F("槽位 {0} {1}（错误 {2}）", i + 1, key, System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
            }
        }
        Report(!configurationReadable ? UiText.T("配置读取失败，未注册有效绑定并禁止覆盖原文件。") : failures.Count == 0 ? UiText.T("配置已就绪；仅已绑定槽位注册全局快捷键。") : UiText.T("快捷键注册失败：") + string.Join("、", failures));
    }
    private SourceWindow Resolve(int index)
    {
        var b = settings.Slots[index];
        if (sources[index] is { } live && live.IsAlive()) return live;
        var matches = Native.Windows().Where(w => w.Matches(b)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? UiText.T("找不到源窗口。标题可能已改变，请重新绑定。") : UiText.T("存在多个同名同类窗口，请从列表中重新绑定具体窗口。"));
        return sources[index] = new SourceWindow(matches[0]);
    }
    private void ShowMirror(int index)
    {
        if (index < 0 || !settings.Slots[index].Configured) { Report(UiText.T("请先绑定此槽位。")); return; }
        try
        {
            slotErrors[index] = null;
            SourceWindow source = Resolve(index);
            if (!Native.IsWindowVisible(source.Handle) || Native.IsIconic(source.Handle)) throw new InvalidOperationException(UiText.T("源窗口需要保持显示且未最小化。"));
            if (settings.Slots[index].Mode == SummonMode.Move)
            {
                // A window can have been bound to several slots. Never leave
                // an interactive mirror of that same window running as it moves.
                for (int i = 0; i < mirrors.Length; i++)
                    if (mirrors[i]?.Source.Handle == source.Handle) HideMirror(i);
                HideMirror(index); real.Summon(source, index); realError = null;
                Report(UiText.T("正在召回真实浮动面板并确认位置；") + ReturnHint(index)); return;
            }
            if (real.Moves(source)) throw new InvalidOperationException(UiText.T("此源窗口正在移动模式中使用，请先放回，再打开它的镜像。"));
            if (mirrors[index] is { IsDisposed: false } existing && ReferenceEquals(existing.Source, source)) { existing.Summon(); return; }
            HideMirror(index);
            var owner = settings.Slots[index];
            var mirror = new MirrorForm(source, owner, index + 1, ReturnHint(index), Report);
            mirror.SizeRemembered += size => RememberMirrorSize(index, owner, size);
            mirrors[index] = mirror;
            mirror.Summon();
        }
        catch (Exception e) { HideMirror(index); slotErrors[index] = UiText.T("召回失败：") + e.Message; NotifyFailure(slotErrors[index]!); }
        finally { UpdateSlotStates(); }
    }
    private void CloseSlot(int index)
    {
        if (index < 0) return;
        HideMirror(index);
        try { bool relevant = real.ActiveSlot == index; real.Restore(index); slotErrors[index] = null; if (relevant) realError = null; }
        catch (Exception e) { realError = e.Message; slotErrors[index] = UiText.T("放回失败：") + e.Message; NotifyFailure(UiText.T("无法放回真实面板，恢复记录已保留：") + e.Message); }
        UpdateSlotStates();
    }
    private string ReturnHint(int index)
    {
        var b = settings.Slots[index];
        return b.SeparateReturnKey ? UiText.F("{0} 放回／关闭", b.ReturnShortcut(index)) : UiText.F("再按 {0} 放回／关闭", b.SummonShortcut(index));
    }
    private void ToggleSlot(int index)
    {
        if (real.ActiveSlot == index || mirrors[index] is { IsDisposed: false, Visible: true }) CloseSlot(index);
        else ShowMirror(index);
    }
    private void HideMirror(int index)
    {
        if (index < 0) return;
        mirrors[index]?.Close(); mirrors[index] = null;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            int id = (int)m.WParam;
            if (registered.Contains(id))
            {
                int i = (id - 100) / 2;
                if ((id - 100) % 2 != 0) CloseSlot(i);
                else if (settings.Slots[i].SeparateReturnKey) ShowMirror(i);
                else ToggleSlot(i);
                return;
            }
        }
        base.WndProc(ref m);
    }
}
