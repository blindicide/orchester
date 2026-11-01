using System.Collections.Immutable;
namespace Orchester.Core;

public enum BootStrategy { CasperIso, DebianHdMedia }
public sealed record InstallerRecipe(string ProfileId, BootStrategy Strategy, string KernelPath,
    string InitrdPath, string IsoPath, ImmutableArray<string> KernelArguments, bool RequiresAdditionalHdMedia);
public sealed record StagedFile(string RelativePath, long Length, string Sha256);
public sealed record StagingReceipt(int SchemaVersion, Guid OperationId, string PlanFingerprint,
    string ImageSha256, string ProfileId, ImmutableArray<StagedFile> Files, DateTimeOffset Created);
public sealed record OwnedPartition(Guid OperationId, string DiskId, Guid PartitionId, int Number,
    long Offset, long Length, string Type, string FileSystem);
public sealed record BootEntryOwnership(Guid OperationId, ushort Number, string ContentSha256,
    Guid PartitionId, string EfiPath, byte[]? PreviousBootNext);
public sealed record FirmwareBootEntry(ushort Number, string Description, string EfiPath,
    Guid? PartitionId, bool Active, bool WindowsBootManager, string ContentSha256);
public sealed record FirmwareSnapshot(ImmutableArray<ushort> BootOrder, ushort? BootCurrent,
    ushort? BootNext, ImmutableArray<FirmwareBootEntry> Entries);
public sealed record AutomationTarget(string LinuxDiskSerial, int RootNumber, Guid RootId,
    int EfiNumber, Guid EfiId, bool DedicatedEfiOwned, bool RootFormatApproved);
public sealed record UnattendedOptions(string Locale, string Keyboard, string TimeZone,
    string Hostname, string Username, string PasswordHash, AutomationTarget Target,
    ImmutableArray<string> Packages);
public sealed record GeneratedConfiguration(string FileName, string Content, bool PreviewOnly,
    ImmutableArray<string> Warnings);
public sealed record InstalledSystemEvidence(Guid OperationId, Guid RootPartitionId, string DiskId,
    string DistributionId, string Version, string FileSystem, bool RootReadVerified,
    bool BootEntryVerified, bool InstallerReceiptVerified, bool LinuxBootVerified, bool WindowsBootVerified);
public sealed record KnownInstallation(Guid OperationId, string DistributionId, string Version,
    string DiskId, Guid RootPartitionId, DateTimeOffset VerifiedAt, bool Verified);

public interface IFirmwareVariables {
    byte[]? Read(string name);
    void Write(string name, byte[] value);
    void Delete(string name);
}
public interface IInstalledSystemProbe {
    Task<InstalledSystemEvidence> InspectAsync(Guid operationId, CancellationToken ct);
}
// Every command is typed. Implementations must re-enumerate stable identities before mutations.
public interface IStorageOperations {
    Task<DiskSnapshot> InspectAsync(int diskNumber, CancellationToken ct);
    Task ShrinkNtfsAsync(InstallationPlan plan, CancellationToken ct);
    Task<OwnedPartition> CreateStagingAsync(InstallationPlan plan, CancellationToken ct);
    Task FormatStagingAsync(InstallationPlan plan, OwnedPartition partition, CancellationToken ct);
    Task DeleteOwnedAsync(InstallationPlan plan, OwnedPartition partition, CancellationToken ct);
}
