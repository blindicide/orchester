using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orchester.Adapters;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Images;
using Orchester.Security;
using Orchester.Storage;
namespace Orchester.Engine;

[SupportedOSPlatform("windows")]
public sealed class WindowsInstallationBackend: IExecutionBackend {
    private readonly PrivilegedOperationStore store;
    private readonly ReleaseProfile profile;
    private readonly ExecutionRequest request;
    private readonly InstallerPartitionStorage partitions=new(new WindowsStorageDiscovery(false));
    private readonly Func<OperationProgress,Task>? progress;
    public WindowsInstallationBackend(PrivilegedOperationStore store,ReleaseProfile profile,ExecutionRequest request,Func<OperationProgress,Task>? progress=null) {
        this.store=store;this.profile=profile;this.request=request;this.progress=progress;
    }
    private async Task<NativeOperationState> State(CancellationToken ct)=>await store.LoadAsync(request.Plan.OperationId,ct);
    private async Task Save(NativeOperationState state,CancellationToken ct) {await store.SaveAsync(state,ct);if(progress is not null)await progress(new(state.Plan.OperationId,state.Phase.ToString(),null));}
    public async Task PreflightAsync(InstallationPlan plan,CancellationToken ct) {
        if(!request.StorageConsent||!request.BackupConfirmed||plan.DryRun||profile.Id!=plan.ProfileId)throw new SafetyException("AuthorizationRequired");
        var adapter=DistributionAdapter.For(profile);adapter.ValidateRelease(profile,false);adapter.ValidateHost(HostInspector.Inspect(),profile);
        if(plan.Mode!=InstallationMode.Guided)throw new SafetyException("AutomationUnavailable");
        var snapshot=(await new WindowsStorageDiscovery().DiscoverAsync(ct)).SingleOrDefault(d=>d.Number==plan.OriginalDisk.Number)??throw new SafetyException("DiskChanged");
        PlanGuard.Validate(plan,snapshot,request.Fingerprint,true);
        string cached=Path.Combine(store.Folder(plan.OperationId),"source.iso");
        await ImageVerifier.VerifyAsync(cached,plan.Image.Sha256,plan.ProfileId,plan.Image.Trust,ct);
        if(!(await IsoInspector.InspectAsync(cached,profile,ct)).Compatible)throw new SafetyException("UnsupportedImage");
        bool debian=profile.Id.StartsWith("debian-",StringComparison.Ordinal);
        if(debian&&(request.HdMediaKernel is null||request.HdMediaInitrd is null))throw new SafetyException("HdMediaRequired");
        if(debian&&plan.Image.Length>=uint.MaxValue)throw new SafetyException("Fat32FileLimit");
        await WindowsBootBackup.SaveAsync(store.Folder(plan.OperationId),ct);
        await ValidateBootFilesAsync(cached,plan,ct); // All image/firmware compatibility refusals happen before shrinking.
        await Save((await State(ct)) with {ImageCachePath=cached,Phase=OperationPhase.PREFLIGHT},ct);
    }
    public async Task<ExecutionArtifact> PrepareStorageAsync(InstallationPlan plan,CancellationToken ct) {
        await new WindowsStorageOperations(new WindowsStorageDiscovery()).ShrinkNtfsAsync(plan,ct);
        foreach(string role in new[]{"InstallerEfi","InstallerData"}) {
            var state=await State(ct);
            var created=await partitions.CreateAsync(plan,role,state.Partitions,ct);
            state=state with {Partitions=state.Partitions.Add(created),Phase=OperationPhase.STORAGE_PREPARED};
            await Save(state,ct); // Ownership is durable before formatting or mounting.
            string fs=role=="InstallerEfi"||profile.Id.StartsWith("debian-",StringComparison.Ordinal)?"FAT32":"NTFS";
            var formatted=await partitions.FormatAsync(plan,created,fs,state.Partitions,ct);
            state=state with {Partitions=state.Partitions.Replace(created,formatted)};await Save(state,ct);
            var mount=await partitions.MountAsync(plan,formatted,state.Partitions,ct);
            state=state with {Mounts=state.Mounts.Add(mount)};await Save(state,ct);
        }
        return await Receipt(plan,"operation.json",ct);
    }
    public async Task<ExecutionArtifact> StageInstallerAsync(InstallationPlan plan,CancellationToken ct) {
        var state=await State(ct);
        var data=state.Partitions.Single(p=>p.Type==NativeLayout.DataType);
        var efi=state.Partitions.Single(p=>p.Type==NativeLayout.EspType);
        var dataMount=state.Mounts.Single(m=>m.PartitionId==data.PartitionId);
        var efiMount=state.Mounts.Single(m=>m.PartitionId==efi.PartitionId);
        string source=state.ImageCachePath??throw new SafetyException("ImageUnverified");
        var files=ImmutableArray.CreateBuilder<StagedFile>();
        string destination=WorkspaceSafety.ResolveChild(dataMount.DriveRoot,"orchester/"+plan.OperationId.ToString("D")+"/source.iso");Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await CopyVerified(source,destination,plan.Image.Sha256,ct);
        files.Add(new("DATA/orchester/"+plan.OperationId.ToString("D")+"/source.iso",new FileInfo(destination).Length,plan.Image.Sha256));
        await using var iso=await Iso9660Reader.OpenAsync(source,ct);await iso.VerifySourceAsync(plan.Image.Sha256,ct);
        async Task Extract(string path,string relative) {
            string output=WorkspaceSafety.ResolveChild(efiMount.DriveRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var file=await iso.ExtractAsync(await iso.FindAsync(path,ct),output,"EFI/"+relative,ct);files.Add(file);
        }
        await Extract("/EFI/BOOT/BOOTX64.EFI","EFI/ORCHESTER/shim.efi");
        await Extract("/EFI/BOOT/grubx64.efi","EFI/ORCHESTER/grubx64.efi");
        try {await Extract("/EFI/BOOT/mmx64.efi","EFI/ORCHESTER/mmx64.efi");}catch(SafetyException e)when(e.Code=="ImageFileMissing"){}
        if(profile.Id.StartsWith("debian-",StringComparison.Ordinal)) {
            await CopyVerified(Path.Combine(store.Folder(plan.OperationId),"hd-vmlinuz"),WorkspaceSafety.ResolveChild(dataMount.DriveRoot,"orchester/"+plan.OperationId.ToString("D")+"/vmlinuz"),request.HdMediaKernel!.Sha256,ct);
            await CopyVerified(Path.Combine(store.Folder(plan.OperationId),"hd-initrd"),WorkspaceSafety.ResolveChild(dataMount.DriveRoot,"orchester/"+plan.OperationId.ToString("D")+"/initrd"),request.HdMediaInitrd!.Sha256,ct);
        }
        // Signed distribution GRUB builds can use either their embedded vendor prefix or cmdpath.
        string cfg=GrubConfiguration(profile,dataMount.FileSystemUuid,plan.OperationId);
        string vendor=profile.Id.StartsWith("debian-",StringComparison.Ordinal)?"debian":"ubuntu";
        foreach(string relative in new[]{"EFI/ORCHESTER/grub.cfg","EFI/"+vendor+"/grub.cfg","boot/grub/grub.cfg"}) {
            string output=WorkspaceSafety.ResolveChild(efiMount.DriveRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            byte[] bytes=Encoding.UTF8.GetBytes(cfg);using var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
            await file.WriteAsync(bytes,ct);file.Flush(true);files.Add(new("EFI/"+relative,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes))));
        }
        if(profile.Id.StartsWith("debian-",StringComparison.Ordinal)) {
            foreach(var pair in new[]{("orchester/"+plan.OperationId.ToString("D")+"/vmlinuz",request.HdMediaKernel!.Sha256),("orchester/"+plan.OperationId.ToString("D")+"/initrd",request.HdMediaInitrd!.Sha256)})
                files.Add(new("DATA/"+pair.Item1,new FileInfo(WorkspaceSafety.ResolveChild(dataMount.DriveRoot,pair.Item1)).Length,pair.Item2));
        }
        await Save(state with {Phase=OperationPhase.INSTALLER_STAGED,BootFiles=files.ToImmutable()},ct);
        return await Receipt(plan,"operation.json",ct);
    }
    public async Task<ExecutionArtifact> ConfigureBootAsync(InstallationPlan plan,CancellationToken ct) {
        var state=await State(ct);var efi=state.Partitions.Single(p=>p.Type==NativeLayout.EspType);
        WindowsFirmwareVariables.EnableReadPrivilege();var service=new UefiBootService(new WindowsFirmwareVariables());
        var ownership=service.Register(plan.OperationId,efi,plan.OriginalDisk.LogicalSector,@"\EFI\ORCHESTER\shim.efi");
        await Save(state with {Boot=ownership,Phase=OperationPhase.BOOT_CONFIGURED},ct);
        return new(efi.PartitionId,ownership.ContentSha256);
    }
    public async Task RequestOneTimeBootAsync(InstallationPlan plan,CancellationToken ct) {
        var state=await State(ct);if(state.Boot is null)throw new SafetyException("BootOwnershipMismatch");
        WindowsFirmwareVariables.EnableReadPrivilege();new UefiBootService(new WindowsFirmwareVariables()).RequestOneTimeBoot(state.Boot);
        await Save(state with {Phase=OperationPhase.REBOOT_REQUESTED},ct);
    }
    public static string GrubConfiguration(ReleaseProfile profile,string filesystemUuid,Guid operationId) {
        if(!System.Text.RegularExpressions.Regex.IsMatch(filesystemUuid,@"\A[A-Fa-f0-9-]{8,36}\z"))throw new SafetyException("BootTargetUnsafe");
        bool debian=profile.Id.StartsWith("debian-",StringComparison.Ordinal);
        string prefix="/orchester/"+operationId.ToString("D");
        string linux=debian?$"linux ($orchester){prefix}/vmlinuz iso-scan/filename={prefix}/source.iso priority=high ---":$"loopback loop ($orchester){prefix}/source.iso\n linux (loop){SafePath(profile.KernelPath)} boot=casper iso-scan/filename={prefix}/source.iso ---";
        string initrd=debian?$"initrd ($orchester){prefix}/initrd":$"initrd (loop){SafePath(profile.InitrdPath)}";
        return $"# ORCHESTER {operationId:D}\nset timeout=10\nmenuentry 'ORCHESTER installer' {{\n search --no-floppy --fs-uuid --set=orchester {filesystemUuid}\n {linux}\n {initrd}\n}}\n";
    }
    private static string SafePath(string path) {if(!System.Text.RegularExpressions.Regex.IsMatch(path,@"\A/[A-Za-z0-9/_.-]+\z")||path.Contains(".."))throw new SafetyException("UnsafePath");return path;}
    private async Task ValidateBootFilesAsync(string source,InstallationPlan plan,CancellationToken ct) {
        string check=Path.Combine(store.Folder(plan.OperationId),"boot-check");Directory.CreateDirectory(check);
        await using var iso=await Iso9660Reader.OpenAsync(source,ct);
        foreach(string name in new[]{"BOOTX64.EFI","grubx64.efi"}) {
            string target=Path.Combine(check,name);
            await iso.ExtractAsync(await iso.FindAsync("/EFI/BOOT/"+name,ct),target,name,ct);
        }
        WindowsFirmwareVariables.EnableReadPrivilege();var firmware=new UefiBootService(new WindowsFirmwareVariables()).Inspect();
        if(firmware.BootNext is not null)throw new SafetyException("BootConfigurationChanged");
        if(!firmware.Entries.Any(e=>e.WindowsBootManager))throw new SafetyException("WindowsBootMissing");
        if(HostInspector.Inspect().SecureBoot==true) {
            if(!profile.Validated)throw new SafetyException("SecureBootReleaseRequired");
            BootTrust.VerifyFirmwareLoader(Path.Combine(check,"BOOTX64.EFI"));
            BootTrust.VerifySbat(Path.Combine(check,"BOOTX64.EFI"),Path.Combine(check,"grubx64.efi"));
        }
    }
    private static async Task CopyVerified(string source,string destination,string expected,CancellationToken ct) {
        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous))
        using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.WriteThrough|FileOptions.Asynchronous)){await input.CopyToAsync(output,1024*1024,ct);await output.FlushAsync(ct);output.Flush(true);}
        using var file=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new SafetyException("ChecksumMismatch");
    }
    private async Task<ExecutionArtifact> Receipt(InstallationPlan plan,string file,CancellationToken ct) {
        using var input=new FileStream(Path.Combine(store.Folder(plan.OperationId),file),FileMode.Open,FileAccess.Read,FileShare.Read);
        return new(plan.OperationId,Convert.ToHexString(await SHA256.HashDataAsync(input,ct)));
    }
}
