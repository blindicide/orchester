using System.Collections.Immutable;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Engine;
public static class Planner {
 public static InstallationPlan Create(DiskSnapshot disk, Guid partition, long requestedBytes, ReleaseProfile profile, VerifiedImage image) {
  if (image.ProfileId != profile.Id) throw new SafetyException("ImageMismatch");
  if (requestedBytes < profile.MinimumStorageBytes) throw new SafetyException("InsufficientSpace");
  PlanGuard.ValidateDisk(disk);
  var source = disk.Partitions.SingleOrDefault(p=>p.Id==partition) ?? throw new SafetyException("SourceMissing");
  const long mib=1024*1024;
  if (requestedBytes % mib != 0) throw new SafetyException("InvalidGeometry");
  long staging = checked(((image.Length + 2L*1024*1024*1024 + mib-1)/mib)*mib);
  long total = checked(requestedBytes + staging);
  long start = checked(source.Offset + source.Length - total);
  var plan = new InstallationPlan(Guid.NewGuid(), DateTimeOffset.UtcNow, disk, partition, requestedBytes, staging, profile.Id, profile.AdapterVersion, InstallationMode.Guided, image,
   [new("ShrinkNtfs", partition, source.Offset, checked(source.Length-total), true), new("ReserveLinux", Guid.Empty, start, requestedBytes, false), new("TemporaryStaging", Guid.Empty, checked(start+requestedBytes), staging, false)], true);
  PlanGuard.Validate(plan, disk, PlanGuard.Fingerprint(plan), false);
  return plan;
 }
}
