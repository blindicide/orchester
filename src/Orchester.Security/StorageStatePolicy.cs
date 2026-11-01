using Orchester.Core;
namespace Orchester.Security;

public static class StorageStatePolicy {
    public static void ValidateAfterShrink(InstallationPlan plan, DiskSnapshot disk, OwnedPartition? owned=null) {
        PlanGuard.ValidateDisk(disk);
        if (disk.StableId != plan.OriginalDisk.StableId || disk.Number != plan.OriginalDisk.Number || disk.Capacity != plan.OriginalDisk.Capacity ||
            disk.LogicalSector != plan.OriginalDisk.LogicalSector || disk.PhysicalSector != plan.OriginalDisk.PhysicalSector)
            throw new SafetyException("DiskChanged");
        if (disk.Partitions.Length != plan.OriginalDisk.Partitions.Length + (owned is null ? 0 : 1)) throw new SafetyException("LayoutChanged");
        foreach (var original in plan.OriginalDisk.Partitions) {
            var current = disk.Partitions.SingleOrDefault(p => p.Id == original.Id) ?? throw new SafetyException("LayoutChanged");
            long expectedLength = original.Id == plan.SourcePartition ? original.Length - plan.RequestedBytes - plan.StagingBytes : original.Length;
            if (current.Offset != original.Offset || current.Length != expectedLength || current.Type != original.Type || current.FileSystem != original.FileSystem ||
                current.IsSystem != original.IsSystem || current.IsBoot != original.IsBoot || current.Health != original.Health || current.Encryption != original.Encryption || current.Dirty != original.Dirty)
                throw new SafetyException("LayoutChanged");
        }
        if (owned is not null) ValidateOwnership(plan, owned, disk);
    }
    public static void ValidateOwnership(InstallationPlan plan, OwnedPartition owned, DiskSnapshot disk) {
        var source = plan.OriginalDisk.Partitions.Single(p => p.Id == plan.SourcePartition);
        if (owned.OperationId != plan.OperationId || owned.DiskId != plan.OriginalDisk.StableId || disk.StableId != owned.DiskId || owned.PartitionId == Guid.Empty ||
            owned.Offset != source.Offset + source.Length - plan.StagingBytes || owned.Length != plan.StagingBytes ||
            plan.OriginalDisk.Partitions.Any(p => p.Id == owned.PartitionId)) throw new SafetyException("OwnershipMismatch");
        var p = disk.Partitions.SingleOrDefault(p => p.Id == owned.PartitionId) ?? throw new SafetyException("OwnershipMismatch");
        if (p.Protected || p.IsBoot || p.Offset != owned.Offset || p.Length != owned.Length || p.Type != owned.Type || p.Number != owned.Number)
            throw new SafetyException("OwnershipMismatch");
    }
}
