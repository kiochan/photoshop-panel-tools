using System.Diagnostics;
using PanelMirror;

namespace PanelTools.Demo;

// This harness is built separately, never included in the product ZIP. It uses
// the real configuration/crop/mirror/mover code against an owned synthetic app.
internal static class DemoProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "demo-startup-error.txt"), error.ToString());
            Environment.ExitCode = 1;
        }
    }
    private static void Run(string[] args)
    {
        if (args.Contains("--self-test")) { SelfCheck.Run(); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--source")) { Application.Run(new DemoSource()); return; }

        using var single = new Mutex(true, "Local\\PanelMirror.Configuration", out bool first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(PanelMirror.Program.WakeEvent, out var signal))
            { using (signal) signal.Set(); }
            return;
        }
        using var recallLock = new Mutex(true, "Local\\PanelRecall.Configuration.v1", out bool ownsRecall);
        if (!ownsRecall) return;
        string profile = Path.Combine(AppContext.BaseDirectory, "profile");
        Settings.DataDirectory = Path.Combine(profile, "mirror");
        PanelRecall.Storage.DataDirectory = Path.Combine(profile, "recall");
        UiText.SetLanguage(Settings.Load().Language);
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--source") { UseShellExecute = true })
            ?? throw new InvalidOperationException("Cannot start the synthetic source.");
        child.WaitForInputIdle(5000);
        Native.WindowFilter = item => item.ProcessId == child.Id;
        using var manager = new ManagerForm();
        manager.FormClosed += (_, _) => { if (!child.HasExited) child.CloseMainWindow(); };
        Application.Run(manager);
    }
}

internal sealed class DemoSource : Form
{
    internal DemoSource()
    {
        Text = "Panel Tools Demo · synthetic source";
        ClientSize = new Size(370, 420);
        StartPosition = FormStartPosition.Manual; Location = new Point(50, 80);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(34, 37, 43); ForeColor = Color.WhiteSmoke;
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        stack.Controls.Add(new Label { Text = "DEMO / 示例面板", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        stack.Controls.Add(new Label { Text = "Synthetic controls. No Photoshop or plugin.\n独立演示控件，不含 Photoshop 或插件。", AutoSize = true, Margin = new Padding(0, 16, 0, 16) });
        var result = new Label { Text = "Clicks / 点击：0", AutoSize = true };
        int clicks = 0;
        var button = new Button { Text = "Test click / 测试点击", AutoSize = true, ForeColor = Color.Black, BackColor = Color.WhiteSmoke };
        button.Click += (_, _) => result.Text = "Clicks / 点击：" + ++clicks;
        stack.Controls.Add(button); stack.Controls.Add(result);
        stack.Controls.Add(new Label { Text = "Brush size / 笔刷大小", AutoSize = true, Margin = new Padding(0, 18, 0, 6) });
        var slider = new TrackBar { Minimum = 1, Maximum = 100, Value = 36, Width = 300, TickFrequency = 10 };
        var value = new Label { Text = "36 px", AutoSize = true };
        slider.ValueChanged += (_, _) => value.Text = slider.Value + " px";
        stack.Controls.Add(slider); stack.Controls.Add(value);
        stack.Controls.Add(new TextBox { Text = "Demo notes / 示例文本", Width = 300, Margin = new Padding(0, 16, 0, 0) });
        Controls.Add(stack);
    }
}
