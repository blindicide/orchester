using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
namespace Orchester.Storage;

public static class WindowsVolumeHealth {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle file,uint code,IntPtr input,uint inputLength,out uint output,uint outputLength,out uint returned,IntPtr overlapped);
    [SupportedOSPlatform("windows")]
    public static bool? QueryDirty(string driveLetter) {
        if(driveLetter.Length!=1||driveLetter[0] is < 'A' or > 'Z')return null;
        using var file=CreateFileW(@"\\.\"+driveLetter+":",0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(file.IsInvalid)return null;
        // FSCTL_IS_VOLUME_DIRTY: read-only filesystem-aware query, no resizing or raw writes.
        if(!DeviceIoControl(file,0x00090078,IntPtr.Zero,0,out uint flags,4,out uint returned,IntPtr.Zero)||returned!=4)return null;
        return flags!=0; // Dirty, chkdsk in progress, or an unknown flagged state is unsafe.
    }
}
