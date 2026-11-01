using System.Collections.Immutable;
namespace Orchester.Core;
public enum OperationPhase { CREATED, PREFLIGHT, IMAGE_READY, PLAN_APPROVED, STORAGE_PREPARED, INSTALLER_STAGED, BOOT_CONFIGURED, REBOOT_REQUESTED, INSTALLER_RUNNING, POST_INSTALL_PENDING, COMPLETED, FAILED, CANCELLED, RECOVERY_REQUIRED, UNKNOWN_EXTERNAL_STATE }
public enum InstallationMode { Guided, Automated, Advanced }
public enum EncryptionState { Unknown, Unencrypted, Protected, Suspended, Locked }
public enum TrustLevel { Unverified, UserAuthenticatedDigest, PublisherAuthenticated }
public sealed record HostSnapshot(string WindowsVersion, bool SupportedWindows, bool Uefi, bool? SecureBoot, bool Is64Bit);
public sealed record PartitionSnapshot(int Number, Guid Id, string Type, long Offset, long Length, string FileSystem, string MountPoint, string Health, EncryptionState Encryption, long? ShrinkBytes, bool IsSystem, bool IsBoot, bool? Dirty = null) {
 public bool Protected => IsSystem || Guid.TryParse(Type,out var type) && (
  type==new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B") ||
  type==new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC") ||
  type==new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE"));
}
public sealed record DiskSnapshot(int Number, string StableId, string Name, long Capacity, int LogicalSector, int PhysicalSector, string PartitionStyle, string BusType, string Health, bool Offline, bool ReadOnly, ImmutableArray<PartitionSnapshot> Partitions);
public sealed record ReleaseProfile(string Id, string Distribution, string Version, string Architecture, Uri DownloadUrl, Uri ChecksumUrl, string Installer, string KernelPath, string InitrdPath, long MinimumStorageBytes, bool SecureBoot, string AdapterVersion, bool Validated, ImmutableArray<InstallationMode> Modes);
public sealed record VerifiedImage(string Path, string Sha256, long Length, TrustLevel Trust, string ProfileId, DateTimeOffset VerifiedAt);
public sealed record ImageInspection(bool Iso9660, bool X64Efi, bool HasKernel, bool HasInitrd, ImmutableArray<string> Findings) { public bool Compatible => Iso9660 && X64Efi && HasKernel && HasInitrd; }
public sealed record PlannedChange(string Kind, Guid SourcePartition, long Offset, long Length, bool Irreversible);
public sealed record InstallationPlan(Guid OperationId, DateTimeOffset Created, DiskSnapshot OriginalDisk, Guid SourcePartition, long RequestedBytes, long StagingBytes, string ProfileId, string AdapterVersion, InstallationMode Mode, VerifiedImage Image, ImmutableArray<PlannedChange> Changes, bool DryRun);
public sealed record OperationManifest(Guid OperationId, OperationPhase Phase, InstallationPlan Plan, ImmutableArray<Guid> OwnedPartitions, ImmutableArray<string> OwnedBootEntries, ImmutableArray<string> Checkpoints);
public sealed record JournalEntry(long Sequence, DateTimeOffset Timestamp, Guid OperationId, OperationPhase Phase, string Component, string ActionCode, bool Verified, string? ErrorCode, ImmutableArray<string> RecoveryOptions);
public sealed class SafetyException(string code) : Exception(code) { public string Code { get; } = code; }
public interface IStorageDiscovery { Task<ImmutableArray<DiskSnapshot>> DiscoverAsync(CancellationToken cancellationToken); }
public interface IInstallationAdapter {
 string Id { get; } string Version { get; }
 bool DetectImage(ReleaseProfile profile, ImageInspection inspection);
 void ValidateRelease(ReleaseProfile profile, bool execution);
 void ValidateHost(HostSnapshot host, ReleaseProfile profile);
 long CalculateSpaceRequirements(ReleaseProfile profile, long imageBytes);
 void ValidateInstallationPlan(InstallationPlan plan, ReleaseProfile profile);
 string PrepareInstaller(ReleaseProfile profile);
 string GenerateBootConfiguration(ReleaseProfile profile);
 string GenerateUnattendedConfiguration(ReleaseProfile profile);
 string GetRecoveryInstructions();
}
