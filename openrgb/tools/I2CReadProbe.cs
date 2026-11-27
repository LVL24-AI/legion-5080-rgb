using System;
using System.Runtime.InteropServices;

// Read-only probe: reads registers of the Lenovo GPU light chip (8-bit address 0x92) two ways:
//   A) the way OpenRGB's i2c_smbus_nvapi does it (not a DDC port, port 1, default speed, NvAPI_I2CReadEx)
//   B) the way Lenovo's service / LGPU does it (DDC port, output 0x100, port 1, 100 kHz)
// No writes are made.
class I2CReadProbe
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QI(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D0();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DEnum([Out] IntPtr[] h, out int n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DI2C(IntPtr gpu, IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DI2CEx(IntPtr gpu, IntPtr info, ref uint unknown);

    static IntPtr gpu;
    static DI2C read;
    static DI2CEx readEx;

    static int Read(bool ex, bool ddc, uint mask, int khz, byte dev8, byte reg, byte[] outBuf, int len)
    {
        IntPtr info = Marshal.AllocHGlobal(64), pReg = Marshal.AllocHGlobal(8), pData = Marshal.AllocHGlobal(64);
        try
        {
            for (int i = 0; i < 64; i++) { Marshal.WriteByte(info, i, 0); Marshal.WriteByte(pData, i, 0xEE); }
            Marshal.WriteByte(pReg, reg);
            Marshal.WriteInt32(info, 0, 64 | (3 << 16));
            Marshal.WriteInt32(info, 4, (int)mask);
            Marshal.WriteByte(info, 8, (byte)(ddc ? 1 : 0));
            Marshal.WriteByte(info, 9, dev8);
            Marshal.WriteInt64(info, 16, pReg.ToInt64());
            Marshal.WriteInt32(info, 24, 1);
            Marshal.WriteInt64(info, 32, pData.ToInt64());
            Marshal.WriteInt32(info, 40, len);
            Marshal.WriteInt32(info, 44, 0xFFFF);
            Marshal.WriteInt32(info, 48, khz);
            Marshal.WriteByte(info, 52, 1);
            Marshal.WriteInt32(info, 56, 1);
            uint unk = 0;
            int r = ex ? readEx(gpu, info, ref unk) : read(gpu, info);
            Marshal.Copy(pData, outBuf, 0, len);
            return r;
        }
        finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(pReg); Marshal.FreeHGlobal(pData); }
    }

    static void Dump(string label, bool ex, bool ddc, uint mask, int khz, byte dev8)
    {
        Console.WriteLine("== " + label);
        byte[] regs = { 0x00, 0x01, 0x02, 0x03, 0x10, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x20, 0x21, 0x22, 0x23, 0x30, 0x31, 0x32, 0x40, 0x50 };
        var b = new byte[16];
        foreach (var reg in regs)
        {
            int r = Read(ex, ddc, mask, khz, dev8, reg, b, 1);
            Console.Write(" {0:X2}:{1}", reg, r == 0 ? b[0].ToString("X2") : "e" + r);
        }
        Console.WriteLine();
        int rb = Read(ex, ddc, mask, khz, dev8, 0x00, b, 16);
        Console.WriteLine(" block@00 ({0}): {1}", rb, BitConverter.ToString(b));
    }

    static void Main()
    {
        Console.WriteLine("Init: " + ((D0)Marshal.GetDelegateForFunctionPointer(QI(0x0150E828), typeof(D0)))());
        var h = new IntPtr[64]; int n;
        Console.WriteLine("Enum: " + ((DEnum)Marshal.GetDelegateForFunctionPointer(QI(0xE5AC921F), typeof(DEnum)))(h, out n) + " gpus=" + n);
        gpu = h[0];
        read = (DI2C)Marshal.GetDelegateForFunctionPointer(QI(0x2FDE12C5), typeof(DI2C));      // NvAPI_I2CRead
        readEx = (DI2CEx)Marshal.GetDelegateForFunctionPointer(QI(0x4D7B0709), typeof(DI2CEx)); // NvAPI_I2CReadEx

        Dump("A  OpenRGB style: ReadEx, not DDC, mask 0, default speed", true, false, 0, 0, 0x92);
        Dump("B1 Lenovo style: Read, DDC, mask 0x100, 100 kHz", false, true, 0x100, 4, 0x92);
        Dump("B2 Lenovo style via ReadEx", true, true, 0x100, 4, 0x92);
        Dump("C  OpenRGB style at an empty address 0x90 (control)", true, false, 0, 0, 0x90);
    }
}
