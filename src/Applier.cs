using System;
using System.Collections.Generic;
using System.Threading;

namespace LegionRgb
{
    /// Runs hardware writes on one background thread; only the newest request is applied.
    public class Applier
    {
        readonly List<ILightingBackend> backends;
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly object sync = new object();
        Settings pending; bool pendingForce;
        public event Action<string> Status;
        public event Action DeviceRestarted;

        public Applier(List<ILightingBackend> b)
        {
            backends = b;
            new Thread(Loop) { IsBackground = true, Name = "applier" }.Start();
        }

        public void Request(Settings s, bool force)
        {
            lock (sync) { pending = s.Clone(); pendingForce |= force; }
            signal.Set();
        }

        void Loop()
        {
            while (true)
            {
                signal.WaitOne();
                Settings s; bool force;
                lock (sync) { s = pending; force = pendingForce; pending = null; pendingForce = false; }
                if (s == null) continue;
                var errors = new List<string>();
                bool restarted = false;
                foreach (var b in backends)
                {
                    try { if (b.IsPresent) b.Apply(s, force); }
                    catch (Exception e) { errors.Add(b.Name + ": " + e.Message); }
                    restarted |= b.TakeRestarted();
                }
                var h = Status;
                if (h != null) h(errors.Count == 0 ? "Applied " + DateTime.Now.ToString("HH:mm:ss") : string.Join("  |  ", errors));
                var r = DeviceRestarted;
                if (restarted && r != null) r();
            }
        }
    }
}
