using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Orchester.Core;
using Orchester.Security;
using Orchester.Storage;
namespace Orchester.Engine;

[SupportedOSPlatform("windows")]
public static class NativeSourceRecovery {
    public static async Task ReclaimAsync(NativeOperationState state,string folder,CancellationToken ct) {
        var plan=state.Plan;ReleaseGate.RequireNativeWrites(plan.OperationId);
        if(state.Phase!=OperationPhase.CANCELLED||!state.Partitions.IsEmpty||File.Exists(Path.Combine(folder,"completion.json")))throw new SafetyException("CleanupDependency");
        var disk=(await new WindowsStorageDiscovery().DiscoverAsync(ct)).SingleOrDefault(d=>d.Number==plan.OriginalDisk.Number)??throw new SafetyException("DiskChanged");
        InstallerPartitionStorage.ValidatePreservation(plan,disk,[],false);
        var source=disk.Partitions.Single(p=>p.Id==plan.SourcePartition);var original=plan.OriginalDisk.Partitions.Single(p=>p.Id==plan.SourcePartition);
        if(source.Dirty!=false||source.Health!="Healthy"||source.Encryption!=EncryptionState.Unencrypted||source.FileSystem!="NTFS")throw new SafetyException("UnsafeSource");
        string journal=Path.Combine(folder,"recovery.jsonl");await Log(journal,plan.OperationId,"RestoreAllocation","INTENT");
        const string script="""
$ErrorActionPreference='Stop'
$q=[Console]::In.ReadToEnd()|ConvertFrom-Json
$d=Get-Disk -Number $q.Disk
if($d.UniqueId -cne $q.Identity -or $d.PartitionStyle -ne 'GPT' -or $d.IsReadOnly -or $d.IsOffline){throw 'DiskChanged'}
$p=Get-Partition -DiskNumber $q.Disk|Where-Object {([guid]$_.Guid) -eq ([guid]$q.Partition)}
if(!$p -or $p.Offset -ne $q.Offset -or $p.Size -ne $q.Current){throw 'LayoutChanged'}
foreach($other in @(Get-Partition -DiskNumber $q.Disk)){if($other.PartitionNumber -ne $p.PartitionNumber -and $other.Offset -lt ($q.Offset+$q.Original) -and ($other.Offset+$other.Size) -gt ($q.Offset+$q.Current)){throw 'LayoutChanged'}}
$v=$p|Get-Volume
$b=Get-CimInstance -Namespace root/CIMV2/Security/MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume|Where-Object {$_.DriveLetter -eq (([string]$v.DriveLetter)+':')}
$status=Invoke-CimMethod -InputObject $b -MethodName GetConversionStatus
if($v.FileSystemType -ne 'NTFS' -or $v.HealthStatus -ne 'Healthy' -or $status.ReturnValue -ne 0 -or $status.ConversionStatus -ne 0){throw 'UnsafeSource'}
$s=$p|Get-PartitionSupportedSize
if($s.SizeMax -lt $q.Original){throw 'ShrinkLimit'}
$p|Resize-Partition -Size $q.Original -Confirm:$false
""";
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var start=new ProcessStartInfo(Path.Combine(windows,"System32","WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["PSModulePath"]=Path.Combine(windows,"System32","WindowsPowerShell","v1.0","Modules");
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new SafetyException("StorageVerificationFailed");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new{Disk=disk.Number,Identity=disk.StableId,Partition=source.Id,Offset=source.Offset,Current=source.Length,Original=original.Length}));process.StandardInput.Close();
        await process.WaitForExitAsync(CancellationToken.None);await output;await error;
        if(process.ExitCode!=0){await Log(journal,plan.OperationId,"RestoreAllocation","UNKNOWN");throw new SafetyException("StorageVerificationFailed");}
        var after=(await new WindowsStorageDiscovery(false).DiscoverAsync(CancellationToken.None)).Single(d=>d.Number==disk.Number);
        if(after.StableId!=disk.StableId||after.Partitions.Single(p=>p.Id==source.Id).Length!=original.Length)throw new SafetyException("StorageVerificationFailed");
        await Log(journal,plan.OperationId,"RestoreAllocation","VERIFIED");
    }
    public static async Task Log(string path,Guid id,string action,string result) {
        using var file=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(file,new{Operation=id,Timestamp=DateTimeOffset.UtcNow,Action=action,Result=result});await file.WriteAsync(new byte[]{10});file.Flush(true);
    }
}
