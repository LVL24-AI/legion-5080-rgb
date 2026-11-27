using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LegionRgb
{
    public class MainForm : Form
    {
        static readonly Color Bg = Ui.Bg, Panel2 = Ui.Panel2, Fg = Ui.Fg, Dim = Ui.Dim;

        public static readonly int ShowMessage = RegisterWindowMessage("LegionRgb.ShowWindow");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string s);

        static readonly Zone[] ZoneOrder = { Zone.Icon, Zone.RearFan, Zone.FrontFan, Zone.TopFan, Zone.Pump, Zone.Gpu };
        static readonly string[] ZoneNames = { "Legion icon", "Rear fan", "Front fan", "Top fan", "Water cooling pump", "GPU" };
        static readonly string[] CaseEffects = { "Always", "Breathing", "Spiral rainbow", "Color cycle" };
        static readonly string[] GpuEffects = { "Always", "Blinking", "Spiral rainbow" };

        readonly Settings settings;
        readonly Applier applier;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ListBox zoneList = new ListBox();
        readonly CheckBox chkOn = new CheckBox(), chkLogo = new CheckBox(), chkAutostart = new CheckBox();
        readonly ComboBox cmbEffect = new ComboBox(), cmbAmbient = new ComboBox();
        static readonly int[] AmbientModes = { 0, 1, 3, 2 };   // Off, On, Breathing, Blinking
        readonly Panel swatch = new Panel();
        readonly NumericUpDown numR = new NumericUpDown(), numG = new NumericUpDown(), numB = new NumericUpDown();
        readonly TextBox txtHex = new TextBox();
        readonly TrackBar trkBright = new TrackBar(), trkSpeed = new TrackBar();
        readonly Label lblBright = new Label(), lblStatus = new Label(), lblSpeed = new Label();
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 150 };
        bool loading, reallyExit, gpuSelected;

        public MainForm(Settings s, List<ILightingBackend> backends, bool startHidden)
        {
            settings = s;
            applier = new Applier(backends);
            applier.Status += msg => { try { BeginInvoke((Action)(() => lblStatus.Text = msg)); } catch { } };
            // After the case controller restarts, Windows and Lenovo's service may repaint it: paint ours again.
            applier.DeviceRestarted += () => { try { BeginInvoke((Action)(() => { DelayedReapply(4000); DelayedReapply(10000); })); } catch { } };

            Text = "Legion RGB";
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            ClientSize = new Size(440, 534);
            BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);
            Icon = MakeIcon();
            StartPosition = FormStartPosition.CenterScreen;

            // Left: components
            Controls.Add(Caption("Lighting component", 14, 12));
            zoneList.SetBounds(14, 34, 150, 170);
            zoneList.BackColor = Panel2; zoneList.ForeColor = Fg; zoneList.BorderStyle = BorderStyle.None;
            zoneList.ItemHeight = 22; zoneList.DrawMode = DrawMode.OwnerDrawFixed;
            zoneList.DrawItem += DrawZoneItem;
            zoneList.Items.Add("All case lights");
            zoneList.Items.AddRange(ZoneNames);
            zoneList.SelectedIndexChanged += delegate { LoadZone(); };
            Controls.Add(zoneList);

            // Right: settings of the selected component
            int x = 180, w = 246;
            chkOn.Text = "Light effect switch"; chkOn.SetBounds(x, 32, w, 24); chkOn.CheckedChanged += delegate { Changed(); };
            Controls.Add(chkOn);
            Controls.Add(Caption("Lighting effect", x, 62));
            cmbEffect.SetBounds(x, 84, w, 26); cmbEffect.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEffect.FlatStyle = FlatStyle.Flat; cmbEffect.BackColor = Panel2; cmbEffect.ForeColor = Fg;
            cmbEffect.SelectedIndexChanged += delegate { UpdateEnabled(); Changed(); };
            Controls.Add(cmbEffect);

            Controls.Add(Caption("Color", x, 118));
            swatch.SetBounds(x, 140, 36, 25); swatch.Cursor = Cursors.Hand; swatch.BorderStyle = BorderStyle.FixedSingle;
            swatch.Click += delegate { PickColor(); };
            Controls.Add(swatch);
            int nx = x + 44;
            foreach (var n in new[] { numR, numG, numB })
            {
                n.SetBounds(nx, 141, 60, 24); n.Maximum = 255; n.BackColor = Panel2; n.ForeColor = Fg; n.BorderStyle = BorderStyle.FixedSingle;
                n.ValueChanged += delegate { if (!loading) { SyncHex(); Changed(); } };
                Controls.Add(n); nx += 67;
            }
            txtHex.SetBounds(x, 172, 90, 24); txtHex.BackColor = Panel2; txtHex.ForeColor = Fg; txtHex.BorderStyle = BorderStyle.FixedSingle;
            txtHex.Leave += delegate { ParseHex(); };
            txtHex.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { ParseHex(); e.SuppressKeyPress = true; } };
            Controls.Add(txtHex);
            var palette = new[] { Color.FromArgb(255, 0, 0), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 220, 0), Color.FromArgb(0, 255, 80), Color.FromArgb(0, 211, 254), Color.FromArgb(0, 60, 255), Color.FromArgb(160, 0, 255), Color.FromArgb(255, 0, 160), Color.White };
            int px = x + 98;
            foreach (var c in palette)
            {
                var p = new Panel { BackColor = c, Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle };
                p.SetBounds(px, 175, 15, 18); px += 16;
                var col = c; p.Click += delegate { SetColor(col); };
                Controls.Add(p);
            }

            lblSpeed.Text = "Speed"; lblSpeed.ForeColor = Dim; lblSpeed.AutoSize = true; lblSpeed.Location = new Point(x, 204);
            Controls.Add(lblSpeed);
            trkSpeed.SetBounds(x, 222, w, 30); trkSpeed.Minimum = 1; trkSpeed.Maximum = 3; trkSpeed.TickStyle = TickStyle.BottomRight;
            trkSpeed.ValueChanged += delegate { Changed(); };
            Controls.Add(trkSpeed);

            chkLogo.Text = "GPU logo LED"; chkLogo.SetBounds(14, 212, 150, 24);
            chkLogo.CheckedChanged += delegate { if (!loading) { settings.GpuLogo = chkLogo.Checked; Changed(); } };
            Controls.Add(chkLogo);

            Controls.Add(Caption("Top ambient light", 14, 242));
            cmbAmbient.SetBounds(14, 262, 150, 26); cmbAmbient.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAmbient.FlatStyle = FlatStyle.Flat; cmbAmbient.BackColor = Panel2; cmbAmbient.ForeColor = Fg;
            cmbAmbient.Items.AddRange(new object[] { "Off", "On", "Breathing", "Blinking" });
            cmbAmbient.SelectedIndexChanged += delegate { if (!loading) { settings.Ambient = AmbientModes[cmbAmbient.SelectedIndex]; Changed(); } };
            Controls.Add(cmbAmbient);

            // Global brightness
            lblBright.ForeColor = Dim; lblBright.AutoSize = true; lblBright.Location = new Point(14, 300);
            Controls.Add(lblBright);
            trkBright.SetBounds(14, 320, 412, 30); trkBright.Minimum = 1; trkBright.Maximum = 10; trkBright.TickStyle = TickStyle.BottomRight;
            trkBright.ValueChanged += delegate { settings.Brightness = trkBright.Value * 10; lblBright.Text = "Brightness  " + settings.Brightness + "%"; if (!loading) Changed(); };
            Controls.Add(trkBright);
            ClickToPosition(trkBright);
            ClickToPosition(trkSpeed);

            // Themes (same as Legion Space)
            Controls.Add(Caption("Lighting themes", 14, 362));
            int bx = 14;
            foreach (var t in new[] { "Default", "Blue", "Breath", "GPU only", "Off" })
            {
                var btn = new Button { Text = t, FlatStyle = FlatStyle.Flat, BackColor = Panel2, ForeColor = Fg };
                btn.FlatAppearance.BorderColor = Color.FromArgb(75, 75, 80);
                btn.SetBounds(bx, 384, 78, 30); bx += 84;
                string name = t; btn.Click += delegate { Theme(name); };
                Controls.Add(btn);
            }

            chkAutostart.Text = "Start with Windows (in the tray)"; chkAutostart.SetBounds(14, 428, 300, 24);
            chkAutostart.Checked = IsAutostart();
            chkAutostart.CheckedChanged += delegate { SetAutostart(chkAutostart.Checked); };
            Controls.Add(chkAutostart);

            var note = new Label { Text = "Closing the window keeps the app in the tray (needed for breathing, color cycle and per-part rainbow on the case). Use Exit in the tray menu to quit.", ForeColor = Dim, Font = new Font("Segoe UI", 8.25f) };
            note.SetBounds(14, 456, 412, 34);
            Controls.Add(note);
            lblStatus.SetBounds(14, 500, 412, 22); lblStatus.ForeColor = Dim;
            Controls.Add(lblStatus);

            debounce.Tick += delegate { debounce.Stop(); Push(false); };

            // Tray
            tray.Icon = Icon; tray.Text = "Legion RGB"; tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { ShowFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            foreach (var t in new[] { "Default", "Blue", "Breath", "GPU only", "Off" }) { string name = t; menu.Items.Add(name, null, delegate { Theme(name); }); }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Re-apply lighting", null, delegate { Push(true); });
            menu.Items.Add("Exit", null, delegate { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

            loading = true;
            trkBright.Value = Math.Max(1, Math.Min(10, settings.Level));
            lblBright.Text = "Brightness  " + trkBright.Value * 10 + "%";
            chkLogo.Checked = settings.GpuLogo;
            cmbAmbient.SelectedIndex = Math.Max(0, Array.IndexOf(AmbientModes, settings.Ambient));
            loading = false;
            zoneList.SelectedIndex = 0;

            hideOnStart = startHidden;

            // Lenovo's service re-applies its own profile at boot and after sleep, so re-apply ours afterwards.
            Push(true);
            DelayedReapply(10000); DelayedReapply(45000);
            SystemEvents.PowerModeChanged += (o, e) => { if (e.Mode == PowerModes.Resume) { DelayedReapply(4000); DelayedReapply(20000); } };
            SystemEvents.SessionSwitch += (o, e) => { if (e.Reason == SessionSwitchReason.SessionUnlock) DelayedReapply(3000); };
        }

        bool hideOnStart;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.DarkTitleBar(this);
        }

        protected override void SetVisibleCore(bool value)
        {
            // Start hidden in the tray without flashing the window (the handle still exists for messages).
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
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); t.Dispose(); Push(true); };
            t.Start();
        }

        static Label Caption(string t, int x, int y) { return Ui.Caption(t, x, y); }
        static void ClickToPosition(TrackBar t) { Ui.ClickToPosition(t); }

        void DrawZoneItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Color.FromArgb(70, 70, 78) : Panel2)) e.Graphics.FillRectangle(b, e.Bounds);
            Color dot = Color.Gray;
            if (e.Index > 0)
            {
                var st = settings.Get(ZoneOrder[e.Index - 1]);
                dot = !st.On ? Color.FromArgb(60, 60, 60) : st.Effect == Effect.Rainbow || st.Effect == Effect.ColorCycle ? Color.Violet : Color.FromArgb(st.R, st.G, st.B);
            }
            using (var b = new SolidBrush(dot)) e.Graphics.FillEllipse(b, e.Bounds.X + 6, e.Bounds.Y + 7, 9, 9);
            TextRenderer.DrawText(e.Graphics, zoneList.Items[e.Index].ToString(), Font, new Point(e.Bounds.X + 20, e.Bounds.Y + 2), Fg);
        }

        IEnumerable<Zone> SelectedZones()
        {
            int i = zoneList.SelectedIndex;
            if (i <= 0) return Settings.CaseZones;
            return new[] { ZoneOrder[i - 1] };
        }

        void LoadZone()
        {
            loading = true;
            var zones = SelectedZones().ToArray();
            var s = settings.Get(zones[0]);
            gpuSelected = zones.Length == 1 && zones[0] == Zone.Gpu;
            cmbEffect.Items.Clear();
            cmbEffect.Items.AddRange(gpuSelected ? GpuEffects : CaseEffects);
            cmbEffect.SelectedIndex = Math.Min((int)s.Effect, cmbEffect.Items.Count - 1);
            chkOn.Checked = s.On;
            numR.Value = s.R; numG.Value = s.G; numB.Value = s.B;
            trkSpeed.Value = Math.Max(1, Math.Min(3, s.Speed));
            SyncHex();
            UpdateEnabled();
            loading = false;
        }

        void UpdateEnabled()
        {
            var e = (Effect)Math.Max(0, cmbEffect.SelectedIndex);
            bool usesColor = e == Effect.Static || e == Effect.Breathing;
            foreach (Control c in new Control[] { numR, numG, numB, txtHex, swatch }) c.Enabled = usesColor;
            trkSpeed.Enabled = e != Effect.Static;
            lblSpeed.Text = "Speed (slow - fast)";
        }

        void Changed()
        {
            if (loading) return;
            foreach (var z in SelectedZones())
            {
                var s = settings.Get(z);
                s.On = chkOn.Checked; s.Effect = (Effect)Math.Max(0, cmbEffect.SelectedIndex);
                s.R = (int)numR.Value; s.G = (int)numG.Value; s.B = (int)numB.Value; s.Speed = trkSpeed.Value;
            }
            zoneList.Invalidate();
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

        void Theme(string name)
        {
            var gpu = settings.Get(Zone.Gpu);
            foreach (Zone z in ZoneOrder)
            {
                var s = settings.Get(z);
                switch (name)
                {
                    case "Default": s.On = true; s.Effect = Effect.Rainbow; s.Speed = 2; break;
                    case "Blue": s.On = true; s.Effect = Effect.Static; s.R = 0; s.G = 211; s.B = 254; break;
                    case "Breath": s.On = true; s.Effect = Effect.Breathing; s.R = 0; s.G = 211; s.B = 254; s.Speed = 2; break;
                    case "GPU only": s.On = z == Zone.Gpu; break;
                    case "Off": s.On = false; break;
                }
            }
            settings.GpuLogo = name == "Default" || name == "Blue" || name == "GPU only";
            if (name == "Off") settings.Ambient = 0;   // other themes leave the top ambient light as it is
            loading = true; chkLogo.Checked = settings.GpuLogo; cmbAmbient.SelectedIndex = Math.Max(0, Array.IndexOf(AmbientModes, settings.Ambient)); loading = false;
            LoadZone(); zoneList.Invalidate();
            Push(false);
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

        static bool IsAutostart() { return Ui.IsAutostart("LegionRgb"); }
        static void SetAutostart(bool on) { Ui.SetAutostart("LegionRgb", on); }
        static Icon MakeIcon() { return Ui.MakeIcon(Color.FromArgb(255, 40, 90), Color.FromArgb(0, 200, 255), "L"); }
    }
}
