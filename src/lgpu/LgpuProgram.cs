using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LegionRgb;

namespace Lgpu
{
    static class LgpuProgram
    {
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "LGPU.SingleInstance", out created))
            {
                if (!created) { PostMessage((IntPtr)0xFFFF, LgpuForm.ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // First run: start from the GPU settings of Legion RGB, if it was used before.
                Settings.AppFolder = "LegionRgb";
                Settings legion = Settings.Exists() ? Settings.Load() : null;
                Settings.AppFolder = "LGPU";
                var s = Settings.Exists() || legion == null ? Settings.Load() : legion;
                Application.Run(new LgpuForm(s, Array.IndexOf(args, "--tray") >= 0));
            }
        }
    }
}
