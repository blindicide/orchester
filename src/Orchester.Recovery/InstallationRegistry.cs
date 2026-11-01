using System.Collections.Immutable;
using System.Text.Json;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Recovery;

public sealed class InstallationRegistry(string root, IInstalledSystemProbe probe) {
    public async Task<KnownInstallation> ConfirmAsync(InstallationPlan plan, ReleaseProfile profile, Guid expectedRoot, CancellationToken ct) {
        var e = await probe.InspectAsync(plan.OperationId, ct);
        string expectedDistribution=profile.Id.StartsWith("mint-",StringComparison.Ordinal)?"linuxmint":profile.Id.Split('-')[0];
        if (profile.Id!=plan.ProfileId || e.DistributionId!=expectedDistribution || e.Version!=profile.Version || plan.DryRun || e.OperationId != plan.OperationId || e.RootPartitionId != expectedRoot || expectedRoot == Guid.Empty ||
            e.DiskId != plan.OriginalDisk.StableId || !e.RootReadVerified || !e.BootEntryVerified || !e.InstallerReceiptVerified ||
            !e.LinuxBootVerified || !e.WindowsBootVerified || e.FileSystem is not ("ext4" or "btrfs" or "xfs") ||
            string.IsNullOrWhiteSpace(e.DistributionId) || string.IsNullOrWhiteSpace(e.Version) ||
            plan.OriginalDisk.Partitions.Any(p => p.Id == expectedRoot)) throw new SafetyException("CompletionEvidenceInsufficient");
        Directory.CreateDirectory(root); WorkspaceSafety.RejectReparsePoints(root);
        string path = WorkspaceSafety.ResolveChild(root, plan.OperationId.ToString("D") + ".json");
        var record = new KnownInstallation(plan.OperationId, e.DistributionId, e.Version, e.DiskId, e.RootPartitionId, DateTimeOffset.UtcNow, true);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(file, record, cancellationToken: ct); file.Flush(true);
        return record;
    }
    public async Task<ImmutableArray<KnownInstallation>> ListAsync(CancellationToken ct) {
        if (!Directory.Exists(root)) return [];
        var records = ImmutableArray.CreateBuilder<KnownInstallation>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json")) {
            WorkspaceSafety.RejectReparsePoints(path);
            var record = JsonSerializer.Deserialize<KnownInstallation>(await File.ReadAllTextAsync(path, ct)) ?? throw new SafetyException("JournalIncomplete");
            records.Add(record);
        }
        return records.ToImmutable();
    }
}

public static class CleanupPolicy {
    public static void Validate(InstallationPlan plan, OwnedPartition owned, DiskSnapshot current, bool referencedByBoot, bool installerMayBeRunning) {
        if (referencedByBoot || installerMayBeRunning) throw new SafetyException("CleanupDependency");
        if (owned.OperationId != plan.OperationId || owned.DiskId != plan.OriginalDisk.StableId || current.StableId != owned.DiskId ||
            owned.PartitionId == Guid.Empty || plan.OriginalDisk.Partitions.Any(p => p.Id == owned.PartitionId)) throw new SafetyException("OwnershipMismatch");
        StorageStatePolicy.ValidateOwnership(plan, owned, current);
        var partition = current.Partitions.SingleOrDefault(p => p.Id == owned.PartitionId) ?? throw new SafetyException("OwnershipMismatch");
        if (partition.Protected || partition.IsBoot || partition.Offset != owned.Offset || partition.Length != owned.Length || partition.Type != owned.Type)
            throw new SafetyException("OwnershipMismatch");
    }
}
