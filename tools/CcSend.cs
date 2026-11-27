using System;
using System.Linq;
using System.Threading;
using LegionRgb;

// Sends Lenovo vendor commands (feature report 0xCC) to the case controller 17EF:C955.
// Usage: CcSend "45 00" 6 "45 01" 6 ...   (hex bytes after 0xCC, then seconds to wait)
// The device is reopened for every command because some commands make the controller restart.
class CcSend
{
    static bool SendOnce(byte[] bytes)
    {
        var dev = Hid.Find(0x17EF, 0xC955).FirstOrDefault(d => d.UsagePage == 0xFF89 && d.Usage == 0xCC);
        if (dev == null) return false;
        using (var h = Hid.OpenRw(dev.Path))
        {
            if (h.IsInvalid) return false;
            var buf = new byte[dev.FeatLen]; Array.Copy(bytes, buf, bytes.Length);
            return Hid.HidD_SetFeature(h, buf, buf.Length);
        }
    }

    static void Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            int wait;
            if (int.TryParse(args[i], out wait) && !args[i].Contains(" ")) { Thread.Sleep(wait * 1000); continue; }
            var bytes = new[] { (byte)0xCC }.Concat(args[i].Split(' ').Select(x => Convert.ToByte(x, 16))).ToArray();
            bool ok = false; int tries = 0;
            while (!ok && tries++ < 20) { ok = SendOnce(bytes); if (!ok) Thread.Sleep(250); }
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.f") + "  CC " + args[i] + " -> " + (ok ? "ok" : "FAILED") + (tries > 1 ? " (after " + tries + " tries)" : ""));
        }
    }
}
