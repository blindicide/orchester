using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Boot;

[SupportedOSPlatform("windows")]
public sealed class WindowsFirmwareVariables : IFirmwareVariables {
    private const string GlobalGuid = "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}";
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    private static extern uint GetFirmwareEnvironmentVariableW(string name, string guid, byte[] buffer, uint size);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFirmwareEnvironmentVariableExW(string name, string guid, byte[] buffer, uint size, uint attributes);
    [DllImport("advapi32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValueW(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Id; public uint Attributes; }

    public static void EnableReadPrivilege() {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token)) throw new SafetyException("FirmwareAccessDenied");
        try {
            if (!LookupPrivilegeValueW(null, "SeSystemEnvironmentPrivilege", out var luid)) throw new SafetyException("FirmwareAccessDenied");
            var privileges = new TokenPrivileges { Count=1, Id=luid, Attributes=2 };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
                throw new SafetyException("FirmwareAccessDenied");
        } finally { CloseHandle(token); }
    }
    public byte[]? Read(string name) {
        ValidateName(name);
        var buffer = new byte[1024 * 1024];
        uint size = GetFirmwareEnvironmentVariableW(name, GlobalGuid, buffer, (uint)buffer.Length);
        if (size == 0) {
            int error = Marshal.GetLastWin32Error();
            if (error is 203 or 2) return null;
            throw new SafetyException(error is 5 or 1314 ? "FirmwareAccessDenied" : "FirmwareUnavailable");
        }
        return buffer.AsSpan(0, checked((int)size)).ToArray();
    }
    public void Write(string name, byte[] value) {
        ValidateName(name); ReleaseGate.RequireNativeWrites();
        if (name is "BootOrder" or "BootCurrent" || value.Length > 1024 * 1024) throw new SafetyException("BootTargetUnsafe");
        if (!SetFirmwareEnvironmentVariableExW(name, GlobalGuid, value, (uint)value.Length, 7)) throw new SafetyException("BootVerificationFailed");
    }
    public void Delete(string name) {
        ValidateName(name); ReleaseGate.RequireNativeWrites();
        if (name is "BootOrder" or "BootCurrent") throw new SafetyException("BootTargetUnsafe");
        if (!SetFirmwareEnvironmentVariableExW(name, GlobalGuid, [], 0, 0)) throw new SafetyException("BootVerificationFailed");
    }
    private static void ValidateName(string name) {
        if (name is not ("BootOrder" or "BootCurrent" or "BootNext") &&
            !System.Text.RegularExpressions.Regex.IsMatch(name, @"\ABoot[0-9A-F]{4}\z")) throw new SafetyException("UnauthorizedRequest");
    }
}
