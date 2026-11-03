using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Orchester.Core;

internal static class NativePeer {
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint id);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetTokenInformation(IntPtr token,int information,IntPtr data,uint length,out uint required);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(IntPtr handle);
    public static int ProcessId(NamedPipeServerStream pipe)=>GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint id)?checked((int)id):throw new SafetyException("UnauthorizedRequest");
    public static string OwnerSid(int processId) {
        using var process=Process.GetProcessById(processId);
        if(!OpenProcessToken(process.Handle,8,out var token))throw new SafetyException("UnauthorizedRequest");
        IntPtr buffer=IntPtr.Zero;
        try {
            GetTokenInformation(token,1,IntPtr.Zero,0,out uint length);
            if(length==0||length>65536)throw new SafetyException("UnauthorizedRequest");
            buffer=Marshal.AllocHGlobal((int)length);
            if(!GetTokenInformation(token,1,buffer,length,out _))throw new SafetyException("UnauthorizedRequest");
            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
        } finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);CloseHandle(token);}
    }
}
