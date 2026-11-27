using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Read-only: checks that the chassis LampArray (17EF:C955 MI_01) can be opened read/write by a normal process
// and reads its LampArrayAttributes feature report (report id 1). Sends nothing that changes the lights.
class LampProbe
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);

    static void Main()
    {
        string path = @"\\?\hid#vid_17ef&pid_c955&mi_01#7&9c59d0e&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        using (var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            Console.WriteLine("open RW: " + (h.IsInvalid ? "FAILED err " + Marshal.GetLastWin32Error() : "ok"));
            if (h.IsInvalid) return;
            var b = new byte[51]; b[0] = 1;
            bool ok = HidD_GetFeature(h, b, b.Length);
            Console.WriteLine("GetFeature(1): " + ok + " err " + Marshal.GetLastWin32Error());
            if (ok)
            {
                Console.WriteLine(BitConverter.ToString(b, 0, 23));
                Console.WriteLine("lamps={0} box={1}x{2}x{3}um kind={4} minUpdate={5}us",
                    BitConverter.ToUInt16(b, 1), BitConverter.ToUInt32(b, 3), BitConverter.ToUInt32(b, 7), BitConverter.ToUInt32(b, 11), BitConverter.ToUInt32(b, 15), BitConverter.ToUInt32(b, 19));
            }
        }
    }
}
