using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Core;
namespace Orchester.Security;
public static class PlanGuard {
 public static string Fingerprint(InstallationPlan plan) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan)));
 public static void Validate(InstallationPlan plan, DiskSnapshot current, string approvedFingerprint, bool executing) {
  if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(Fingerprint(plan)), System.Text.Encoding.UTF8.GetBytes(approvedFingerprint))) throw new SafetyException("PlanChanged");
  if (plan.OriginalDisk.StableId != current.StableId || plan.OriginalDisk.Number != current.Number || string.IsNullOrWhiteSpace(current.StableId)) throw new SafetyException("DiskChanged");
  // Compare all geometry and safety observations, including partition identities and shrink limits.
  if (JsonSerializer.Serialize(plan.OriginalDisk) != JsonSerializer.Serialize(current)) throw new SafetyException("LayoutChanged");
  ValidateDisk(current);
  var source = current.Partitions.SingleOrDefault(x => x.Id == plan.SourcePartition) ?? throw new SafetyException("SourceMissing");
  if (source.Protected || source.FileSystem != "NTFS" || source.Health != "Healthy" || source.Encryption != EncryptionState.Unencrypted || source.ShrinkBytes is null) throw new SafetyException("UnsafeSource");
  if (plan.RequestedBytes <= 0 || plan.StagingBytes <= 0 || checked(plan.RequestedBytes + plan.StagingBytes) > source.ShrinkBytes) throw new SafetyException("ShrinkLimit");
  long total=checked(plan.RequestedBytes+plan.StagingBytes);
  long start=checked(source.Offset+source.Length-total);
  const long mib=1024*1024;
  if(total>=source.Length || start % mib!=0 || plan.RequestedBytes % mib!=0 || plan.StagingBytes % mib!=0) throw new SafetyException("InvalidGeometry");
  PlannedChange[] expected=[new("ShrinkNtfs",source.Id,source.Offset,source.Length-total,true),new("ReserveLinux",Guid.Empty,start,plan.RequestedBytes,false),new("TemporaryStaging",Guid.Empty,start+plan.RequestedBytes,plan.StagingBytes,false)];
  if(!plan.Changes.SequenceEqual(expected)) throw new SafetyException("PlanChanged");
  if (plan.Image.Trust == TrustLevel.Unverified || plan.Image.Sha256.Length != 64 || !plan.Image.Sha256.All(Uri.IsHexDigit)) throw new SafetyException("ImageUnverified");
  if (executing) throw new SafetyException("ExecutionUnavailable");
 }
 public static void ValidateDisk(DiskSnapshot disk) {
  if (disk.PartitionStyle != "GPT" || disk.Health != "Healthy" || disk.Offline || disk.ReadOnly || disk.BusType is not ("SATA" or "NVMe" or "SAS" or "ATA" or "Virtual" or "File Backed Virtual") || string.IsNullOrWhiteSpace(disk.StableId) || disk.LogicalSector is not (512 or 4096) || disk.PhysicalSector < disk.LogicalSector) throw new SafetyException("UnsupportedDisk");
  long end = disk.LogicalSector * 34L;
  var ids = new HashSet<Guid>();
  foreach (var p in disk.Partitions.OrderBy(p=>p.Offset)) {
   if (p.Id == Guid.Empty || !ids.Add(p.Id) || p.Offset < end || p.Length <= 0 || p.Offset % disk.LogicalSector != 0 || p.Length % disk.LogicalSector != 0 || p.Length > disk.Capacity - disk.LogicalSector*33L - p.Offset) throw new SafetyException("InvalidGeometry");
   end = checked(p.Offset + p.Length);
  }
 }
}
public sealed record HelperRequest(int ProtocolVersion, Guid OperationId, string PlanFingerprint, HelperCommand Command);
public enum HelperCommand { Inspect, ValidatePlan, PrepareStorage, ConfigureBoot, CleanupOwnedResources }
// No string commands, paths to executables, or arbitrary scripts cross this boundary.
public static class HelperPolicy {
 public static void Authorize(HelperRequest request, InstallationPlan plan, DiskSnapshot current) {
  if (request.ProtocolVersion != 1 || request.OperationId != plan.OperationId || !Enum.IsDefined(request.Command)) throw new SafetyException("UnauthorizedRequest");
  PlanGuard.Validate(plan, current, request.PlanFingerprint, request.Command is not (HelperCommand.Inspect or HelperCommand.ValidatePlan));
 }
}
