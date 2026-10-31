using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orchester.Core;
using Orchester.Engine;
namespace Orchester.Recovery;
public sealed record RecoveryRecord(Guid OperationId,OperationPhase Phase,bool Consistent,bool DryRun,string ActionCode);
public sealed class RecoveryService(OperationStore store) {
 public async Task<ImmutableArray<RecoveryRecord>> InspectAsync(CancellationToken ct) {
  var records=ImmutableArray.CreateBuilder<RecoveryRecord>();
  foreach(var id in store.ListOperations()) {
   try { var m=await store.LoadAsync(id,ct); records.Add(new(id,m.Phase,true,m.Plan.DryRun,"InspectJournal")); }
   catch(Exception e) when(e is SafetyException or JsonException or IOException) { records.Add(new(id,OperationPhase.RECOVERY_REQUIRED,false,false,"JournalIncomplete")); }
  }
  return records.ToImmutable();
 }
 public async Task ExportRedactedAsync(string destination,HostSnapshot host,IEnumerable<DiskSnapshot> disks,CancellationToken ct) {
  // Explicit allowlist: never serialize manifests, local image paths, mount points or credentials.
  string Alias(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
  var payload=new { SchemaVersion=1,ExportedAt=DateTimeOffset.UtcNow,Host=host,Disks=disks.Select(d=>new {Disk=Alias(d.StableId),d.Capacity,d.LogicalSector,d.PhysicalSector,d.PartitionStyle,d.Health,Partitions=d.Partitions.Select(p=>new {p.Number,p.Offset,p.Length,p.Type,p.FileSystem,p.Health,p.Encryption})}),Operations=await InspectAsync(ct) };
  await File.WriteAllTextAsync(destination,JsonSerializer.Serialize(payload,OperationStore.Json),ct);
 }
}
