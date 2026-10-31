using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orchester.Core;
namespace Orchester.Storage;
public sealed class WindowsStorageDiscovery(bool includeSafetyDetails=true) : IStorageDiscovery {
 // Fixed, read-only script. User input is never interpolated into PowerShell.
 private const string Script = """
 $ErrorActionPreference='Stop'
 $ProgressPreference='SilentlyContinue'
 [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
 $disks=@(Get-Disk | ForEach-Object {
  $d=$_
  $parts=@(Get-Partition -DiskNumber $d.Number -ErrorAction Stop | ForEach-Object {
   $p=$_; $v=$null; $limit=$null; $enc='Unknown'
   try { $v=$p | Get-Volume -ErrorAction Stop } catch {}
   if ($probeSafety -and $v -and $v.FileSystemType -eq 'NTFS') {
    try { $s=$p | Get-PartitionSupportedSize -ErrorAction Stop; $limit=[long]($p.Size-$s.SizeMin) } catch {}
    try {
     $b=Get-BitLockerVolume -MountPoint $v.Path -ErrorAction Stop
     if ($b.LockStatus -eq 'Locked') {$enc='Locked'}
     elseif ($b.VolumeStatus -eq 'FullyDecrypted') {$enc='Unencrypted'}
     elseif ($b.ProtectionStatus -eq 'On') {$enc='Protected'}
     else {$enc='Suspended'}
    } catch {}
   }
   [pscustomobject]@{ Number=[int]$p.PartitionNumber; Id=[string]$p.Guid; Type=[string]$p.GptType; Offset=[long]$p.Offset; Length=[long]$p.Size; FileSystem=[string]$v.FileSystemType; MountPoint=[string]$v.DriveLetter; Health=[string]$v.HealthStatus; Encryption=$enc; ShrinkBytes=$limit; IsSystem=[bool]$p.IsSystem; IsBoot=[bool]$p.IsBoot }
  })
  [pscustomobject]@{ Number=[int]$d.Number; StableId=[string]$d.UniqueId; Name=[string]$d.FriendlyName; Capacity=[long]$d.Size; LogicalSector=[int]$d.LogicalSectorSize; PhysicalSector=[int]$d.PhysicalSectorSize; PartitionStyle=[string]$d.PartitionStyle; BusType=[string]$d.BusType; Health=[string]$d.HealthStatus; Offline=[bool]$d.IsOffline; ReadOnly=[bool]$d.IsReadOnly; Partitions=$parts }
 })
 ConvertTo-Json -InputObject $disks -Depth 8 -Compress
 """;
 public async Task<ImmutableArray<DiskSnapshot>> DiscoverAsync(CancellationToken cancellationToken) {
  if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WindowsRequired");
  var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
  var start=new ProcessStartInfo(executable) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8 };
  foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("$probeSafety="+(includeSafetyDetails?"$true":"$false")+";\n"+Script))}) start.ArgumentList.Add(arg);
  using var process=Process.Start(start) ?? throw new SafetyException("DiscoveryFailed");
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(includeSafetyDetails?180:30));
  var output=process.StandardOutput.ReadToEndAsync(timeout.Token); var error=process.StandardError.ReadToEndAsync(timeout.Token);
  try { await process.WaitForExitAsync(timeout.Token); } catch { if(!process.HasExited) process.Kill(true); throw; }
  var json=await output; await error; // Never surface raw PowerShell output, which may contain personal paths.
  if(process.ExitCode!=0) throw new SafetyException("DiscoveryFailed");
  var options=new JsonSerializerOptions { PropertyNameCaseInsensitive=true, Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()} };
  // Missing GPT GUIDs on unsupported disks are represented as empty for diagnostic display.
  using var doc=JsonDocument.Parse(json);
  var result=ImmutableArray.CreateBuilder<DiskSnapshot>();
  foreach(var element in doc.RootElement.EnumerateArray()) {
   var node=System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText())!;
   foreach(var p in node["Partitions"]!.AsArray()) if(!Guid.TryParse(p!["Id"]?.GetValue<string>(),out _)) p["Id"]=Guid.Empty.ToString();
   result.Add(node.Deserialize<DiskSnapshot>(options) ?? throw new SafetyException("DiscoveryFailed"));
  }
  return result.ToImmutable();
 }
}
