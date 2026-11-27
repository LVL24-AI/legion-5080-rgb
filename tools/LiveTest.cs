using System;
using System.Threading;
using LegionRgb;

// Scripted live test of the app backends. Each step holds long enough for a person to look at the PC.
// Ends by restoring Lenovo's "Default" theme (case firmware rainbow, GPU rainbow, logo on, brightness 7).
class LiveTest
{
    static void Step(string name, int seconds, Action a)
    {
        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + name);
        try { a(); } catch (Exception e) { Console.WriteLine("    ERROR: " + e.Message); }
        Thread.Sleep(seconds * 1000);
    }

    static Settings Default()
    {
        var s = new Settings { Brightness = 70, GpuLogo = true };
        foreach (Zone z in Enum.GetValues(typeof(Zone))) { var st = s.Get(z); st.On = true; st.Effect = Effect.Rainbow; st.Speed = 2; st.R = 0; st.G = 211; st.B = 254; }
        return s;
    }

    static void Main(string[] args)
    {
        string what = args.Length > 0 ? args[0] : "all";
        var cas = new CaseBackend();
        var gpu = new GpuBackend();
        Console.WriteLine("case present: " + cas.IsPresent + ", gpu present: " + gpu.IsPresent);

        if (what == "all" || what == "case")
        {
            var s = Default(); s.Brightness = 100;
            Action<Zone, int, int, int> set = (z, r, g, b) => { var st = s.Get(z); st.Effect = Effect.Static; st.R = r; st.G = g; st.B = b; };
            set(Zone.FrontFan, 255, 0, 0); set(Zone.Icon, 0, 255, 0); set(Zone.TopFan, 0, 0, 255); set(Zone.RearFan, 255, 255, 255); set(Zone.Pump, 255, 0, 255);
            Step("C1 case zones: front fan RED, Legion icon GREEN, top fan BLUE, rear fan WHITE, pump PURPLE (brightness 100%)", 15, () => cas.Apply(s, true));
            s.Brightness = 30;
            Step("C2 same colors at brightness 30%", 6, () => cas.Apply(s, true));
            s.Brightness = 100;
            foreach (var z in Settings.CaseZones) s.Get(z).Effect = Effect.Breathing;
            Step("C3 all case zones breathing (software)", 8, () => cas.Apply(s, true));
            foreach (var z in Settings.CaseZones) s.Get(z).Effect = Effect.Rainbow;
            s.Get(Zone.Pump).Effect = Effect.Static;   // keep host mode so the software rainbow runs
            Step("C4 software spiral rainbow on fans + icon, pump stays purple", 8, () => cas.Apply(s, true));
        }
        if (what == "all" || what == "gpu")
        {
            var s = Default(); s.Brightness = 100;
            var g = s.Get(Zone.Gpu);
            g.Effect = Effect.Static; g.R = 255; g.G = 0; g.B = 0;
            Step("G1 GPU always RED, logo ON", 8, () => gpu.Apply(s, true));
            g.R = 0; g.B = 255;
            Step("G2 GPU always BLUE", 6, () => gpu.Apply(s, true));
            g.Effect = Effect.Breathing;
            Step("G3 GPU blinking blue", 8, () => gpu.Apply(s, true));
            g.Effect = Effect.Rainbow;
            Step("G4 GPU spiral rainbow", 8, () => gpu.Apply(s, true));
            s.GpuLogo = false;
            Step("G5 GPU logo OFF (rainbow continues)", 6, () => gpu.Apply(s, true));
            g.On = false;
            Step("G6 GPU light switch OFF", 6, () => gpu.Apply(s, true));
        }
        Step("Restoring Default theme (rainbow everywhere, logo on, brightness 70%)", 1, () => { var d = Default(); cas.Apply(d, true); gpu.Apply(d, true); });
        Console.WriteLine("done");
    }
}
