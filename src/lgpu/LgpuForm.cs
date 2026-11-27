using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using LegionRgb;

namespace Lgpu
{
    /// LGPU: the GPU part of Legion RGB only (light strip effect + color, speed, brightness, logo LED).
    public class LgpuForm : Form
    {
        public static readonly int ShowMessage = RegisterWindowMessage("LGPU.ShowWindow");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string s);

        readonly Settings settings;
        readonly Applier applier;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly CheckBox chkOn = new CheckBox(), chkLogo = new CheckBox(), chkAutostart = new CheckBox();
        readonly ComboBox cmbEffect = new ComboBox();
        readonly Panel swatch = new Panel();
        readonly NumericUpDown numR = new NumericUpDown(), numG = new NumericUpDown(), numB = new NumericUpDown();
        readonly TextBox txtHex = new TextBox();
        readonly TrackBar trkBright = new TrackBar(), trkSpeed = new TrackBar();
        readonly Label lblBright = new Label(), lblStatus = new Label();
        readonly ToolStripMenuItem miOn = new ToolStripMenuItem("Light on"), miLogo = new ToolStripMenuItem("Logo LED");
        readonly Timer debounce = new Timer { Interval = 150 };
        bool loading, reallyExit, hideOnStart;

        ZoneState Gpu { get { return settings.Get(Zone.Gpu); } }

