namespace PanelMirror;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, "Local\\PanelMirror.Configuration", out bool first);
        if (!first) return;
        // Both tools share the durable real-window recovery journal.
        using var recallLock = new Mutex(true, "Local\\PanelRecall.Configuration.v1", out bool ownsRecall);
        if (!ownsRecall) { MessageBox.Show("请先关闭独立 Panel Recall，再使用统一面板工具。"); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ManagerForm());
    }
}
