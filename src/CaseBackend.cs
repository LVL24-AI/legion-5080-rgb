using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LegionRgb
{
    /// Case lighting: Lenovo controller USB 17EF:C955.
    ///  - MI_01 is a standard HID LampArray (70 lamps) used for per-zone colors ("host mode").
    ///  - MI_00 report 0xCC is Lenovo's vendor channel: firmware effects, global brightness and host/firmware switch.
    /// Command sequences mirror what Lenovo's lighting service sends (captured with tools\LightTrace).
    public class CaseBackend : ILightingBackend
    {
        // Lamp index ranges per zone, from the LampArray lamp positions.
        public static readonly Dictionary<Zone, int[]> Ranges = new Dictionary<Zone, int[]>
        {
            { Zone.FrontFan, new[] { 0, 11 } },
            { Zone.Icon,     new[] { 12, 27 } },
            { Zone.TopFan,   new[] { 28, 39 } },
            { Zone.RearFan,  new[] { 40, 51 } },
            { Zone.Pump,     new[] { 52, 69 } },
        };
        const int LampCount = 70;

        readonly object sync = new object();
        SafeFileHandle lamp, vendor;
        int lampLen = 51, vendorLen = 64;
        Settings current;          // what is on the hardware now
        Timer anim;
        DateTime animStart = DateTime.UtcNow;
        public string LastError;
        /// Set when the controller restarted (ambient light change); Lenovo's service may repaint after that.
        public bool Restarted;
        public bool TakeRestarted() { bool r = Restarted; Restarted = false; return r; }

        public string Name { get { return "Legion case"; } }
        public bool IsPresent { get { return Hid.Find(0x17EF, 0xC955).Any(d => d.UsagePage == 0x59); } }

        bool Open()
        {
            if (lamp != null && !lamp.IsInvalid && vendor != null && !vendor.IsInvalid) return true;
            Close();
            foreach (var d in Hid.Find(0x17EF, 0xC955))
            {
                if (d.UsagePage == 0x59) { lamp = Hid.OpenRw(d.Path); lampLen = d.FeatLen; }
                else if (d.UsagePage == 0xFF89 && d.Usage == 0xCC) { vendor = Hid.OpenRw(d.Path); vendorLen = d.FeatLen; }
            }
            return lamp != null && !lamp.IsInvalid && vendor != null && !vendor.IsInvalid;
        }

        void Close()
        {
            if (lamp != null) lamp.Dispose();
            if (vendor != null) vendor.Dispose();
            lamp = vendor = null;
        }

        /// Feature write to the LampArray (toLamp) or the vendor collection. If the handle went stale because the
        /// controller restarted (Lenovo's service can trigger that too), reopen once and retry.
        void Send(bool toLamp, params byte[] data)
        {
            for (int attempt = 0; ; attempt++)
            {
                var h = toLamp ? lamp : vendor; int len = toLamp ? lampLen : vendorLen;
                var b = new byte[len];
                Array.Copy(data, b, Math.Min(data.Length, len));
                if (h != null && !h.IsInvalid && Hid.HidD_SetFeature(h, b, len)) return;
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Close();
                if (attempt >= 1) throw new Exception("case controller write failed (error " + err + ")");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                do Thread.Sleep(300); while (!Open() && sw.ElapsedMilliseconds < 6000);
            }
        }

        void Vendor(params byte[] cmd) { var b = new byte[cmd.Length + 1]; b[0] = 0xCC; cmd.CopyTo(b, 1); Send(false, b); }

        void Autonomous(bool on) { Send(true, 0x06, (byte)(on ? 1 : 0)); }

        // LampRangeUpdateReport (id 5): flags, start(16), end(16), R, G, B, I
        void Range(int start, int end, int r, int g, int b, bool complete)
        {
            Send(true, 0x05, (byte)(complete ? 1 : 0), (byte)start, (byte)(start >> 8), (byte)end, (byte)(end >> 8), (byte)r, (byte)g, (byte)b, 0xFF);
        }

        // LampMultiUpdateReport (id 4): count, flags, 8 x lampId(16), 8 x (R, G, B, I)
        void Multi(int[] ids, int[][] rgb, bool complete)
        {
            var b = new byte[3 + 16 + 32];
            b[0] = 0x04; b[1] = (byte)ids.Length; b[2] = (byte)(complete ? 1 : 0);
            for (int i = 0; i < ids.Length; i++)
            {
                b[3 + 2 * i] = (byte)ids[i]; b[4 + 2 * i] = (byte)(ids[i] >> 8);
                b[19 + 4 * i] = (byte)rgb[i][0]; b[20 + 4 * i] = (byte)rgb[i][1]; b[21 + 4 * i] = (byte)rgb[i][2]; b[22 + 4 * i] = 0xFF;
            }
            Send(true, b);
        }

        static bool AllRainbow(Settings s) { return Settings.CaseZones.All(z => s.Get(z).On && s.Get(z).Effect == Effect.Rainbow); }
        static bool Animated(Settings s) { return Settings.CaseZones.Any(z => s.Get(z).On && s.Get(z).Effect != Effect.Static); }

        /// CC 45 01 enables the controller's lighting (Legion Space's "Off" theme sends 45 00, after which the
        /// LampArray colors no longer show). Changing it makes the controller restart and re-enumerate, so if the
        /// handles die afterwards, wait for it to come back and let the caller repaint again later.
        void EnsureEnabled()
        {
            if (enabledSent) return;
            Vendor(0x45, 0x01);
            enabledSent = true;
            Thread.Sleep(1000);
            var probe = new byte[lampLen]; probe[0] = 1;
            if (lamp != null && Hid.HidD_GetFeature(lamp, probe, probe.Length)) return;   // no restart happened
            Close();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            do Thread.Sleep(300); while (!Open() && sw.ElapsedMilliseconds < 8000);
            Thread.Sleep(500);
            Restarted = true;
        }
        bool enabledSent;

        /// Top ambient light = single-color zone 0x18: CC 18 <0 off (fades out), 1 on, 2 blinking, 3 breathing> <speed> <level 1-10>.
        void Ambient(Settings s)
        {
            int mode = s.Ambient >= 0 && s.Ambient <= 3 ? s.Ambient : 0;
            Vendor(0x18, (byte)mode, 0x02, (byte)s.Level);
        }

        public void Apply(Settings s, bool force)
        {
            lock (sync)
            {
                if (!force && current != null && current.Level == s.Level && current.Ambient == s.Ambient && Settings.CaseZones.All(z => current.Get(z).SameAs(s.Get(z)))) return;
                StopAnim();
                if (!Open()) throw new Exception("case controller not found");
                if (force) enabledSent = false;
                EnsureEnabled();
                if (!Open()) throw new Exception("case controller did not come back after restart");
                byte level = (byte)s.Level;
                if (AllRainbow(s))
                {
                    // Firmware rainbow, Lenovo's "Default" theme sequence. Runs with no software at all.
                    byte speed = (byte)Math.Max(1, Math.Min(3, s.Get(Zone.Icon).Speed));
                    Vendor(0x1F, 0x09, speed, level);   // fans + pump
                    Vendor(0x17, 0x09, speed, level);   // Legion icon
                    Ambient(s);
                    Vendor(0x2D, 0xFF, 0x00);           // apply
                    Autonomous(true);
                }
                else
                {
                    // Host mode (Lenovo's custom-theme sequence), then paint the zones through the LampArray.
                    Vendor(0x1F, 0x00);
                    Vendor(0x17, 0x00);
                    Ambient(s);
                    Vendor(0x2D, 0xFF, 0x01);
                    Autonomous(false);
                    current = s.Clone();
                    Render(current, 0);
                    if (Animated(current)) { animStart = DateTime.UtcNow; anim = new Timer(Tick, null, 40, 40); }
                }
                current = s.Clone();
            }
        }

        /// Puts the controller back on its own firmware rainbow (used on exit if the user wants that).
        public void Release()
        {
            lock (sync) { StopAnim(); current = null; Close(); }
        }

        void StopAnim() { if (anim != null) { anim.Dispose(); anim = null; } }

        void Tick(object _)
        {
            if (!Monitor.TryEnter(sync)) return;
            try
            {
                if (anim == null || current == null) return;
                Render(current, (DateTime.UtcNow - animStart).TotalSeconds);
                LastError = null;
            }
            catch (Exception e) { LastError = e.Message; }
            finally { Monitor.Exit(sync); }
        }

        static double Period(int speed) { return speed >= 3 ? 1.5 : speed == 2 ? 3.0 : 6.0; }

        static int[] Hsv(double h)
        {
            h = (h % 1 + 1) % 1 * 6; int i = (int)h; double f = h - i, q = 1 - f;
            double r = 0, g = 0, b = 0;
            switch (i) { case 0: r = 1; g = f; break; case 1: r = q; g = 1; break; case 2: g = 1; b = f; break; case 3: g = q; b = 1; break; case 4: r = f; b = 1; break; default: r = 1; b = q; break; }
            return new[] { (int)(r * 255), (int)(g * 255), (int)(b * 255) };
        }

        void Render(Settings s, double t)
        {
            var perLamp = new List<KeyValuePair<int, int[]>>();
            var ranges = new List<int[]>();   // start, end, r, g, b
            foreach (var z in Settings.CaseZones)
            {
                var st = s.Get(z); var rg = Ranges[z];
                if (!st.On) { ranges.Add(new[] { rg[0], rg[1], 0, 0, 0 }); continue; }
                double p = Period(st.Speed);
                switch (st.Effect)
                {
                    case Effect.Static: ranges.Add(new[] { rg[0], rg[1], st.R, st.G, st.B }); break;
                    case Effect.Breathing:
                        double k = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / p); k = 0.03 + 0.97 * k * k;
                        ranges.Add(new[] { rg[0], rg[1], (int)(st.R * k), (int)(st.G * k), (int)(st.B * k) }); break;
                    case Effect.ColorCycle:
                        var c = Hsv(t / (p * 4)); ranges.Add(new[] { rg[0], rg[1], c[0], c[1], c[2] }); break;
                    case Effect.Rainbow:
                        int n = rg[1] - rg[0] + 1;
                        for (int i = 0; i < n; i++) perLamp.Add(new KeyValuePair<int, int[]>(rg[0] + i, Hsv((double)i / Math.Min(n, 12) - t / p)));
                        break;
                }
            }
            // Brightness: LampArray colors are not dimmed by the controller, so scale them here.
            double kb = Math.Max(10, Math.Min(100, s.Brightness)) / 100.0;
            Func<int, int> dim = v => (int)Math.Round(v * kb);
            int total = ranges.Count + (perLamp.Count + 7) / 8, sent = 0;
            foreach (var r in ranges) Range(r[0], r[1], dim(r[2]), dim(r[3]), dim(r[4]), ++sent == total);
            for (int i = 0; i < perLamp.Count; i += 8)
            {
                var chunk = perLamp.Skip(i).Take(8).ToArray();
                Multi(chunk.Select(c => c.Key).ToArray(), chunk.Select(c => c.Value.Select(dim).ToArray()).ToArray(), ++sent == total);
            }
        }
    }
}