        public LgpuForm(Settings s, bool startHidden)
        {
            settings = s;
            applier = new Applier(new List<ILightingBackend> { new GpuBackend() });
            applier.Status += msg => { try { BeginInvoke((Action)(() => lblStatus.Text = msg)); } catch { } };

            Text = "LGPU";
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            ClientSize = new Size(300, 392);
            BackColor = Ui.Bg; ForeColor = Ui.Fg; Font = new Font("Segoe UI", 9.5f);
            Icon = Ui.MakeIcon(Color.FromArgb(118, 185, 0), Color.FromArgb(0, 200, 255), "G");
            StartPosition = FormStartPosition.CenterScreen;

            int x = 14, w = 272;
            chkOn.Text = "Light effect switch"; chkOn.SetBounds(x, 12, w, 24); chkOn.CheckedChanged += delegate { Changed(); };
            chkLogo.Text = "Logo LED"; chkLogo.SetBounds(x, 40, w, 24); chkLogo.CheckedChanged += delegate { Changed(); };
            Controls.Add(chkOn); Controls.Add(chkLogo);

            Controls.Add(Ui.Caption("Lighting effect", x, 72));
            cmbEffect.SetBounds(x, 92, w, 26); cmbEffect.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEffect.FlatStyle = FlatStyle.Flat; cmbEffect.BackColor = Ui.Panel2; cmbEffect.ForeColor = Ui.Fg;
            cmbEffect.Items.AddRange(new object[] { "Always", "Blinking", "Spiral rainbow" });
            cmbEffect.SelectedIndexChanged += delegate { UpdateEnabled(); Changed(); };
            Controls.Add(cmbEffect);

            Controls.Add(Ui.Caption("Color", x, 126));
            swatch.SetBounds(x, 148, 36, 25); swatch.Cursor = Cursors.Hand; swatch.BorderStyle = BorderStyle.FixedSingle;
            swatch.Click += delegate { PickColor(); };
            Controls.Add(swatch);
            int nx = x + 44;
            foreach (var n in new[] { numR, numG, numB })
            {
                n.SetBounds(nx, 149, 70, 24); n.Maximum = 255; n.BackColor = Ui.Panel2; n.ForeColor = Ui.Fg; n.BorderStyle = BorderStyle.FixedSingle;
                n.ValueChanged += delegate { if (!loading) { SyncHex(); Changed(); } };
                Controls.Add(n); nx += 76;
            }
            txtHex.SetBounds(x, 180, 90, 24); txtHex.BackColor = Ui.Panel2; txtHex.ForeColor = Ui.Fg; txtHex.BorderStyle = BorderStyle.FixedSingle;
            txtHex.Leave += delegate { ParseHex(); };
            txtHex.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { ParseHex(); e.SuppressKeyPress = true; } };
            Controls.Add(txtHex);
            var palette = new[] { Color.FromArgb(255, 0, 0), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 220, 0), Color.FromArgb(0, 255, 80), Color.FromArgb(0, 211, 254), Color.FromArgb(0, 60, 255), Color.FromArgb(160, 0, 255), Color.FromArgb(255, 0, 160), Color.White };
            int px = x + 100;
            foreach (var c in palette)
            {
                var p = new Panel { BackColor = c, Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle };
                p.SetBounds(px, 183, 17, 18); px += 19;
                var col = c; p.Click += delegate { SetColor(col); };
                Controls.Add(p);
            }

            Controls.Add(Ui.Caption("Speed (slow - fast)", x, 214));
            trkSpeed.SetBounds(x, 232, w, 30); trkSpeed.Minimum = 1; trkSpeed.Maximum = 3; trkSpeed.TickStyle = TickStyle.BottomRight;
            trkSpeed.ValueChanged += delegate { Changed(); };
            Controls.Add(trkSpeed);

            lblBright.ForeColor = Ui.Dim; lblBright.AutoSize = true; lblBright.Location = new Point(x, 272);
            Controls.Add(lblBright);
            trkBright.SetBounds(x, 292, w, 30); trkBright.Minimum = 1; trkBright.Maximum = 10; trkBright.TickStyle = TickStyle.BottomRight;
            trkBright.ValueChanged += delegate { lblBright.Text = "Brightness  " + trkBright.Value * 10 + "%"; Changed(); };
            Controls.Add(trkBright);
            Ui.ClickToPosition(trkBright); Ui.ClickToPosition(trkSpeed);

            chkAutostart.Text = "Start with Windows (in the tray)"; chkAutostart.SetBounds(x, 334, w, 24);
            chkAutostart.Checked = Ui.IsAutostart("LGPU");
            chkAutostart.CheckedChanged += delegate { Ui.SetAutostart("LGPU", chkAutostart.Checked); };
            Controls.Add(chkAutostart);
            lblStatus.SetBounds(x, 364, w, 22); lblStatus.ForeColor = Ui.Dim;
            Controls.Add(lblStatus);

            debounce.Tick += delegate { debounce.Stop(); Push(false); };

            tray.Icon = Icon; tray.Text = "LGPU"; tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { ShowFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            miOn.Click += delegate { chkOn.Checked = !chkOn.Checked; };
            miLogo.Click += delegate { chkLogo.Checked = !chkLogo.Checked; };
            menu.Items.Add(miOn); menu.Items.Add(miLogo);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Re-apply lighting", null, delegate { Push(true); });
            menu.Items.Add("Exit", null, delegate { reallyExit = true; Close(); });
            menu.Opening += delegate { miOn.Checked = chkOn.Checked; miLogo.Checked = chkLogo.Checked; };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

            loading = true;
            var g = Gpu;
            chkOn.Checked = g.On; chkLogo.Checked = settings.GpuLogo;
            cmbEffect.SelectedIndex = Math.Max(0, Math.Min(2, (int)g.Effect));
            numR.Value = g.R; numG.Value = g.G; numB.Value = g.B;
            trkSpeed.Value = Math.Max(1, Math.Min(3, g.Speed));
            trkBright.Value = Math.Max(1, Math.Min(10, settings.Level));
            lblBright.Text = "Brightness  " + trkBright.Value * 10 + "%";
            SyncHex(); UpdateEnabled();
            loading = false;

            hideOnStart = startHidden;

            // Lenovo's service re-applies its own GPU lighting at boot and after sleep, so re-apply ours afterwards.
            Push(true);
            DelayedReapply(10000); DelayedReapply(45000);
            SystemEvents.PowerModeChanged += (o, e) => { if (e.Mode == PowerModes.Resume) { DelayedReapply(4000); DelayedReapply(20000); } };
            SystemEvents.SessionSwitch += (o, e) => { if (e.Reason == SessionSwitchReason.SessionUnlock) DelayedReapply(3000); };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.DarkTitleBar(this); }

        protected override void SetVisibleCore(bool value)
        {
            if (hideOnStart) { hideOnStart = false; if (!IsHandleCreated) CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ShowMessage) ShowFromTray();
            base.WndProc(ref m);
        }

        void DelayedReapply(int ms)
        {
            var t = new Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); t.Dispose(); Push(true); };
            t.Start();
        }

        void UpdateEnabled()
        {
            var e = (Effect)Math.Max(0, cmbEffect.SelectedIndex);
            bool usesColor = e != Effect.Rainbow;
            foreach (Control c in new Control[] { numR, numG, numB, txtHex, swatch }) c.Enabled = usesColor;
            trkSpeed.Enabled = e != Effect.Static;
        }

        void Changed()
        {
            if (loading) return;
            var g = Gpu;
            g.On = chkOn.Checked; g.Effect = (Effect)Math.Max(0, cmbEffect.SelectedIndex);
            g.R = (int)numR.Value; g.G = (int)numG.Value; g.B = (int)numB.Value; g.Speed = trkSpeed.Value;
            settings.GpuLogo = chkLogo.Checked;
            settings.Brightness = trkBright.Value * 10;
            debounce.Stop(); debounce.Start();
        }

        void Push(bool force)
        {
            applier.Request(settings, force);
            try { settings.Save(); } catch { }
        }

        void SyncHex()
        {
            txtHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", (int)numR.Value, (int)numG.Value, (int)numB.Value);
            swatch.BackColor = Color.FromArgb((int)numR.Value, (int)numG.Value, (int)numB.Value);
        }

        void ParseHex()
        {
            string h = txtHex.Text.Trim().TrimStart('#');
            int v;
            if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out v)) SetColor(Color.FromArgb(v >> 16 & 255, v >> 8 & 255, v & 255));
            else SyncHex();
        }

        void SetColor(Color c)
        {
            if (!numR.Enabled) { loading = true; cmbEffect.SelectedIndex = 0; loading = false; UpdateEnabled(); }
            loading = true; numR.Value = c.R; numG.Value = c.G; numB.Value = c.B; loading = false;
            SyncHex(); Changed();
        }

        void PickColor()
        {
            using (var d = new ColorDialog { FullOpen = true, Color = swatch.BackColor })
                if (d.ShowDialog(this) == DialogResult.OK) SetColor(d.Color);
        }

        void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && IsHandleCreated) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            tray.Visible = false;
            try { settings.Save(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
