using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LegionRgb
{
    /// Small UI helpers shared by Legion RGB and LGPU.
    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(20, 20, 22), Panel2 = Color.FromArgb(38, 38, 42), Fg = Color.FromArgb(230, 230, 230), Dim = Color.FromArgb(150, 150, 155);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);

        public static void DarkTitleBar(Form f)
        {
            int on = 1;
            try { DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch { }
        }

        public static Label Caption(string t, int x, int y)
        {
            var l = new Label { Text = t, AutoSize = true, ForeColor = Dim };
            l.Location = new Point(x, y);
            return l;
        }

        /// A click on the track jumps straight to that value (instead of paging by LargeChange).
        public static void ClickToPosition(TrackBar t)
        {
            t.LargeChange = 0;
            t.MouseDown += (o, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                RECT ch = new RECT(), th = new RECT();
                SendMessage(t.Handle, 0x41A, IntPtr.Zero, ref ch);   // TBM_GETCHANNELRECT
                SendMessage(t.Handle, 0x419, IntPtr.Zero, ref th);   // TBM_GETTHUMBRECT
                if (e.X >= th.L && e.X <= th.R && e.Y >= th.T && e.Y <= th.B) return;   // grabbing the thumb: normal drag
                int half = (th.R - th.L) / 2;
                double span = Math.Max(1, ch.R - ch.L - 2 * half);
                double f = Math.Max(0, Math.Min(1, (e.X - ch.L - half) / span));
                t.Value = t.Minimum + (int)Math.Round(f * (t.Maximum - t.Minimum));
            };
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAutostart(string name)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(name) != null;
        }

        public static void SetAutostart(string name, bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (on) k.SetValue(name, "\"" + Application.ExecutablePath + "\" --tray");
                else k.DeleteValue(name, false);
            }
        }

        public static Icon MakeIcon(Color a, Color b, string letter)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 32, 32), a, b, 45f))
                    g.FillEllipse(br, 1, 1, 30, 30);
                using (var f = new Font("Segoe UI", 14, FontStyle.Bold))
                    TextRenderer.DrawText(g, letter, f, new Rectangle(0, 0, 32, 32), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
