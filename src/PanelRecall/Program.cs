using System;
using System.Threading;
using System.Windows.Forms;

namespace PanelRecall
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var single = new Mutex(true, "Local\\PanelRecall.Configuration.v1", out first))
            {
                if (!first) { MessageBox.Show("面板召回工具已在运行。", "Panel Recall"); return; }
                try
                {
                    Native.InitializeDpi();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new ManagerForm());
                }
                finally { CanvasClickHook.Stop(); single.ReleaseMutex(); }
            }
        }
    }
}
