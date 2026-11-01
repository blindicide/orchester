using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Storage;

// Fixed CIM/Storage commands, no raw NTFS resizing and no arbitrary PowerShell input.
// Native execution is disabled by ReleaseGate until the disposable VM matrix is qualified.
public sealed class WindowsStorageOperations(IStorageDiscovery discovery) : IStorageOperations {
    private const string Script = """
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$q=[Console]::In.ReadToEnd() | ConvertFrom-Json
$d=Get-Disk -Number $q.DiskNumber
if ($d.UniqueId -cne $q.DiskId -or $d.PartitionStyle -ne 'GPT' -or $d.IsReadOnly -or $d.IsOffline -or $d.HealthStatus -ne 'Healthy') {throw 'DiskChanged'}
$p=Get-Partition -DiskNumber $d.Number | Where-Object {([guid]$_.Guid) -eq ([guid]$q.PartitionId)}
if ($q.Kind -eq 'Shrink') {
 if (!$p -or $p.IsSystem -or $p.Offset -ne $q.Offset -or $p.Size -ne $q.Length -or $p.GptType -ne '{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}') {throw 'LayoutChanged'}
 $v=$p | Get-Volume
 $b=Get-BitLockerVolume -MountPoint $v.Path
 if ($v.FileSystemType -ne 'NTFS' -or $v.HealthStatus -ne 'Healthy' -or $b.VolumeStatus -ne 'FullyDecrypted' -or $b.LockStatus -eq 'Locked') {throw 'UnsafeSource'}
 $s=$p | Get-PartitionSupportedSize
 if ($q.NewSize -lt $s.SizeMin -or $q.NewSize -ge $p.Size) {throw 'ShrinkLimit'}
 $p | Resize-Partition -Size $q.NewSize -Confirm:$false
} elseif ($q.Kind -eq 'Create') {
 $parts=@(Get-Partition -DiskNumber $d.Number)
 foreach($existing in $parts) {if($existing.Offset -lt ($q.Offset+$q.Length) -and ($existing.Offset+$existing.Size) -gt $q.Offset) {throw 'LayoutChanged'}}
 New-Partition -DiskNumber $d.Number -Offset $q.Offset -Size $q.Length -GptType '{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}' -AssignDriveLetter:$false | Out-Null
} elseif ($q.Kind -eq 'Format' -or $q.Kind -eq 'Delete') {
 if (!$p -or $p.IsSystem -or $p.IsBoot -or $p.Offset -ne $q.Offset -or $p.Size -ne $q.Length -or $p.GptType -ne '{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}') {throw 'OwnershipMismatch'}
 if ($q.Kind -eq 'Format') {
  $v=$p | Get-Volume -ErrorAction SilentlyContinue
  if ($v -and $v.FileSystemType -ne 'Unknown' -and $v.FileSystemType -ne 'RAW') {throw 'OwnershipMismatch'}
  $p | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'ORCHESTER' -Force -Confirm:$false | Out-Null
 } else { $p | Remove-Partition -Confirm:$false }
} else {throw 'UnauthorizedRequest'}
""";
    public async Task<DiskSnapshot> InspectAsync(int diskNumber, CancellationToken ct) =>
        (await discovery.DiscoverAsync(ct)).SingleOrDefault(d => d.Number == diskNumber) ?? throw new SafetyException("DiskChanged");
    public async Task ShrinkNtfsAsync(InstallationPlan plan, CancellationToken ct) {
        ReleaseGate.RequireNativeWrites();
        var disk = await InspectAsync(plan.OriginalDisk.Number, ct);
        PlanGuard.Validate(plan, disk, PlanGuard.Fingerprint(plan), false);
        var source = disk.Partitions.Single(p => p.Id == plan.SourcePartition);
        await RunAsync(new("Shrink", disk.Number, disk.StableId, source.Id, source.Offset, source.Length,
            source.Length - plan.RequestedBytes - plan.StagingBytes), ct);
        var after = await InspectAsync(disk.Number, ct);
        if (after.Partitions.Single(p => p.Id == source.Id).Length != source.Length - plan.RequestedBytes - plan.StagingBytes)
            throw new SafetyException("StorageVerificationFailed");
    }
    public async Task<OwnedPartition> CreateStagingAsync(InstallationPlan plan, CancellationToken ct) {
        ReleaseGate.RequireNativeWrites();
        var original = plan.OriginalDisk.Partitions.Single(p => p.Id == plan.SourcePartition);
        long offset = original.Offset + original.Length - plan.StagingBytes;
        var disk = await InspectAsync(plan.OriginalDisk.Number, ct);
        if (disk.StableId != plan.OriginalDisk.StableId) throw new SafetyException("DiskChanged");
        StorageStatePolicy.ValidateAfterShrink(plan, disk);
        await RunAsync(new("Create", disk.Number, disk.StableId, Guid.Empty, offset, plan.StagingBytes, 0), ct);
        var after = await InspectAsync(disk.Number, ct);
        var created = after.Partitions.Single(p => p.Offset == offset && p.Length == plan.StagingBytes);
        if (plan.OriginalDisk.Partitions.Any(p => p.Id == created.Id)) throw new SafetyException("OwnershipMismatch");
        return new(plan.OperationId, disk.StableId, created.Id, created.Number, offset, created.Length, created.Type, "RAW");
    }
    public async Task FormatStagingAsync(InstallationPlan plan, OwnedPartition partition, CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(); var current = await ValidateOwnedAsync(plan, partition, ct);
        if (current.FileSystem is not ("" or "RAW" or "Unknown")) throw new SafetyException("OwnershipMismatch");
        await RunAsync(new("Format", plan.OriginalDisk.Number, partition.DiskId, partition.PartitionId, partition.Offset, partition.Length, 0), ct);
        if ((await InspectAsync(plan.OriginalDisk.Number, ct)).Partitions.Single(p => p.Id == partition.PartitionId).FileSystem != "NTFS")
            throw new SafetyException("StorageVerificationFailed");
    }
    public async Task DeleteOwnedAsync(InstallationPlan plan, OwnedPartition partition, CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(); await ValidateOwnedAsync(plan, partition, ct);
        await RunAsync(new("Delete", plan.OriginalDisk.Number, partition.DiskId, partition.PartitionId, partition.Offset, partition.Length, 0), ct);
        if ((await InspectAsync(plan.OriginalDisk.Number, ct)).Partitions.Any(p => p.Id == partition.PartitionId)) throw new SafetyException("StorageVerificationFailed");
    }
    private async Task<PartitionSnapshot> ValidateOwnedAsync(InstallationPlan plan, OwnedPartition owned, CancellationToken ct) {
        if (owned.OperationId != plan.OperationId || owned.DiskId != plan.OriginalDisk.StableId ||
            plan.OriginalDisk.Partitions.Any(p => p.Id == owned.PartitionId) || owned.PartitionId == Guid.Empty)
            throw new SafetyException("OwnershipMismatch");
        var disk = await InspectAsync(plan.OriginalDisk.Number, ct);
        if (disk.StableId != owned.DiskId) throw new SafetyException("DiskChanged");
        StorageStatePolicy.ValidateAfterShrink(plan, disk, owned);
        var p = disk.Partitions.SingleOrDefault(p => p.Id == owned.PartitionId) ?? throw new SafetyException("OwnershipMismatch");
        if (p.Protected || p.IsBoot || p.Offset != owned.Offset || p.Length != owned.Length || p.Type != owned.Type)
            throw new SafetyException("OwnershipMismatch");
        return p;
    }
    private sealed record Command(string Kind, int DiskNumber, string DiskId, Guid PartitionId, long Offset, long Length, long NewSize);
    private static async Task RunAsync(Command command, CancellationToken ct) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(path) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new SafetyException("StorageVerificationFailed");
        var output = process.StandardOutput.ReadToEndAsync(ct); var errors = process.StandardError.ReadToEndAsync(ct);
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(command)); process.StandardInput.Close();
        // Do not kill a filesystem resize once started. Wait for a definite result or external recovery.
        await process.WaitForExitAsync(CancellationToken.None); await output; await errors;
        if (process.ExitCode != 0) throw new SafetyException("StorageVerificationFailed");
    }
}
