using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LegionRgb
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "LegionRgb.SingleInstance", out created))
            {
                if (!created)
                {
                    // Already running (probably in the tray): bring that window up instead.
                    PostMessage((IntPtr)0xFFFF, MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool tray = Array.IndexOf(args, "--tray") >= 0;
                var backends = new List<ILightingBackend> { new CaseBackend(), new GpuBackend() };
                Application.Run(new MainForm(Settings.Load(), backends, tray));
            }
        }
    }
}
