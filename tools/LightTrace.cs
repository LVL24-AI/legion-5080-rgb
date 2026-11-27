// LightTrace: read-only tracer for the Lenovo lighting stack.
// Attaches as a debugger to the Lenovo SmartEngine process that hosts SEBasicLighting64.dll and logs the
// arguments of HidD_SetFeature / HidD_GetFeature, NvAPI I2C calls and D3DKMT escapes (how NvAPI reaches the
// NVIDIA kernel driver). It never sends anything to hardware itself. On exit it restores every patched byte
// and detaches, leaving the target running.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

class LightTrace
{
    const uint DBG_CONTINUE = 0x00010002, DBG_EXCEPTION_NOT_HANDLED = 0x80010001;
    const uint EXCEPTION_BREAKPOINT = 0x80000003, EXCEPTION_SINGLE_STEP = 0x80000004;

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcess(int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcessStop(int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugSetProcessKillOnExit(bool k);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WaitForDebugEvent(IntPtr ev, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint a, bool i, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenThread(uint a, bool i, int tid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FlushInstructionCache(IntPtr h, IntPtr addr, IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetThreadContext(IntPtr h, IntPtr ctx);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetThreadContext(IntPtr h, IntPtr ctx);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LoadLibrary(string n);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr m, string n);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr NvQI(uint id);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool LookupPrivilegeValue(string s, string n, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr t, bool d, byte[] s, int l, IntPtr p, IntPtr r);

    class Proc
    {
        public int Pid; public IntPtr H; public string Name;
        public Dictionary<long, byte> Orig = new Dictionary<long, byte>();   // armed hooks -> original byte
        public Dictionary<int, long> Rearm = new Dictionary<int, long>();    // thread -> hook being stepped over
        public bool InitialBpSeen;
    }

    static readonly Dictionary<long, string> hooks = new Dictionary<long, string>();
    static readonly Dictionary<long, byte[]> hookBytes = new Dictionary<long, byte[]>(); // first bytes in our process
    static readonly Dictionary<int, Proc> procs = new Dictionary<int, Proc>();
    static readonly Dictionary<string, int> seen = new Dictionary<string, int>();
    static StreamWriter log;

    static void Log(string s)
    {
        string l = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
        Console.WriteLine(l.Length > 300 ? l.Substring(0, 300) + " ..." : l);
        log.WriteLine(l); log.Flush();
    }

    static byte[] Read(IntPtr h, long addr, int n)
    {
        if (n <= 0 || addr == 0) return new byte[0];
        n = Math.Min(n, 4096);
        var b = new byte[n]; IntPtr r;
        return ReadProcessMemory(h, new IntPtr(addr), b, new IntPtr(n), out r) ? b : new byte[0];
    }
    static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", " "); }

    static void EnableDebugPriv()
    {
        IntPtr t; if (!OpenProcessToken(GetCurrentProcess(), 0x28, out t)) return;
        long luid; LookupPrivilegeValue(null, "SeDebugPrivilege", out luid);
        var tp = new byte[16]; BitConverter.GetBytes(1).CopyTo(tp, 0); BitConverter.GetBytes(luid).CopyTo(tp, 4); BitConverter.GetBytes(2).CopyTo(tp, 12);
        AdjustTokenPrivileges(t, false, tp, 16, IntPtr.Zero, IntPtr.Zero);
    }

    static void AddHook(IntPtr a, string name)
    {
        if (a == IntPtr.Zero) { Console.WriteLine("hook missing: " + name); return; }
        hooks[a.ToInt64()] = name;
        var b = new byte[8]; Marshal.Copy(a, b, 0, 8); hookBytes[a.ToInt64()] = b;
    }

    static void Arm(Proc p)
    {
        int n = 0;
        foreach (var kv in hooks)
        {
            if (p.Orig.ContainsKey(kv.Key) || p.Rearm.ContainsValue(kv.Key)) continue;
            var o = Read(p.H, kv.Key, 8);
            if (o.Length != 8 || !o.SequenceEqual(hookBytes[kv.Key])) continue;   // module not loaded there or different code
            IntPtr w;
            if (WriteProcessMemory(p.H, new IntPtr(kv.Key), new byte[] { 0xCC }, new IntPtr(1), out w))
            {
                FlushInstructionCache(p.H, new IntPtr(kv.Key), new IntPtr(1));
                p.Orig[kv.Key] = o[0]; n++;
                Log(string.Format("  armed {0} in {1} ({2})", kv.Value, p.Name, p.Pid));
            }
        }
    }

    static void Disarm(Proc p)
    {
        foreach (var kv in p.Orig)
        {
            IntPtr w; WriteProcessMemory(p.H, new IntPtr(kv.Key), new byte[] { kv.Value }, new IntPtr(1), out w);
            FlushInstructionCache(p.H, new IntPtr(kv.Key), new IntPtr(1));
        }
    }

    static void DumpCall(Proc p, string name, IntPtr ctx, int tid)
    {
        long rcx = Marshal.ReadInt64(ctx, 0x80), rdx = Marshal.ReadInt64(ctx, 0x88), r8 = Marshal.ReadInt64(ctx, 0xB8), r9 = Marshal.ReadInt64(ctx, 0xC0);
        var sb = new StringBuilder();
        string key;
        if (name.StartsWith("HidD_"))
        {
            string data = Hex(Read(p.H, rdx, (int)Math.Min(r8, 512)));
            sb.AppendFormat("{0} handle=0x{1:X} len={2} data={3}", name, rcx, r8, data);
            key = name + data;
        }
        else if (name == "D3DKMTEscape")
        {
            // D3DKMT_ESCAPE: hAdapter(0) hDevice(4) Type(8) Flags(12) pPrivateDriverData(16) PrivateDriverDataSize(24) hContext(28)
            byte[] e = Read(p.H, rcx, 32);
            if (e.Length < 32) return;
            long pData = BitConverter.ToInt64(e, 16); int size = BitConverter.ToInt32(e, 24);
            string data = Hex(Read(p.H, pData, Math.Min(size, 1024)));
            sb.AppendFormat("{0} adapter=0x{1:X} type={2} flags=0x{3:X} size={4} data={5}", name, BitConverter.ToUInt32(e, 0), BitConverter.ToInt32(e, 8), BitConverter.ToUInt32(e, 12), size, data);
            key = name + data;
        }
        else if (name.StartsWith("NvAPI_I2C"))
        {
            byte[] info = Read(p.H, rdx, 64);
            sb.AppendFormat("{0} gpu=0x{1:X} info={2}", name, rcx, Hex(info));
            if (info.Length == 64)
            {
                long pReg = BitConverter.ToInt64(info, 16); int regSz = BitConverter.ToInt32(info, 24);
                long pData = BitConverter.ToInt64(info, 32); int dataSz = BitConverter.ToInt32(info, 40);
                sb.AppendFormat("\n      dev8=0x{0:X2} (7bit 0x{1:X2}) ddc={2} port={3} portSet={4} speedKhz={5} reg[{6}]={7} data[{8}]={9}",
                    info[9], info[9] >> 1, info[8], info[52], BitConverter.ToInt32(info, 56), BitConverter.ToInt32(info, 48),
                    regSz, Hex(Read(p.H, pReg, Math.Min(regSz, 16))), dataSz, Hex(Read(p.H, pData, Math.Min(dataSz, 512))));
            }
            key = sb.ToString();
        }
        else
        {
            sb.AppendFormat("{0} rcx=0x{1:X} rdx=0x{2:X} r8=0x{3:X} r9=0x{4:X}\n      [rcx]={5}\n      [rdx]={6}", name, rcx, rdx, r8, r9, Hex(Read(p.H, rcx, 64)), Hex(Read(p.H, rdx, 64)));
            key = sb.ToString();
        }
        // Collapse exact repeats (periodic polling) after the 3rd occurrence.
        int c; seen.TryGetValue(key, out c); seen[key] = ++c;
        if (c <= 3) Log(string.Format("[{0}/{1}] ", p.Pid, tid) + sb.ToString());
        else if (c == 4 || c % 100 == 0) Log(string.Format("[{0}/{1}] (repeat x{2}) {3}", p.Pid, tid, c, name));
    }

    static List<string> ModulesOf(Process pr)
    {
        try { return pr.Modules.Cast<ProcessModule>().Select(m => m.ModuleName).ToList(); }
        catch { return null; }
    }

    static void Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 300;
        string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trace_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        log = new StreamWriter(outPath);
        Console.Title = "Lenovo lighting tracer";
        EnableDebugPriv();

        IntPtr hid = LoadLibrary("hid.dll");
        AddHook(GetProcAddress(hid, "HidD_SetFeature"), "HidD_SetFeature");
        AddHook(GetProcAddress(hid, "HidD_GetFeature"), "HidD_GetFeature");
        IntPtr w32u = LoadLibrary("win32u.dll");
        AddHook(GetProcAddress(w32u, "NtGdiDdDDIEscape"), "D3DKMTEscape");
        IntPtr nv = LoadLibrary("nvapi64.dll");
        AddHook(GetProcAddress(nv, "nvapi_Direct_GetMethod"), "nvapi_Direct_GetMethod");
        try
        {
            NvQI(0x0150E828);
            AddHook(NvQI(0x283AC65A), "NvAPI_I2CWriteEx");
            AddHook(NvQI(0x4D7B0709), "NvAPI_I2CReadEx");
            AddHook(NvQI(0xE812EB07), "NvAPI_I2CWrite");
            AddHook(NvQI(0x2FDE12C5), "NvAPI_I2CRead");
        }
        catch (Exception e) { Log("nvapi: " + e.Message); }
        foreach (var kv in hooks) Log(string.Format("hook {0} @ 0x{1:X} bytes {2}", kv.Value, kv.Key, Hex(hookBytes[kv.Key])));

        // Pick targets: Lenovo SmartEngine processes that host the lighting plugin.
        var candidates = Process.GetProcesses().Where(pr => pr.ProcessName.StartsWith("SmartEngineHost") || pr.ProcessName.StartsWith("Lenovo") || pr.ProcessName == "seworker" || pr.ProcessName == "LSDaemon").ToList();
        var targets = new List<Process>();
        foreach (var pr in candidates)
        {
            var mods = ModulesOf(pr);
            Log(string.Format("candidate {0} {1}: {2}", pr.ProcessName, pr.Id, mods == null ? "(no access)" : string.Join(",", mods.Where(m => m.StartsWith("SE") || m.StartsWith("nvapi") || m.StartsWith("Lenovo")))));
            if (mods != null && mods.Any(m => m.Equals("SEBasicLighting64.dll", StringComparison.OrdinalIgnoreCase))) targets.Add(pr);
        }
        if (targets.Count == 0) { Log("SEBasicLighting64.dll not found in any process; attaching to all SmartEngine hosts instead"); targets = candidates.Where(pr => pr.ProcessName.StartsWith("SmartEngineHost")).ToList(); }

        foreach (var pr in targets)
        {
            if (!DebugActiveProcess(pr.Id)) { Log("attach failed " + pr.ProcessName + " " + pr.Id + " err " + Marshal.GetLastWin32Error()); continue; }
            procs[pr.Id] = new Proc { Pid = pr.Id, Name = pr.ProcessName, H = OpenProcess(0x1F0FFF, false, pr.Id) };
            Log("attached " + pr.ProcessName + " " + pr.Id);
        }
        DebugSetProcessKillOnExit(false);

        IntPtr ev = Marshal.AllocHGlobal(256);
        IntPtr ctx = Marshal.AllocHGlobal(1232 + 16);
        IntPtr ctxA = new IntPtr((ctx.ToInt64() + 15) & ~15L);
        var sw = Stopwatch.StartNew();
        int lastNotice = -1;
        try
        {
            Log("");
            Log(">>> TRACING for " + seconds + " seconds. Do the clicks in Legion Space now. <<<");
            Log("");
            while (sw.Elapsed.TotalSeconds < seconds && procs.Count > 0)
            {
                int left = seconds - (int)sw.Elapsed.TotalSeconds;
                if (left % 30 == 0 && left != lastNotice) { lastNotice = left; Console.WriteLine("    ... " + left + " s left"); }
                if (!WaitForDebugEvent(ev, 200)) continue;
                int code = Marshal.ReadInt32(ev, 0), pid = Marshal.ReadInt32(ev, 4), tid = Marshal.ReadInt32(ev, 8);
                uint cont = DBG_CONTINUE;
                Proc p; procs.TryGetValue(pid, out p);
                if (code == 1 && p != null) // EXCEPTION_DEBUG_EVENT
                {
                    uint exc = (uint)Marshal.ReadInt32(ev, 16);
                    long addr = Marshal.ReadInt64(ev, 32);
                    if (exc == EXCEPTION_BREAKPOINT && p.Orig.ContainsKey(addr))
                    {
                        IntPtr th = OpenThread(0x1FFFFF, false, tid);
                        Marshal.WriteInt32(ctxA, 0x30, 0x100003);
                        if (GetThreadContext(th, ctxA))
                        {
                            try { DumpCall(p, hooks[addr], ctxA, tid); } catch (Exception e) { Log("dump error: " + e.Message); }
                            IntPtr wr; WriteProcessMemory(p.H, new IntPtr(addr), new byte[] { p.Orig[addr] }, new IntPtr(1), out wr);
                            FlushInstructionCache(p.H, new IntPtr(addr), new IntPtr(1));
                            Marshal.WriteInt64(ctxA, 0xF8, addr);                                   // re-run original instruction
                            Marshal.WriteInt32(ctxA, 0x44, Marshal.ReadInt32(ctxA, 0x44) | 0x100);  // trap flag: step one instruction
                            SetThreadContext(th, ctxA);
                            p.Orig.Remove(addr);
                            p.Rearm[tid] = addr;
                        }
                        CloseHandle(th);
                    }
                    else if (exc == EXCEPTION_BREAKPOINT && hooks.ContainsKey(addr))
                    {
                        // Second thread hit the int3 while another thread was stepping over it (byte already restored):
                        // rewind so it simply runs the original instruction.
                        IntPtr th = OpenThread(0x1FFFFF, false, tid);
                        Marshal.WriteInt32(ctxA, 0x30, 0x100003);
                        if (GetThreadContext(th, ctxA)) { Marshal.WriteInt64(ctxA, 0xF8, addr); SetThreadContext(th, ctxA); }
                        CloseHandle(th);
                    }
                    else if (exc == EXCEPTION_BREAKPOINT && !p.InitialBpSeen) { p.InitialBpSeen = true; Arm(p); }
                    else if (exc == EXCEPTION_SINGLE_STEP && p.Rearm.ContainsKey(tid))
                    {
                        long a = p.Rearm[tid]; p.Rearm.Remove(tid);
                        var o = Read(p.H, a, 1);
                        IntPtr wr;
                        if (o.Length == 1 && WriteProcessMemory(p.H, new IntPtr(a), new byte[] { 0xCC }, new IntPtr(1), out wr))
                        { FlushInstructionCache(p.H, new IntPtr(a), new IntPtr(1)); p.Orig[a] = o[0]; }
                    }
                    else cont = DBG_EXCEPTION_NOT_HANDLED;
                }
                else if (code == 6 && p != null && p.InitialBpSeen) Arm(p);      // DLL loaded: maybe nvapi now present
                else if (code == 5 && p != null) { Log("process exited " + pid); procs.Remove(pid); }
                ContinueDebugEvent(pid, tid, cont);
            }
        }
        finally
        {
            // Remove breakpoints, then drain pending events so no thread is left mid-step or on a stale int3.
            foreach (var p in procs.Values) Disarm(p);
            var drain = Stopwatch.StartNew();
            while (drain.ElapsedMilliseconds < 1500)
            {
                if (!WaitForDebugEvent(ev, 100)) continue;
                int code = Marshal.ReadInt32(ev, 0), pid = Marshal.ReadInt32(ev, 4), tid = Marshal.ReadInt32(ev, 8);
                uint cont = DBG_CONTINUE;
                Proc p; procs.TryGetValue(pid, out p);
                if (code == 1)
                {
                    uint exc = (uint)Marshal.ReadInt32(ev, 16);
                    long addr = Marshal.ReadInt64(ev, 32);
                    if (exc == EXCEPTION_SINGLE_STEP && p != null && p.Rearm.ContainsKey(tid)) p.Rearm.Remove(tid);
                    else if (exc == EXCEPTION_BREAKPOINT && p != null && hooks.ContainsKey(addr))
                    {
                        // Hit the int3 just before it was removed: rewind to re-run the restored instruction.
                        IntPtr th = OpenThread(0x1FFFFF, false, tid);
                        Marshal.WriteInt32(ctxA, 0x30, 0x100003);
                        if (GetThreadContext(th, ctxA)) { Marshal.WriteInt64(ctxA, 0xF8, addr); SetThreadContext(th, ctxA); }
                        CloseHandle(th);
                    }
                    else if (!(exc == EXCEPTION_BREAKPOINT && p != null && !p.InitialBpSeen)) cont = DBG_EXCEPTION_NOT_HANDLED;
                }
                ContinueDebugEvent(pid, tid, cont);
            }
            foreach (var p in procs.Values) { DebugActiveProcessStop(p.Pid); Log("detached " + p.Name + " " + p.Pid); }
            Log("done. Log saved to " + outPath);
            log.Close();
        }
        Console.WriteLine("Finished. This window closes in 10 seconds.");
        System.Threading.Thread.Sleep(10000);
    }
}
