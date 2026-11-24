using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
namespace Kopra;

static class FileSafety
{
    [StructLayout(LayoutKind.Sequential)]
    struct TagInfo
    {
        public uint attributes, tag;
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32", SetLastError = true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out TagInfo info, uint size);
    public static bool IsSupportedTag(uint tag) => (tag & 0x20000000) == 0;
    public static void Check(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
            return;
        using var file = CreateFile(path, 0x80, 7, 0, 3, 0x02000000 | 0x00200000, 0);
        if (file.IsInvalid || !GetFileInformationByHandleEx(file, 9, out var info, 8))
            throw new IOException("Ordnerverknüpfung konnte nicht geprüft werden.");
        if (!IsSupportedTag(info.tag))
            throw new IOException("Symbolische Verknüpfungen werden nicht rekursiv gesichert.");
        // Cloud placeholders describe this file, not another path. Reading
        // them uses the installed sync provider's normal hydration mechanism.
    }
}
