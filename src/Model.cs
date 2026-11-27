using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LegionRgb
{
    public enum Zone { Icon, RearFan, FrontFan, TopFan, Pump, Gpu }

    /// Static = "Always"; Breathing = "Breathing" on the case, "Blinking" on the GPU; Rainbow = "Spiral rainbow".
    public enum Effect { Static, Breathing, Rainbow, ColorCycle }

    [DataContract]
    public class ZoneState
    {
        [DataMember] public bool On = true;
        [DataMember] public Effect Effect = Effect.Rainbow;
        [DataMember] public int R = 0, G = 211, B = 254;
        [DataMember] public int Speed = 2;   // 1 slow .. 3 fast

        public ZoneState Clone() { return (ZoneState)MemberwiseClone(); }
        public bool SameAs(ZoneState o) { return o != null && On == o.On && Effect == o.Effect && R == o.R && G == o.G && B == o.B && Speed == o.Speed; }
    }

    [DataContract]
    public class Settings
    {
        public static readonly Zone[] CaseZones = { Zone.Icon, Zone.RearFan, Zone.FrontFan, Zone.TopFan, Zone.Pump };

        [DataMember] public int Brightness = 70;      // 10..100 in steps of 10 (hardware has 10 levels)
        [DataMember] public bool GpuLogo = true;
        [DataMember] public int Ambient = 0;          // top ambient light: 0 off, 1 on, 2 blinking, 3 breathing (Lenovo "Atmospherelight")
        [DataMember] public bool StartWithWindows = false;
        [DataMember] public Dictionary<string, ZoneState> Zones = new Dictionary<string, ZoneState>();

        public ZoneState Get(Zone z)
        {
            ZoneState s;
            if (Zones == null) Zones = new Dictionary<string, ZoneState>();
            if (!Zones.TryGetValue(z.ToString(), out s)) { s = new ZoneState(); Zones[z.ToString()] = s; }
            return s;
        }

        public int Level { get { return Math.Max(1, Math.Min(10, (Brightness + 5) / 10)); } }

        public Settings Clone()
        {
            var c = (Settings)MemberwiseClone();
            c.Zones = new Dictionary<string, ZoneState>();
            foreach (var z in (Zone[])Enum.GetValues(typeof(Zone))) c.Zones[z.ToString()] = Get(z).Clone();
            return c;
        }

        /// Settings folder under %APPDATA% (each app keeps its own).
        public static string AppFolder = "LegionRgb";
        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }
        static DataContractJsonSerializer Ser { get { return new DataContractJsonSerializer(typeof(Settings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }); } }

        public static bool Exists() { return File.Exists(FilePath); }

        public static Settings Load()
        {
            try { using (var f = File.OpenRead(FilePath)) return (Settings)Ser.ReadObject(f); }
            catch { return new Settings(); }
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            using (var f = File.Create(tmp)) Ser.WriteObject(f, this);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }
    }

    /// Hardware backend: the case controller (USB 17EF:C955) or the GPU light chip (NVIDIA I2C).
    public interface ILightingBackend
    {
        string Name { get; }
        bool IsPresent { get; }
        void Apply(Settings s, bool force);
        /// True once after the device restarted itself (others may repaint it, so the caller re-applies later).
        bool TakeRestarted();
    }
}
