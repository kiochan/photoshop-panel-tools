namespace PanelMirror;

internal sealed class ManagerForm : Form
{
    private Settings settings = new();
    private readonly ComboBox slot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 680 };
    private readonly NumericUpDown[] crop = Enumerable.Range(0, 4).Select(_ => new NumericUpDown { Minimum = 0, Maximum = 100000, Width = 90 }).ToArray();
    private readonly CheckBox keyboard = new() { Text = "尝试转发键盘输入（实验性，需要先点击镜像中的控件）", AutoSize = true };
    private readonly Label binding = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly MirrorForm?[] mirrors = new MirrorForm?[5];
    private readonly SourceWindow?[] sources = new SourceWindow?[5];
    private readonly HashSet<int> registered = new();
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 480 };
    private readonly HotkeyBox summonKey = new(), returnKey = new();
    private readonly CheckBox separateKeys = new() { Text = "分别设置召唤键和放回键", AutoSize = true };
    private readonly RealPanelController real = new();
    private readonly System.Windows.Forms.Timer moveTimer = new() { Interval = 60 };
    private bool closeAfterRestore, leaveRecovery, shuttingDown;
    private readonly System.Windows.Forms.Timer picker = new() { Interval = 100 };
    private long pickDeadline;
    private bool pickRegion, configurationReadable = true;

    public ManagerForm()
    {
        Text = "Panel Mirror · 通用面板召回配置";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 550); MinimumSize = new Size(780, 550);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "每个面板独立选择召回方式与快捷键；默认 Alt+1～5 按一次召唤，再按一次关闭／放回。", AutoSize = true });
        layout.Controls.Add(new Label { Text = Compatibility.Targets, AutoSize = true });
        var row = new FlowLayoutPanel { AutoSize = true };
        slot.Items.AddRange(new object[] { "槽位 1", "槽位 2", "槽位 3", "槽位 4", "槽位 5" });
        row.Controls.Add(slot);
        row.Controls.Add(Button("刷新窗口列表", RefreshWindows));
        row.Controls.Add(Button("3 秒后拾取窗口", () => StartPick(false)));
        row.Controls.Add(Button("3 秒后拾取控件区域", () => StartPick(true)));
        layout.Controls.Add(row); layout.Controls.Add(windows);
        mode.Items.AddRange(new object[] { "镜像＋输入转发（原面板保持原位）", "移动真实浮动面板（用完恢复原位置）" });
        mode.SelectedIndex = 0;
        mode.SelectedIndexChanged += (_, _) =>
        {
            bool mirrorMode = mode.SelectedIndex == (int)SummonMode.Mirror;
            foreach (var value in crop) value.Enabled = mirrorMode;
            keyboard.Enabled = mirrorMode;
        };
        layout.Controls.Add(mode);
        layout.Controls.Add(separateKeys);
        var shortcuts = new FlowLayoutPanel { AutoSize = true };
        shortcuts.Controls.Add(new Label { Text = "召唤 / 切换", AutoSize = true }); shortcuts.Controls.Add(summonKey);
        shortcuts.Controls.Add(new Label { Text = "独立放回", AutoSize = true }); shortcuts.Controls.Add(returnKey);
        shortcuts.Controls.Add(Button("恢复本槽默认键", () =>
        {
            if (slot.SelectedIndex < 0) return;
            separateKeys.Checked = false; summonKey.Set(Hotkey.Default(slot.SelectedIndex)); returnKey.Set(Hotkey.Default(slot.SelectedIndex, true));
        }));
        layout.Controls.Add(shortcuts);
        layout.Controls.Add(new Label { Text = "点击输入框后直接按组合键，Tab 切换输入框。检查后保存生效；录入时暂停本工具的全局快捷键。", AutoSize = true });
        separateKeys.CheckedChanged += (_, _) => returnKey.Enabled = separateKeys.Checked;
        returnKey.Enabled = false;
        foreach (var box in new[] { summonKey, returnKey })
        {
            box.Rejected = Report;
            box.Enter += (_, _) => UnregisterKeys();
            box.Leave += (_, _) => BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) RegisterKeys(); }));
        }
        layout.Controls.Add(new Label { Text = "每槽独立保存。移动模式需要先手动浮动，不使用裁剪／输入转发；修改绑定前须先放回。", AutoSize = true });
        layout.Controls.Add(new Label { Text = "可选择浮动面板；停靠面板请选择 Photoshop 主窗口，再填写客户区裁剪坐标。", AutoSize = true });
        var crops = new FlowLayoutPanel { AutoSize = true };
        string[] names = { "X", "Y", "宽", "高" };
        for (int i = 0; i < 4; i++) { crops.Controls.Add(new Label { Text = names[i], AutoSize = true, Padding = new Padding(0, 5, 0, 0) }); crops.Controls.Add(crop[i]); }
        layout.Controls.Add(crops);
        layout.Controls.Add(new Label { Text = "坐标以源窗口客户区为基准，单位为像素；宽、高都为 0 表示整个客户区。", AutoSize = true });
        layout.Controls.Add(keyboard);
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.Add(Button("绑定所选窗口并保存", SaveBinding));
        actions.Controls.Add(Button("召回面板", () => ShowMirror(slot.SelectedIndex)));
        actions.Controls.Add(Button("关闭 / 放回", () => CloseSlot(slot.SelectedIndex)));
        actions.Controls.Add(Button("清除此槽", ClearBinding));
        layout.Controls.Add(actions); layout.Controls.Add(binding); layout.Controls.Add(status);
        var recovery = new FlowLayoutPanel { AutoSize = true };
        recovery.Controls.Add(Button("恢复上次移动的真实面板", () => { try { real.Restore(); } catch (Exception e) { Report(e.Message); } }));
        recovery.Controls.Add(Button("仅退出，保留恢复记录", () => { leaveRecovery = true; Close(); }));
        layout.Controls.Add(recovery);
        layout.Controls.Add(new Label { Text = "原型边界：只镜像源客户区；外部菜单、弹窗不会自动跟随。\n源窗口隐藏、最小化或被对话框阻塞时暂停输入。后台消息是否被目标接受，仍需实测。\n数位笔目前按鼠标点击／拖动处理，不转发压感与倾斜。", AutoSize = true, MaximumSize = new Size(700, 0) });
        slot.SelectedIndexChanged += (_, _) => LoadSlot();
        Shown += (_, _) =>
        {
            try { settings = Settings.Load(); }
            catch (Exception e) { configurationReadable = false; Report("读取配置失败，已禁止保存以保护原文件：" + e.Message); }
            RefreshWindows(); slot.SelectedIndex = 0; RegisterKeys();
            try { if (real.HasRecovery) Report("发现未处理的真实面板恢复记录；请先点击恢复。镜像模式仍可使用。"); }
            catch (Exception e) { Report("恢复记录读取失败，移动模式已阻止新召回：" + e.Message); }
            moveTimer.Start();
        };
        picker.Tick += (_, _) => FinishPick();
        moveTimer.Tick += (_, _) =>
        {
            try
            {
                string? message = real.Tick();
                if (message is not null) Report(real.Present ? "面板已召回；" + ReturnHint(real.ActiveSlot) : message);
                if (closeAfterRestore && !real.Busy) { closeAfterRestore = false; Close(); }
            }
            catch (Exception e) { closeAfterRestore = false; Report("真实面板处理失败，恢复记录已保留：" + e.Message); }
        };
        FormClosing += (_, e) =>
        {
            if (!leaveRecovery && real.Busy)
            {
                e.Cancel = true;
                try { real.Restore(); closeAfterRestore = real.Busy; }
                catch (Exception error) { closeAfterRestore = false; Report("放回失败，可重试或仅退出保留记录：" + error.Message); }
                if (!real.Busy) BeginInvoke((Action)Close);
                return;
            }
            shuttingDown = true; picker.Stop(); moveTimer.Stop();
            for (int i = 0; i < 5; i++) HideMirror(i);
            foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
        };
        FormClosed += (_, _) => { picker.Dispose(); moveTimer.Dispose(); real.Dispose(); };
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action(); return button;
    }
    private void Report(string text) => status.Text = text;
    private void StartPick(bool region)
    {
        pickRegion = region; pickDeadline = Environment.TickCount64 + 3000;
        slot.Enabled = false; picker.Start();
        Report("请将指针移到目标面板；3 秒后只读取窗口，不点击、不移动原面板。Esc 取消。拾取后需手动保存。");
    }
    private void CancelPick()
    { picker.Stop(); slot.Enabled = true; }
    private void FinishPick()
    {
        if (Environment.TickCount64 < pickDeadline) return;
        CancelPick();
        try
        {
            if (!Native.GetCursorPos(out var point)) throw new InvalidOperationException("无法读取指针位置。");
            nint leaf = Native.WindowFromPoint(point), root = Native.GetAncestor(leaf, 2);
            if (leaf == 0 || root == 0 || Native.Describe(root) is not { } item ||
                !Native.IsWindowVisible(root) || Native.IsIconic(root))
                throw new InvalidOperationException("未找到可用的源顶层窗口，请避开本工具和镜像窗口。");
            Rectangle area = Rectangle.Empty;
            if (pickRegion)
            {
                if (!Native.GetClientRect(leaf, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
                    throw new InvalidOperationException("控件没有可用客户区，请改用拾取窗口并手动裁剪。");
                var origin = new Native.POINT(0, 0);
                if (!Native.ClientToScreen(leaf, ref origin) || !Native.ScreenToClient(root, ref origin) ||
                    !Native.GetClientRect(root, out var client)) throw new InvalidOperationException("无法转换控件区域。");
                area = new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
                if (!new Rectangle(0, 0, client.Right, client.Bottom).Contains(area) ||
                    area.X < 0 || area.Y < 0 || area.Right > 100000 || area.Bottom > 100000)
                    throw new InvalidOperationException("控件区域超出源客户区，请手动裁剪。");
            }
            RefreshWindows();
            windows.SelectedItem = windows.Items.Cast<WindowItem>().SingleOrDefault(w => w.Handle == item.Handle && w.ProcessId == item.ProcessId && w.StartedAt == item.StartedAt);
            if (windows.SelectedItem is null) throw new InvalidOperationException("源窗口在拾取时已变化，请重新拾取。");
            int[] values = { area.X, area.Y, area.Width, area.Height };
            for (int i = 0; i < 4; i++) crop[i].Value = values[i];
            Report(pickRegion ? "已填入指针命中控件的客户区；可能只是一枚按钮，请检查并调整裁剪后保存。原布局变化后需重新拾取。" : "已选中源顶层窗口，裁剪已重置；请检查后绑定保存。");
        }
        catch (Exception e) { Report("拾取失败：" + e.Message); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (picker.Enabled && keyData == Keys.Escape) { CancelPick(); Report("已取消拾取。"); return true; }
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
        binding.Text = b.Configured ? $"当前绑定：{(b.Mode == SummonMode.Move ? "移动真实面板" : "镜像＋转发")} · {Compatibility.Display(b.ProcessName, b.PhotoshopMajor)} · {b.Title} · {b.WindowClass}" : "当前槽位尚未绑定。";
        SelectBoundWindow();
    }
    private void SaveBinding()
    {
        CancelPick();
        if (!configurationReadable) { Report("配置读取失败，禁止覆盖原文件；请先修复或备份配置后重新启动。"); return; }
        if (windows.SelectedItem is not WindowItem w) { Report("请从列表中选择明确的源窗口。"); return; }
        if (mode.SelectedIndex == (int)SummonMode.Mirror && (crop[2].Value == 0) != (crop[3].Value == 0)) { Report("宽、高需要同时填写，或同时为 0。"); return; }
        int i = slot.SelectedIndex;
        if (real.ActiveSlot == i) { Report("请先放回此槽位的真实面板，再修改绑定或切换方式。"); return; }
        var previous = settings.Slots[i];
        if (Native.Describe(w.Handle) is not { } current || current.ProcessId != w.ProcessId || current.StartedAt != w.StartedAt)
        { Report("列表中的源窗口已退出或改变，请刷新后重新选择。"); return; }
        w = current;
        if (mode.SelectedIndex == (int)SummonMode.Move && !PanelRecall.Native.IsFloating(w.Handle))
        { Report("移动模式只能绑定真实浮动面板。停靠面板可选择镜像方式，或先手动拖成浮动窗口。"); return; }
        settings.Slots[i] = new Binding { Mode = (SummonMode)mode.SelectedIndex, ProcessName = w.ProcessName, WindowClass = w.WindowClass, Title = w.Title, PhotoshopMajor = w.PhotoshopMajor,
            SeparateReturnKey = separateKeys.Checked, SummonKey = summonKey.Value.Copy(), ReturnKey = returnKey.Value.Copy(),
            X = (int)crop[0].Value, Y = (int)crop[1].Value,
            Width = mode.SelectedIndex == (int)SummonMode.Move ? 0 : (int)crop[2].Value,
            Height = mode.SelectedIndex == (int)SummonMode.Move ? 0 : (int)crop[3].Value, Keyboard = keyboard.Checked };
        var conflicts = ShortcutConflicts();
        if (conflicts.Count > 0) { settings.Slots[i] = previous; Report("未保存：" + string.Join("、", conflicts)); return; }
        try { settings.Save(); }
        catch (Exception e) { settings.Slots[i] = previous; Report("保存失败：" + e.Message); return; }
        HideMirror(i); sources[i] = new SourceWindow(w); LoadSlot(); RegisterKeys();
    }
    private void ClearBinding()
    {
        CancelPick();
        if (!configurationReadable) { Report("配置读取失败，禁止覆盖原文件；请先修复或备份配置后重新启动。"); return; }
        int i = slot.SelectedIndex;
        if (real.ActiveSlot == i) { Report("请先放回此槽位的真实面板，再清除绑定。"); return; }
        var previous = settings.Slots[i]; settings.Slots[i] = new();
        try { settings.Save(); }
        catch (Exception e) { settings.Slots[i] = previous; Report("保存失败：" + e.Message); return; }
        HideMirror(i); sources[i] = null; LoadSlot(); RegisterKeys();
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
                string name = $"槽位 {i + 1} {(close == 0 ? "召唤／切换" : "放回")}";
                if (!key.Valid) { failures.Add(name + "快捷键无效"); continue; }
                if (used.TryGetValue((key.Modifiers, key.Key), out var previous)) failures.Add($"{previous} 与 {name} 重复使用 {key}；相同键请使用单键切换");
                else used.Add((key.Modifiers, key.Key), name);
            }
        }
        return failures;
    }
    private void RegisterKeys()
    {
        UnregisterKeys();
        if (shuttingDown) return;
        if (summonKey.ContainsFocus || returnKey.ContainsFocus) return;
        var failures = ShortcutConflicts();
        if (failures.Count > 0) { Report("快捷键未注册：" + string.Join("、", failures)); return; }
        for (int i = 0; i < 5; i++)
        {
            if (!settings.Slots[i].Configured) continue;
            var b = settings.Slots[i];
            for (int close = 0; close < (b.SeparateReturnKey ? 2 : 1); close++)
            {
                int id = 100 + i * 2 + close;
                var key = close == 0 ? b.SummonShortcut(i) : b.ReturnShortcut(i);
                if (Native.RegisterHotKey(Handle, id, 0x4000 | key.Modifiers, (uint)key.Key)) registered.Add(id);
                else failures.Add($"槽位 {i + 1} {key}（错误 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）");
            }
        }
        Report(!configurationReadable ? "配置读取失败，未注册有效绑定并禁止覆盖原文件。" : failures.Count == 0 ? "配置已就绪；仅已绑定槽位注册全局快捷键。" : "快捷键注册失败：" + string.Join("、", failures));
    }
    private SourceWindow Resolve(int index)
    {
        var b = settings.Slots[index];
        if (sources[index] is { } live && live.IsAlive()) return live;
        var matches = Native.Windows().Where(w => w.Matches(b)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? "找不到源窗口。标题可能已改变，请重新绑定。" : "存在多个同名同类窗口，请从列表中重新绑定具体窗口。");
        return sources[index] = new SourceWindow(matches[0]);
    }
    private void ShowMirror(int index)
    {
        if (index < 0 || !settings.Slots[index].Configured) { Report("请先绑定此槽位。"); return; }
        try
        {
            SourceWindow source = Resolve(index);
            if (!Native.IsWindowVisible(source.Handle) || Native.IsIconic(source.Handle)) throw new InvalidOperationException("源窗口需要保持显示且未最小化。");
            if (settings.Slots[index].Mode == SummonMode.Move)
            {
                // A window can have been bound to several slots. Never leave
                // an interactive mirror of that same window running as it moves.
                for (int i = 0; i < mirrors.Length; i++)
                    if (mirrors[i]?.Source.Handle == source.Handle) HideMirror(i);
                HideMirror(index); real.Summon(source, index);
                Report("正在召回真实浮动面板并确认位置；" + ReturnHint(index)); return;
            }
            if (real.Moves(source)) throw new InvalidOperationException("此源窗口正在移动模式中使用，请先放回，再打开它的镜像。");
            if (mirrors[index] is { IsDisposed: false } existing && ReferenceEquals(existing.Source, source)) { existing.Summon(); return; }
            HideMirror(index);
            var mirror = new MirrorForm(source, settings.Slots[index], index + 1, ReturnHint(index), Report);
            mirrors[index] = mirror;
            mirror.Summon();
        }
        catch (Exception e) { HideMirror(index); Report("无法召回面板：" + e.Message); }
    }
    private void CloseSlot(int index)
    {
        if (index < 0) return;
        HideMirror(index);
        try { real.Restore(index); }
        catch (Exception e) { Report("无法放回真实面板，恢复记录已保留：" + e.Message); }
    }
    private string ReturnHint(int index)
    {
        var b = settings.Slots[index];
        return b.SeparateReturnKey ? $"{b.ReturnShortcut(index)} 放回／关闭" : $"再按 {b.SummonShortcut(index)} 放回／关闭";
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
