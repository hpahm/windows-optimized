using System.Runtime.InteropServices;
namespace SystemOptimizer.Native;
public static partial class NativeMethods
{
    [Flags]
    public enum RecycleBinFlags : uint
    {
        SHERB_NOCONFIRMATION = 0x00000001,
        SHERB_NOPROGRESSUI = 0x00000002,
        SHERB_NOSOUND = 0x00000004
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }
    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinNative(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath,
        RecycleBinFlags dwFlags);

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinNative(
        [MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath,
        ref SHQUERYRBINFO pSHQueryRBInfo);
    public static bool EmptyRecycleBin()
    {
        const RecycleBinFlags silentFlags =
            RecycleBinFlags.SHERB_NOCONFIRMATION |
            RecycleBinFlags.SHERB_NOPROGRESSUI |
            RecycleBinFlags.SHERB_NOSOUND;
        int hResult = SHEmptyRecycleBinNative(IntPtr.Zero, null, silentFlags);
        return hResult == 0 || hResult == unchecked((int)0x80070057);
    }
    public static (long TotalBytes, long ItemCount) QueryRecycleBinSize()
    {
        var info = new SHQUERYRBINFO
        {
            cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>()
        };

        int hResult = SHQueryRecycleBinNative(null, ref info);
        return hResult == 0
            ? (info.i64Size, info.i64NumItems)
            : (0L, 0L);
    }
}
