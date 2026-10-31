using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orchester.Core;
namespace Orchester.Engine;
public sealed class OperationStore {
 public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
 private static readonly JsonSerializerOptions Lines = new() { Converters = { new JsonStringEnumConverter() } };
 private readonly string root;
 private readonly SemaphoreSlim gate = new(1,1);
 public OperationStore(string root) { this.root=Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
 private string Folder(Guid id) => Path.Combine(root,id.ToString("D"));
 public async Task SaveDryRunAsync(InstallationPlan plan, CancellationToken ct) {
  await gate.WaitAsync(ct);
  try {
   var folder=Folder(plan.OperationId); Directory.CreateDirectory(folder);
   if (File.Exists(Path.Combine(folder,"manifest.json"))) throw new SafetyException("OperationExists");
   // Persist intent before any future action; v0.1 performs only reversible file writes.
   await AppendAsync(folder,new(1,DateTimeOffset.UtcNow,plan.OperationId,OperationPhase.CREATED,"Coordinator","DryRunCreated",true,null,[]),ct);
   var manifest=new OperationManifest(plan.OperationId,OperationPhase.CREATED,plan,[],[],["DryRunOnly"]);
   await WriteManifestAsync(folder,manifest,ct);
   await TransitionInternalAsync(manifest,OperationPhase.PREFLIGHT,"PreflightPassed",ct);
  } finally { gate.Release(); }
 }
 public static bool CanTransition(OperationPhase from, OperationPhase to) {
  if (from is OperationPhase.COMPLETED or OperationPhase.CANCELLED) return false;
  if (to is OperationPhase.FAILED or OperationPhase.RECOVERY_REQUIRED or OperationPhase.UNKNOWN_EXTERNAL_STATE) return true;
  if (to==OperationPhase.CANCELLED) return from is OperationPhase.CREATED or OperationPhase.PREFLIGHT or OperationPhase.IMAGE_READY or OperationPhase.PLAN_APPROVED;
  return from switch {
   OperationPhase.CREATED=>to==OperationPhase.PREFLIGHT, OperationPhase.PREFLIGHT=>to==OperationPhase.IMAGE_READY,
   OperationPhase.IMAGE_READY=>to==OperationPhase.PLAN_APPROVED, OperationPhase.PLAN_APPROVED=>to==OperationPhase.STORAGE_PREPARED,
   OperationPhase.STORAGE_PREPARED=>to==OperationPhase.INSTALLER_STAGED, OperationPhase.INSTALLER_STAGED=>to==OperationPhase.BOOT_CONFIGURED,
   OperationPhase.BOOT_CONFIGURED=>to==OperationPhase.REBOOT_REQUESTED, OperationPhase.REBOOT_REQUESTED=>to==OperationPhase.INSTALLER_RUNNING,
   OperationPhase.INSTALLER_RUNNING=>to==OperationPhase.POST_INSTALL_PENDING, _=>false };
 }
 public async Task TransitionAsync(Guid id, OperationPhase next, string actionCode, CancellationToken ct) {
  await gate.WaitAsync(ct);
  try { await TransitionInternalAsync(await LoadAsync(id,ct),next,actionCode,ct); } finally { gate.Release(); }
 }
 private async Task TransitionInternalAsync(OperationManifest manifest, OperationPhase next,string actionCode,CancellationToken ct) {
  if (!CanTransition(manifest.Phase,next)) throw new SafetyException("InvalidTransition");
  if (manifest.Plan.DryRun && next is not (OperationPhase.PREFLIGHT or OperationPhase.IMAGE_READY or OperationPhase.PLAN_APPROVED or OperationPhase.CANCELLED or OperationPhase.FAILED or OperationPhase.RECOVERY_REQUIRED)) throw new SafetyException("ExecutionUnavailable");
  var entries=await ReadJournalAsync(manifest.OperationId,ct);
  await AppendAsync(Folder(manifest.OperationId),new(entries.Length+1,DateTimeOffset.UtcNow,manifest.OperationId,next,"Coordinator",actionCode,true,null,["InspectJournal"]),ct);
  await WriteManifestAsync(Folder(manifest.OperationId),manifest with { Phase=next },ct);
 }
 public async Task<OperationManifest> LoadAsync(Guid id,CancellationToken ct) {
  var manifest=JsonSerializer.Deserialize<OperationManifest>(await File.ReadAllTextAsync(Path.Combine(Folder(id),"manifest.json"),ct),Json) ?? throw new SafetyException("JournalIncomplete");
  var entries=await ReadJournalAsync(id,ct);
  if(manifest.OperationId!=id || entries.IsEmpty || entries[^1].Phase!=manifest.Phase) throw new SafetyException("JournalIncomplete");
  return manifest;
 }
 public async Task<ImmutableArray<JournalEntry>> ReadJournalAsync(Guid id,CancellationToken ct) {
  var result=ImmutableArray.CreateBuilder<JournalEntry>();
  foreach(var line in await File.ReadAllLinesAsync(Path.Combine(Folder(id),"journal.jsonl"),ct)) {
   JournalEntry entry;
   try { entry=JsonSerializer.Deserialize<JournalEntry>(line,Lines) ?? throw new SafetyException("JournalIncomplete"); } catch(JsonException) { throw new SafetyException("JournalIncomplete"); }
   if(entry.OperationId!=id || entry.Sequence!=result.Count+1 || !entry.Verified) throw new SafetyException("JournalIncomplete");
   if(result.Count==0 && entry.Phase!=OperationPhase.CREATED || result.Count>0 && !CanTransition(result[^1].Phase,entry.Phase)) throw new SafetyException("JournalIncomplete");
   result.Add(entry);
  }
  return result.ToImmutable();
 }
 public IEnumerable<Guid> ListOperations() => Directory.EnumerateDirectories(root).Select(Path.GetFileName).Select(x=>Guid.TryParse(x,out var id)?id:Guid.Empty).Where(x=>x!=Guid.Empty);
 private static async Task AppendAsync(string folder,JournalEntry entry,CancellationToken ct) {
  var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry,Lines)+"\n");
  using var stream=new FileStream(Path.Combine(folder,"journal.jsonl"),FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
  await stream.WriteAsync(bytes,ct); stream.Flush(true);
  // Supplementary readable log; JSONL remains authoritative for recovery.
  var readable=$"{entry.Timestamp:O} [{entry.Component}] {entry.Phase}: {entry.ActionCode} verified={entry.Verified}\n";
  await File.AppendAllTextAsync(Path.Combine(folder,"journal.log"),readable,ct);
 }
 private static async Task WriteManifestAsync(string folder,OperationManifest manifest,CancellationToken ct) {
  var path=Path.Combine(folder,"manifest.json");
  using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
   await JsonSerializer.SerializeAsync(stream,manifest,Json,ct); await stream.FlushAsync(ct); stream.Flush(true);
  }
  File.Move(path+".tmp",path,true);
 }
}
