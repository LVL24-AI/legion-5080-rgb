using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LegionRgb
{
    static class Hid
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, byte[] caps);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr di);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);

        [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

        public class DevInfo { public string Path; public ushort UsagePage, Usage, FeatLen; }

        /// All HID collections of a VID/PID with their usage page, usage and feature report length.
        public static List<DevInfo> Find(ushort vid, ushort pid)
        {
            var list = new List<DevInfo>();
            Guid hid; HidD_GetHidGuid(out hid);
            IntPtr set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12);
            var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
            string tag = string.Format("vid_{0:x4}&pid_{1:x4}", vid, pid);
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d); i++)
            {
                int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(req);
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero);
                string path = Marshal.PtrToStringAuto(buf + 4);
                Marshal.FreeHGlobal(buf);
                if (!path.ToLowerInvariant().Contains(tag)) continue;
                using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    IntPtr p; if (h.IsInvalid || !HidD_GetPreparsedData(h, out p)) continue;
                    var caps = new byte[64]; HidP_GetCaps(p, caps); HidD_FreePreparsedData(p);
                    list.Add(new DevInfo { Path = path, Usage = BitConverter.ToUInt16(caps, 0), UsagePage = BitConverter.ToUInt16(caps, 2), FeatLen = BitConverter.ToUInt16(caps, 8) });
                }
            }
            SetupDiDestroyDeviceInfoList(set);
            return list;
        }

        public static SafeFileHandle OpenRw(string path) { return CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); }
    }

    /// NVIDIA driver I2C access (same calls Lenovo's lighting service makes).
    static class NvApi
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QI(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D0();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DEnum([Out] IntPtr[] h, out int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DI2C(IntPtr gpu, IntPtr info);

        static readonly object sync = new object();
        static bool? avail;
        static IntPtr gpu;
        static DI2C i2cWrite;

        public static bool Available
        {
            get
            {
                lock (sync)
                {
                    if (avail.HasValue) return avail.Value;
                    try
                    {
                        var init = (D0)Marshal.GetDelegateForFunctionPointer(QI(0x0150E828), typeof(D0));
                        if (init() != 0) return (avail = false).Value;
                        var en = (DEnum)Marshal.GetDelegateForFunctionPointer(QI(0xE5AC921F), typeof(DEnum));
                        var hs = new IntPtr[64]; int n;
                        if (en(hs, out n) != 0 || n < 1) return (avail = false).Value;
                        gpu = hs[0];
                        IntPtr w = QI(0xE812EB07);   // NvAPI_I2CWrite
                        if (w == IntPtr.Zero) return (avail = false).Value;
                        i2cWrite = (DI2C)Marshal.GetDelegateForFunctionPointer(w, typeof(DI2C));
                        avail = true;
                    }
                    catch { avail = false; }
                    return avail.Value;
                }
            }
        }

        /// One-byte register write, NV_I2C_INFO_V3 filled exactly like Lenovo's lighting service does it.
        public static int WriteReg(uint displayMask, byte port, byte dev8, byte reg, byte value)
        {
            if (!Available) return -1;
            lock (sync)
            {
                IntPtr info = Marshal.AllocHGlobal(64), pReg = Marshal.AllocHGlobal(8), pData = Marshal.AllocHGlobal(8);
                try
                {
                    for (int i = 0; i < 64; i++) Marshal.WriteByte(info, i, 0);
                    Marshal.WriteByte(pReg, reg);
                    Marshal.WriteByte(pData, value);
                    Marshal.WriteInt32(info, 0, 64 | (3 << 16));     // version 3, size 0x40
                    Marshal.WriteInt32(info, 4, (int)displayMask);    // output id of the DDC line
                    Marshal.WriteByte(info, 8, 1);                    // bIsDDCPort
                    Marshal.WriteByte(info, 9, dev8);                 // 8-bit address
                    Marshal.WriteInt64(info, 16, pReg.ToInt64());
                    Marshal.WriteInt32(info, 24, 1);                  // regAddrSize
                    Marshal.WriteInt64(info, 32, pData.ToInt64());
                    Marshal.WriteInt32(info, 40, 1);                  // cbSize
                    Marshal.WriteInt32(info, 44, 0xFFFF);             // i2cSpeed (deprecated field)
                    Marshal.WriteInt32(info, 48, 4);                  // i2cSpeedKhz = NVAPI_I2C_SPEED_100KHZ
                    Marshal.WriteByte(info, 52, port);                // portId
                    Marshal.WriteInt32(info, 56, 1);                  // bIsPortIdSet
                    return i2cWrite(gpu, info);
                }
                finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(pReg); Marshal.FreeHGlobal(pData); }
            }
        }
    }
}
