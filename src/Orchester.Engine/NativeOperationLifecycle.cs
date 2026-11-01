using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Security;
using Orchester.Storage;
namespace Orchester.Engine;

[SupportedOSPlatform("windows")]
public sealed class NativeOperationLifecycle(PrivilegedOperationStore store) {
    private readonly InstallerPartitionStorage storage=new(new WindowsStorageDiscovery(false));
    private async Task<NativeOperationState> EnsureMounted(NativeOperationState state,CancellationToken ct) {
        var disk=await storage.ReadAsync(state.Plan,ct);
        foreach(var owned in state.Partitions) {
            InstallerPartitionStorage.ValidateOwned(state.Plan,owned,disk);
            var part=disk.Partitions.Single(p=>p.Id==owned.PartitionId);
            var recorded=state.Mounts.FirstOrDefault(m=>m.PartitionId==owned.PartitionId);
            if(recorded is not null&&part.MountPoint+":\\"==recorded.DriveRoot)continue;
            if(!string.IsNullOrEmpty(part.MountPoint))throw new SafetyException("OwnershipMismatch");
            var mount=await storage.MountAsync(state.Plan,owned,state.Partitions,ct);
            state=state with {Mounts=state.Mounts.Where(m=>m.PartitionId!=owned.PartitionId).Append(mount).ToImmutableArray()};
            await store.SaveAsync(state,ct);
        }
        return state;
    }
    public async Task VerifyStagedAsync(NativeOperationState input,CancellationToken ct) {
        var state=await EnsureMounted(input,ct);
        if(state.BootFiles.IsDefaultOrEmpty)throw new SafetyException("StagingIncomplete");
        foreach(var expected in state.BootFiles) {
            var (mount,path)=Locate(state,expected.RelativePath);
            string filePath=WorkspaceSafety.ResolveChild(mount.DriveRoot,path);
            using var file=new FileStream(filePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length!=expected.Length||!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(expected.Sha256,StringComparison.OrdinalIgnoreCase))throw new SafetyException("StagingChanged");
        }
    }
    public async Task<NativeOperationSummary> CleanupAsync(NativeOperationState input,CancellationToken ct) {
        if(input.Phase is OperationPhase.INSTALLER_RUNNING)throw new SafetyException("CleanupDependency");
        if(input.Partitions.IsEmpty) {
            foreach(string name in new[]{"source.iso","hd-vmlinuz","hd-initrd"}){string path=Path.Combine(store.Folder(input.Plan.OperationId),name);if(File.Exists(path))File.Delete(path);}
            await store.SaveAsync(input with {Phase=OperationPhase.CANCELLED},ct);
            return new(input.Plan.OperationId,input.Plan.ProfileId,OperationPhase.CANCELLED,input.Plan.Created,null,false,false);
        }
        var state=await EnsureMounted(input,ct);
        WindowsFirmwareVariables.EnableReadPrivilege();var vars=new WindowsFirmwareVariables();
        if(state.Boot is not null) {
            new UefiBootService(vars).RemoveOwned(state.Boot);
            state=state with {Boot=null};await store.SaveAsync(state,ct);
        }
        var firmware=new UefiBootService(vars).Inspect();
        foreach(var owned in state.Partitions.Where(p=>!state.RetainedPartitions.Contains(p.PartitionId)).OrderBy(p=>p.Type==NativeLayout.EspType?1:0).ToArray()) {
            var mount=state.Mounts.Single(m=>m.PartitionId==owned.PartitionId);
            var known=state.BootFiles.Where(f=>f.RelativePath.StartsWith(owned.Type==NativeLayout.EspType?"EFI/":"DATA/",StringComparison.Ordinal)).ToArray();
            var allowed=known.Select(f=>Path.GetFullPath(WorkspaceSafety.ResolveChild(mount.DriveRoot,Locate(state,f.RelativePath).Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool foreign=firmware.Entries.Any(e=>e.PartitionId==owned.PartitionId);
            foreach(var file in EnumerateFiles(mount.DriveRoot)) {
                // Windows may create volume metadata; it is not an installed bootloader or user installation.
                string relative=Path.GetRelativePath(mount.DriveRoot,file);
                if(relative.StartsWith("System Volume Information"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
                if(!allowed.Contains(Path.GetFullPath(file)))foreign=true;
            }
            foreach(var expected in known) {
                string path=WorkspaceSafety.ResolveChild(mount.DriveRoot,Locate(state,expected.RelativePath).Path);
                if(!File.Exists(path))continue;
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    if(!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(expected.Sha256,StringComparison.OrdinalIgnoreCase)){foreign=true;continue;}
                }
                File.Delete(path);
            }
            await storage.UnmountAsync(state.Plan,owned,mount,ct);
            state=state with {Mounts=state.Mounts.Remove(mount)};await store.SaveAsync(state,ct);
            if(!foreign) {await storage.DeleteAsync(state.Plan,owned,state.Partitions,ct);state=state with {Partitions=state.Partitions.Remove(owned)};}
            else state=state with {RetainedPartitions=state.RetainedPartitions.Add(owned.PartitionId)};
            await store.SaveAsync(state,ct);
        }
        foreach(string name in new[]{"source.iso","hd-vmlinuz","hd-initrd"}) {string path=Path.Combine(store.Folder(state.Plan.OperationId),name);if(File.Exists(path))File.Delete(path);}
        state=state with {Phase=state.Phase==OperationPhase.COMPLETED?OperationPhase.COMPLETED:OperationPhase.CANCELLED,ErrorCode=null};await store.SaveAsync(state,ct);
        return new(state.Plan.OperationId,state.Plan.ProfileId,state.Phase,state.Plan.Created,null,false,false);
    }
    public async Task<NativeCompletion> ConfirmAsync(NativeOperationState state,bool linuxBootConfirmed,CancellationToken ct) {
        if(!linuxBootConfirmed||state.Phase is not (OperationPhase.REBOOT_REQUESTED or OperationPhase.POST_INSTALL_PENDING or OperationPhase.UNKNOWN_EXTERNAL_STATE))throw new SafetyException("CompletionEvidenceInsufficient");
        var disk=await storage.ReadAsync(state.Plan,ct);InstallerPartitionStorage.ValidatePreservation(state.Plan,disk,state.Partitions,true);
        DateTimeOffset windowsBoot=DateTimeOffset.UtcNow-TimeSpan.FromMilliseconds(GetTickCount64());
        if(windowsBoot<=state.AuthorizedAt)throw new SafetyException("CompletionEvidenceInsufficient");
        WindowsFirmwareVariables.EnableReadPrivilege();var firmware=new UefiBootService(new WindowsFirmwareVariables()).Inspect();
        var reserved=state.Plan.Changes.Single(c=>c.Kind=="ReserveLinux");
        string family=state.Plan.ProfileId.StartsWith("mint-",StringComparison.Ordinal)?"linuxmint":state.Plan.ProfileId.Split('-')[0];
        foreach(var partition in disk.Partitions.Where(p=>!state.Plan.OriginalDisk.Partitions.Any(o=>o.Id==p.Id)&&p.Offset>=reserved.Offset&&p.Offset+p.Length<=reserved.Offset+reserved.Length)) {
            try {
                using var raw=new FileStream(@"\\.\PhysicalDrive"+disk.Number,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,4096,FileOptions.RandomAccess);
                var os=await new Ext4Identification(raw,partition.Offset,partition.Length,disk.LogicalSector).IdentifyAsync(ct);
                string vendor=family=="debian"?"debian":"ubuntu";
                bool boot=firmware.Entries.Any(e=>!e.WindowsBootManager&&e.PartitionId is not null&&e.EfiPath.StartsWith("\\EFI\\"+vendor+"\\",StringComparison.OrdinalIgnoreCase));
                var profile=Orchester.Images.CatalogService.LoadBuiltin().Single(p=>p.Id==state.Plan.ProfileId);
                var expected=profile.Version.Split(' ')[0].Split('.');var actual=os.Version.Split('.');int pieces=family=="debian"?1:Math.Min(2,expected.Length);
                if(os.Id!=family||!boot||actual.Length<pieces||!expected.Take(pieces).SequenceEqual(actual.Take(pieces)))continue;
                var completion=new NativeCompletion(state.Plan.OperationId,partition.Id,os.Id,os.Version,DateTimeOffset.UtcNow,true,true,null);
                string path=Path.Combine(store.Folder(state.Plan.OperationId),"completion.json");
                using(var file=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){await JsonSerializer.SerializeAsync(file,completion,cancellationToken:ct);file.Flush(true);}
                await store.SaveAsync(state with {Phase=OperationPhase.COMPLETED},ct);return completion;
            } catch(SafetyException e)when(e.Code is "InstalledFilesystemUnsupported" or "CompletionEvidenceInsufficient"){}
        }
        await store.SaveAsync(state with {Phase=OperationPhase.POST_INSTALL_PENDING},ct);
        throw new SafetyException("CompletionEvidenceInsufficient");
    }
    private static (MountOwnership Mount,string Path) Locate(NativeOperationState state,string path) {
        bool efi=path.StartsWith("EFI/",StringComparison.Ordinal);if(!efi&&!path.StartsWith("DATA/",StringComparison.Ordinal))throw new SafetyException("UnsafePath");
        var owned=state.Partitions.Single(p=>p.Type==(efi?NativeLayout.EspType:NativeLayout.DataType));
        return(state.Mounts.Single(m=>m.PartitionId==owned.PartitionId),path[(efi?4:5)..]);
    }
    private static IEnumerable<string> EnumerateFiles(string root) {
        foreach(var path in Directory.EnumerateFileSystemEntries(root)) {
            string name=Path.GetFileName(path);
            if(name.Equals("System Volume Information",StringComparison.OrdinalIgnoreCase))continue;
            WorkspaceSafety.RejectReparsePoints(path);
            if(Directory.Exists(path)){foreach(string child in EnumerateFiles(path))yield return child;}
            else yield return path;
        }
    }
    [DllImport("kernel32.dll")]private static extern ulong GetTickCount64();
}
