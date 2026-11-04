using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PanelRecall
{
    internal sealed class ManagerForm : Form
    {
        private Settings settings = new Settings();
        private readonly WindowInfo[] live = new WindowInfo[5];
        private readonly HashSet<int> hotkeys = new HashSet<int>();
        private readonly ComboBox slots = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
        private readonly ComboBox windows = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 760, DropDownWidth = 1000 };
        private readonly CheckBox psOnly = new CheckBox { Text = UiText.T("只列出 Photoshop 相关浮动窗口"), AutoSize = true, Checked = true };
        private readonly CheckBox autoReturn = new CheckBox { Text = UiText.T("识别到 PS 画布点击后放回；点原生面板不放回（未实机验证）"), AutoSize = true, Checked = true };
        private readonly Label binding = new Label { AutoSize = true, MaximumSize = new Size(760, 0) };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Color.FromArgb(130, 215, 190) };
        private readonly Button capture = new Button { Text = UiText.T("3 秒后绑定指针下的浮动面板"), AutoSize = true };
        private readonly Timer timer = new Timer { Interval = 60 };
        private RecallSession active;
        private ReturnRecord recovery;
        private bool storageBlocked, returnBlocked, closeWhenReturned, allowClose;
        private int pendingSlot = -1;
        private Native.Point pendingPoint;
        private DateTime captureAt;
        private int captureSlot = -1;

        internal ManagerForm()
        {
            Text = UiText.T("Panel Recall · 通用浮动面板召回（实验版）");
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(24, 29, 36); ForeColor = Color.FromArgb(225, 231, 238);
            ClientSize = new Size(810, 560); MinimumSize = new Size(800, 530);
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) };
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = UiText.T("召回真实面板，保留原插件交互"), Font = new Font(Font, FontStyle.Bold), AutoSize = true });
            layout.Controls.Add(Note(UiText.T("Alt+1～5 召到指针旁；Shift+Alt+1～5 放回原位置。松键保持。\n快捷键为桌面全局，仅绑定的槽位注册；冲突会明确提示。")));
            layout.Controls.Add(Note(UiText.T("先把目标插件面板从停靠栏拖成浮动窗口；不自动拆解 Photoshop 工作区。")));
            var selection = Row();
            slots.Items.AddRange(new object[] { UiText.T("槽位 1"), UiText.T("槽位 2"), UiText.T("槽位 3"), UiText.T("槽位 4"), UiText.T("槽位 5") });
            selection.Controls.Add(slots); selection.Controls.Add(ActionButton(UiText.T("刷新窗口列表"), RefreshWindows)); selection.Controls.Add(psOnly);
            layout.Controls.Add(selection); layout.Controls.Add(windows);
            var bindActions = Row();
            bindActions.Controls.Add(ActionButton(UiText.T("绑定所选窗口"), BindSelected));
            capture.Click += delegate { if (captureSlot >= 0) CancelCapture(); else { captureSlot = slots.SelectedIndex; captureAt = DateTime.UtcNow.AddSeconds(3); slots.Enabled = false; capture.Text = UiText.T("取消捕获"); Report(UiText.T("将指针移到已浮动的目标面板上，3 秒后读取窗口，不会点击或移动它。")); } };
            bindActions.Controls.Add(capture); bindActions.Controls.Add(ActionButton(UiText.T("清除此槽绑定"), ClearBinding));
            layout.Controls.Add(bindActions); layout.Controls.Add(binding);
            var actions = Row();
            actions.Controls.Add(ActionButton(UiText.T("召到指针旁"), delegate { Summon(slots.SelectedIndex); }));
            actions.Controls.Add(ActionButton(UiText.T("放回原位 / 恢复上次会话"), Restore));
            actions.Controls.Add(ActionButton(UiText.T("仅退出，保留恢复记录"), delegate { allowClose = true; Close(); }));
            layout.Controls.Add(actions); layout.Controls.Add(autoReturn);
            layout.Controls.Add(Note(UiText.T("原生面板及插件弹窗继续由 Photoshop / 原插件处理；不镜像，不转发或模拟鼠标与数位笔。\n同一时刻召回一个面板，切换槽位先确认放回前一个。未知画布布局不自动放回。")));
            layout.Controls.Add(Note(UiText.T("关闭本工具前会先尝试放回。强制结束后，重开本工具可恢复上次记录；不自动移动任何窗口。\n配置与恢复记录：") + Storage.Folder));
            layout.Controls.Add(Note(UiText.T("Windows x64 / .NET Framework 4.8。PS 2019、2020、2025 及第三方插件均待实机验收。")));
            layout.Controls.Add(status);
            StyleControls(layout);
            slots.SelectedIndexChanged += delegate { LoadSlot(); };
            psOnly.CheckedChanged += delegate { RefreshWindows(); };
            autoReturn.CheckedChanged += delegate
            {
                CanvasClickHook.Disarm();
                if (active != null && active.Present && autoReturn.Checked) active.EnableCanvasReturn();
            };
            Shown += delegate
            {
                try { settings = Settings.Load(); } catch (Exception e) { storageBlocked = true; Report(UiText.T("配置读取失败，未覆盖原文件：") + e.Message); }
                try
                {
                    recovery = Storage.Read<ReturnRecord>("return.json");
                    if (recovery != null) { returnBlocked = true; Report(UiText.T("有上次会话的放回记录，请先点“放回原位 / 恢复上次会话”。未自动移动窗口。")); }
                }
                catch (Exception e) { returnBlocked = true; Report(UiText.T("恢复记录读取失败，暂停新召回：") + e.Message); }
                bool mouse = CanvasClickHook.Install();
                if (!mouse) { autoReturn.Checked = false; autoReturn.Enabled = false; Report(UiText.T("点击监听未启动，可用 Shift+Alt+数字 手动放回。")); }
                RefreshWindows(); slots.SelectedIndex = 0; RegisterKeys(); timer.Start();
            };
            timer.Tick += delegate { TickSession(); };
            FormClosing += OnClosing;
            FormClosed += delegate { timer.Dispose(); CanvasClickHook.Stop(); if (active != null) active.Dispose(); foreach (int id in hotkeys) Native.UnregisterHotKey(Handle, id); };
        }
        private static FlowLayoutPanel Row() { return new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 8, 0, 8) }; }
        private static Label Note(string text) { return new Label { Text = text, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 7, 0, 7) }; }
        private Button ActionButton(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Padding = new Padding(5, 3, 5, 3) };
            button.Click += delegate { Guard(action); }; return button;
        }
        private static void StyleControls(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (control is Button || control is ComboBox)
                { control.BackColor = Color.FromArgb(41, 50, 61); control.ForeColor = Color.FromArgb(225, 231, 238); }
                Button button = control as Button;
                if (button != null) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(68, 91, 94); }
                StyleControls(control);
            }
        }
        private void Guard(Action action) { try { action(); } catch (Exception e) { Report(e.Message); } }
        private void Report(string message) { status.Text = message; }
        private void RefreshWindows()
        {
            WindowInfo selected = windows.SelectedItem as WindowInfo;
            windows.Items.Clear();
            foreach (WindowInfo item in Native.Windows(psOnly.Checked)) windows.Items.Add(item);
            if (selected != null) foreach (WindowInfo item in windows.Items) if (item.Handle == selected.Handle) windows.SelectedItem = item;
        }
        private void LoadSlot()
        {
            if (slots.SelectedIndex < 0) return;
            Binding b = settings.Slots[slots.SelectedIndex];
            binding.Text = b.Configured ? UiText.T("当前绑定：") + b.Title + " · " + b.Class : UiText.T("此槽位尚未绑定。");
            WindowInfo found = null; int count = 0;
            foreach (WindowInfo item in windows.Items)
            {
                WindowInfo current = live[slots.SelectedIndex];
                if (current != null ? item.Handle == current.Handle && item.Pid == current.Pid && item.Started == current.Started : item.Matches(b)) { found = item; count++; }
            }
            windows.SelectedItem = count == 1 ? found : null;
        }
        private void RequireStorage()
        { if (storageBlocked) throw new InvalidOperationException(UiText.T("配置读取失败，不能覆盖；请先备份并修复 settings.json 后重启。")); }
        private void BindSelected()
        {
            WindowInfo chosen = windows.SelectedItem as WindowInfo;
            if (chosen == null) throw new InvalidOperationException(UiText.T("请明确选择一个浮动面板。"));
            Bind(chosen, slots.SelectedIndex);
        }
        private void Bind(WindowInfo chosen, int index)
        {
            RequireStorage();
            if (index < 0 || active != null || returnBlocked) throw new InvalidOperationException(UiText.T("请先放回当前面板并处理恢复记录，再修改绑定。"));
            WindowInfo current = Native.Describe(chosen.Handle);
            if (current == null || current.Pid != chosen.Pid || current.Started != chosen.Started) throw new InvalidOperationException(UiText.T("目标已改变，请重新选择。"));
            for (int i = 0; i < live.Length; i++) if (i != index && live[i] != null && live[i].Handle == current.Handle) throw new InvalidOperationException(UiText.T("这个面板已绑定到槽位 ") + (i + 1));
            Binding old = settings.Slots[index]; settings.Slots[index] = current.Binding();
            try { Storage.Write("settings.json", settings); } catch { settings.Slots[index] = old; throw; }
            live[index] = current; LoadSlot(); if (RegisterKeys()) Report(UiText.T("已绑定槽位 ") + (index + 1) + UiText.T("；尚未移动面板。"));
        }
        private void ClearBinding()
        {
            RequireStorage(); int index = slots.SelectedIndex;
            if (active != null || returnBlocked) throw new InvalidOperationException(UiText.T("请先放回当前面板并处理恢复记录。"));
            Binding previous = settings.Slots[index]; settings.Slots[index] = new Binding();
            try { Storage.Write("settings.json", settings); } catch { settings.Slots[index] = previous; throw; }
            live[index] = null; LoadSlot(); RegisterKeys();
        }
        private bool RegisterKeys()
        {
            foreach (int id in hotkeys) Native.UnregisterHotKey(Handle, id);
            hotkeys.Clear(); var failed = new List<string>();
            for (int slot = 0; slot < 5; slot++) if (settings.Slots[slot].Configured)
                for (int restore = 0; restore < 2; restore++)
                {
                    int id = 300 + slot * 2 + restore;
                    if (Native.RegisterHotKey(Handle, id, (uint)(0x4000 | 1 | (restore == 1 ? 4 : 0)), (uint)('1' + slot))) hotkeys.Add(id);
                    else failed.Add((restore == 1 ? "Shift+" : "") + "Alt+" + (slot + 1));
                }
            if (failed.Count != 0) Report(UiText.T("快捷键冲突或注册失败：") + String.Join("、", failed.ToArray()));
            return failed.Count == 0;
        }
        private WindowInfo Resolve(int index)
        {
            WindowInfo remembered = live[index];
            if (remembered != null)
            {
                WindowInfo current = Native.Describe(remembered.Handle);
                if (current != null && current.Pid == remembered.Pid && current.Started == remembered.Started && current.Class == remembered.Class) return current;
            }
            WindowInfo found = null; int count = 0;
            foreach (WindowInfo item in Native.Windows(false)) if (item.Matches(settings.Slots[index])) { found = item; count++; }
            if (count != 1) throw new InvalidOperationException(count == 0 ? UiText.T("未找到绑定面板，请打开并浮动该面板后重新绑定。") : UiText.T("有多个同名同类面板，请明确重新绑定，未自动选择。"));
            return live[index] = found;
        }
        private void Summon(int index)
        {
            if (captureSlot >= 0) throw new InvalidOperationException(UiText.T("请先完成或取消窗口捕获。"));
            if (returnBlocked || recovery != null) throw new InvalidOperationException(UiText.T("请先处理上次会话的恢复记录。"));
            if (index < 0 || !settings.Slots[index].Configured) throw new InvalidOperationException(UiText.T("请先绑定此槽位。"));
            Native.Point point; if (!Native.GetCursorPos(out point)) throw new InvalidOperationException(UiText.T("无法读取指针位置。"));
            if (active != null && active.Record.Slot != index)
            { active.Restore(); pendingSlot = index; pendingPoint = point; Report(UiText.T("先放回前一个面板，再召回新槽位…")); return; }
            BeginSummon(index, point);
        }
        private void BeginSummon(int index, Native.Point point)
        {
            if (active == null)
            {
                // Re-read journal: a failed disk write may have persisted it
                // even though no first move was issued.
                if (Storage.Read<ReturnRecord>("return.json") != null) { returnBlocked = true; throw new InvalidOperationException(UiText.T("有待处理的恢复记录，请先恢复。")); }
                active = RecallSession.Capture(Resolve(index), index);
            }
            active.Summon(point); Report(UiText.T("已请求移动，正在确认面板位置…"));
        }
        private void Restore()
        {
            pendingSlot = -1; CancelCapture();
            if (active == null)
            {
                recovery = Storage.Read<ReturnRecord>("return.json");
                if (recovery == null) { returnBlocked = false; Report(UiText.T("没有待放回的面板。")); return; }
                if (RecallSession.IsDefinitelyGone(recovery)) { Storage.ClearReturnRecord(); recovery = null; returnBlocked = false; Report(UiText.T("原窗口或原进程已不存在，已清理失效记录，未移动其他窗口。")); return; }
                active = RecallSession.Recover(recovery); recovery = null;
            }
            if (active.DiscardIfGone()) { CompleteSession(); return; }
            active.Restore(); Report(UiText.T("正在放回并核对原位置、尺寸…"));
        }
        private void CancelCapture() { captureSlot = -1; slots.Enabled = true; capture.Text = UiText.T("3 秒后绑定指针下的浮动面板"); }
        private void TickSession()
        {
            if (captureSlot >= 0 && DateTime.UtcNow >= captureAt)
            {
                int index = captureSlot; CancelCapture();
                Guard(delegate
                {
                    Native.Point point; if (!Native.GetCursorPos(out point)) throw new InvalidOperationException(UiText.T("无法读取指针位置。"));
                    WindowInfo chosen = Native.Describe(Native.GetAncestor(Native.WindowFromPoint(point), 2));
                    if (chosen == null) throw new InvalidOperationException(UiText.T("指针下不是可绑定的浮动面板。停靠面板需先手动浮动。"));
                    Bind(chosen, index);
                });
            }
            if (active == null) return;
            try
            {
                if (active.DiscardIfGone()) { Report(UiText.T("原面板已关闭，已清理失效恢复记录。")); CompleteSession(); return; }
                string message = active.Tick(autoReturn.Checked);
                if (message != null) Report(message);
                if (active.Finished) CompleteSession();
            }
            catch (Exception e) { active.Fail(); pendingSlot = -1; closeWhenReturned = false; Report(e.Message); }
        }
        private void CompleteSession()
        {
            if (active != null) active.Dispose(); active = null; recovery = null; returnBlocked = false;
            if (closeWhenReturned) { allowClose = true; Close(); return; }
            int next = pendingSlot; pendingSlot = -1;
            if (next >= 0) Guard(delegate { BeginSummon(next, pendingPoint); });
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (allowClose || active == null) return;
            e.Cancel = true; closeWhenReturned = true;
            Guard(delegate { Restore(); });
            if (active == null && !returnBlocked && recovery == null) { allowClose = true; BeginInvoke(new Action(Close)); }
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape && captureSlot >= 0) { CancelCapture(); Report(UiText.T("已取消窗口捕获。")); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && hotkeys.Contains(message.WParam.ToInt32()))
            {
                int id = message.WParam.ToInt32(), index = (id - 300) / 2;
                Guard(delegate
                {
                    if ((id - 300) % 2 == 0) Summon(index);
                    else if (active == null || active.Record.Slot == index) Restore();
                });
                return;
            }
            base.WndProc(ref message);
        }
    }
}
