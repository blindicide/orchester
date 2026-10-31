using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Orchester.Core;
namespace Orchester.Boot;
public static class HostInspector {
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFirmwareType(out uint firmwareType);
 [SupportedOSPlatform("windows")]
 public static HostSnapshot Inspect() {
  using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
  var build=Convert.ToInt32(key?.GetValue("CurrentBuildNumber","0"));
  var installation=key?.GetValue("InstallationType") as string;
  bool uefi=GetFirmwareType(out var firmware) && firmware==2;
  bool? secure=null;
  using var sb=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
  if(sb?.GetValue("UEFISecureBootEnabled") is int value) secure=value==1;
  return new($"{key?.GetValue("ProductName")} ({build})",installation=="Client" && build>=19045,uefi,secure,RuntimeInformation.OSArchitecture==Architecture.X64);
 }
}
public sealed class BootManager {
 public Task RegisterInstallerAsync(Guid operationId,CancellationToken ct) => Task.FromException(new SafetyException("ExecutionUnavailable"));
 public Task RequestOneTimeBootAsync(Guid operationId,CancellationToken ct) => Task.FromException(new SafetyException("ExecutionUnavailable"));
 public Task RemoveOwnedEntryAsync(Guid operationId,string entryId,CancellationToken ct) => Task.FromException(new SafetyException("ExecutionUnavailable"));
}
