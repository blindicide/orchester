using System.Collections.Immutable;
namespace Orchester.Core;

public static class NativeLayout {
    public const long EfiBytes=512L*1024*1024;
    public const string EspType="{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}";
    public const string DataType="{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}";
    public static InstallationPlan ForExecution(InstallationPlan preview) {
        var source=preview.OriginalDisk.Partitions.Single(p=>p.Id==preview.SourcePartition);
        long total=checked(preview.RequestedBytes+preview.StagingBytes), start=checked(source.Offset+source.Length-total);
        if(preview.StagingBytes<=EfiBytes)throw new SafetyException("InsufficientStagingSpace");
        return preview with {DryRun=false,Changes=[
            new("ShrinkNtfs",source.Id,source.Offset,source.Length-total,true),
            new("ReserveLinux",Guid.Empty,start,preview.RequestedBytes,false),
            new("InstallerEfi",Guid.Empty,start+preview.RequestedBytes,EfiBytes,false),
            new("InstallerData",Guid.Empty,start+preview.RequestedBytes+EfiBytes,preview.StagingBytes-EfiBytes,false)]};
    }
}
public sealed record MountOwnership(Guid OperationId,Guid PartitionId,string DriveRoot,string FileSystemUuid);
public sealed record ExecutionRequest(InstallationPlan Plan,string Fingerprint,bool StorageConsent,bool BackupConfirmed,
    VerifiedImage? HdMediaKernel=null,VerifiedImage? HdMediaInitrd=null);
public sealed record NativeOperationState(int SchemaVersion,InstallationPlan Plan,string OwnerSid,DateTimeOffset AuthorizedAt,
    ImmutableArray<OwnedPartition> Partitions,ImmutableArray<MountOwnership> Mounts,BootEntryOwnership? Boot,
    OperationPhase Phase,string? ErrorCode,string? ImageCachePath,ImmutableArray<StagedFile> BootFiles) { public ImmutableArray<Guid> RetainedPartitions {get;init;}=[]; }
public sealed record NativeOperationSummary(Guid OperationId,string ProfileId,OperationPhase Phase,DateTimeOffset Created,
    string? ErrorCode,bool CanRequestBoot,bool CanCleanup);
public sealed record NativeCompletion(Guid OperationId,Guid RootPartitionId,string Distribution,string Version,
    DateTimeOffset WindowsBootVerifiedAt,bool LinuxBootConfirmed,bool Verified,string? Reason);

public sealed record OperationProgress(Guid OperationId,string Phase,double? Fraction);
