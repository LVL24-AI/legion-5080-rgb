using System;
using System.Runtime.InteropServices;

// Read-only probe of NVIDIA client illumination API (no Set calls).
class NvIllumProbe
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QI(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D0();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DEnum([Out] IntPtr[] h, out int n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DHBuf(IntPtr h, IntPtr buf);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DHStr(IntPtr h, IntPtr buf);

    static T F<T>(uint id) where T : class
    {
        IntPtr p = QI(id);
        if (p == IntPtr.Zero) { Console.WriteLine("QI 0x{0:X8} missing", id); return null; }
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    static void TryGet(string name, uint id, IntPtr gpu, int minSize, int maxSize)
    {
        var f = F<DHBuf>(id); if (f == null) return;
        IntPtr buf = Marshal.AllocHGlobal(maxSize + 16);
        for (int ver = 1; ver <= 2; ver++)
        for (int size = minSize; size <= maxSize; size += 4)
        {
            for (int i = 0; i < maxSize + 16; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, size | (ver << 16));
            int r = f(gpu, buf);
            if (r == 0)
            {
                Console.WriteLine("{0}: OK size={1} ver={2}", name, size, ver);
                int n = Marshal.ReadInt32(buf, 4);
                Console.WriteLine("  count field={0}", n);
                var bytes = new byte[Math.Min(size, 1024)];
                Marshal.Copy(buf, bytes, 0, bytes.Length);
                for (int i = 0; i < bytes.Length; i += 32)
                {
                    bool nz = false; for (int j = i; j < i + 32 && j < bytes.Length; j++) if (bytes[j] != 0) nz = true;
                    if (nz) Console.WriteLine("  {0:X4}: {1}", i, BitConverter.ToString(bytes, i, Math.Min(32, bytes.Length - i)));
                }
                Marshal.FreeHGlobal(buf);
                return;
            }
            if (r != -9) { Console.WriteLine("{0}: ret {1} at size {2} ver {3}", name, r, size, ver); Marshal.FreeHGlobal(buf); return; }
        }
        Console.WriteLine("{0}: no matching struct size", name);
        Marshal.FreeHGlobal(buf);
    }

    static void Main()
    {
        Console.WriteLine("Init: " + F<D0>(0x0150E828)());
        var h = new IntPtr[64]; int n;
        Console.WriteLine("Enum: " + F<DEnum>(0xE5AC921F)(h, out n) + " gpus=" + n);
        for (int g = 0; g < n; g++)
        {
            IntPtr s = Marshal.AllocHGlobal(64);
            F<DHStr>(0xCEEE8E9F)(h[g], s);
            Console.WriteLine("GPU {0}: {1}", g, Marshal.PtrToStringAnsi(s));
            TryGet("ClientIllumDevicesGetInfo", 0xD4100E58, h[g], 8, 8192);
            TryGet("ClientIllumZonesGetInfo", 0x4B81241B, h[g], 8, 16384);
            TryGet("ClientIllumDevicesGetControl", 0x73C01D58, h[g], 8, 8192);
            TryGet("ClientIllumZonesGetControl", 0x3DBF5764, h[g], 8, 16384);
        }
    }
}
