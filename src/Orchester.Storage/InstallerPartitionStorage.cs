using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Storage;

[SupportedOSPlatform("windows")]
public sealed class InstallerPartitionStorage(IStorageDiscovery discovery) {
    private const string Script="""
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$q=[Console]::In.ReadToEnd()|ConvertFrom-Json
$d=Get-Disk -Number $q.Disk
if($d.UniqueId -cne $q.DiskId -or $d.PartitionStyle -ne 'GPT' -or $d.IsOffline -or $d.IsReadOnly -or $d.HealthStatus -ne 'Healthy'){throw 'DiskChanged'}
$p=$null
if($q.Id -ne [guid]::Empty){$p=Get-Partition -DiskNumber $q.Disk|Where-Object {([guid]$_.Guid) -eq ([guid]$q.Id)}}
if($q.Kind -eq 'Create') {
 foreach($existing in @(Get-Partition -DiskNumber $q.Disk)){if($existing.Offset -lt ($q.Offset+$q.Length) -and ($existing.Offset+$existing.Size) -gt $q.Offset){throw 'LayoutChanged'}}
 $p=New-Partition -DiskNumber $q.Disk -Offset $q.Offset -Size $q.Length -GptType $q.Type -AssignDriveLetter:$false
} else {
 if(!$p -or $p.Offset -ne $q.Offset -or $p.Size -ne $q.Length -or $p.GptType -ne $q.Type -or $p.IsBoot){throw 'OwnershipMismatch'}
 if($q.Kind -eq 'Format') {
  if($q.FileSystem -notin @('FAT32','NTFS')){throw 'UnauthorizedRequest'}
  $v=$p|Get-Volume
  if($v.FileSystemType -notin @('Unknown','RAW','')){throw 'OwnershipMismatch'}
  $v|Format-Volume -FileSystem $q.FileSystem -NewFileSystemLabel 'ORCHESTER' -Force -Confirm:$false|Out-Null
 } elseif($q.Kind -eq 'Mount') {
  if($p.DriveLetter){throw 'OwnershipMismatch'}
  $used=@((Get-Volume).DriveLetter)+@((Get-PSDrive -PSProvider FileSystem).Name)
  $letter=([char[]](90..68))|Where-Object {([string]$_) -notin $used}|Select-Object -First 1
  if(!$letter){throw 'NoMountPoint'}
  $p|Add-PartitionAccessPath -AccessPath (([string]$letter)+':\')
 } elseif($q.Kind -eq 'Unmount') {
  if(!$p.DriveLetter -or (([string]$p.DriveLetter)+':\') -cne $q.Root){throw 'OwnershipMismatch'}
  $p|Remove-PartitionAccessPath -AccessPath $q.Root
 } elseif($q.Kind -eq 'Delete') {
  $p|Remove-Partition -Confirm:$false
  '{}' ;exit 0
 } else {throw 'UnauthorizedRequest'}
}
$p=Get-Partition -DiskNumber $q.Disk -PartitionNumber $p.PartitionNumber
$v=$p|Get-Volume -ErrorAction SilentlyContinue
[pscustomobject]@{Id=([guid]$p.Guid).ToString('D');Number=[int]$p.PartitionNumber;Root=if($p.DriveLetter){([string]$p.DriveLetter)+':\'}else{''};FileSystem=[string]$v.FileSystemType}|ConvertTo-Json -Compress
""";
    private sealed record Command(string Kind,int Disk,string DiskId,Guid Id,long Offset,long Length,string Type,string FileSystem,string Root);
    private sealed record Result(Guid Id,int Number,string Root,string FileSystem);
    public async Task<DiskSnapshot> ReadAsync(InstallationPlan plan,CancellationToken ct) {
        var disk=(await discovery.DiscoverAsync(ct)).SingleOrDefault(d=>d.Number==plan.OriginalDisk.Number)??throw new SafetyException("DiskChanged");
        if(disk.StableId!=plan.OriginalDisk.StableId||disk.Capacity!=plan.OriginalDisk.Capacity||disk.LogicalSector!=plan.OriginalDisk.LogicalSector||disk.PhysicalSector!=plan.OriginalDisk.PhysicalSector)throw new SafetyException("DiskChanged");
        PlanGuard.ValidateDisk(disk);return disk;
    }
    public async Task<OwnedPartition> CreateAsync(InstallationPlan plan,string role,IReadOnlyCollection<OwnedPartition> known,CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(plan.OperationId);
        if(role is not ("InstallerEfi" or "InstallerData"))throw new SafetyException("UnauthorizedRequest");
        var change=plan.Changes.Single(c=>c.Kind==role);
        var disk=await ReadAsync(plan,ct);ValidatePreservation(plan,disk,known,false);
        string type=role=="InstallerEfi"?NativeLayout.EspType:NativeLayout.DataType;
        var result=await RunAsync(new("Create",disk.Number,disk.StableId,Guid.Empty,change.Offset,change.Length,type,"",""),ct);
        if(result.Id==Guid.Empty||plan.OriginalDisk.Partitions.Any(p=>p.Id==result.Id)||known.Any(p=>p.PartitionId==result.Id))throw new SafetyException("OwnershipMismatch");
        return new(plan.OperationId,disk.StableId,result.Id,result.Number,change.Offset,change.Length,type,"RAW");
    }
    public async Task<OwnedPartition> FormatAsync(InstallationPlan plan,OwnedPartition owned,string fileSystem,IReadOnlyCollection<OwnedPartition> known,CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(plan.OperationId);var disk=await ReadAsync(plan,ct);ValidatePreservation(plan,disk,known,false);ValidateOwned(plan,owned,disk);
        var current=disk.Partitions.Single(p=>p.Id==owned.PartitionId);
        if(current.FileSystem is not ("" or "RAW" or "Unknown"))throw new SafetyException("OwnershipMismatch");
        var result=await RunAsync(CommandFor(plan,owned,"Format",fileSystem),ct);
        if(result.FileSystem!=fileSystem)throw new SafetyException("StorageVerificationFailed");
        return owned with {FileSystem=fileSystem};
    }
    public async Task<MountOwnership> MountAsync(InstallationPlan plan,OwnedPartition owned,IReadOnlyCollection<OwnedPartition> known,CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(plan.OperationId);var disk=await ReadAsync(plan,ct);ValidatePreservation(plan,disk,known,true);ValidateOwned(plan,owned,disk);
        var result=await RunAsync(CommandFor(plan,owned,"Mount",owned.FileSystem),ct);
        if(result.Root.Length!=3||result.Root[0] is <'A' or >'Z'||result.Root[1..]!=":\\")throw new SafetyException("OwnershipMismatch");
        return new(plan.OperationId,owned.PartitionId,result.Root,FilesystemUuid(result.Root,owned.FileSystem));
    }
    public async Task UnmountAsync(InstallationPlan plan,OwnedPartition owned,MountOwnership mount,CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(plan.OperationId);var disk=await ReadAsync(plan,ct);ValidateOwned(plan,owned,disk);
        if(mount.OperationId!=plan.OperationId||mount.PartitionId!=owned.PartitionId||disk.Partitions.Single(p=>p.Id==owned.PartitionId).MountPoint+":\\"!=mount.DriveRoot)throw new SafetyException("OwnershipMismatch");
        await RunAsync(CommandFor(plan,owned,"Unmount",owned.FileSystem,mount.DriveRoot),ct);
    }
    public async Task DeleteAsync(InstallationPlan plan,OwnedPartition owned,IReadOnlyCollection<OwnedPartition> known,CancellationToken ct) {
        ReleaseGate.RequireNativeWrites(plan.OperationId);var disk=await ReadAsync(plan,ct);ValidatePreservation(plan,disk,known,true);ValidateOwned(plan,owned,disk);
        await RunAsync(CommandFor(plan,owned,"Delete",owned.FileSystem),ct);
        if((await ReadAsync(plan,ct)).Partitions.Any(p=>p.Id==owned.PartitionId))throw new SafetyException("StorageVerificationFailed");
    }
    public static void ValidatePreservation(InstallationPlan plan,DiskSnapshot disk,IReadOnlyCollection<OwnedPartition> known,bool allowInstalledLinux) {
        foreach(var original in plan.OriginalDisk.Partitions) {
            var current=disk.Partitions.SingleOrDefault(p=>p.Id==original.Id)??throw new SafetyException("LayoutChanged");
            long length=original.Id==plan.SourcePartition?original.Length-plan.RequestedBytes-plan.StagingBytes:original.Length;
            if(current.Offset!=original.Offset||current.Length!=length||current.Type!=original.Type||current.FileSystem!=original.FileSystem||current.IsBoot!=original.IsBoot)
                throw new SafetyException("LayoutChanged");
        }
        var reserve=plan.Changes.Single(c=>c.Kind=="ReserveLinux");
        foreach(var extra in disk.Partitions.Where(p=>!plan.OriginalDisk.Partitions.Any(o=>o.Id==p.Id))) {
            if(known.Any(p=>p.PartitionId==extra.Id))continue;
            if(!allowInstalledLinux||extra.Offset<reserve.Offset||extra.Offset+extra.Length>reserve.Offset+reserve.Length)throw new SafetyException("LayoutChanged");
        }
    }
    public static void ValidateOwned(InstallationPlan plan,OwnedPartition owned,DiskSnapshot disk) {
        if(owned.OperationId!=plan.OperationId||owned.DiskId!=disk.StableId||plan.OriginalDisk.Partitions.Any(p=>p.Id==owned.PartitionId))throw new SafetyException("OwnershipMismatch");
        var p=disk.Partitions.SingleOrDefault(p=>p.Id==owned.PartitionId)??throw new SafetyException("OwnershipMismatch");
        if(p.IsBoot||p.Number!=owned.Number||p.Offset!=owned.Offset||p.Length!=owned.Length||p.Type!=owned.Type)throw new SafetyException("OwnershipMismatch");
        string role=owned.Type==NativeLayout.EspType?"InstallerEfi":"InstallerData";
        var change=plan.Changes.Single(c=>c.Kind==role);
        if(change.Offset!=p.Offset||change.Length!=p.Length)throw new SafetyException("OwnershipMismatch");
        if(owned.FileSystem!="RAW"&&p.FileSystem!=owned.FileSystem)throw new SafetyException("OwnershipMismatch");
    }
    private static Command CommandFor(InstallationPlan p,OwnedPartition o,string kind,string fs,string root="")=>new(kind,p.OriginalDisk.Number,o.DiskId,o.PartitionId,o.Offset,o.Length,o.Type,fs,root);
    private static async Task<Result> RunAsync(Command command,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var start=new ProcessStartInfo(Path.Combine(windows,"System32","WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        start.Environment["PSModulePath"]=Path.Combine(windows,"System32","WindowsPowerShell","v1.0","Modules");
        foreach(string arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(Script))})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new SafetyException("StorageVerificationFailed");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(command));process.StandardInput.Close();
        await process.WaitForExitAsync(CancellationToken.None);string json=await output;string nativeError=await error;
        if(process.ExitCode!=0)throw new NativeCommandException("Storage",command.Kind,process.ExitCode,nativeError);
        if(command.Kind=="Delete")return new(Guid.Empty,0,"","");
        return JsonSerializer.Deserialize<Result>(json)??throw new SafetyException("StorageVerificationFailed");
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]private static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,IntPtr input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlapped);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetVolumeInformationW(string root,StringBuilder name,int nameLength,out uint serial,out uint maxComponent,out uint flags,StringBuilder fs,int fsLength);
    private static string FilesystemUuid(string root,string fileSystem) {
        if(fileSystem=="NTFS") {
            using var file=CreateFileW(@"\\.\"+root[..2],0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);var data=new byte[128];
            if(file.IsInvalid||!DeviceIoControl(file,0x90064,IntPtr.Zero,0,data,128,out uint length,IntPtr.Zero)||length<8)throw new SafetyException("StorageVerificationFailed");
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data).ToString("X16");
        }
        if(!GetVolumeInformationW(root,new StringBuilder(256),256,out uint serial,out _,out _,new StringBuilder(64),64))throw new SafetyException("StorageVerificationFailed");
        string hex=serial.ToString("X8");return hex[..4]+"-"+hex[4..];
    }
}
