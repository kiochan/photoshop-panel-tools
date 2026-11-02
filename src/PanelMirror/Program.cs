using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PanelMirror;

internal static class Program
{
    // A named event avoids window-title matching and carries no external data.
    internal const string WakeEvent = "Local\\PanelMirror.ShowConfiguration.v1";
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, "Local\\PanelMirror.Configuration", out bool first);
        if (!first)
        {
            // The first instance may still be constructing its UI.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (EventWaitHandle.TryOpenExisting(WakeEvent, out var signal))
                { using (signal) { GrantForeground(); signal.Set(); } return; }
                Thread.Sleep(100);
            }
            MessageBox.Show("配置程序已运行，但暂时无法唤回；请从系统托盘打开配置。");
            return;
        }
        // Both tools share the durable real-window recovery journal.
        using var recallLock = new Mutex(true, "Local\\PanelRecall.Configuration.v1", out bool ownsRecall);
        if (!ownsRecall) { MessageBox.Show("请先关闭独立 Panel Recall，再使用统一面板工具。"); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ManagerForm());
    }

    private static void GrantForeground()
    {
        // Transfer the foreground permission of this user-initiated launch to
        // the existing copy of the same executable in this Windows session.
        using var current = Process.GetCurrentProcess();
        foreach (var candidate in Process.GetProcessesByName(current.ProcessName))
        {
            using (candidate)
            {
                try
                {
                    if (candidate.Id != current.Id && candidate.SessionId == current.SessionId &&
                        string.Equals(candidate.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        AllowSetForegroundWindow((uint)candidate.Id);
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
    }
}
