using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Orchester.Core;
namespace Orchester.Storage;

[SupportedOSPlatform("windows")]
public static class WindowsStoragePolicy {
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern IntPtr OpenSCManagerW(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern IntPtr OpenServiceW(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryServiceStatus(IntPtr service,out Status status);
    [DllImport("advapi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseServiceHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)]private struct Status {public uint Type,State,Controls,Exit,Specific,Checkpoint,Wait;}
    public static void RequireSupportedStack() {
        var allowed=new HashSet<string>(new[]{"PartMgr","fvevol","volsnap","rdyboost","EhStorClass","volmgr","volmgrx","spaceport","storqosflt","StorageQoS"},StringComparer.OrdinalIgnoreCase);
        foreach(string type in new[]{"{4d36e967-e325-11ce-bfc1-08002be10318}","{71a27cdd-812a-11d0-bec7-08002be2092f}"}) {
            using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+type);
            foreach(string property in new[]{"UpperFilters","LowerFilters"}) {
                object? value=key?.GetValue(property);IEnumerable<string> filters=value is string[] multiple?multiple:value is string single?[single]:[];
                if(filters.Any(f=>!allowed.Contains(f)))throw new SafetyException("UnsupportedStorageDriver");
            }
        }
        IntPtr manager=OpenSCManagerW(null,null,1);if(manager==IntPtr.Zero)throw new SafetyException("UnsupportedStorageDriver");
        try {
            foreach(string name in new[]{"veracrypt","truecrypt","dcrypt","dcman","BestCrypt","BCFVE"}) {
                IntPtr service=OpenServiceW(manager,name,4);if(service==IntPtr.Zero)continue;
                try {if(!QueryServiceStatus(service,out var status)||status.State is 2 or 3 or 4 or 5 or 6 or 7)throw new SafetyException("UnsupportedStorageDriver");}
                finally {CloseServiceHandle(service);}
            }
        } finally {CloseServiceHandle(manager);}
    }
}
