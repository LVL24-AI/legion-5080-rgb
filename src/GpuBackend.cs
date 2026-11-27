using System;
using System.Threading;

namespace LegionRgb
{
    /// GPU lighting: light chip on the Lenovo RTX card, reached through the NVIDIA driver's I2C
    /// (DDC line of output 0x100, port 1, chip address 0x92). Register map from Lenovo's lighting service:
    ///   0x15 mode (0 off, 1 always, 3 blinking, 4 rainbow), 0x17 brightness %, 0x18-0x1A R G B,
    ///   0x20-0x23 effect timing, 0x14 commit mode, 0x50 logo LED, 0x30/0x31/0x32 = B0/B1/B2 write handshake.
    public class GpuBackend : ILightingBackend
    {
        const uint DisplayMask = 0x100;
        const byte Port = 1, Dev = 0x92;

        readonly object sync = new object();
        ZoneState last; int lastBright = -1; bool lastLogo; bool written;

        public string Name { get { return "RTX GPU"; } }
        public bool IsPresent { get { return NvApi.Available; } }
        public bool TakeRestarted() { return false; }

        void W(byte reg, byte val)
        {
            int r = NvApi.WriteReg(DisplayMask, Port, Dev, reg, val);
            if (r != 0) throw new Exception("GPU I2C write failed (NvAPI status " + r + ")");
            Thread.Sleep(10);
        }

        public void Apply(Settings s, bool force)
        {
            lock (sync)
            {
                var z = s.Get(Zone.Gpu);
                if (!force && written && z.SameAs(last) && lastBright == s.Brightness && lastLogo == s.GpuLogo) return;

                byte mode = !z.On ? (byte)0 : z.Effect == Effect.Static ? (byte)1 : z.Effect == Effect.Breathing ? (byte)3 : (byte)4;
                int bright = Math.Max(10, Math.Min(100, s.Brightness));
                // Lenovo used 0x48 at 50% brightness in rainbow mode, and the plain percentage in the other modes.
                byte r17 = (byte)(mode == 4 ? Math.Min(100, (int)Math.Round(bright * 1.44)) : bright);
                int t1 = 0, t2 = 0;
                if (mode == 3) t1 = t2 = z.Speed >= 3 ? 1000 : z.Speed == 2 ? 2000 : 3000;   // Lenovo: 2000/2000
                if (mode == 4) t1 = z.Speed >= 3 ? 100 : z.Speed == 2 ? 200 : 300;            // Lenovo: 200

                W(0x30, 0xB0); W(0x40, 0x01);
                W(0x15, mode); W(0x16, 0x00); W(0x17, r17);
                W(0x18, (byte)z.R); W(0x19, (byte)z.G); W(0x1A, (byte)z.B);
                W(0x1B, 0x00); W(0x1C, 0xC8); W(0x1D, 0xFF);
                W(0x20, (byte)(t1 >> 8)); W(0x21, (byte)t1); W(0x22, (byte)(t2 >> 8)); W(0x23, (byte)t2);
                W(0x31, 0xB1); W(0x32, 0xB2);

                W(0x30, 0xB0); W(0x40, 0x01); W(0x14, mode); W(0x31, 0xB1);

                for (int i = 0; i < 4; i++) { W(0x30, 0xB0); W(0x50, (byte)(s.GpuLogo ? 1 : 0)); W(0x31, 0xB1); W(0x32, 0xB2); }

                last = z.Clone(); lastBright = s.Brightness; lastLogo = s.GpuLogo; written = true;
            }
        }
    }
}
