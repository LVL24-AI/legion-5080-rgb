using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Read-only: dumps feature report IDs / usages of every 17EF:C955 HID collection (parsed descriptor only).
class CapsProbe
{
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, byte[] caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort len, IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, byte[] caps, ref ushort len, IntPtr p);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);

    static void Main()
    {
        string[] paths = {
            @"\\?\hid#vid_17ef&pid_c955&mi_00&col01#7&2d9cdad0&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            @"\\?\hid#vid_17ef&pid_c955&mi_00&col02#7&2d9cdad0&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            @"\\?\hid#vid_17ef&pid_c955&mi_01#7&9c59d0e&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}" };
        foreach (var path in paths)
        {
            using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                IntPtr p; if (h.IsInvalid || !HidD_GetPreparsedData(h, out p)) { Console.WriteLine("open failed " + path); continue; }
                var caps = new byte[64]; HidP_GetCaps(p, caps);
                ushort nVal = BitConverter.ToUInt16(caps, 60), nBtn = BitConverter.ToUInt16(caps, 58);
                Console.WriteLine(path.Substring(4, 30) + "  featLen=" + BitConverter.ToUInt16(caps, 8) + " featValueCaps=" + nVal + " featButtonCaps=" + nBtn);
                // HIDP_VALUE_CAPS is 72 bytes: UsagePage(0) ReportID(2) IsAlias(3) BitField(4) LinkCollection(6) LinkUsage(8) LinkUsagePage(10)
                // IsRange(12) IsStringRange(13) IsDesignatorRange(14) IsAbsolute(15) HasNull(16) Reserved(17) BitSize(18) ReportCount(20)
                // Reserved2[5](22) UnitsExp(32) Units(36) LogicalMin(40) LogicalMax(44) PhysicalMin(48) PhysicalMax(52) Range/NotRange: Usage(56)...
                if (nVal > 0)
                {
                    var vc = new byte[72 * nVal]; ushort n = nVal; HidP_GetValueCaps(2, vc, ref n, p);
                    for (int i = 0; i < n; i++)
                    {
                        int o = 72 * i;
                        Console.WriteLine("   value: page=0x{0:X2} reportId=0x{1:X2} usage=0x{2:X2}{3} bits={4} count={5} logical={6}..{7}",
                            BitConverter.ToUInt16(vc, o), vc[o + 2], BitConverter.ToUInt16(vc, o + 56), vc[o + 12] != 0 ? "-0x" + BitConverter.ToUInt16(vc, o + 58).ToString("X2") : "",
                            BitConverter.ToUInt16(vc, o + 18), BitConverter.ToUInt16(vc, o + 20), BitConverter.ToInt32(vc, o + 40), BitConverter.ToInt32(vc, o + 44));
                    }
                }
                if (nBtn > 0)
                {
                    var bc = new byte[72 * nBtn]; ushort n = nBtn; HidP_GetButtonCaps(2, bc, ref n, p);
                    for (int i = 0; i < n; i++)
                    {
                        int o = 72 * i;
                        Console.WriteLine("   button: page=0x{0:X2} reportId=0x{1:X2} usage=0x{2:X2}", BitConverter.ToUInt16(bc, o), bc[o + 2], BitConverter.ToUInt16(bc, o + 56));
                    }
                }
                HidD_FreePreparsedData(p);
            }
        }
    }
}

